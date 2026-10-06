using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Scene;

namespace Noxtend.Application.Scene;

/// <summary>revision 한 줄 — 이력 화면의 재료 (§14.1 실행 이력).</summary>
public sealed record SceneRevisionView(
    Guid Id,
    int Revision,
    SceneLayoutState State,
    SceneLayoutOrigin Origin,
    DateTimeOffset ComposedAt);

/// <summary>Design Ref: background-similarity-tuning §10 — revision 이력, 최신부터.</summary>
public sealed class ListSceneRevisionsHandler(ISceneLayoutRepository layouts)
{
    public async Task<Result<IReadOnlyList<SceneRevisionView>>> HandleAsync(
        Guid jobId, CancellationToken ct)
        => Result<IReadOnlyList<SceneRevisionView>>.Ok(
            [.. (await layouts.ListByJobAsync(jobId, ct)).Select(layout =>
                new SceneRevisionView(
                    layout.Id, layout.Revision, layout.State, layout.Origin, layout.ComposedAt))]);
}

/// <summary>
/// revision 복원 (§4.3) — 과거 행을 되살리지 않고 값을 새 활성 revision 으로 복사한다.
///
/// Design Ref: background-similarity-tuning §4.3 · §10
///
/// mesh 가 바뀐 뒤의 복원은 거짓말이다 — 그 배치는 지금 mesh 의 것이 아니라
/// SceneRevisionStale 로 거절한다. 활성 교대(Superseded + 새 Active)는 한 저장 단위다.
/// </summary>
public sealed class RestoreSceneRevisionHandler(
    IJobRepository jobs,
    ISceneLayoutRepository layouts,
    IClock clock)
{
    public async Task<Result<SceneLayout>> HandleAsync(
        Guid jobId, Guid layoutId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<SceneLayout>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        if (job.ProductionMode == ProductionMode.TwoD)
            return Result<SceneLayout>.Fail(ErrorCode.SpriteWrongMode, "3D 작업이 필요합니다");

        var source = await layouts.GetAsync(layoutId, ct);
        if (source is null || source.JobId != jobId)
        {
            return Result<SceneLayout>.Fail(ErrorCode.SceneRevisionStale, "이 작업의 revision 이 아닙니다");
        }

        // 현재 합성 서명 — 복원 대상이 지금 입력의 것인지 (§4.3)
        var signature = SceneMeshSignature.Compute(job.LayoutSignatureInputs());

        var revision = await layouts.MaxRevisionAsync(jobId, ct) + 1;

        SceneLayout restored;
        try
        {
            restored = SceneLayout.CreateRestored(source, signature, revision, clock.Now);
        }
        catch (SceneRevisionStaleException ex)
        {
            return Result<SceneLayout>.Fail(ErrorCode.SceneRevisionStale, ex.Message);
        }

        // 활성 교대 — 같은 저장 단위 (§4.1)
        var active = await layouts.GetActiveByJobAsync(jobId, ct);
        if (active is not null)
        {
            if (active.Id == source.Id)
            {
                // 이미 활성인 revision 의 복원은 할 일이 없다 — 멱등
                return Result<SceneLayout>.Ok(active);
            }

            active.MarkSuperseded();
        }

        await layouts.AddAsync(restored, ct);

        return Result<SceneLayout>.Ok(restored);
    }
}
