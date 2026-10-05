using Noxtend.Domain.Scene;
using Noxtend.Domain.Similarity;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 평가 한 건의 생애와 저장 상한. Design Ref: background-similarity-tuning §5.2 · §5.4
///
/// **기준은 렌더와 함께 태어나고, 후보는 렌더를 기다리며 태어난다** — 후보 revision 은
/// 서버가 만들지만 렌더는 브라우저의 WebGL 만 만들 수 있어서다. 보정 제안·재생성 노트의
/// 상한은 프롬프트가 계약을 어겨도 저장이 부풀지 않게 하는 방어선이다.
/// </summary>
public sealed class SimilarityEvaluationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly EvaluationRenderArtifact Render = new(
        "similarity/run/render-1.png", "image/png", 123_456, new string('A', 64));

    // ─── 생성 ───

    [Fact]
    public void Baseline_IsBornPendingWithItsRender()
    {
        var evaluation = SimilarityEvaluation.CreateBaseline(
            Guid.NewGuid(), Guid.NewGuid(), Render, Now);

        Assert.Equal(SimilarityEvaluationKind.Baseline, evaluation.Kind);
        Assert.Equal(SimilarityEvaluationStatus.Pending, evaluation.Status);
        Assert.Equal(1, evaluation.Sequence);
        Assert.Equal(Render, evaluation.Render);
    }

    [Fact]
    public void Candidate_IsBornAwaitingItsRender()
    {
        var evaluation = SimilarityEvaluation.CreateCandidate(
            Guid.NewGuid(), Guid.NewGuid(), sequence: 2, Now);

        Assert.Equal(SimilarityEvaluationStatus.AwaitingRender, evaluation.Status);
        Assert.Null(evaluation.Render);
    }

    [Fact]
    public void AttachRender_MovesTheCandidateToPending()
    {
        var evaluation = Candidate();

        evaluation.AttachRender(Render);

        Assert.Equal(SimilarityEvaluationStatus.Pending, evaluation.Status);
        Assert.Equal(Render, evaluation.Render);

        // 이미 렌더가 붙었으면 거절 — 덮어쓰면 평가와 렌더가 어긋난다
        Assert.Throws<InvalidOperationException>(() => evaluation.AttachRender(Render));
    }

    // ─── 실행·성공 ───

    [Fact]
    public void BeginAttempt_CountsAndLeases()
    {
        var evaluation = Baseline();

        evaluation.BeginAttempt(Now.AddMinutes(5), Now);

        Assert.Equal(SimilarityEvaluationStatus.Running, evaluation.Status);
        Assert.Equal(1, evaluation.AttemptCount);
        Assert.Equal(Now.AddMinutes(5), evaluation.LeaseExpiresAt);
    }

    [Fact]
    public void Succeed_KeepsScoreAdjustmentsAndNotes()
    {
        var evaluation = Baseline();
        evaluation.BeginAttempt(Now.AddMinutes(5), Now);
        var promptVersionId = Guid.NewGuid();

        evaluation.Succeed(
            Score(60),
            [Proposal(0.9)],
            ["지평선을 유지한 채 근경을 낮춘다"],
            promptVersionId,
            Now);

        Assert.Equal(SimilarityEvaluationStatus.Succeeded, evaluation.Status);
        Assert.Equal(60, evaluation.Score!.Overall);
        Assert.Single(evaluation.Adjustments);
        Assert.Single(evaluation.RegenerationNotes);
        Assert.Equal(promptVersionId, evaluation.PromptVersionId);
        Assert.Null(evaluation.LeaseExpiresAt);
    }

    // ─── 저장 상한 (§5.4) ───

    [Fact]
    public void Succeed_RejectsMoreThanTwentyFourAdjustments()
    {
        var evaluation = Running();

        var tooMany = Enumerable.Range(0, 25).Select(_ => Proposal(0.5)).ToList();
        Assert.Throws<ArgumentException>(() =>
            evaluation.Succeed(Score(50), tooMany, [], Guid.NewGuid(), Now));
    }

    [Fact]
    public void Succeed_RejectsOverlongReasonsNotesAndBadConfidence()
    {
        Assert.Throws<ArgumentException>(() => Running().Succeed(
            Score(50), [Proposal(0.5, reason: new string('가', 301))], [], Guid.NewGuid(), Now));

        Assert.Throws<ArgumentException>(() => Running().Succeed(
            Score(50), [], Enumerable.Range(0, 13).Select(i => $"노트{i}").ToList(),
            Guid.NewGuid(), Now));

        Assert.Throws<ArgumentException>(() => Running().Succeed(
            Score(50), [Proposal(confidence: 1.5)], [], Guid.NewGuid(), Now));
    }

    // ─── 실패·취소 ───

    [Fact]
    public void Fail_ReleasesTheLeaseAndAllowsRetry()
    {
        var evaluation = Running();

        evaluation.Fail(Now);

        Assert.Equal(SimilarityEvaluationStatus.Failed, evaluation.Status);
        Assert.Null(evaluation.LeaseExpiresAt);

        // terminal 실패 후 retry — 새 평가 행이 아니라 같은 행의 attempt 가 늘어난다 (§9.3)
        evaluation.BeginAttempt(Now.AddMinutes(5), Now);
        Assert.Equal(2, evaluation.AttemptCount);
    }

    [Fact]
    public void Cancel_RefusesASucceededEvaluation()
    {
        var evaluation = Running();
        evaluation.Succeed(Score(50), [], [], Guid.NewGuid(), Now);

        Assert.Throws<InvalidOperationException>(() => evaluation.Cancel(Now));
    }

    // ─── 설정 ───

    private static SimilarityEvaluation Baseline()
        => SimilarityEvaluation.CreateBaseline(Guid.NewGuid(), Guid.NewGuid(), Render, Now);

    private static SimilarityEvaluation Candidate()
        => SimilarityEvaluation.CreateCandidate(Guid.NewGuid(), Guid.NewGuid(), 2, Now);

    private static SimilarityEvaluation Running()
    {
        var evaluation = Baseline();
        evaluation.BeginAttempt(Now.AddMinutes(5), Now);
        return evaluation;
    }

    private static SimilarityAdjustment Proposal(double confidence, string reason = "근경이 크다")
        => new(Guid.NewGuid(), new ScaleScene(1.1), confidence, reason);

    private static SimilarityScore Score(int uniform)
        => SimilarityScore.Create(
            [.. Enum.GetValues<SimilarityDimensionKind>()
                .Select(kind => new SimilarityDimension(kind, uniform, "관찰", "권고"))]);
}
