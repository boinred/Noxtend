using Noxtend.Application.Similarity;
using Noxtend.Domain.Common;
using Noxtend.Domain.Similarity;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Queue;
using Noxtend.Infrastructure.Persistence.InMemory;

namespace Noxtend.Tests.Application;

/// <summary>
/// 재시도·취소·완료·스위퍼 (§9.3 · §12).
///
/// **어떤 실패·취소 경로도 기존 활성 layout 과 성공한 평가 이력을 지우지 않는다.**
/// 재시도는 저장된 렌더로 유료 호출 1회를 다시 열 뿐 — 새 캡처도 새 행도 없다.
/// </summary>
public sealed class SimilarityLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>재시도 — 실패 run 이 다시 열리고 같은 평가가 재적재된다 (§9.3).</summary>
    [Fact]
    public async Task Retry_ReopensTheRunAndRequeuesTheStoredRender()
    {
        var fixture = await SimilarityFixture.CreateAsync();
        var started = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);
        var run = started.Value!.Run;
        var evaluation = started.Value.BaselineEvaluation;

        // 계약 위반 3회로 run 실패까지 간다
        evaluation.BeginAttempt(Now.AddMinutes(5), Now);
        evaluation.Fail(Now);
        evaluation.BeginAttempt(Now.AddMinutes(5), Now);
        evaluation.Fail(Now);
        evaluation.BeginAttempt(Now.AddMinutes(5), Now);
        evaluation.Fail(Now);
        run.Fail(ErrorCode.SimilarityEvaluationInvalid, Now);

        var retry = new RetrySimilarityRunHandler(
            fixture.Similarity, fixture.Queue, new FixedClock(Now));
        var result = await retry.HandleAsync(fixture.Job.Id, run.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SimilarityRunStatus.Evaluating, result.Value!.Status);
        Assert.Null(result.Value.FailureCode);
        // 최초 시작 1 + 재시도 재적재 1 — 같은 평가 id 다
        Assert.Equal(2, fixture.Queue.Enqueued.Count);
        Assert.All(fixture.Queue.Enqueued, id => Assert.Equal(evaluation.Id, id));
    }

    [Fact]
    public async Task Retry_RefusesANonFailedRun()
    {
        var fixture = await SimilarityFixture.CreateAsync();
        var started = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);

        var retry = new RetrySimilarityRunHandler(
            fixture.Similarity, fixture.Queue, new FixedClock(Now));
        var result = await retry.HandleAsync(
            fixture.Job.Id, started.Value!.Run.Id, CancellationToken.None);

        Assert.Equal(ErrorCode.SimilarityConflict, result.ErrorCode);
    }

    /// <summary>취소 — run 종료 + 미시작 평가 정리. 멱등이다 (§9.3).</summary>
    [Fact]
    public async Task Cancel_ClosesTheRunAndPendingEvaluations()
    {
        var fixture = await SimilarityFixture.CreateAsync();
        var started = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);

        var cancel = new CancelSimilarityRunHandler(
            fixture.Similarity, fixture.Layouts, new FixedClock(Now));
        var result = await cancel.HandleAsync(
            fixture.Job.Id, started.Value!.Run.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SimilarityRunStatus.Canceled, result.Value!.Status);

        var evaluation = await fixture.Similarity.GetEvaluationAsync(
            started.Value.BaselineEvaluation.Id, CancellationToken.None);
        Assert.Equal(SimilarityEvaluationStatus.Canceled, evaluation!.Status);

        // 멱등 — 두 번째 취소도 성공이다
        var again = await cancel.HandleAsync(
            fixture.Job.Id, started.Value.Run.Id, CancellationToken.None);
        Assert.True(again.IsSuccess);
    }

    /// <summary>스위퍼 — lease 만료 Running 평가를 실패로 돌리고 재적재한다 (§12).</summary>
    [Fact]
    public async Task Sweep_ReclaimsExpiredLeases()
    {
        var fixture = await SimilarityFixture.CreateAsync();
        var started = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);
        var evaluation = started.Value!.BaselineEvaluation;

        // 워커가 집었다가 죽었다 — lease 가 과거에 만료
        evaluation.BeginAttempt(Now.AddMinutes(-1), Now.AddMinutes(-6));

        var sweep = new SweepSimilarityHandler(
            fixture.Similarity, fixture.Queue, new FixedClock(Now));
        var reclaimed = await sweep.HandleAsync(CancellationToken.None);

        Assert.Equal(1, reclaimed);
        Assert.Equal(SimilarityEvaluationStatus.Failed, evaluation.Status);
        Assert.Equal(2, fixture.Queue.Enqueued.Count);   // 시작 1 + 회수 1
    }

    /// <summary>terminal run 의 만료 lease 는 회수하지 않는다 — 취소 뒤 유료 호출 금지 (§12).</summary>
    [Fact]
    public async Task Sweep_SkipsTerminalRuns()
    {
        var fixture = await SimilarityFixture.CreateAsync();
        var started = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);
        var evaluation = started.Value!.BaselineEvaluation;
        evaluation.BeginAttempt(Now.AddMinutes(-1), Now.AddMinutes(-6));
        started.Value.Run.Cancel(Now);

        var sweep = new SweepSimilarityHandler(
            fixture.Similarity, fixture.Queue, new FixedClock(Now));

        Assert.Equal(0, await sweep.HandleAsync(CancellationToken.None));
        Assert.Single(fixture.Queue.Enqueued);   // 시작 1 뿐
    }
}
