using System.Buffers.Binary;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Blob;

/// <summary>
/// 3D 산출물 저장소.
///
/// Design Ref: §6.4 · §7.5 · Plan NFR-04
///
/// **키가 실행 ID 로 결정된다.** 일반 Blob 저장소는 GUID 로 새 키를 만들지만, 여기서는
/// 같은 실행이 다시 내려받으면 같은 자리에 덮어쓴다 — 크래시 재시도가 고아 Blob 을
/// 계속 만들지 않게 하는 것이 이 차이의 이유다.
/// </summary>
public sealed class AzureMeshArtifactStorage(BlobServiceClient client, string containerName)
    : IMeshArtifactStorage
{
    /// <summary>glTF 이진 컨테이너의 첫 네 바이트.</summary>
    private static readonly byte[] GlbMagic = "glTF"u8.ToArray();

    /// <summary>magic 4 + version 4 + length 4.</summary>
    private const int GlbHeaderBytes = 12;

    /// <summary>Autodesk 이진 FBX 의 서명. ASCII FBX 는 받지 않는다.</summary>
    private static readonly byte[] FbxMagic = "Kaydara FBX Binary  \0"u8.ToArray();

    /// <summary>
    /// 산출물 하나를 검증하고 저장한다 (§4.3 · D-08).
    ///
    /// **검증 규칙을 <paramref name="kind"/> 가 정한다.** 종류마다 메서드를 두면 새 형식이
    /// 올 때마다 포트가 넓어지고, 그때 검증을 건너뛴 경로가 하나 생긴다.
    /// </summary>
    public async Task<StoredMeshArtifact> SaveAsync(
        Guid meshRunId,
        MeshArtifactKind kind,
        Stream content,
        string? declaredContentType,
        long maxBytes,
        CancellationToken ct)
    {
        var bytes = await ReadBoundedAsync(content, maxBytes, Describe(kind), ct);

        var (key, contentType) = kind switch
        {
            MeshArtifactKind.Glb => ValidatedGlb(meshRunId, bytes),
            MeshArtifactKind.Fbx => ValidatedFbx(meshRunId, bytes),
            MeshArtifactKind.Preview => ValidatedPreview(meshRunId, bytes, declaredContentType),
            _ => throw new MeshArtifactRejectedException($"모르는 산출물 종류입니다: {kind}"),
        };

        await UploadAsync(key, bytes, contentType, ct);

        return new StoredMeshArtifact(key, contentType, bytes.Length);
    }

    private static (string Key, string ContentType) ValidatedGlb(Guid meshRunId, byte[] bytes)
    {
        ValidateGlb(bytes);

        return ($"meshes/{meshRunId:n}/model.glb", GeneratedMesh.GlbContentType);
    }

    private static (string Key, string ContentType) ValidatedFbx(Guid meshRunId, byte[] bytes)
    {
        // 이진 FBX 는 파일 첫머리에 고정 서명이 있다. 없으면 다른 것이 왔다는 뜻이고,
        // 그것을 저장하면 사용자가 열 수 없는 파일을 성공으로 받는다
        if (bytes.Length < FbxMagic.Length || !bytes.AsSpan(0, FbxMagic.Length).SequenceEqual(FbxMagic))
        {
            throw new MeshArtifactRejectedException("FBX 형식이 아닙니다");
        }

        return ($"meshes/{meshRunId:n}/model.fbx", GeneratedMesh.FbxContentType);
    }

    private static (string Key, string ContentType) ValidatedPreview(
        Guid meshRunId, byte[] bytes, string? declaredContentType)
    {
        // **공급자가 말한 형식을 믿지 않는다** (§7.5). 헤더가 정본이라 그것으로 정규화한다 —
        // 잘못된 Content-Type 으로 저장하면 브라우저가 이미지를 그리지 못한다
        var contentType = SniffImage(bytes)
            ?? throw new MeshArtifactRejectedException(
                $"미리보기가 이미지가 아닙니다. 공급자 표기: {declaredContentType ?? "없음"}");

        return ($"meshes/{meshRunId:n}/preview.{Extension(contentType)}", contentType);
    }

    private static string Describe(MeshArtifactKind kind) => kind switch
    {
        MeshArtifactKind.Glb => "GLB",
        MeshArtifactKind.Fbx => "FBX",
        _ => "미리보기",
    };

    public async Task<Stream> OpenAsync(string blobKey, CancellationToken ct)
        => await client.GetBlobContainerClient(containerName)
            .GetBlobClient(blobKey)
            .OpenReadAsync(cancellationToken: ct);

    /// <summary>
    /// **길이를 우리가 센다** (§13.2). `Content-Length` 가 없어도, 거짓말을 해도
    /// 상한을 넘기면 끊는다 — 공급자 응답 하나가 파드 메모리를 다 먹을 수 있다.
    /// </summary>
    private static async Task<byte[]> ReadBoundedAsync(
        Stream content, long maxBytes, string what, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];

        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new MeshArtifactRejectedException($"{what} 가 상한을 넘습니다");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }

        return buffer.Length == 0
            ? throw new MeshArtifactRejectedException($"{what} 가 비어 있습니다")
            : buffer.ToArray();
    }

    /// <summary>
    /// glTF 2.0 이진 헤더를 확인한다 (§7.5).
    ///
    /// **성공으로 확정하기 전에 봐야 한다.** 여기를 지나면 공정이 성공이 되는데, 그러고도
    /// 파일이 열리지 않으면 사용자는 완료를 보고 빈손이 된다.
    /// </summary>
    private static void ValidateGlb(byte[] bytes)
    {
        if (bytes.Length < GlbHeaderBytes)
        {
            throw new MeshArtifactRejectedException("GLB 헤더가 없습니다");
        }

        if (!bytes.AsSpan(0, 4).SequenceEqual(GlbMagic))
        {
            throw new MeshArtifactRejectedException("GLB 형식이 아닙니다");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
        if (version != 2)
        {
            throw new MeshArtifactRejectedException($"지원하지 않는 glTF 버전입니다: {version}");
        }

        // 헤더가 말하는 전체 길이와 실제 바이트가 다르면 내려받다 끊긴 것이다
        var declared = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        if (declared != bytes.Length)
        {
            throw new MeshArtifactRejectedException(
                $"GLB 길이가 헤더와 다릅니다. 헤더 {declared}, 실제 {bytes.Length}");
        }
    }

    /// <summary>magic bytes 로 이미지 형식을 정한다. 모르면 <c>null</c>.</summary>
    private static string? SniffImage(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        // RIFF....WEBP
        if (bytes.Length >= 12
            && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    private static string Extension(string contentType) => contentType switch
    {
        "image/png" => "png",
        "image/webp" => "webp",
        _ => "jpg",
    };

    private async Task UploadAsync(string key, byte[] bytes, string contentType, CancellationToken ct)
    {
        var container = client.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        using var content = new MemoryStream(bytes);

        // 같은 실행이 다시 내려받으면 덮어쓴다 — 그것이 결정된 키를 쓰는 이유다
        await container.GetBlobClient(key).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);
    }

    /// <summary>없으면 조용히 넘어간다 — 최선 삭제라 두 번 부르는 것이 정상이다.</summary>
    public async Task DeleteAsync(string blobKey, CancellationToken ct)
        => await client.GetBlobContainerClient(containerName)
            .GetBlobClient(blobKey)
            .DeleteIfExistsAsync(cancellationToken: ct);
}
