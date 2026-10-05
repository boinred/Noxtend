using Noxtend.Domain.Similarity;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 유사도 실행 상태 기계. Design Ref: background-similarity-tuning §5.1 · §9 · D-01
///
/// **run 은 제작 파이프라인을 건드리지 않는 별도 경계다** — 완료된 작업을 다시 진행
/// 상태로 만들면 화면·삭제·재시도 규칙이 전부 흔들린다. 반복 상한(1..3)과 총 호출 수
/// (1+MaxIterations ≤ 4)가 유료 호출 폭주의 방어선이다.
/// </summary>
public sealed class SimilarityRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    // ─── 시작 ───

    [Fact]
    public void Start_BeginsEvaluatingWithZeroIterations()
    {
        var run = Run(maxIterations: 2);

        Assert.Equal(SimilarityRunStatus.Evaluating, run.Status);
        Assert.Equal(0, run.CurrentIteration);
        Assert.False(run.IsTerminal);
        // 총 유료 호출 = 기준 1 + 후보 반복 (§5.1)
        Assert.Equal(3, run.MaxCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Start_RejectsIterationsOutsideOneToThree(int iterations)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Run(iterations));
    }

    [Fact]
    public void Start_RequiresAnIdempotencyKey()
    {
        Assert.Throws<ArgumentException>(() => SimilarityRun.Start(
            Guid.NewGuid(), Guid.NewGuid(), "m", 1, idempotencyKey: " ", Now));
    }

    // ─── 정상 흐름: Evaluating → ReadyForAdjustment → AwaitingRender → Evaluating ───

    [Fact]
    public void HappyPath_WalksTheDesignedTransitions()
    {
        var run = Run(maxIterations: 1);

        run.MarkReadyForAdjustment(Now);           // 기준 평가 성공
        Assert.Equal(SimilarityRunStatus.ReadyForAdjustment, run.Status);

        run.BeginCandidate(Now);                   // 후보 생성 — 렌더 업로드 대기
        Assert.Equal(SimilarityRunStatus.AwaitingRender, run.Status);
        Assert.Equal(1, run.CurrentIteration);

        run.ResumeEvaluating(Now);                 // 렌더 도착 — 평가 재개
        Assert.Equal(SimilarityRunStatus.Evaluating, run.Status);

        run.Complete(Now);                         // 반복 소진 — 종료
        Assert.True(run.IsTerminal);
    }

    /// <summary>반복 상한 — MaxIterations 를 소진하면 후보를 더 만들 수 없다 (§5.1).</summary>
    [Fact]
    public void BeginCandidate_RefusesBeyondMaxIterations()
    {
        var run = Run(maxIterations: 1);
        run.MarkReadyForAdjustment(Now);
        run.BeginCandidate(Now);
        run.ResumeEvaluating(Now);
        run.MarkReadyForAdjustment(Now);

        Assert.Throws<InvalidOperationException>(() => run.BeginCandidate(Now));
    }

    [Fact]
    public void BeginCandidate_RequiresReadyForAdjustment()
    {
        var run = Run(maxIterations: 2);

        Assert.Throws<InvalidOperationException>(() => run.BeginCandidate(Now));
    }

    // ─── 종료 ───

    [Fact]
    public void Fail_KeepsTheCodeAndBecomesTerminal()
    {
        var run = Run(maxIterations: 1);

        run.Fail("SIMILARITY_EVALUATION_INVALID", Now);

        Assert.Equal(SimilarityRunStatus.Failed, run.Status);
        Assert.Equal("SIMILARITY_EVALUATION_INVALID", run.FailureCode);
        Assert.Equal(Now, run.CompletedAt);
    }

    /// <summary>종료 뒤의 전이는 전부 거절 — 취소된 run 이 다시 살아나면 안 된다.</summary>
    [Fact]
    public void TerminalRuns_RefuseEveryTransition()
    {
        var run = Run(maxIterations: 1);
        run.Cancel(Now);

        Assert.Throws<InvalidOperationException>(() => run.MarkReadyForAdjustment(Now));
        Assert.Throws<InvalidOperationException>(() => run.Complete(Now));
        Assert.Throws<InvalidOperationException>(() => run.Fail("X", Now));
    }

    /// <summary>사용자 종료(complete)는 ReadyForAdjustment·AwaitingRender 에서만 (§9.3).</summary>
    [Fact]
    public void Complete_AllowsUserFinishWhileWaiting()
    {
        var run = Run(maxIterations: 1);
        run.MarkReadyForAdjustment(Now);
        run.BeginCandidate(Now);

        run.Complete(Now);

        Assert.Equal(SimilarityRunStatus.Completed, run.Status);
    }

    // ─── 설정 ───

    private static SimilarityRun Run(int maxIterations)
        => SimilarityRun.Start(
            Guid.NewGuid(), Guid.NewGuid(), "gpt-test", maxIterations, "key-1", Now);
}
