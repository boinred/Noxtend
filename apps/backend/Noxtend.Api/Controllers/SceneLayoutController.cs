using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Application.Scene;

namespace Noxtend.Api.Controllers;

/// <summary>
/// Design Ref: scene-assembly §4 — 조립 명세는 전용 창구다.
/// 작업 상세에 끼우면 홈·진행 화면까지 인스턴스 목록을 실어 나른다.
/// </summary>
[ApiController]
[Route("api/jobs/{jobId:guid}/scene-layout")]
public sealed class SceneLayoutController(
    GetSceneLayoutHandler handler,
    ListSceneRevisionsHandler revisions,
    RestoreSceneRevisionHandler restore) : ControllerBase
{
    /// <summary>없거나 낡았으면 유도해 저장한 뒤 준다 — 조회가 곧 생성이다 (§3.4).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAsync(Guid jobId, CancellationToken ct)
        => ApiResults.From(await handler.HandleAsync(jobId, ct), SceneLayoutResponse.From);

    /// <summary>revision 이력 — 최신부터 (background-similarity-tuning §10).</summary>
    [HttpGet("revisions")]
    public async Task<IActionResult> ListRevisionsAsync(Guid jobId, CancellationToken ct)
        => ApiResults.From(
            await revisions.HandleAsync(jobId, ct),
            list => list.Select(SceneRevisionResponse.From).ToList());

    /// <summary>복원 = 값 복사 (§4.3) — 새 활성 revision 이 만들어진다.</summary>
    [HttpPost("revisions/{layoutId:guid}/restore")]
    public async Task<IActionResult> RestoreAsync(
        Guid jobId, Guid layoutId, CancellationToken ct)
        => ApiResults.From(
            await restore.HandleAsync(jobId, layoutId, ct),
            layout => SceneRevisionResponse.From(new Noxtend.Application.Scene.SceneRevisionView(
                layout.Id, layout.Revision, layout.State, layout.Origin, layout.ComposedAt)),
            StatusCodes.Status201Created);
}
