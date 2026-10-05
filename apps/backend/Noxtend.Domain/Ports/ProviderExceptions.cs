namespace Noxtend.Domain.Ports;

/// <summary>
/// 공급자가 계약을 어긴 응답을 냈다.
///
/// Design Ref: §6 — <c>PROVIDER_BAD_RESPONSE</c> 로 매핑된다.
///
/// 이 예외가 Domain 에 있는 이유는 <see cref="ILlmProvider"/> 계약의 일부이기 때문이다.
/// Infrastructure 에 두면 Application 이 이것을 잡기 위해 Infrastructure 를 참조해야 하고,
/// 그 순간 의존 방향이 뒤집힌다 (§9.2).
/// </summary>
public sealed class ProviderBadResponseException(string message) : Exception(message);

/// <summary>
/// 공급자 호출 자체가 실패했다 (인증·네트워크·상태 코드).
///
/// Design Ref: §4.2 #13 — **원문 메시지를 그대로 흘리지 않는다.** 키가 섞일 수 있다.
/// 어댑터가 정규화한 뒤 이 예외로 바꾼다.
///
/// **원문은 <paramref name="innerException"/> 으로 보존한다.** #13 은 클라이언트 응답에
/// 대한 규칙이고, 서버 로그는 다른 문제다. 원문까지 지우면 400 을 받았을 때 무엇이
/// 잘못됐는지 알 방법이 없다 — 실제 공급자 첫 관통에서 이 공백이 드러났다.
/// </summary>
public sealed class ProviderCallFailedException(
    string message,
    Exception? innerException = null,
    bool isTransient = false) : Exception(message, innerException)
{
    /// <summary>
    /// 다시 걸어볼 만한가.
    ///
    /// **영구 실패와 일시 실패를 가르지 않으면 둘 중 하나가 손해다.** 인증 실패나 크레딧
    /// 부족(4xx)은 몇 번을 걸어도 같으므로 재시도가 비용과 지연만 늘린다. 반대로
    /// 공급자 서버 오류(5xx)나 네트워크 끊김은 다시 걸면 대개 성공하는데, 한 번에
    /// 포기하면 10분짜리 작업을 남의 사정 때문에 버리게 된다.
    ///
    /// 실제로 OpenAI 가 500 을 한 번 냈을 때 작업 전체가 실패했다 — 그 구분이
    /// 없던 시절이다.
    /// </summary>
    public bool IsTransient { get; } = isTransient;
}
