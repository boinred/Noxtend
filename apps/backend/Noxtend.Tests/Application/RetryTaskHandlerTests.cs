using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Image;
using Noxtend.Infrastructure.Llm;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Application;

/// <summary>
/// Design Ref: §8.1 #18 · §4.2 #3 — 파츠 하나만 다시 돌린다 (FR-08).
///
/// **비싼 성공분을 다시 만들지 않는 것이 목적이다.** 파츠 9개 중 2개가 실패했을 때
/// 작업을 통째로 다시 돌리면 성공한 7장을 버리고 그만큼 다시 지불한다.
/// </summary>
public sealed class RetryTaskHandlerTests
{
    /// <summary>
    /// 파츠 둘 중 하나만 실패한 부분 성공 상태를 만든다.
    /// </summary>
    /// <returns>
    /// <c>Heal</c> 을 부르면 그 뒤의 호출이 성공한다 — 재시도가 실제로 성공으로
    /// 이어지는지 보려면 공급자가 낫는 순간이 있어야 한다.
    /// </returns>
    private static async Task<(PipelineFixture Fixture, PipelineJob Job, Action Heal)>
        PartiallySucceededAsync()
    {
        var calls = 0;
        var healed = false;

        var fixture = new PipelineFixture(
            FakeLlmProvider.Succeeding("등대", "부두"),
            // 여덟 방향 중 마지막 하나만 실패시킨다 — 재시도 하나로 완주 가능한 상태
            imageProvider: FakeImageProvider.Throwing(
                () => healed || ++calls != 8
                    ? null
                    : new ProviderCallFailedException("402", isTransient: false)));

        var job = await fixture.FanOutAsync();
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToList())
        {
            await fixture.RunGenerationUntilTerminalAsync(task);
        }

        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        return (fixture, job, () => healed = true);
    }

    [Fact]
    public async Task ReopensTheJobAndRequeuesOnlyTheFailedTask()
    {
        var (fixture, job, _) = await PartiallySucceededAsync();
        var failed = job.Tasks.First(t => t.Status == TaskStatus.Failed);

        var before = fixture.Queue.Enqueued.Count;

        var result = await fixture.Retry.HandleAsync(job.Id, failed.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // 부분 성공은 종료 상태다 — 되돌리지 않으면 오케스트레이터가 이 공정을 적재하지 않는다
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Null(job.CompletedAt);

        Assert.Equal(TaskStatus.Pending, failed.Status);
        Assert.Null(failed.FailureReason);

        // 성공한 형제는 다시 걸리지 않는다 — 비싼 성공분을 다시 만들지 않는 것이 목적이다
        var requeued = fixture.Queue.Enqueued.Skip(before).ToList();
        Assert.Equal([failed.Id], requeued.Select(e => e.TaskId));
    }

    [Fact]
    public async Task ResetsTheAttemptBudget()
    {
        var (fixture, job, _) = await PartiallySucceededAsync();
        var failed = job.Tasks.First(t => t.Status == TaskStatus.Failed);

        await fixture.Retry.HandleAsync(job.Id, failed.Id, CancellationToken.None);

        // 자동 재시도 한도는 "공급자가 흔들리는가" 를 재는 값이라
        // 사용자의 명시적 결정과 예산을 나눠 쓰지 않는다
        Assert.Equal(0, failed.AttemptCount);
    }

    [Fact]
    public async Task PromotesThePartialJobToSucceeded_WhenTheRetryWorks()
    {
        var (fixture, job, heal) = await PartiallySucceededAsync();
        var failed = job.Tasks.First(t => t.Status == TaskStatus.Failed);

        await fixture.Retry.HandleAsync(job.Id, failed.Id, CancellationToken.None);
        heal();

        await fixture.RunGenerationUntilTerminalAsync(failed);

        // V-7 — 재시도가 성공하면 ReconcileFromTasks 가 부분 성공을 성공으로 올린다
        Assert.Equal(TaskStatus.Succeeded, failed.Status);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(8, job.GeneratedImages.Count);
    }

    [Fact]
    public async Task RejectsASucceededTask()
    {
        var (fixture, job, _) = await PartiallySucceededAsync();
        var succeeded = job.Tasks.First(
            t => t.Kind == TaskKind.Generate && t.Status == TaskStatus.Succeeded);

        var result = await fixture.Retry.HandleAsync(job.Id, succeeded.Id, CancellationToken.None);

        Assert.Equal(ErrorCode.TaskNotRetryable, result.ErrorCode);
    }

    [Fact]
    public async Task RejectsANonGenerationTask()
    {
        var fixture = new PipelineFixture(
            FakeLlmProvider.Failing(new ProviderCallFailedException("401", isTransient: false)));

        var job = await fixture.StartJobWithGenerationAsync();
        await fixture.RunAllStagesAsync(job);

        var analyze = job.Tasks.First(t => t.Kind == TaskKind.Analyze);
        Assert.Equal(TaskStatus.Failed, analyze.Status);

        var result = await fixture.Retry.HandleAsync(job.Id, analyze.Id, CancellationToken.None);

        // 앞 세 단계를 다시 돌리면 뒤의 결과가 전부 무의미해진다 — 장면이 바뀌면
        // 이미 그린 이미지들이 다른 장면의 것이 된다
        Assert.Equal(ErrorCode.TaskNotRetryable, result.ErrorCode);
    }

    [Fact]
    public async Task RejectsAnUnknownJob()
    {
        var fixture = new PipelineFixture();

        var result = await fixture.Retry.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ErrorCode.JobNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task RejectsAnUnknownTask()
    {
        var (fixture, job, _) = await PartiallySucceededAsync();

        var result = await fixture.Retry.HandleAsync(job.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ErrorCode.TaskNotRetryable, result.ErrorCode);
    }
}
