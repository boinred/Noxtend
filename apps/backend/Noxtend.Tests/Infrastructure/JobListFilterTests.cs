using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 끝난 작업을 목록 조회가 끝난 것으로 보는가.
///
/// Design Ref: §2.2 · §9.3
///
/// **도메인과 저장소가 종료의 정의를 따로 들고 있다.** `PipelineJob.IsTerminal` 은 계산
/// 속성이라 SQL 로 번역되지 않아 저장소가 상태 목록을 따로 나열하는데, 부분 성공이
/// 생겼을 때 그 목록만 갱신되지 않았다.
///
/// 증상은 조용하다. 끝난 작업이 홈의 진행 중 목록에 영구히 남고, 스위퍼가 매번 후보로
/// 집어 든다. mesh 가 붙으면 부분 성공이 훨씬 흔해지므로 (§4.6) 먼저 고친다.
/// </summary>
public sealed class JobListFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task PartiallySucceededJob_IsListedAsTerminal()
    {
        await using var db = Database();
        var repository = new EfJobRepository(db);
        await Persist(db, repository);

        var terminal = await repository.ListAsync(JobListFilter.Terminal, null, 10, default);

        Assert.Equal(JobStatus.PartiallySucceeded, Assert.Single(terminal).Status);
    }

    [Fact]
    public async Task PartiallySucceededJob_IsNotListedAsActive()
    {
        await using var db = Database();
        var repository = new EfJobRepository(db);
        await Persist(db, repository);

        // 진행 중 목록에 남으면 사용자는 영원히 끝나지 않는 작업을 본다
        Assert.Empty(await repository.ListAsync(JobListFilter.Active, null, 10, default));
    }

    [Fact]
    public async Task PartiallySucceededJob_IsNotSweptAgain()
    {
        await using var db = Database();
        var repository = new EfJobRepository(db);
        await Persist(db, repository);

        // 스위퍼가 끝난 작업을 계속 집으면 매 주기마다 헛일을 한다
        Assert.Empty(await repository.ListSweepCandidatesAsync(Now, default));
    }

    /// <summary>이미지 넷 중 하나가 실패한 작업 — 부분 성공의 가장 흔한 모양이다.</summary>
    private static async Task Persist(NoxtendDbContext db, EfJobRepository repository)
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini-image");

        job.ApplyParts(["석조 다리"]);
        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        var generate = job.Tasks.Where(task => task.Kind == TaskKind.Generate).ToArray();
        foreach (var (task, index) in generate.Select((task, index) => (task, index)))
        {
            task.Claim(Now, Lease);
            if (index == 1)
            {
                task.Fail("GENERATION_EMPTY_RESPONSE", Now);
            }
            else
            {
                task.Succeed(Now);
            }
        }

        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);

        await repository.AddAsync(job, default);
        await db.SaveChangesAsync();
    }

    private static NoxtendDbContext Database()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
