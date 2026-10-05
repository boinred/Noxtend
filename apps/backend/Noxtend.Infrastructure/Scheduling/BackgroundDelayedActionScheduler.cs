using Microsoft.Extensions.Logging;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Scheduling;

/// <summary>
/// <see cref="IDelayedActionScheduler"/>의 실제 구현 — 인메모리 타이머.
///
/// **프로세스가 재시작되면 예약이 사라진다.** 새 인프라(지연 큐 등)를 두지 않은 이유는
/// 스위퍼가 이미 안전망이기 때문이다 — 최악의 경우도 이 스케줄러가 없던 이전 상태와
/// 같다(§3② 리뷰 지적 대응). 정확한 시각을 지키는 건 "있으면 더 좋은" 최적화지,
/// 정확성을 좌우하는 필수 경로가 아니다.
/// </summary>
public sealed class BackgroundDelayedActionScheduler(
    ILogger<BackgroundDelayedActionScheduler> logger) : IDelayedActionScheduler
{
    public void Schedule(TimeSpan delay, Func<CancellationToken, Task> action)
    {
        // 발사 후 잊는다 — 호출자(TaskExecution.Apply)는 이 완료를 기다리지 않는다.
        // 실패해도 예외가 프로세스를 죽이면 안 되므로 여기서 끝까지 삼킨다
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);
                await action(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "예약된 재확인 실행 실패 — 스위퍼가 대신 처리한다");
            }
        });
    }
}
