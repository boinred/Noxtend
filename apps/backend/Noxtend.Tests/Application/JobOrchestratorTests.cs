using Noxtend.Application.Job;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Application;

/// <summary>
/// Design Ref: §8.2 #15~17
///
/// **#16 이 미래 대비다.** 이번 사이클의 단계는 하나뿐이지만 픽스처로 두 단계를 만들어
/// 오케스트레이터가 다음 단계를 적재하는지 검증한다. 분해 단계를 붙일 때 이 테스트가
/// 이미 있으면 회귀가 즉시 드러난다 (§8.2 주석).
/// </summary>
public sealed class JobOrchestratorTests
{
    /// <summary>공정 2개짜리 선형 작업. 단계가 늘었을 때의 모양을 지금 만들어 둔다.</summary>
    private static async Task<(PipelineFixture Fixture, PipelineJob Job)> TwoTaskJobAsync()
    {
        var fixture = new PipelineFixture();
        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), fixture.Clock.Now);

        var first = job.PlanTask(TaskKind.Extract, ordinal: 0);
        job.PlanTask(TaskKind.Extract, ordinal: 1, dependsOnTaskId: first.Id);

        await fixture.Jobs.AddAsync(job, CancellationToken.None);
        return (fixture, job);
    }

    // #15 — 다음 공정 없음 → 작업 종료
    [Fact]
    public async Task CompletesJob_WhenNoTaskRemains()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();

        // 마지막 공정이 끝나야 작업이 끝난다 — 셋을 다 성공시킨다
        foreach (var task in job.Tasks.OrderBy(t => t.Ordinal))
        {
            task.Claim(fixture.Clock.Now, fixture.Options.Lease);
            task.Succeed(fixture.Clock.Now);
            await fixture.Orchestrator.OnTaskCompletedAsync(job, CancellationToken.None);
        }

        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public async Task CompletesJob_FromLatestPersistedTaskStates()
    {
        var fixture = new PipelineFixture();

        // 마지막 워커의 오래된 스냅샷
        var stale = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), fixture.Clock.Now);
        var staleFirst = stale.PlanTask(TaskKind.Generate, ordinal: 0);
        stale.PlanTask(TaskKind.Generate, ordinal: 1);
        staleFirst.Claim(fixture.Clock.Now, fixture.Options.Lease);
        staleFirst.Succeed(fixture.Clock.Now);
        stale.MarkRunning();

        // 병렬 워커들이 모두 저장한 최신 스냅샷
        var latest = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), fixture.Clock.Now);
        var latestFirst = latest.PlanTask(TaskKind.Generate, ordinal: 0);
        var latestSecond = latest.PlanTask(TaskKind.Generate, ordinal: 1);
        latestFirst.Claim(fixture.Clock.Now, fixture.Options.Lease);
        latestFirst.Succeed(fixture.Clock.Now);
        latestSecond.Claim(fixture.Clock.Now, fixture.Options.Lease);
        latestSecond.Succeed(fixture.Clock.Now);
        latest.MarkRunning();

        var jobs = new PersistedSnapshotJobRepository(latest);
        var orchestrator = new JobOrchestrator(fixture.Queue, jobs, fixture.Clock);

        await orchestrator.OnTaskCompletedAsync(stale, CancellationToken.None);

        Assert.Equal(JobStatus.Succeeded, latest.Status);
    }

    // #16 — 다음 공정 있음 → Enqueue
    [Fact]
    public async Task EnqueuesNextTask_WhenDependencyIsSatisfied()
    {
        var (fixture, job) = await TwoTaskJobAsync();
        var first = job.Tasks[0];
        var second = job.Tasks[1];

        first.Claim(fixture.Clock.Now, fixture.Options.Lease);
        first.Succeed(fixture.Clock.Now);

        await fixture.Orchestrator.OnTaskCompletedAsync(job, CancellationToken.None);

        Assert.Contains(fixture.Queue.Enqueued, e => e.TaskId == second.Id);
        // 다음 공정이 남았으므로 작업은 아직 끝나지 않는다
        Assert.NotEqual(JobStatus.Succeeded, job.Status);
    }

    // #17 — 의존 미충족 공정은 적재하지 않는다
    [Fact]
    public async Task DoesNotEnqueueTask_WhoseDependencyHasNotSucceeded()
    {
        var (fixture, job) = await TwoTaskJobAsync();
        var second = job.Tasks[1];

        // 첫 공정이 아직 대기 중이다 — 두 번째는 준비되지 않았다
        await fixture.Orchestrator.StartAsync(job, CancellationToken.None);

        Assert.DoesNotContain(fixture.Queue.Enqueued, e => e.TaskId == second.Id);
        Assert.Contains(fixture.Queue.Enqueued, e => e.TaskId == job.Tasks[0].Id);
    }

    [Fact]
    public async Task DoesNotEnqueueRemainingTasks_WhenAnEarlierTaskFailed()
    {
        var (fixture, job) = await TwoTaskJobAsync();
        var first = job.Tasks[0];
        var second = job.Tasks[1];

        first.Claim(fixture.Clock.Now, fixture.Options.Lease);
        first.Fail("PROVIDER_CALL_FAILED", fixture.Clock.Now);

        await fixture.Orchestrator.OnTaskCompletedAsync(job, CancellationToken.None);

        // 실패한 작업의 뒷단계를 돌리는 것은 낭비이자 오염이다
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.DoesNotContain(fixture.Queue.Enqueued, e => e.TaskId == second.Id);
        Assert.Equal(TaskStatus.Pending, second.Status);
    }

    [Fact]
    public async Task StartEnqueuesTheFirstTask()
    {
        var fixture = new PipelineFixture();

        // StartJobHandler 가 오케스트레이터를 통해 적재한다 — 첫 공정만 특별 취급하지 않는다
        var job = await fixture.StartJobAsync();

        // 의존이 없는 첫 공정 하나만 들어간다 — 뒤 둘은 앞이 끝나야 준비된다
        Assert.Single(fixture.Queue.Enqueued);
        Assert.Equal(TaskKind.Analyze, fixture.Queue.Enqueued.First().Kind);
    }

    [Fact]
    public async Task DoesNotReadCategory_SoStudiosShareTheBackbone()
    {
        // Design Ref: §2.4 — 백본은 카테고리를 모른다. 캐릭터·소품·배경이 같은 경로를 탄다
        foreach (var category in Enum.GetValues<AssetCategory>())
        {
            var fixture = new PipelineFixture();
            var job = await fixture.StartJobAsync(category: category);

            Assert.Single(fixture.Queue.Enqueued);
            Assert.Equal(
                job.Tasks.First(t => t.Ordinal == 0).Id,
                fixture.Queue.Enqueued.First().TaskId);
        }
    }

    /// <summary>병렬 워커 밖 DB 경계의 최신 작업 스냅샷.</summary>
    private sealed class PersistedSnapshotJobRepository(PipelineJob latest) : IJobRepository
    {
        public Task AddAsync(PipelineJob job, CancellationToken ct) => Task.CompletedTask;

        public Task<PipelineJob?> GetAsync(Guid jobId, CancellationToken ct)
            => Task.FromResult<PipelineJob?>(latest);

        public Task<PipelineJob?> ReloadAsync(Guid jobId, CancellationToken ct)
            => Task.FromResult<PipelineJob?>(latest);

        public Task<PipelineJob?> GetByTaskAsync(Guid taskId, CancellationToken ct)
            => Task.FromResult<PipelineJob?>(latest);

        public Task<int> CountAsync(JobListFilter filter, AssetCategory? category, CancellationToken ct, ProductionMode? productionMode = null)
            => Task.FromResult(0);

        public Task<IReadOnlyList<PipelineJob>> ListAsync(
            JobListFilter filter,
            AssetCategory? category,
            int limit,
            CancellationToken ct,
            ProductionMode? productionMode = null)
            => Task.FromResult<IReadOnlyList<PipelineJob>>([latest]);

        public Task<IReadOnlyList<PipelineJob>> ListBySourceImageAsync(
            Guid sourceImageId,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PipelineJob>>([latest]);

        public Task<IReadOnlyList<PipelineJob>> ListSweepCandidatesAsync(
            DateTimeOffset now,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PipelineJob>>([latest]);

        public Task<GeneratedImage?> GetGeneratedImageAsync(Guid imageId, CancellationToken ct)
            => Task.FromResult<GeneratedImage?>(null);

        public Task<GeneratedMesh?> GetGeneratedMeshAsync(Guid meshId, CancellationToken ct)
            => Task.FromResult<GeneratedMesh?>(null);

        public Task<DeletedJobBlobs?> DeleteIfTerminalAsync(Guid jobId, CancellationToken ct)
            => Task.FromResult<DeletedJobBlobs?>(null);

        public Task<SpriteAcceptedRequest?> GetSpriteRequestAsync(Guid requestId, CancellationToken ct)
            => Task.FromResult<SpriteAcceptedRequest?>(null);

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
