namespace Noxtend.Domain.Ports;

/// <summary>
/// 지연 실행 예약 (generation-rate-limiting §3② 후속 — 리뷰 지적).
///
/// **`TaskExecution.Apply`가 백오프 시각을 세팅해도, 그 시각에 딱 맞춰 재확인해줄 장치가
/// 없으면 스위퍼 주기(기본 60초)에 종속된다** — 승인된 5초/30초 값이 실제로는 최대
/// 60초가 될 수 있다는 독립 리뷰 지적을 해결하는 자리다.
///
/// 스케줄러는 "언제·어떻게 나중에 돌릴지"만 알고, "무엇을 할지"는 모른다 — 재적재
/// 로직(`JobOrchestrator`)에 대한 지식이 이 포트에 새지 않는다.
///
/// **실패해도 유실이 아니다.** 프로세스가 재시작되면 예약된 콜백은 사라지지만, 스위퍼가
/// 여전히 안전망이라 최악의 경우도 이 장치가 없던 이전 상태와 같다 — 더 나빠지지 않는다.
/// </summary>
public interface IDelayedActionScheduler
{
    void Schedule(TimeSpan delay, Func<CancellationToken, Task> action);
}
