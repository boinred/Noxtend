using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Persistence;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// review-gate-staged 사이클 0 — 검수 승인 bool 을 단계 enum 으로 옮기는 마이그레이션.
///
/// **이관 순서가 틀리면 이미 승인된 작업이 검수 대기로 되돌아간다.** 컬럼을 먼저 지우면
/// 승인 값을 옮길 곳이 없고, 기본값만 믿으면 전부 Boxes 가 된다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ReviewPhaseMigrationTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(ReviewPhaseMigrationTests));

    // 이 마이그레이션 바로 앞
    private const string BeforeReviewPhase = "20260909150025_CharacterHintScopeWins";

    private static readonly DateTimeOffset Now = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);

    // 승인 작업은 Approved, 미승인 작업은 Boxes 로 옮겨지고 Down 은 bool 로 되돌린다
    [Fact]
    public async Task MigratingUpAndDown_PreservesApproval()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeReviewPhase);

        var approvedId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        await InsertJobAsync(db, approvedId, reviewApproved: true);
        await InsertJobAsync(db, pendingId, reviewApproved: false);

        await db.Database.MigrateAsync();

        Assert.Equal(nameof(ReviewPhase.Approved), await ScalarAsync(db, "ReviewPhase", approvedId));
        Assert.Equal(nameof(ReviewPhase.Boxes), await ScalarAsync(db, "ReviewPhase", pendingId));

        // Down — 직전 마이그레이션으로 되돌림
        await db.Database.MigrateAsync(BeforeReviewPhase);

        Assert.Equal("1", await ScalarAsync(db, "ReviewApproved", approvedId));
        Assert.Equal("0", await ScalarAsync(db, "ReviewApproved", pendingId));
    }

    // EF 모델이 단계를 문자열 컬럼으로 매핑하고 옛 bool 은 남기지 않는다
    [Fact]
    public void Model_MapsReviewPhaseAsStringAndDropsReviewApproved()
    {
        using var db = Context();
        var jobType = db.Model.FindEntityType(typeof(PipelineJob))!;

        var phase = jobType.FindProperty(nameof(PipelineJob.ReviewPhase))!;
        Assert.Equal(typeof(string), phase.GetTypeMapping().Converter?.ProviderClrType);
        Assert.Equal(32, phase.GetMaxLength());
        Assert.Null(jobType.FindProperty("ReviewApproved"));
    }

    // 직전 스키마 기준 raw SQL — 그 시점엔 ReviewPhase 가 없어 EF INSERT 를 쓸 수 없다
    private static Task InsertJobAsync(NoxtendDbContext db, Guid id, bool reviewApproved)
        => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Jobs (Id, Category, CreatedAt, RequiresReview, ReviewApproved, SourceImageId, Status) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
            id, nameof(AssetCategory.Character), Now, true, reviewApproved, Guid.NewGuid(),
            nameof(JobStatus.Running));

    // 이관 확인용 단일 값 조회 — bit·nvarchar 공통 비교를 위한 문자열 CAST
    private static async Task<string> ScalarAsync(NoxtendDbContext db, string column, Guid id)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT CAST([{column}] AS nvarchar(32)) FROM Jobs WHERE Id = '{id}'";
        var value = await command.ExecuteScalarAsync();
        return value switch
        {
            string s => s,
            _ => throw new InvalidOperationException($"{column} 값을 읽지 못했습니다: {value}"),
        };
    }

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
