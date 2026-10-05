using Noxtend.Domain.Mesh;

namespace Noxtend.Domain.Job;

/// <summary>
/// 자체 저장소에 들어간 3D 결과. 공정 하나에 행 하나.
///
/// Design Ref: §3.3 · D-08 · Plan FR-09 · NFR-07
///
/// **공급자 URL 을 저장하지 않는 것이 이 타입의 요지다.** 성공 응답이 주는 model URL 은
/// 5분이면 만료된다. 그것을 결과로 두면 화면이 어제 만든 에셋을 못 내려받는다. 여기 남는
/// 것은 우리 Blob 의 키뿐이다.
///
/// **산출물이 목록이다.** 전에는 GLB 와 미리보기가 각각 열 셋이었는데, Meshy 가 FBX 까지
/// 내면서 형식이 셋이 됐다. 열로 두면 형식이 늘 때마다 스키마·저장·내려받기가 함께
/// 넓어진다 (D-08).
///
/// <see cref="GeneratedImage"/> 와 같은 규칙으로 **재시도하면 행이 쌓인다** — 이전 것을
/// 지우지 않고 파츠가 가장 나중 것을 가리킨다.
/// </summary>
public sealed class GeneratedMesh
{
    /// <summary>GLB 는 형식이 하나뿐이다.</summary>
    public const string GlbContentType = "model/gltf-binary";

    /// <summary>FBX 는 Autodesk 독점 이진 형식이라 등록된 MIME 이 없다.</summary>
    public const string FbxContentType = "application/octet-stream";

    private readonly List<MeshArtifact> _artifacts = [];

    private GeneratedMesh()
    {
        // EF Core 재구성용
    }

    private GeneratedMesh(
        Guid id, Guid jobId, Guid partId, Guid taskId, Guid meshRunId,
        IReadOnlyList<MeshArtifactDescriptor> artifacts, int? creditsConsumed, DateTimeOffset now)
    {
        Id = id;
        JobId = jobId;
        PartId = partId;
        TaskId = taskId;
        MeshRunId = meshRunId;
        CreditsConsumed = creditsConsumed;
        CreatedAt = now;

        _artifacts.AddRange(artifacts.Select(descriptor => new MeshArtifact(
            descriptor.Kind, descriptor.BlobKey, descriptor.ContentType, descriptor.SizeBytes, now)));
    }

    /// <summary>API 내려받기 ID.</summary>
    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }
    public Guid PartId { get; private set; }

    /// <summary>이 결과를 낳은 3D 재구성 공정.</summary>
    public Guid TaskId { get; private set; }

    /// <summary>외부 실행 추적. 진단할 때 공급자 쪽 기록과 잇는 고리다.</summary>
    public Guid MeshRunId { get; private set; }

    public IReadOnlyList<MeshArtifact> Artifacts => _artifacts;

    /// <summary>공급자가 보고한 credit. USD 환산은 이번 범위가 아니다 (Plan D-06).</summary>
    public int? CreditsConsumed { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public MeshArtifact? Find(MeshArtifactKind kind)
        => _artifacts.FirstOrDefault(artifact => artifact.Kind == kind);

    /// <summary>GLB 없이는 만들어지지 않으므로 늘 있다.</summary>
    public MeshArtifact Model => Find(MeshArtifactKind.Glb)
        ?? throw new InvalidOperationException("GLB 없는 3D 결과는 만들어질 수 없습니다");

    public bool HasPreview => Find(MeshArtifactKind.Preview) is not null;

    /// <summary>Meshy 결과에만 있다. Tripo 는 FBX 를 내지 않는다 (Plan D-05).</summary>
    public bool HasFbx => Find(MeshArtifactKind.Fbx) is not null;

    internal static GeneratedMesh Create(
        Guid jobId, Guid partId, Guid taskId, Guid meshRunId,
        IReadOnlyList<MeshArtifactDescriptor> artifacts, int? creditsConsumed, DateTimeOffset now)
    {
        // **GLB 가 없으면 성공이 아니다.** 여기를 지나면 공정이 성공으로 확정되는데,
        // 그러고도 내려받을 것이 없으면 사용자가 성공을 보고 빈손이 된다
        if (artifacts.All(artifact => artifact.Kind != MeshArtifactKind.Glb))
        {
            throw new InvalidOperationException("GLB 없이 3D 결과를 만들 수 없습니다");
        }

        return new GeneratedMesh(
            Guid.NewGuid(), jobId, partId, taskId, meshRunId, artifacts, creditsConsumed, now);
    }
}
