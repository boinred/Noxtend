using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;

namespace Noxtend.Tests;

/// <summary>
/// 산출물 서술을 짓는 테스트 도우미.
///
/// Design Ref: §3.1 · D-08
///
/// 목록을 손으로 짜면 호출부마다 MIME 문자열이 흩어지고, 그중 하나가 틀려도 컴파일러가
/// 잡지 못한다.
/// </summary>
internal static class MeshArtifacts
{
    public static IReadOnlyList<MeshArtifactDescriptor> Glb(string blobKey, long sizeBytes)
        => [new(MeshArtifactKind.Glb, blobKey, GeneratedMesh.GlbContentType, sizeBytes)];

    public static IReadOnlyList<MeshArtifactDescriptor> GlbAndPreview(
        string blobKey,
        long sizeBytes,
        string previewBlobKey,
        long previewSizeBytes,
        string previewContentType = "image/png")
        =>
        [
            new(MeshArtifactKind.Glb, blobKey, GeneratedMesh.GlbContentType, sizeBytes),
            new(MeshArtifactKind.Preview, previewBlobKey, previewContentType, previewSizeBytes),
        ];

    /// <summary>Meshy 결과 — GLB·FBX·미리보기 셋.</summary>
    public static IReadOnlyList<MeshArtifactDescriptor> All(string prefix, long sizeBytes)
        =>
        [
            new(MeshArtifactKind.Glb, $"{prefix}/model.glb", GeneratedMesh.GlbContentType, sizeBytes),
            new(MeshArtifactKind.Fbx, $"{prefix}/model.fbx", GeneratedMesh.FbxContentType, sizeBytes),
            new(MeshArtifactKind.Preview, $"{prefix}/preview.png", "image/png", 12_000),
        ];
}

/// <summary>내려받기 묶음에서 종류 하나를 꺼내는 도우미.</summary>
internal static class MeshDownloads
{
    public static Noxtend.Domain.Ports.MeshResultPart? Part(
        this Noxtend.Domain.Ports.IMeshResultDownload download, MeshArtifactKind kind)
        => download.Parts.FirstOrDefault(part => part.Kind == kind);
}
