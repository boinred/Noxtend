using Noxtend.Domain.Llm;

namespace Noxtend.Tuning.Domain.Call;

/// <summary>
/// LLM 호출 한 건의 기록.
///
/// Design Ref: §3.2 · FR-06·FR-07 — **성공과 실패를 모두 남긴다.**
///
/// 실패를 남기는 것이 특히 중요하다. 사이클 #4 에서 공정이 실패했을 때 남는 것이
/// `PROVIDER_CALL_FAILED` 코드뿐이라 원인을 알 방법이 없었고, 로깅을 붙여서야
/// 크레딧 부족임을 알았다. 로그는 흘러가지만 이 표는 남는다.
///
/// **`PromptVersionId` 가 필수(non-nullable)인 것이 계약이다.** 이것이 없으면
/// "이 결과가 어느 프롬프트에서 나왔나" 를 알 수 없고, 그러면 비교 자체가 성립하지 않는다.
/// </summary>
public sealed class LlmCall
{
    private LlmCall()
    {
        // EF Core 재구성용
    }

    private LlmCall(
        Guid id,
        Guid? jobId,
        Guid? taskId,
        Guid? similarityEvaluationId,
        LlmOperationKind kind,
        Guid promptVersionId,
        Guid providerConfigId,
        string model,
        string requestPayload,
        string? responsePayload,
        int? inputTokens,
        int? outputTokens,
        int? outputImages,
        int latencyMs,
        bool succeeded,
        string? failureReason,
        DateTimeOffset at,
        Guid? sourceGenerationId = null)
    {
        Id = id;
        JobId = jobId;
        TaskId = taskId;
        SimilarityEvaluationId = similarityEvaluationId;
        SourceGenerationId = sourceGenerationId;
        Kind = kind;
        PromptVersionId = promptVersionId;
        ProviderConfigId = providerConfigId;
        Model = model;
        RequestPayload = requestPayload;
        ResponsePayload = responsePayload;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        OutputImages = outputImages;
        LatencyMs = latencyMs;
        Succeeded = succeeded;
        FailureReason = failureReason;
        At = at;
    }

    public Guid Id { get; private set; }
    public Guid? JobId { get; private set; }
    /// <summary>상관관계는 공정·유사도 평가·원본 생성 중 정확히 하나 — DB check constraint 가 지킨다.</summary>
    public Guid? TaskId { get; private set; }

    public Guid? SimilarityEvaluationId { get; private set; }
    public Guid? SourceGenerationId { get; private set; }

    public LlmOperationKind Kind { get; private set; }

    /// <summary>재현성의 핵심 (FR-10). 실행 시점의 활성 버전이 여기 박힌다.</summary>
    public Guid PromptVersionId { get; private set; }

    public Guid ProviderConfigId { get; private set; }
    public string Model { get; private set; } = string.Empty;

    /// <summary>
    /// 렌더된 프롬프트 전문.
    ///
    /// **이미지는 담지 않는다** — `StoredImage` 에 이미 있고, 바이트를 여기 또 넣으면
    /// 표가 수십 배로 부푼다 (§7 S-3).
    ///
    /// **API 키가 들어올 경로가 없다** (§7 S-1). 데코레이터는 프롬프트·이미지·스키마만
    /// 보고, 키는 어댑터 생성자 안에서만 존재한다.
    /// </summary>
    public string RequestPayload { get; private set; } = string.Empty;

    /// <summary>실패했으면 null 일 수 있다.</summary>
    public string? ResponsePayload { get; private set; }

    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }

    /// <summary>
    /// 생성한 이미지 수. 텍스트 호출이면 <c>null</c> — **장당 과금의 곱수다** (사이클 #7 §3.3).
    ///
    /// 이미지 호출은 토큰 칸이 둘 다 비므로, 이 값이 없으면 단가 환산이 성립하지 않는다.
    /// </summary>
    public int? OutputImages { get; private set; }

    public int LatencyMs { get; private set; }

    /// <summary>
    /// **공급자 호출이 성공했는가** — 그 단계가 성공했는가가 아니다.
    ///
    /// Check 단계에서 이 구분이 오해를 부른다는 것이 드러났다 (G-5). 공급자가 유효한
    /// JSON 을 돌려줬는데 그 내용이 기대와 달라 파싱·유효성에서 실패하면, 호출은
    /// 성공이고 공정은 실패다. 실제로 `succeeded=true` 인 호출이 실패한 작업에 남았다.
    ///
    /// 기록 시점이 데코레이터라 그 이후의 해석 결과를 알 수 없다 — 알게 하려면
    /// 데코레이터가 파이프라인 내부를 알아야 하고, 그러면 분리한 의미가 없다.
    /// 대신 <see cref="TaskId"/> 로 공정 상태를 이어 붙이면 화면이 둘을 함께 보여줄 수 있다.
    /// </summary>
    public bool Succeeded { get; private set; }

    /// <summary>공급자 원문이 아니라 정규화된 사유다 (§4.2 #13 유지).</summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static LlmCall Success(
        Guid? jobId,
        Guid? taskId,
        Guid? similarityEvaluationId,
        LlmOperationKind kind,
        Guid promptVersionId,
        Guid providerConfigId,
        string model,
        string requestPayload,
        string responsePayload,
        int? inputTokens,
        int? outputTokens,
        int latencyMs,
        DateTimeOffset at,
        int? outputImages = null,
        Guid? sourceGenerationId = null)
        => new(Guid.NewGuid(), jobId, taskId, similarityEvaluationId, kind, promptVersionId, providerConfigId, model,
            requestPayload, responsePayload, inputTokens, outputTokens, outputImages, latencyMs,
            succeeded: true, failureReason: null, at, sourceGenerationId);

    public static LlmCall Failure(
        Guid? jobId,
        Guid? taskId,
        Guid? similarityEvaluationId,
        LlmOperationKind kind,
        Guid promptVersionId,
        Guid providerConfigId,
        string model,
        string requestPayload,
        string failureReason,
        int latencyMs,
        DateTimeOffset at,
        Guid? sourceGenerationId = null)
        => new(Guid.NewGuid(), jobId, taskId, similarityEvaluationId, kind, promptVersionId, providerConfigId, model,
            requestPayload, responsePayload: null, inputTokens: null, outputTokens: null,
            outputImages: null, latencyMs, succeeded: false, failureReason, at, sourceGenerationId);
}
