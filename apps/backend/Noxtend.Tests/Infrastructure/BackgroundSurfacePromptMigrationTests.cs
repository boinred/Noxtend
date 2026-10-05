using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 배경 분해 표면 프롬프트 마이그레이션 계약 (#20 SC-06).
///
/// 버전은 MAX(Version)+1 로 할당해야 운영자가 만든 프롬프트를 덮어쓰지 않고,
/// 바뀌는 것은 배경 분해 슬롯 하나뿐이어야 한다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class BackgroundSurfacePromptMigrationTests(SqlServerFixture sql)
{
    private readonly string connectionString =
        sql.FreshDatabase(nameof(BackgroundSurfacePromptMigrationTests));

    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 5, 0, 0, TimeSpan.Zero);

    private const string BeforeThis = "20260906050329_SceneInstancePerAxisScale";

    private const string ThisMigration = "20260906051134_BackgroundSurfacePartsPrompt";

    [Fact]
    public async Task MigratingUp_ActivatesTheSurfaceAwareDecomposePrompt()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);

        // 다른 분해 슬롯 불변 — 배경만 건드린다
        var untouchedIds = await OtherDecomposeSlotIds(db);

        await db.Database.MigrateAsync(ThisMigration);

        var decompose = Assert.Single(await ActiveBackgroundDecompose(db));
        Assert.Contains("surface", decompose.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"surface\"", decompose.JsonSchema);

        Assert.Equal(untouchedIds, await OtherDecomposeSlotIds(db));
    }

    [Fact]
    public async Task MigratingUp_AllocatesAfterOperatorCreatedVersions()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);
        await InsertBackgroundDecompose(db, version: 9);

        await db.Database.MigrateAsync(ThisMigration);

        Assert.Equal(10, Assert.Single(await ActiveBackgroundDecompose(db)).Version);
    }

    [Fact]
    public async Task MigratingDown_RestoresThePreviousBackgroundRow()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);
        await InsertBackgroundDecompose(db, version: 5);
        await db.Database.MigrateAsync(ThisMigration);

        await db.Database.MigrateAsync(BeforeThis);

        Assert.Equal(5, Assert.Single(await ActiveBackgroundDecompose(db)).Version);
    }

    private static Task<List<PromptVersion>> ActiveBackgroundDecompose(NoxtendDbContext db)
        => db.PromptVersions
            .Where(prompt => prompt.Kind == LlmOperationKind.Decompose &&
                prompt.Category == AssetCategory.Background &&
                prompt.IsActive)
            .ToListAsync();

    private static Task<List<Guid>> OtherDecomposeSlotIds(NoxtendDbContext db)
        => db.PromptVersions
            .Where(prompt => prompt.IsActive &&
                prompt.Kind == LlmOperationKind.Decompose &&
                (prompt.Category == null || prompt.Category != AssetCategory.Background))
            .Select(prompt => prompt.Id)
            .OrderBy(id => id)
            .ToListAsync();

    // 운영자가 만든 종류별 프롬프트 — 버전 충돌 검사의 재료
    private static Task InsertBackgroundDecompose(NoxtendDbContext db, int version)
        => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO PromptVersions " +
            "(Id, Kind, Category, Version, [System], [User], JsonSchema, Note, IsActive, CreatedAt) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9})",
            Guid.NewGuid(), LlmOperationKind.Decompose.ToString(),
            AssetCategory.Background.ToString(), version,
            "operator system", "operator user", "{}", "operator row", true, Now);

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString, options => options.EnableRetryOnFailure())
            .Options);
}
