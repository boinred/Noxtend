using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Persistence;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// review-gate-staged 사이클 1 — 서술 출처 컬럼과 서술 단계 롤백 이관.
///
/// **Down 이 서술 단계를 Boxes 로 옮기지 않으면** 옛 코드가 모르는 값에서 예외가 나거나,
/// Approved 로 옮길 경우 생성 0장으로 성공 확정된다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class DescriptionSourceMigrationTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(DescriptionSourceMigrationTests));

    // 이 마이그레이션 바로 앞
    private const string BeforeDescriptionSource = "20260913172047_ReviewPhaseColumn";

    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    // Down — 서술 단계 작업은 상자 검수 대기로, 다른 단계는 그대로
    [Fact]
    public async Task MigratingDown_MovesDescriptionsPhaseBackToBoxes()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var descriptionsId = Guid.NewGuid();
        var approvedId = Guid.NewGuid();
        await InsertJobAsync(db, descriptionsId, nameof(ReviewPhase.Descriptions));
        await InsertJobAsync(db, approvedId, nameof(ReviewPhase.Approved));

        await db.Database.MigrateAsync(BeforeDescriptionSource);

        Assert.Equal(nameof(ReviewPhase.Boxes), await PhaseAsync(db, descriptionsId));
        Assert.Equal(nameof(ReviewPhase.Approved), await PhaseAsync(db, approvedId));
    }

    // EF 모델이 서술 출처를 owned 파츠의 문자열 컬럼으로 매핑
    [Fact]
    public void Model_MapsDescriptionSourceAsString()
    {
        using var db = Context();
        var partType = db.Model.FindEntityType(typeof(AssetPart))!;

        var source = partType.FindProperty(nameof(AssetPart.DescriptionSource))!;
        Assert.True(partType.IsOwned());
        Assert.Equal(typeof(string), source.GetTypeMapping().Converter?.ProviderClrType);
        Assert.Equal(16, source.GetMaxLength());
    }

    private static Task InsertJobAsync(NoxtendDbContext db, Guid id, string phase)
        => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Jobs (Id, Category, CreatedAt, RequiresReview, ReviewPhase, SourceImageId, Status) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
            id, nameof(AssetCategory.Character), Now, true, phase, Guid.NewGuid(),
            nameof(JobStatus.PendingReview));

    private static async Task<string> PhaseAsync(NoxtendDbContext db, Guid id)
        => await db.Database
            .SqlQueryRaw<string>("SELECT [ReviewPhase] AS [Value] FROM Jobs WHERE Id = {0}", id)
            .SingleAsync();

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
