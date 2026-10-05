using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Infrastructure.Scheduling;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 실제 Task.Delay 배선이 맞는지 짧은 실제 지연으로 증명한다 — 로직 자체는
/// `RetryReenqueueSchedulingTests`(가짜 스케줄러, 실제 대기 없음)가 이미 검증했다.
/// </summary>
public sealed class BackgroundDelayedActionSchedulerTests
{
    [Fact]
    public async Task Schedule_RunsTheActionAfterTheDelay()
    {
        var scheduler = new BackgroundDelayedActionScheduler(
            NullLogger<BackgroundDelayedActionScheduler>.Instance);
        var signal = new TaskCompletionSource();

        scheduler.Schedule(TimeSpan.FromMilliseconds(30), ct =>
        {
            signal.SetResult();
            return Task.CompletedTask;
        });

        // 30ms 안엔 아직이어야 한다 — "예약 즉시 실행"이 아님을 확인
        var immediate = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromMilliseconds(5)));
        Assert.NotSame(signal.Task, immediate);

        // 여유를 두고 기다리면 실행돼 있어야 한다
        var completed = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(signal.Task, completed);
    }

    // 콜백이 예외를 던져도 프로세스(테스트 프로세스)를 죽이지 않는다 — 스위퍼가 안전망
    [Fact]
    public async Task Schedule_SwallowsExceptionsFromTheAction()
    {
        var scheduler = new BackgroundDelayedActionScheduler(
            NullLogger<BackgroundDelayedActionScheduler>.Instance);

        scheduler.Schedule(TimeSpan.FromMilliseconds(10), ct =>
            throw new InvalidOperationException("일부러 던짐"));

        // 예외가 여기까지 전파되지 않고, 테스트가 그냥 통과하면 성공
        await Task.Delay(TimeSpan.FromMilliseconds(100));
    }
}
