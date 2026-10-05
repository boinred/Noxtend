using Noxtend.Domain.Scene;

namespace Noxtend.Domain.Similarity;

public enum SimilarityEvaluationKind
{
    Baseline = 0,
    Candidate = 1,
}

public enum SimilarityEvaluationStatus
{
    AwaitingRender = 0,
    Pending = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Canceled = 5,
}

/// <summary>
/// 평가 렌더의 정체 — blob 참조만. 생성 렌더를 사용자 업로드(StoredImage)로 가장하지
/// 않는다 (§5.2): 업로드 파이프라인의 검증·수명 규칙이 섞이면 둘 다 흐려진다.
/// </summary>
public sealed record EvaluationRenderArtifact(
    string BlobKey, string ContentType, long SizeBytes, string Sha256);

/// <summary>
/// 모델이 제안한 보정 하나 — 사용자가 고르는 단위. 실제 적용 값은 이 저장본에서 서버가
/// 다시 읽는다 (§6): 클라이언트가 값을 보내면 allowlist 검증을 우회할 수 있다.
/// </summary>
public sealed record SimilarityAdjustment(
    Guid Id,
    SceneAdjustmentCommand Command,
    double Confidence,
    string Reason);

/// <summary>
/// 평가 한 건 — LLM 호출 한 번의 대상·입력·결과.
///
/// Design Ref: background-similarity-tuning §5.2 · §5.4 · §9
///
/// **기준은 렌더와 함께 태어나고(Pending), 후보는 렌더를 기다리며 태어난다
/// (AwaitingRender)** — 후보 revision 은 서버가 만들지만 렌더는 브라우저의 WebGL 만
/// 만들 수 있다. terminal 실패 후 재시도는 새 행이 아니라 같은 행의 attempt 증가다 (§9.3).
/// </summary>
public sealed class SimilarityEvaluation
{
    // 저장 상한 (§5.4) — 프롬프트가 계약을 어겨도 저장이 부풀지 않는다
    private const int MaxAdjustments = 24;
    private const int MaxReasonLength = 300;
    private const int MaxNotes = 12;
    private const int MaxNoteLength = 500;

    private SimilarityEvaluation()
    {
        // EF Core 재구성용
        Adjustments = [];
        RegenerationNotes = [];
    }

    private SimilarityEvaluation(
        Guid id, Guid runId, Guid layoutId, int sequence,
        SimilarityEvaluationKind kind, SimilarityEvaluationStatus status,
        EvaluationRenderArtifact? render, DateTimeOffset now)
    {
        Id = id;
        RunId = runId;
        LayoutId = layoutId;
        Sequence = sequence;
        Kind = kind;
        Status = status;
        Render = render;
        Adjustments = [];
        RegenerationNotes = [];
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }

    /// <summary>평가 대상 revision — 기준은 활성, 후보는 Candidate layout.</summary>
    public Guid LayoutId { get; private set; }

    /// <summary>run 안의 순번 — 기준 1, 후보 2부터.</summary>
    public int Sequence { get; private set; }

    public SimilarityEvaluationKind Kind { get; private set; }
    public SimilarityEvaluationStatus Status { get; private set; }

    /// <summary>후보의 렌더 업로드 전에는 null.</summary>
    public EvaluationRenderArtifact? Render { get; private set; }

    /// <summary>평가에 쓴 프롬프트 버전 — 재현성 (FR-10과 같은 규칙).</summary>
    public Guid? PromptVersionId { get; private set; }

    public SimilarityScore? Score { get; private set; }

    public IReadOnlyList<SimilarityAdjustment> Adjustments { get; private set; }

