using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Persistence;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 배치를 저장하고 되읽는다 (Design §4.3 · D-02 · D-03a).
///
/// **순서가 이 테스트의 이유다.** 배치를 JSON 으로 담았다면 배열 순서가 곧 순서였겠지만,
/// 별도 테이블은 행 순서를 보장하지 않는다. 정렬 키 없이 읽으면 대개는 넣은 순서로 나오다가
/// 어느 날 뒤섞이고, 그때 FR-09 가 깨진 것을 아무도 눈치채지 못한다.
///
/// InMemory 공급자로는 테이블 매핑을 확인할 수 없어 컨테이너를 띄운다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PartPlacementPersistenceTests(SqlServerFixture sql)
{
    // xUnit 이 테스트마다 클래스를 새로 만든다 — 이 필드가 곧 "테스트당 DB 하나" 다.
    // `Context()` 안에서 뽑으면 한 테스트가 서로 다른 DB 를 보게 된다
    private readonly string connectionString = sql.FreshDatabase(nameof(PartPlacementPersistenceTests));

    private static readonly DateTimeOffset Now = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// **일부러 좌표 순서를 뒤섞는다.** x 오름차순이면 DB 가 우연히 그 순서로 돌려줘도
    /// 정렬이 동작한 것처럼 보인다.
    /// </summary>
    private static readonly Bounds[] Placements =
    [
        new(0.66, 0.64, 0.04, 0.10),
        new(0.10, 0.60, 0.05, 0.12),
        new(0.42, 0.58, 0.03, 0.07),
    ];

    [Fact]
    public async Task PlacementsRoundTripInTheOrderTheModelReturnedThem()
    {
        await using (var seed = Context())
        {
            await seed.Database.MigrateAsync();
            seed.Jobs.Add(JobWithPlacements());
            await seed.SaveChangesAsync();
        }

        // 새 컨텍스트로 읽어야 변경 추적기가 아니라 DB 가 답한다
        await using var db = Context();
        // 소유 엔티티는 소유자와 함께 읽어야 한다 — 따로 투영하면 EF 가 추적할 수 없다
        var job = Assert.Single(await db.Jobs.Include(j => j.Parts).ToListAsync());
        var part = Assert.Single(job.Parts);

        Assert.Equal(Placements, part.Placements);
    }

    private static PipelineJob JobWithPlacements()
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini-3.1-flash-image");
        job.ApplyParts(["가로수"]);
        job.ApplyPartDetails([
            new PartDetail("가로수", "vegetation", "잎이 무성한 가로수", Placements, 1, []),
        ]);

        return job;
    }

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
