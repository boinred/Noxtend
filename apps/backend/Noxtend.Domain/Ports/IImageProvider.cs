using Noxtend.Domain.Job;

namespace Noxtend.Domain.Ports;

/// <summary>
/// 이미지 생성 공급자 — 단계를 모르는 실행기.
///
/// Design Ref: §3.2 · §2.0 · Plan D-6
///
/// **<see cref="ILlmProvider"/> 와 나눈 이유는 반환 타입이다.** 저쪽은 JSON 문자열을
/// 돌려주고 이쪽은 바이트를 돌려준다. 한 인터페이스에 합치면 유니온이나 <c>object</c> 가
/// 생기고, <c>RecordingLlmProvider</c> 데코레이터가 두 경우를 분기하게 되어 사이클 #5 가
/// 세운 G-1("어댑터는 단계를 모른다")이 깨진다.
///
/// **저장하지 않는다.** 바이트를 돌려줄 뿐이고 Blob 에 넣는 것은 Application 이 한다 —
/// 어댑터가 저장소를 알면 그것이 Option A 의 실패다 (§2.0).
///
/// 호출이 장당 수십 초~분 걸리므로 <see cref="CancellationToken"/> 이 계약의 일부다.
/// </summary>
public interface IImageProvider
{
    Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct);
}

/// <summary>
/// 이미지 공급자에게 보내는 것 전부.
///
/// <paramref name="Context"/> 는 **어댑터가 쓰지 않는다** — 기록 데코레이터만 읽는다.
/// <see cref="LlmRequest"/> 와 같은 규약이다 (§2.3-4).
/// </summary>
/// <param name="Reference">
/// 참조 이미지 목록 (Plan D-5 · workstream B §5.1). 화풍·재질 일치가 조립의 전제다 —
/// 장면 명세만으로는 색조·붓질이 흔들린다. 빈 목록이면 텍스트만으로 그린다.
///
/// **순서가 프롬프트의 번호와 맞아야 한다.** 두 공급자 다 "이 이미지가 정면이다" 같은
/// 구조적 필드가 없어 참조는 순서 있는 목록으로만 가고, 어느 것이 정면·원본인지는
/// 프롬프트 문구가 <see cref="ReferenceImage.Role"/> 순서를 따라 말한다.
/// </param>
/// <param name="Size">기존 생성은 서버 고정값, sprite는 모델의 확인된 생성 크기.</param>
public sealed record ImageRequest(
    ImageCallContext Context,
    string Prompt,
    IReadOnlyList<ReferenceImage> Reference,
    string Size,
    ImageBackground? Background = null);

/// <summary>참조 이미지 한 장 + 그것이 무엇인지(§5.1).</summary>
public sealed record ReferenceImage(ImageContent Content, ReferenceRole Role);

public enum ImageBackground { Opaque, Transparent }

/// <summary>
/// 참조가 그리려는 파츠와 어떤 관계인가 — 프롬프트 문구를 고르는 근거 (workstream B §4.2).
/// </summary>
public enum ReferenceRole
{
    /// <summary>사용자가 올린 원본 전신.</summary>
    Original,

    /// <summary>같은 파츠의 정면 생성 결과 — 비정면 공정이 참조한다.</summary>
    FrontView,

    SpriteBase = 2,
}

/// <summary>
/// 이 호출이 어느 작업·공정·파츠·프롬프트 버전에 속하는가.
///
/// 기존 파츠 생성은 <paramref name="PartId"/> 를 유지하고 sprite는 null 이다.
/// 기록의 상관관계는 <paramref name="TaskId"/> 로 조회한다.
/// </summary>
public sealed record ImageCallContext(
    Guid JobId,
    Guid TaskId,
    Guid? PartId,
    Guid PromptVersionId,
    Guid ProviderConfigId,
    string Model,
    TaskKind Kind = TaskKind.Generate);

/// <summary>
/// 이미지 공급자 응답.
///
/// <paramref name="ImageCount"/> 는 **과금 단위**다 (§3.3 · Plan D-8). 지금은 항상 1 이지만
/// 공급자가 여러 장을 낼 수 있으므로 응답이 말하게 둔다 — 호출자가 1 이라고 가정하면
/// 그날 비용 집계가 조용히 틀린다.
/// </summary>
/// <param name="InputTokens">
/// 공급자가 청구한 입력 토큰. 안 주면 <c>null</c>.
///
/// **참조 원본이 여기 들어간다** — D-5 때문에 매 호출마다 원본을 보내므로 장당 정액으로는
/// 그 몫이 표현되지 않는다. 실측을 받아 적어야 실제 청구와 맞는다.
/// </param>
/// <param name="OutputTokens">생성 결과의 출력 토큰. 크기·품질이 여기 반영된다.</param>
/// <param name="RateLimitRemainingRequests">
/// 공급자가 응답 헤더로 알려준, 이번 분당 윈도우에 남은 요청 수(generation-rate-limiting §3①).
/// 헤더가 없으면 <c>null</c> — 0으로 채우면 항상 대기하는 쪽으로 오판하게 된다.
/// </param>
/// <param name="RateLimitResetAfter">이 윈도우가 초기화되기까지 남은 시간. 절대 시각이 아니라 상대 시간이다 — 어댑터는 시계를 모른다.</param>
/// <param name="RateLimitRemainingTokens">
/// 남은 토큰 수(TPM) — RPM 과 독립인 축이다. OpenAI 는 둘 중 먼저 닿는 쪽에서 429 를 낸다.
/// </param>
/// <param name="RateLimitResetTokensAfter">토큰 윈도우가 초기화되기까지 남은 시간(상대 시간).</param>
public sealed record ImageResult(
    byte[] Bytes,
    string ContentType,
    int ImageCount,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? RateLimitRemainingRequests = null,
    TimeSpan? RateLimitResetAfter = null,
    int? RateLimitRemainingTokens = null,
    TimeSpan? RateLimitResetTokensAfter = null);

/// <summary>
/// 설정 id + 모델 → 이미지 어댑터.
///
/// <see cref="ILlmProviderFactory"/> 와 같은 보안 경계다 — Application 은 id 만 넘기고
/// 평문 키를 본 적이 없다 (NFR-05).
/// </summary>
public interface IImageProviderFactory
{
    Task<IImageProvider> CreateAsync(Guid providerConfigId, string model, CancellationToken ct);
}
