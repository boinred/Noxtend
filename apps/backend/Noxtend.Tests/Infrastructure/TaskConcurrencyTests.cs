using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Persistence;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 같은 공정을 두 워커가 동시에 들었을 때.
///
/// **실측에서 두 사본의 변경이 한 행에 섞였다.** 워커 하나는 3D 중복 삽입에 막혀
/// 실패를 썼고, 다른 하나는 성공을 썼는데, 남은 행은 `Succeeded` 이면서 실패 사유를
/// 달고 있었다 — EF 가 바뀐 열만 쓰기 때문에 두 쪽 변경이 어긋난 채 남은 것이다.
/// 화면이 그 필드를 읽으면 성공한 작업에 실패 딱지가 붙는다.
///
/// `MeshRuns` 는 같은 이유로 이미 동시성 토큰을 쓴다. `Tasks` 만 빠져 있었다.
/// 인메모리로는 이 경합을 볼 수 없다 — 사본이 하나뿐이라 섞일 것이 없다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class TaskConcurrencyTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(TaskConcurrencyTests));

    private static readonly DateTimeOffset Now = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>
    /// **나중 쓰기가 앞의 것을 모른 채 덮으면 안 된다.**
    ///
    /// 토큰이 있으면 두 번째 저장이 충돌로 막힌다. 없으면 조용히 섞인다.
    /// </summary>
    [Fact]
    public async Task TwoWorkers_CannotBothSaveTheSameTask()
    {
        await Migrate();
        var (jobId, taskId) = await SeedRunningTaskAsync();

        // 워커 둘이 각자 읽는다 — 서로의 변경을 모른다
        await using var first = Context();
        await using var second = Context();

        var a = await first.Jobs.SingleAsync(job => job.Id == jobId);
        var b = await second.Jobs.SingleAsync(job => job.Id == jobId);

        a.Tasks.Single(t => t.Id == taskId).Succeed(Now);
        b.Tasks.Single(t => t.Id == taskId).Fail("DbUpdateException", Now);

        await first.SaveChangesAsync();

        // 두 번째는 자기가 읽은 뒤 행이 바뀐 것을 알아야 한다
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    /// <summary>
    /// **성공한 공정에 실패 사유가 남지 않는다.**
    ///
    /// 실측에서 나온 바로 그 행의 모양이다 — 상태는 성공인데 사유가 붙어 있었다.
    /// </summary>
    [Fact]
    public async Task SucceededTask_KeepsNoFailureReason_AfterAConflict()
    {
        await Migrate();
        var (jobId, taskId) = await SeedRunningTaskAsync();

        await using (var loser = Context())
        {
            await using var winner = Context();

            var lost = await loser.Jobs.SingleAsync(job => job.Id == jobId);
            var won = await winner.Jobs.SingleAsync(job => job.Id == jobId);

            lost.Tasks.Single(t => t.Id == taskId).Fail("DbUpdateException", Now);
            won.Tasks.Single(t => t.Id == taskId).Succeed(Now);

            await winner.SaveChangesAsync();

            try
            {
                await loser.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // 진 쪽은 물러난다 — 워커가 실제로 하는 일이다
            }
        }

        await using var read = Context();
        var task = (await read.Jobs.SingleAsync(job => job.Id == jobId)).Tasks.Single(t => t.Id == taskId);

        Assert.Equal(TaskStatus.Succeeded, task.Status);
        Assert.Null(task.FailureReason);
    }

    // ─── 설정 ───

    private async Task<(Guid JobId, Guid TaskId)> SeedRunningTaskAsync()
    {
        await using var db = Context();

        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini");
        var task = job.PlanTask(TaskKind.Reconstruct, ordinal: 0);
        task.Claim(Now, Lease);

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        return (job.Id, task.Id);
    }

    private async Task Migrate()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
