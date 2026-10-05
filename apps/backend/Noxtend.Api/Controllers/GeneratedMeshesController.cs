using Microsoft.AspNetCore.Mvc;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;

namespace Noxtend.Api.Controllers;

/// <summary>
/// 완성된 3D 결과.
///
/// Design Ref: §10.4 · Plan NFR-07
///
/// **공급자 링크를 대신하는 자리다.** Tripo 가 주는 model URL 은 5분이면 만료되므로,
/// 화면이 그것을 들고 있으면 어제 만든 에셋을 못 내려받는다. 여기서는 우리 Blob 의
/// 키만 쓰므로 만료가 없다.
///
/// **GUID 로만 조회한다** — 추측도 순회도 되지 않고, 사용자 입력이 경로가 되는 길이 없다.
/// </summary>
[ApiController]
[Route("api/generated-meshes")]
public sealed class GeneratedMeshesController(
    IJobRepository jobs,
    IMeshArtifactStorage artifacts) : ControllerBase
{
    /// <summary>
    /// GLB 내려받기.
    ///
    /// 봉투를 쓰지 않는다 — 바이너리를 JSON 에 담을 수 없다. 생성 이미지와 같은 규약이다.
    /// </summary>
    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
        => SendAsync(id, MeshArtifactKind.Glb, "glb", ct);

    /// <summary>
    /// FBX 내려받기. **Tripo 결과에는 없으므로 404 다** (Plan D-05).
    ///
    /// 형식별 경로를 쓴다 (D-15) — <c>?format=fbx</c> 로 하면 <c>immutable</c> 캐시가
    /// 쿼리별로 나뉘는 것을 중간 캐시가 보장하지 않는다.
    /// </summary>
    [HttpGet("{id:guid}/fbx")]
    public Task<IActionResult> GetFbxAsync(Guid id, CancellationToken ct)
        => SendAsync(id, MeshArtifactKind.Fbx, "fbx", ct);

    /// <summary>미리보기 이미지. 공급자가 주지 않았으면 404 다.</summary>
    [HttpGet("{id:guid}/preview")]
    public Task<IActionResult> GetPreviewAsync(Guid id, CancellationToken ct)
        => SendAsync(id, MeshArtifactKind.Preview, extension: null, ct);

    /// <summary>
    /// 산출물 하나를 내보낸다.
    ///
    /// <paramref name="extension"/> 이 <c>null</c> 이면 내려받기 이름을 붙이지 않는다 —
    /// 미리보기는 브라우저가 그려야 하므로 첨부가 아니다.
    /// </summary>
    private async Task<IActionResult> SendAsync(
        Guid id, MeshArtifactKind kind, string? extension, CancellationToken ct)
    {
        if (await FindAsync(id, ct) is not { } mesh || mesh.Find(kind) is not { } artifact)
        {
            return NotFound();
        }

        var stream = await artifacts.OpenAsync(artifact.BlobKey, ct);

        // 결과는 바뀌지 않는다 — 다시 만들면 새 id 가 나오므로 이 id 의 내용은 불변이다.
        // private 인 이유는 인증이 붙었을 때 공유 캐시에 남아 있으면 안 되기 때문이다
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";

        // **파일명에 id 만 쓴다.** 파츠 이름을 쓰면 사용자 문자열이 헤더로 나가고,
        // 그 안의 따옴표·줄바꿈이 헤더를 쪼갠다
        return extension is null
            ? File(stream, artifact.ContentType)
            : File(stream, artifact.ContentType, $"{mesh.Id}.{extension}");
    }

    private async Task<GeneratedMesh?> FindAsync(Guid id, CancellationToken ct)
        => await jobs.GetGeneratedMeshAsync(id, ct);
}
