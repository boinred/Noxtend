using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 유사도 평가 프롬프트 시드. Design Ref: background-similarity-tuning §15.1 · NFR-05
///
/// **버전 번호는 고정값이 아니다** — 운영자가 이 슬롯에 이미 행을 발급했을 수 있다.
/// 시드는 그 슬롯의 MAX(Version)+1 을 받고, 다른 kind/category 는 건드리지 않는다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SimilarityPromptMigrationTests(SqlServerFixture sql)
{
    private readonly string connectionString =
        sql.FreshDatabase(nameof(SimilarityPromptMigrationTests));

    private const string BeforeThis = "20260901133727_SimilarityRuns";

    [Fact]
    public async Task MigratingUp_SeedsAnActiveBackgroundSlot()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var seeded = Assert.Single(await db.PromptVersions
            .Where(p => p.Kind == LlmOperationKind.SimilarityEvaluate && p.IsActive)
            .ToListAsync());

        Assert.Equal(AssetCategory.Background, seeded.Category);
        Assert.Contains("reference", seeded.System);
        Assert.Contains("six dimensions", seeded.System);
        // overall 을 요구하지 않는다 (D-05) — 스키마에 overall 키가 없다
        Assert.DoesNotContain("overall", seeded.JsonSchema);
    }

    /// <summary>운영자가 먼저 발급한 버전과 충돌하지 않는다 (NFR-05).</summary>
    [Fact]
    public async Task MigratingUp_AllocatesAfterOperatorCreatedVersions()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);

        var operatorRow = PromptVersion.Create(
            LlmOperationKind.SimilarityEvaluate, AssetCategory.Background,
            version: 5, "op-system", "op-user", "{}", null,
            new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero));
        db.PromptVersions.Add(operatorRow);
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();

        var seeded = Assert.Single(await db.PromptVersions
            .Where(p => p.Kind == LlmOperationKind.SimilarityEvaluate && p.IsActive)
            .ToListAsync());
        // 운영자 v5 → 시드 v1(6)·v2(7)·v3(8) — 활성은 늘 마지막 시드 하나다
        Assert.Equal(8, seeded.Version);
    }

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options);
}
