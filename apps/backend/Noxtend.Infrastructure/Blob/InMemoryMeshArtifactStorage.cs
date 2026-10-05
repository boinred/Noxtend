using System.Buffers.Binary;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Blob;

/// <summary>
/// 인프라 없는 <see cref="IMeshArtifactStorage"/>.
///
/// Design Ref: §2.0 · §6.4
///
/// **검증은 실제 구현과 같은 규칙을 쓴다.** 여기서 아무 바이트나 받아 주면 재기동
/// 시나리오가 깨진 GLB 로도 통과하고, 그 결함은 배포 후에야 드러난다.
/// </summary>
public sealed class InMemoryMeshArtifactStorage : IMeshArtifactStorage
{
    private static readonly byte[] GlbMagic = "glTF"u8.ToArray();

    private readonly Dictionary<string, byte[]> blobs = [];

    /// <summary>테스트가 "무엇이 저장되었는가" 를 확인하는 창구.</summary>
    public IReadOnlyDictionary<string, byte[]> Blobs => blobs;

    private static readonly byte[] FbxMagic = "Kaydara FBX Binary  \0"u8.ToArray();

    public async Task<StoredMeshArtifact> SaveAsync(
        Guid meshRunId,
        MeshArtifactKind kind,
        Stream content,
        string? declaredContentType,
        long maxBytes,
        CancellationToken ct)
    {
        var bytes = await ReadAsync(content, maxBytes, ct);

        var (name, contentType) = kind switch
        {
            MeshArtifactKind.Glb => (ValidatedGlb(bytes), GeneratedMesh.GlbContentType),
            MeshArtifactKind.Fbx => (ValidatedFbx(bytes), GeneratedMesh.FbxContentType),
            MeshArtifactKind.Preview => (ValidatedPreview(bytes), "image/png"),
            _ => throw new MeshArtifactRejectedException($"모르는 산출물 종류입니다: {kind}"),
        };

        // 같은 실행이 다시 내려받으면 같은 자리에 덮어쓴다
        var key = $"meshes/{meshRunId:n}/{name}";
        blobs[key] = bytes;

        return new StoredMeshArtifact(key, contentType, bytes.Length);
    }

    private static string ValidatedGlb(byte[] bytes)
    {
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual(GlbMagic))
        {
            throw new MeshArtifactRejectedException("GLB 형식이 아닙니다");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != 2)
        {
            throw new MeshArtifactRejectedException("지원하지 않는 glTF 버전입니다");
        }

        return "model.glb";
    }

    private static string ValidatedFbx(byte[] bytes)
        => bytes.Length >= FbxMagic.Length && bytes.AsSpan(0, FbxMagic.Length).SequenceEqual(FbxMagic)
            ? "model.fbx"
            : throw new MeshArtifactRejectedException("FBX 형식이 아닙니다");

    private static string ValidatedPreview(byte[] bytes)
        => bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50
            ? "preview.png"
            : throw new MeshArtifactRejectedException("미리보기가 PNG 가 아닙니다");

    public Task<Stream> OpenAsync(string blobKey, CancellationToken ct)
        => blobs.TryGetValue(blobKey, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new FileNotFoundException(blobKey);

    private static async Task<byte[]> ReadAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        return buffer.Length > maxBytes
            ? throw new MeshArtifactRejectedException("산출물이 상한을 넘습니다")
            : buffer.ToArray();
    }

    public Task DeleteAsync(string blobKey, CancellationToken ct)
    {
        blobs.Remove(blobKey);
        return Task.CompletedTask;
    }
}
