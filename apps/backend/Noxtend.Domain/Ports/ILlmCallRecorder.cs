namespace Noxtend.Domain.Ports;

/// <summary>
/// LLM 호출 내역 기록.
///
/// Design Ref: §3.3 · G-4 — **구현은 절대 던지지 않는다.**
///
/// 기록은 부수적 관심사다. 저장이 실패했다고 공정까지 실패하면, 관측을 위해 넣은 장치가
/// 시스템을 더 약하게 만든 셈이 된다. 그 규칙을 데코레이터가 아니라 여기 계약에 적어두는
/// 이유는, 나중에 다른 구현이 생겨도 같은 규칙을 따라야 하기 때문이다.
/// </summary>
public interface ILlmCallRecorder
{
    Task RecordAsync(LlmCallEntry entry, CancellationToken ct);
}

/// <summary>
/// 기록 한 건.
///
/// <paramref name="RequestPayload"/> 는 렌더된 프롬프트다 — **이미지는 담지 않는다.**
/// `StoredImage` 에 이미 있고, 바이트를 여기 또 넣으면 표가 수십 배로 부푼다 (§7 S-3).
///
/// API 키가 들어올 경로가 없다는 것이 구조로 보장된다 (§7 S-1): 데코레이터는
/// <see cref="LlmRequest"/> 만 보고, 키는 어댑터 생성자 안에서만 존재한다.
/// </summary>
/// <param name="OutputImages">
/// 생성한 이미지 수. 텍스트 호출이면 <c>null</c> 이다 (사이클 #7 §2.3 A-5).
///
/// **이미지 호출을 이 표에 합친 이유**: Option B 의 분리는 실행 경로에 대한 것이지
/// 관측 데이터에 대한 것이 아니다. "이 작업이 무엇을 보내 무엇을 받았나" 는 하나의
/// 질문이고 화면도 하나다 (FR-18) — 나누면 조회가 두 곳이 되고 합계가 두 번 계산된다.
///
/// **응답 바이트는 담지 않는다.** Blob 에 이미 있고, 담으면 표가 수백 배로 부푼다 (NFR-10).
/// </param>
public sealed record LlmCallEntry(
    LlmCallContext Context,
    string RequestPayload,
    string? ResponsePayload,
    int? InputTokens,
    int? OutputTokens,
    int LatencyMs,
    bool Succeeded,
    string? FailureReason,
    int? OutputImages = null);
