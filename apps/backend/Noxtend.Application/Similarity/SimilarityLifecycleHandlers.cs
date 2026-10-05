using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Similarity;

namespace Noxtend.Application.Similarity;

/// <summary>
/// 실패한 run 의 재시도 (§9.3) — 저장된 렌더를 재사용해 유료 호출 1회를 다시 연다.
/// 새 캡처도 새 평가 행도 만들지 않는다: attempt 이력과 LlmCall 만 는다.
/// </summary>
public sealed class RetrySimilarityRunHandler(
    ISimilarityRepository similarity,
    ISimilarityQueue queue,
    IClock clock)
{
    public async Task<Result<SimilarityRun>> HandleAsync(
        Guid jobId, Guid runId, CancellationToken ct)
    {
        var run = await similarity.GetRunAsync(runId, ct);
        if (run is null || run.JobId != jobId)
        {
            return Result<SimilarityRun>.Fail(ErrorCode.SimilarityRunNotFound, "이 작업의 실행이 아닙니다");
        }

        if (run.Status != SimilarityRunStatus.Failed)
        {
            return Result<SimilarityRun>.Fail(ErrorCode.SimilarityConflict, "실패한 실행만 재시도할 수 있습니다");
        }

        // 실패한 마지막 평가 — 저장된 렌더가 있어야 다시 보낼 수 있다
        var evaluations = await similarity.ListEvaluationsAsync(runId, ct);
        var failed = evaluations.LastOrDefault(
            e => e.Status == SimilarityEvaluationStatus.Failed && e.Render is not null);
        if (failed is null)
        {
            return Result<SimilarityRun>.Fail(ErrorCode.SimilarityConflict, "재시도할 평가가 없습니다");
        }

        run.Retry(clock.Now);
        await similarity.SaveChangesAsync(ct);
        await queue.EnqueueAsync(failed.Id, ct);

        return Result<SimilarityRun>.Ok(run);
    }
}

/// <summary>
/// 취소 (§9.3) — 미래 반복과 아직 시작하지 않은 평가를 멈춘다.
/// 이미 공급자에 전송된 호출은 즉시 서지 않을 수 있고, 그 비용은 실제 usage 에 남는다.
/// 어떤 취소 경로도 기존 Active layout 과 성공한 평가 이력을 지우지 않는다.
/// </summary>
public sealed class CancelSimilarityRunHandler(
    ISimilarityRepository similarity,
    ISceneLayoutRepository layouts,
    IClock clock)
{
    public async Task<Result<SimilarityRun>> HandleAsync(
        Guid jobId, Guid runId, CancellationToken ct)
    {
        var run = await similarity.GetRunAsync(runId, ct);
        if (run is null || run.JobId != jobId)
        {
            return Result<SimilarityRun>.Fail(ErrorCode.SimilarityRunNotFound, "이 작업의 실행이 아닙니다");
        }

        if (run.IsTerminal)
        {
            return Result<SimilarityRun>.Ok(run);   // 멱등
        }

        await CloseOpenCandidatesAsync(similarity, layouts, run, clock.Now, ct);
        run.Cancel(clock.Now);
        await similarity.SaveChangesAsync(ct);

        return Result<SimilarityRun>.Ok(run);
    }

    /// <summary>미평가 후보 정리 — 평가는 Canceled, 후보 revision 은 Rejected 로 닫는다.</summary>
    internal static async Task CloseOpenCandidatesAsync(
        ISimilarityRepository similarity,
        ISceneLayoutRepository layouts,
        SimilarityRun run,
        DateTimeOffset now,
        CancellationToken ct)
    {
        foreach (var evaluation in await similarity.ListEvaluationsAsync(run.Id, ct))
        {
            if (evaluation.Status is not (SimilarityEvaluationStatus.AwaitingRender
                or SimilarityEvaluationStatus.Pending
                or SimilarityEvaluationStatus.Failed))
            {
                continue;
            }

            evaluation.Cancel(now);

            if (evaluation.Kind == SimilarityEvaluationKind.Candidate)
            {
                var layout = await layouts.GetAsync(evaluation.LayoutId, ct);
                if (layout?.State == SceneLayoutState.Candidate)
                {
                    layout.Reject();
                }
            }
        }
    }
}

/// <summary>
/// 사용자의 "현재 결과로 종료" (§9.3) — ReadyForAdjustment·AwaitingRender 에서만.
/// 아직 평가되지 않은 후보는 Rejected 로 닫는다.
/// </summary>
public sealed class CompleteSimilarityRunHandler(
    ISimilarityRepository similarity,
    ISceneLayoutRepository layouts,
    IClock clock)
{
    public async Task<Result<SimilarityRun>> HandleAsync(
        Guid jobId, Guid runId, CancellationToken ct)
    {
        var run = await similarity.GetRunAsync(runId, ct);
        if (run is null || run.JobId != jobId)
        {
            return Result<SimilarityRun>.Fail(ErrorCode.SimilarityRunNotFound, "이 작업의 실행이 아닙니다");
        }

        if (run.Status is not (SimilarityRunStatus.ReadyForAdjustment
            or SimilarityRunStatus.AwaitingRender))
        {
            return Result<SimilarityRun>.Fail(
                ErrorCode.SimilarityConflict, "종료할 수 있는 상태가 아닙니다");
        }

        await CancelSimilarityRunHandler.CloseOpenCandidatesAsync(similarity, layouts, run, clock.Now, ct);
        run.Complete(clock.Now);
        await similarity.SaveChangesAsync(ct);

        return Result<SimilarityRun>.Ok(run);
    }
}

/// <summary>
/// 스위퍼 유스케이스 (§12) — 만료 lease 의 Running 평가를 회수한다.
/// 워커 크래시로 Ack 없이 사라진 평가가 여기서 되살아난다. terminal run 은 건너뛴다.
/// </summary>
public sealed class SweepSimilarityHandler(
    ISimilarityRepository similarity,
    ISimilarityQueue queue,
    IClock clock)
{
    private const int MaxAttempts = 3;

    public async Task<int> HandleAsync(CancellationToken ct)
    {
        var expired = await similarity.ListExpiredRunningAsync(clock.Now, ct);
        var reclaimed = 0;

        foreach (var evaluation in expired)
        {
            evaluation.Fail(clock.Now);

            if (evaluation.AttemptCount < MaxAttempts)
            {
                await queue.EnqueueAsync(evaluation.Id, ct);
                reclaimed++;
            }
            else
            {
                // attempt 소진 — run 을 닫는다. 죽은 워커의 호출 비용은 usage 에 남는다
                var run = await similarity.GetRunAsync(evaluation.RunId, ct);
                if (run is not null && !run.IsTerminal)
                {
                    run.Fail(ErrorCode.ProviderCallFailed, clock.Now);
                }
            }
        }

        if (expired.Count > 0)
        {
            await similarity.SaveChangesAsync(ct);
        }

        return reclaimed;
    }
}
