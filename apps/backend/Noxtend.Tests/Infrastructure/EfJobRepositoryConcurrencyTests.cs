using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// RowVersion 충돌이 원래 EF Core 예외가 아니라 <see cref="ConcurrencyConflictException"/>로
/// 나가는지 확인한다(merge-gate 리뷰 F2). Application 계층은 EF Core 를 참조하지 않으므로,
/// 이 번역이 없으면 그 계층에서 충돌을 사용자용 응답으로 바꿀 방법이 없다.
/// </summary>
public sealed class EfJobRepositoryConcurrencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentSave_ThrowsConcurrencyConflictException_NotRawDbUpdateConcurrencyException()
    {
        var dbName = Guid.NewGuid().ToString();

        var options = new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini-image");
        job.ApplyParts(["칼"]);

        await using (var seedDb = new NoxtendDbContext(options))
        {
            var seedRepository = new EfJobRepository(seedDb);
            await seedRepository.AddAsync(job, CancellationToken.None);
            await seedRepository.SaveChangesAsync(CancellationToken.None);
        }

        // 다른 요청이 이 잡을 새 컨텍스트로 불러온다
        await using var dbB = new NoxtendDbContext(options);
        var repositoryB = new EfJobRepository(dbB);

        var jobB = await repositoryB.GetAsync(job.Id, CancellationToken.None);
        Assert.NotNull(jobB);

        // EF InMemory 공급자는 IsRowVersion() 값을 실제로 자동 생성하지 않는다(관계형
        // 공급자만 그렇다) — 그래서 "다른 요청이 먼저 저장해 RowVersion 이 바뀌었다"를
        // OriginalValue 를 직접 어긋나게 둬서 흉내낸다. 이건 EF 의 동시성 검사 메커니즘
        // 자체를 흉내내는 것이지, 실제 자동 생성 여부와는 무관하다
        dbB.Entry(jobB!).Property("RowVersion").OriginalValue = new byte[] { 1, 2, 3, 4 };

        jobB!.Cancel(Now);

        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => repositoryB.SaveChangesAsync(CancellationToken.None));

        Assert.IsType<DbUpdateConcurrencyException>(ex.InnerException);
    }
}

/// <summary>
/// 실제 SQL Server 에서 파츠당 `Reconstruct` 유니크 제약 위반이 정확히 번역되는지
/// 확인한다(merge-gate 2차 리뷰 B1) — EF InMemory 는 필터 유니크 인덱스를 강제하지
/// 않으므로 위 클래스로는 이 경로(F2의 find-or-create "create" 경합)를 못 본다.
///
/// **왜 번호를 좁혔는지가 이 테스트의 핵심이다.** `EfJobRepository.SaveChangesAsync`가
/// 한때 `DbUpdateException`을 통째로 번역했었는데, 그러면 진짜 스키마 버그도 "동시
/// 충돌"로 둔갑해 로그 없이 삼켜졌다. SQL Server 유니크 위반 번호(2601/2627)만 좁혀서
/// 번역해야 한다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class EfJobRepositorySqlServerConcurrencyTests(SqlServerFixture sql)
{
    private readonly string connectionString =
        sql.FreshDatabase(nameof(EfJobRepositorySqlServerConcurrencyTests));

    private static readonly DateTimeOffset Now = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task ConcurrentReconstructCreate_ViolatesUniqueIndex_TranslatesToConcurrencyConflictException()
    {
        await Migrate();

        Guid jobId;
        Guid partId;
        Guid frontImageId;

        await using (var seedDb = Context())
        {
            var repo = new EfJobRepository(seedDb);

            var job = PipelineJob.Create(
                AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini-image");
            job.ApplyParts(["칼"]);
            var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
            decompose.Claim(Now, Lease);
            decompose.Succeed(Now);
            job.PlanReadyFollowUpTasks();

            partId = job.Parts.Single().Id;
            var front = job.Tasks.Single(
                t => t.Kind == TaskKind.Generate && t.ViewDirection == ViewDirection.Front);
            front.Claim(Now, Lease);
            job.AttachGeneratedImage(partId, front.Id, "front.png", "image/png", 100, Now);
            front.Succeed(Now);

            await repo.AddAsync(job, default);
            await repo.SaveChangesAsync(default);

            jobId = job.Id;
            frontImageId = job.LatestCurrentImage(partId, ViewDirection.Front)!.Value;
        }

        // 두 "요청"이 각자 다른 컨텍스트로 같은 잡을 불러온다 — 이 파츠엔 아직 Reconstruct
        // 공정이 없으므로, 둘 다 find-or-create의 "create" 경로를 탄다(ReplanMeshInputs)
        await using var dbA = Context();
        await using var dbB = Context();
        var repoA = new EfJobRepository(dbA);
        var repoB = new EfJobRepository(dbB);

        var jobA = await repoA.GetAsync(jobId, default);
        var jobB = await repoB.GetAsync(jobId, default);

        jobA!.ReplanMeshInputs(partId, Guid.NewGuid(), "model", new MeshInputSet(frontImageId), Now);
        jobB!.ReplanMeshInputs(partId, Guid.NewGuid(), "model", new MeshInputSet(frontImageId), Now);

        // A 가 먼저 커밋한다 — 이제 이 파츠에 Reconstruct 공정이 하나 생겼다
        await repoA.SaveChangesAsync(default);

        // B 는 자기 사본 기준으로는 "새로 만드는 것"이라 두 번째 Reconstruct 행을 INSERT
        // 하려다 필터 유니크 인덱스(Kind='Reconstruct', 파츠당 하나)에 걸린다
        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => repoB.SaveChangesAsync(default));

        var sqlEx = Assert.IsType<SqlException>(ex.InnerException!.InnerException);
        Assert.True(sqlEx.Number is 2601 or 2627);
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
