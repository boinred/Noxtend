using Noxtend.Application.Pipeline;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Llm;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Application;

/// <summary>
/// 독립 리뷰 지적 — 백오프(`NotBefore`)가 스위퍼 주기(60초)에 종속되던 문제를 고친다.
/// `TaskExecution.Apply`가 백오프를 걸 때 그 시각에 맞춰 재확인이 예약되는지, 그리고
/// 그 시각이 되면 실제로 재적재되는지를 검증한다.
/// </summary>
public sealed class RetryReenqueueSchedulingTests
{
    // 백오프를 걸 때, 그 지연 시간만큼 재확인이 예약돼야 한다 — 스위퍼(60초)에만 맡기면 안 된다
    [Fact]
    public async Task RetryableFailure_SchedulesAReenqueueCheckAtTheBackoffDelay()
    {
        var fixture = new PipelineFixture(FakeLlmProvider.Failing(
            new ProviderCallFailedException("500", isTransient: true)));
        var job = await fixture.StartJobAsync();

        await fixture.RunFirstStageAsync(job);

        var scheduled = Assert.Single(fixture.Scheduler.Scheduled);
        // 1차 실패 → 기준 지연 5초, 지터 ±25% = [3.75초, 6.25초]
        Assert.InRange(scheduled.Delay, TimeSpan.FromSeconds(3.75), TimeSpan.FromSeconds(6.25));
    }

    // 예약된 시각이 되면(시계를 그만큼 돌리고 예약을 발화시키면) 실제로 큐에 재적재된다
    [Fact]
    public async Task WhenScheduledDelayElapses_TheTaskIsReenqueued()
    {
        var fixture = new PipelineFixture(FakeLlmProvider.Failing(
            new ProviderCallFailedException("500", isTransient: true)));
        var job = await fixture.StartJobAsync();
        var analyze = job.Tasks.First(t => t.Kind == TaskKind.Analyze);

        await fixture.RunFirstStageAsync(job);
        Assert.Equal(TaskStatus.Pending, analyze.Status);

        var enqueuedBefore = fixture.Queue.Enqueued.Count(e => e.Kind == TaskKind.Analyze);
        var scheduled = Assert.Single(fixture.Scheduler.Scheduled);

        // 스위퍼가 돌기 훨씬 전, 백오프 시각이 지난 시점을 시뮬레이션한다
        fixture.Clock.Advance(scheduled.Delay);
        await fixture.Scheduler.FireAllAsync(CancellationToken.None);

        var enqueuedAfter = fixture.Queue.Enqueued.Count(e => e.Kind == TaskKind.Analyze);
        Assert.Equal(enqueuedBefore + 1, enqueuedAfter);
    }
}
