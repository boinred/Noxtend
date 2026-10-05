namespace Noxtend.Domain.Similarity;

/// <summary>run 의 생애 — Completed·Failed·Canceled 가 종료다 (§5.1).</summary>
public enum SimilarityRunStatus
{
    Evaluating = 0,
    ReadyForAdjustment = 1,
    AwaitingRender = 2,
    Completed = 3,
    Failed = 4,
    Canceled = 5,
}

/// <summary>
/// 유사도 실행 하나 — 기준 평가 1회 + 후보 반복.
///
/// Design Ref: background-similarity-tuning §5.1 · §9 · D-01
///
/// **제작 파이프라인과 별도 경계다.** `PipelineJob.Status` 와 `PipelineTask` 를 절대
/// 바꾸지 않는다 — 완료된 작업이 다시 진행 상태가 되면 화면·삭제·재시도 규칙이 흔들린다.
/// 반복 상한(1..3)이 유료 호출 폭주의 방어선: 총 호출 = 1 + MaxIterations ≤ 4.
/// </summary>
public sealed class SimilarityRun
{
    private SimilarityRun()
    {
        // EF Core 재구성용
        Model = string.Empty;
        IdempotencyKey = string.Empty;
    }

    private SimilarityRun(
        Guid id, Guid jobId, Guid providerConfigId, string model,
        int maxIterations, string idempotencyKey, DateTimeOffset now)
    {
        Id = id;
        JobId = jobId;
        ProviderConfigId = providerConfigId;
        Model = model;
        MaxIterations = maxIterations;
        CurrentIteration = 0;
        Status = SimilarityRunStatus.Evaluating;
        IdempotencyKey = idempotencyKey;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid JobId { get; private set; }
    public Guid ProviderConfigId { get; private set; }
    public string Model { get; private set; }

    /// <summary>후보 평가 횟수 상한 — 기본 1, 허용 1..3 (§5.1).</summary>
    public int MaxIterations { get; private set; }

    public int CurrentIteration { get; private set; }

    public SimilarityRunStatus Status { get; private set; }

    /// <summary>같은 시작 요청의 중복 접수 방어 — (JobId, Key) 유니크 (§10).</summary>
    public string IdempotencyKey { get; private set; }

    public string? FailureCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>상태 전이 경쟁 감지 — 채택 transaction 과 취소가 겹칠 수 있다.</summary>
    public byte[]? RowVersion { get; private set; }

    public bool IsTerminal => Status is SimilarityRunStatus.Completed
        or SimilarityRunStatus.Failed
        or SimilarityRunStatus.Canceled;

    /// <summary>총 유료 LLM 호출 상한 = 기준 1 + 후보 반복 (§5.1, 하드 제한 4).</summary>
    public int MaxCalls => 1 + MaxIterations;

    public static SimilarityRun Start(
        Guid jobId, Guid providerConfigId, string model,
        int maxIterations, string idempotencyKey, DateTimeOffset now)
    {
        if (maxIterations is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxIterations), maxIterations, "후보 반복은 1..3회입니다");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency-Key 가 필요합니다", nameof(idempotencyKey));
        }

        return new SimilarityRun(
            Guid.NewGuid(), jobId, providerConfigId, model, maxIterations, idempotencyKey, now);
    }

    /// <summary>평가 성공 후 보정 선택 대기 — 반복 여유가 있을 때만 (§9.2).</summary>
    public void MarkReadyForAdjustment(DateTimeOffset now)
    {
        Require(SimilarityRunStatus.Evaluating);
        Status = SimilarityRunStatus.ReadyForAdjustment;
        Touch(now);
    }

    /// <summary>후보 revision 생성 — 렌더는 브라우저만 만들 수 있어 업로드를 기다린다.</summary>
    public void BeginCandidate(DateTimeOffset now)
    {
        Require(SimilarityRunStatus.ReadyForAdjustment);
        if (CurrentIteration >= MaxIterations)
        {
            throw new InvalidOperationException(
                $"후보 반복을 모두 소진했습니다 ({CurrentIteration}/{MaxIterations})");
        }

        CurrentIteration++;
        Status = SimilarityRunStatus.AwaitingRender;
        Touch(now);
    }

    /// <summary>후보 렌더 도착 — 평가 재개. 화면을 떠나도 여기부터는 서버가 끝낸다 (§9.2).</summary>
    public void ResumeEvaluating(DateTimeOffset now)
    {
        Require(SimilarityRunStatus.AwaitingRender);
        Status = SimilarityRunStatus.Evaluating;
        Touch(now);
    }

    /// <summary>정상 종료 — 반복 소진, 거부 종료, 또는 사용자의 "현재 결과로 종료" (§9.3).</summary>
    public void Complete(DateTimeOffset now)
    {
        RequireNotTerminal();
        Status = SimilarityRunStatus.Completed;
        CompletedAt = now;
        Touch(now);
    }

    public void Fail(string code, DateTimeOffset now)
    {
        RequireNotTerminal();
        Status = SimilarityRunStatus.Failed;
        FailureCode = code;
        CompletedAt = now;
        Touch(now);
    }

    /// <summary>
    /// terminal 실패 후 사용자의 재시도 (§9.3) — 저장된 렌더로 유료 호출 1회를 다시 연다.
    /// 새 캡처도 새 평가 행도 없다. Failed 에서만 가능하다.
    /// </summary>
    public void Retry(DateTimeOffset now)
    {
        if (Status != SimilarityRunStatus.Failed)
        {
            throw new InvalidOperationException($"실패한 실행만 재시도할 수 있습니다: {Status}");
        }

        Status = SimilarityRunStatus.Evaluating;
        FailureCode = null;
        CompletedAt = null;
        Touch(now);
    }

    public void Cancel(DateTimeOffset now)
    {
        RequireNotTerminal();
        Status = SimilarityRunStatus.Canceled;
        CompletedAt = now;
        Touch(now);
    }

    private void Require(SimilarityRunStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"이 전이는 {expected} 에서만 가능합니다. 현재 상태: {Status}");
        }
    }

    private void RequireNotTerminal()
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"종료된 실행입니다: {Status}");
        }
    }

    private void Touch(DateTimeOffset now) => UpdatedAt = now;
}
