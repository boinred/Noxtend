using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Llm;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Application;

/// <summary>
/// Design Ref: §8.2 #18~21
///
/// 스위퍼가 "작업의 정본은 DB 다" 를 실제 보장으로 바꾼다 (§1.2 · R-10).
/// 여기가 통과해야 Redis 장애가 유실이 아니라 지연이 된다.
/// </summary>
public sealed class SweepStaleTasksHandlerTests
{
    // #18 — 리스 만료 Running → Pending 재적재
    [Fact]
    public async Task RequeuesTask_WhoseLeaseExpired()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        var task = job.Tasks[0];

        task.Claim(fixture.Clock.Now, fixture.Options.Lease);
        // 워커가 크래시했다. 리스만 만료된다
        fixture.Clock.Advance(fixture.Options.Lease + TimeSpan.FromSeconds(1));

        var report = await fixture.Sweep.HandleAsync(CancellationToken.None);

        Assert.Equal(1, report.Requeued);
        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Null(task.LeaseExpiresAt);
        // 접수 시 1건 + 회수 1건
        Assert.Equal(2, fixture.Queue.Enqueued.Count(e => e.TaskId == task.Id));
    }

    // review-gate-staged 사이클 0 — 매 스윕 후보인 검수 대기 작업의 종료 확정 방지.
    // 단계 가드 변경 시 회귀 방지선
    [Fact]
    public async Task PendingReviewJob_IsSweptButNotCompleted()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();

        var candidates = await fixture.Jobs.ListSweepCandidatesAsync(fixture.Clock.Now, CancellationToken.None);
        await fixture.Sweep.HandleAsync(CancellationToken.None);

        Assert.Contains(candidates, j => j.Id == job.Id);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Null(job.CompletedAt);
    }

    // review-gate-staged 사이클 1 T9 — 서술 단계 검수 대기 작업도 스윕에서 종료 확정되지 않음
    [Fact]
    public async Task DescriptionsPhaseJob_IsSweptButNotCompleted()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();
        await fixture.ApproveReview.HandleAsync(job.Id, CancellationToken.None);

        await fixture.Sweep.HandleAsync(CancellationToken.None);

        Assert.Equal(ReviewPhase.Descriptions, job.ReviewPhase);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Null(job.CompletedAt);
    }

    // #19 — Pending 미적재 → 재적재
    [Fact]
    public async Task RequeuesTask_ThatIsStillPending()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        var task = job.Tasks[0];

        // Redis 가 끊겨 적재가 실패했을 수 있다. 상태만으로는 구분할 수 없으므로 다시 넣는다 (§6)
        var report = await fixture.Sweep.HandleAsync(CancellationToken.None);

        Assert.Equal(1, report.Requeued);
        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Equal(2, fixture.Queue.Enqueued.Count(e => e.TaskId == task.Id));
    }

    // #20 — AttemptCount 초과 → Failed 확정
    [Fact]
    public async Task FailsTask_WhenAttemptLimitIsExceeded()
    {
        var fixture = new PipelineFixture(
            options: new JobOptions { LeaseSeconds = 120, LeaseRenewSeconds = 15, MaxAttempts = 2 });

        var job = await fixture.StartJobAsync();
        var task = job.Tasks[0];

        // 두 번 집혔다가 두 번 다 리스가 만료됐다
        task.Claim(fixture.Clock.Now, fixture.Options.Lease);
        task.ReleaseForRetry();
        task.Claim(fixture.Clock.Now, fixture.Options.Lease);
        fixture.Clock.Advance(fixture.Options.Lease + TimeSpan.FromSeconds(1));

        var report = await fixture.Sweep.HandleAsync(CancellationToken.None);

        // 무한히 되살리면 실패가 영원히 진행 중으로 보인다
        Assert.Equal(1, report.Failed);
        Assert.Equal(0, report.Requeued);
        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("재시도 한도를 초과했습니다", job.FailureReason);
    }

    // #21 — 종료 상태 공정은 건드리지 않는다
    [Fact]
    public async Task LeavesTerminalTasksAlone()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        var task = job.Tasks[0];

        task.Claim(fixture.Clock.Now, fixture.Options.Lease);
        task.Succeed(fixture.Clock.Now);
        await fixture.Orchestrator.OnTaskCompletedAsync(job, CancellationToken.None);

        var enqueuedBefore = fixture.Queue.Enqueued.Count;
        fixture.Clock.Advance(TimeSpan.FromHours(1));

        var report = await fixture.Sweep.HandleAsync(CancellationToken.None);

        // 성공한 결과를 되돌리면 스위퍼가 복구 장치가 아니라 오염원이 된다.
        // 사이클 #5: 앞 공정이 성공하면 다음 공정이 준비 상태가 되므로 1건이 적재된다 —
        // 되살아난 것은 **성공한 공정이 아니라 그다음 공정**이다
        Assert.Equal(1, report.Requeued);
        Assert.Equal(0, report.Failed);
        Assert.Equal(TaskStatus.Succeeded, task.Status);
        // 뒤 공정이 남아 있으므로 작업은 아직 끝나지 않았다
        Assert.False(job.IsTerminal);
        // 늘어난 1건은 되살아난 성공 공정이 아니라 **준비 상태가 된 다음 공정**이다
        Assert.Equal(enqueuedBefore + 1, fixture.Queue.Enqueued.Count);
        Assert.Equal(TaskKind.Extract, fixture.Queue.Enqueued.Last().Kind);
    }

    [Fact]
    public async Task CompletesRunningJob_WhenEveryTaskAlreadySucceeded()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();

        // 병렬 공정은 모두 저장됐지만 상위 상태 조정만 누락된 작업
        foreach (var task in job.Tasks)
        {
            task.Claim(fixture.Clock.Now, fixture.Options.Lease);
            task.Succeed(fixture.Clock.Now);
        }

        job.MarkRunning();

        var report = await fixture.Sweep.HandleAsync(CancellationToken.None);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(0, report.Requeued);
        Assert.Equal(0, report.Failed);
    }

    [Fact]
    public async Task LeavesCanceledJobsAlone()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        await fixture.Cancel.HandleAsync(job.Id, CancellationToken.None);

        var enqueuedBefore = fixture.Queue.Enqueued.Count;
        fixture.Clock.Advance(TimeSpan.FromHours(1));

        var report = await fixture.Sweep.HandleAsync(CancellationToken.None);

        // 취소된 작업을 되살리면 사용자의 결정이 뒤집힌다
        Assert.Equal(0, report.Requeued);
        Assert.Equal(JobStatus.Canceled, job.Status);
        Assert.Equal(enqueuedBefore, fixture.Queue.Enqueued.Count);
    }

    [Fact]
    public async Task DoesNotTouchTask_WhoseLeaseIsStillValid()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        var task = job.Tasks[0];

        task.Claim(fixture.Clock.Now, fixture.Options.Lease);
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));

        var report = await fixture.Sweep.HandleAsync(CancellationToken.None);

        // 살아 있는 워커에게서 공정을 뺏으면 같은 작업이 두 번 돈다
        Assert.Equal(0, report.Requeued);
        Assert.Equal(TaskStatus.Running, task.Status);
    }

    // workstream B §9-4 (독립 리뷰 지적) — 이전 테스트는 도메인에서 직접 Fail +
    // ReconcileFromTasks 를 불러 캐스케이드를 검증했는데, 그러면 SweepStaleTasksHandler
    // 자체의 배선(리스 만료+한도 초과 → task.Fail → job.ReconcileFromTasks) 이 실제로
    // 캐스케이드를 트리거하는지는 증명이 안 된다 — 핸들러 한쪽만 붙이면 새는 게
    // 리뷰 지적 2 의 요지였다. 여기서는 fixture.Sweep 을 직접 통과시킨다
    [Fact]
    public async Task Character_FrontFailsViaSweeper_CascadesSiblingsToFailed()
    {
        var fixture = new PipelineFixture(
            FakeLlmProvider.Succeeding("몸통"),
            options: new JobOptions { LeaseSeconds = 120, LeaseRenewSeconds = 15, MaxAttempts = 1 });
        var job = await fixture.FanOutAsync(AssetCategory.Character);

        var front = job.Tasks.Single(
            t => t.Kind == TaskKind.Generate && t.ViewDirection == ViewDirection.Front);
        front.Claim(fixture.Clock.Now, fixture.Options.Lease);
        // 리스 만료 + 한도(1) 초과 — 스위퍼가 재적재 대신 확정 실패로 내린다 (#20)
        fixture.Clock.Advance(fixture.Options.Lease + TimeSpan.FromSeconds(1));

        var report = await fixture.Sweep.HandleAsync(CancellationToken.None);

        Assert.Equal(1, report.Failed);
        var siblings = job.Tasks.Where(
            t => t.Kind == TaskKind.Generate && t.ViewDirection != ViewDirection.Front);
        Assert.All(siblings, t => Assert.Equal(TaskStatus.Failed, t.Status));
    }

    [Fact]
    public async Task RequeuedTaskCanBeRunToCompletion()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        var task = job.Tasks[0];

        task.Claim(fixture.Clock.Now, fixture.Options.Lease);
        fixture.Clock.Advance(fixture.Options.Lease + TimeSpan.FromSeconds(1));
        await fixture.Sweep.HandleAsync(CancellationToken.None);

        // 회수가 복구로 이어지는지 — Redis 장애가 지연으로 격하되는 것의 실제 의미다
        await fixture.Run.HandleAsync(task.Id, CancellationToken.None);

        Assert.Equal(TaskStatus.Succeeded, task.Status);
        Assert.Equal(2, task.AttemptCount);

        // 회수된 공정이 끝나면 나머지도 정상적으로 이어진다
        await fixture.RunAllStagesAsync(job);
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }
}