    public IReadOnlyList<string> RegenerationNotes { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static SimilarityEvaluation CreateBaseline(
        Guid runId, Guid layoutId, EvaluationRenderArtifact render, DateTimeOffset now)
        => new(Guid.NewGuid(), runId, layoutId, sequence: 1,
            SimilarityEvaluationKind.Baseline, SimilarityEvaluationStatus.Pending, render, now);

    public static SimilarityEvaluation CreateCandidate(
        Guid runId, Guid layoutId, int sequence, DateTimeOffset now)
        => new(Guid.NewGuid(), runId, layoutId, sequence,
            SimilarityEvaluationKind.Candidate, SimilarityEvaluationStatus.AwaitingRender,
            render: null, now);

    /// <summary>후보 렌더 도착 — 한 번만. 덮어쓰면 평가와 렌더가 어긋난다.</summary>
    public void AttachRender(EvaluationRenderArtifact render)
    {
        if (Status != SimilarityEvaluationStatus.AwaitingRender)
        {
            throw new InvalidOperationException($"렌더 대기 상태가 아닙니다: {Status}");
        }

        Render = render;
        Status = SimilarityEvaluationStatus.Pending;
    }

    /// <summary>워커의 집기 — attempt 가 곧 재시도 이력이다 (§9.3).</summary>
    public void BeginAttempt(DateTimeOffset leaseUntil, DateTimeOffset now)
    {
        if (Status is not (SimilarityEvaluationStatus.Pending or SimilarityEvaluationStatus.Failed))
        {
            throw new InvalidOperationException($"집을 수 없는 상태입니다: {Status}");
        }

        _ = now;
        AttemptCount++;
        LeaseExpiresAt = leaseUntil;
        Status = SimilarityEvaluationStatus.Running;
    }

    public void Succeed(
        SimilarityScore score,
        IReadOnlyList<SimilarityAdjustment> adjustments,
        IReadOnlyList<string> regenerationNotes,
        Guid promptVersionId,
        DateTimeOffset now)
    {
        if (Status != SimilarityEvaluationStatus.Running)
        {
            throw new InvalidOperationException($"실행 중이 아닙니다: {Status}");
        }

        ValidateAdjustments(adjustments);
        ValidateNotes(regenerationNotes);

        _ = now;
        Score = score;
        Adjustments = [.. adjustments];
        RegenerationNotes = [.. regenerationNotes];
        PromptVersionId = promptVersionId;
        LeaseExpiresAt = null;
        Status = SimilarityEvaluationStatus.Succeeded;
    }

    public void Fail(DateTimeOffset now)
    {
        if (Status != SimilarityEvaluationStatus.Running)
        {
            throw new InvalidOperationException($"실행 중이 아닙니다: {Status}");
        }

        _ = now;
        LeaseExpiresAt = null;
        Status = SimilarityEvaluationStatus.Failed;
    }

    /// <summary>성공한 평가는 취소되지 않는다 — 이력이자 비용의 근거다 (§9.3).</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status is SimilarityEvaluationStatus.Succeeded or SimilarityEvaluationStatus.Canceled)
        {
            throw new InvalidOperationException($"취소할 수 없는 상태입니다: {Status}");
        }

        _ = now;
        LeaseExpiresAt = null;
        Status = SimilarityEvaluationStatus.Canceled;
    }

    private static void ValidateAdjustments(IReadOnlyList<SimilarityAdjustment> adjustments)
    {
        if (adjustments.Count > MaxAdjustments)
        {
            throw new ArgumentException($"보정 제안은 {MaxAdjustments}개까지입니다", nameof(adjustments));
        }

        foreach (var adjustment in adjustments)
        {
            if (adjustment.Reason.Length > MaxReasonLength)
            {
                throw new ArgumentException(
                    $"보정 사유는 {MaxReasonLength}자까지입니다", nameof(adjustments));
            }

            if (!double.IsFinite(adjustment.Confidence)
                || adjustment.Confidence is < 0 or > 1)
            {
                throw new ArgumentException("confidence 는 0..1 입니다", nameof(adjustments));
            }
        }
    }

    private static void ValidateNotes(IReadOnlyList<string> notes)
    {
        if (notes.Count > MaxNotes)
        {
            throw new ArgumentException($"재생성 노트는 {MaxNotes}개까지입니다", nameof(notes));
        }

        if (notes.Any(note => note.Length > MaxNoteLength))
        {
            throw new ArgumentException($"재생성 노트는 각 {MaxNoteLength}자까지입니다", nameof(notes));
        }
    }
}
