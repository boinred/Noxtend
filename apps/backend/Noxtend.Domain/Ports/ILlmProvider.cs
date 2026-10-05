using Noxtend.Domain.Job;

namespace Noxtend.Domain.Ports;

/// <summary>
/// LLM 공급자 — 단계를 모르는 실행기.
///
/// Design Ref: §3.3 · G-1 — **이 시그니처가 단계 확장 비용을 결정한다.**
///
/// 사이클 #4 는 `ExtractAsync(image)` 였다. 단계가 셋이 되면서 그 모양이 무너진다 —
/// 단계별 메서드를 두면 넷째·다섯째 단계마다 이 인터페이스와 어댑터 2개를 함께 고쳐야
/// 하고, 그러면 #4 §2.3 의 "단계 추가는 행이 늘 뿐" 이 거짓이 된다.
///
/// 지금은 **프롬프트·이미지·스키마를 받아 JSON 문자열을 돌려주는 것**이 전부다.
/// 무엇을 묻고 응답을 어떻게 해석하는지는 Application 의 단계별 Stage 가 안다 (§9.2).
///
/// 호출이 10분 이상 걸릴 수 있으므로 <see cref="CancellationToken"/> 이 계약의 일부다 —
/// 취소는 협조적이며, 이 토큰이 끊기면 어댑터가 호출을 중단한다 (#4 §2.2).
/// </summary>
public interface ILlmProvider
{
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct);
}

/// <summary>
/// 공급자에게 보내는 것 전부.
///
/// <paramref name="Context"/> 는 **어댑터가 쓰지 않는다.** 내역 데코레이터
/// (<c>RecordingLlmProvider</c>)만 읽는다 — 기록에 필요한 식별자를 별도 통로로
/// 나르면 데코레이터가 그것을 알 방법이 없어진다 (§2.3-4).
/// </summary>
public sealed record LlmRequest(
    LlmCallContext Context,
    string System,
    string User,
    IReadOnlyList<LlmImage> Images,
    string JsonSchema)
{
    /// <summary>공통 요청의 이미지 상한 (§7.2) — 유사도 평가도 reference·render 두 장뿐이다.</summary>
    public const int MaxImages = 4;

    public IReadOnlyList<LlmImage> Images
    {
        get;
        init => field = value.Count <= MaxImages
            ? value
            : throw new ArgumentException($"이미지는 요청당 {MaxImages}장까지입니다", nameof(Images));
    } = Images.Count <= MaxImages
        ? Images
        : throw new ArgumentException($"이미지는 요청당 {MaxImages}장까지입니다", nameof(Images));
}

/// <summary>
/// 요청에 싣는 이미지 한 장 — 이름은 어댑터가 각 장 앞에 넣는 label 의 재료다 (§7.2).
/// 유사도 평가는 <c>reference</c>·<c>render</c> 순서로 정확히 2장을 보낸다.
/// </summary>
public sealed record LlmImage(string Name, ImageContent Content);

/// <summary>
/// 이 호출이 어느 작업·프롬프트 버전에 속하고 무엇과 상관되는가.
///
/// <see cref="PromptVersionId"/> 가 여기 있는 것이 재현성의 핵심이다 (FR-10).
///
/// 상관관계는 공정(<see cref="TaskId"/>) 또는 유사도 평가
/// (<see cref="SimilarityEvaluationId"/>) **정확히 하나**다 (§7.3) — 생성자를 닫고
/// factory 만 열어 잘못된 조합이 타입 수준에서 존재하지 않게 한다.
/// </summary>
public sealed record LlmCallContext
{
    private LlmCallContext(
        Guid jobId,
        Guid? taskId,
        Guid? similarityEvaluationId,
        Llm.LlmOperationKind kind,
        Guid promptVersionId,
        Guid providerConfigId,
        string model)
    {
        JobId = jobId;
        TaskId = taskId;
        SimilarityEvaluationId = similarityEvaluationId;
        Kind = kind;
        PromptVersionId = promptVersionId;
        ProviderConfigId = providerConfigId;
        Model = model;
    }

    public Guid JobId { get; }
    public Guid? TaskId { get; }
    public Guid? SimilarityEvaluationId { get; }
    public Llm.LlmOperationKind Kind { get; }
    public Guid PromptVersionId { get; }
    public Guid ProviderConfigId { get; }
    public string Model { get; }

    public static LlmCallContext ForTask(
        Guid jobId, Guid taskId, TaskKind kind,
        Guid promptVersionId, Guid providerConfigId, string model)
        => new(jobId, taskId, similarityEvaluationId: null,
            Llm.LlmOperation.FromTask(kind), promptVersionId, providerConfigId, model);

    public static LlmCallContext ForSimilarityEvaluation(
        Guid jobId, Guid similarityEvaluationId,
        Guid promptVersionId, Guid providerConfigId, string model)
        => new(jobId, taskId: null, similarityEvaluationId,
            Llm.LlmOperationKind.SimilarityEvaluate, promptVersionId, providerConfigId, model);
}

/// <summary>
/// 공급자 응답.
///
/// <paramref name="RawJson"/> 은 **해석하지 않은 원문**이다. 구조화 출력을 요구했으므로
/// JSON 이어야 하지만, 그것이 기대한 모양인지는 단계가 판단한다.
///
/// <paramref name="RateLimitRemainingRequests"/>·<paramref name="RateLimitResetAfter"/>·
/// <paramref name="RateLimitRemainingTokens"/>·<paramref name="RateLimitResetTokensAfter"/> 는
/// <see cref="ImageResult"/>와 같은 자리다(text-generation-rate-limiting §구현변경-2) —
/// 값을 저장·판단하지 않고 어댑터가 응답 헤더를 그대로 실어 보내기만 한다. RPM(요청)·
/// TPM(토큰) 은 OpenAI 가 독립으로 거는 축이라 둘 다 싣는다.
/// </summary>
public sealed record LlmResult(
    string RawJson,
    int? InputTokens,
    int? OutputTokens,
    int? RateLimitRemainingRequests = null,
    TimeSpan? RateLimitResetAfter = null,
    int? RateLimitRemainingTokens = null,
    TimeSpan? RateLimitResetTokensAfter = null);

/// <summary>
/// 공급자에게 넘기는 이미지. Domain 이 Stream 을 들고 있는 것이 아니라
/// 바이트와 형식만 갖는다 — 저장소 어휘가 도메인으로 새지 않는다.
/// </summary>
public sealed record ImageContent(byte[] Bytes, string ContentType);
