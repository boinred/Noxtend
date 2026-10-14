using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 2D 분석 v2 프롬프트 마이그레이션 계약.
///
/// 버전은 MAX(Version)+1 로 할당해야 운영자가 만든 프롬프트를 덮어쓰지 않고,
/// 바뀌는 것은 AnalyzeSprites/Background 슬롯 하나뿐이어야 한다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SpriteAnalyzePromptMigrationTests(SqlServerFixture sql)
{
    private readonly string connectionString =
        sql.FreshDatabase(nameof(SpriteAnalyzePromptMigrationTests));

    private static readonly DateTimeOffset Now =
        new(2026, 10, 13, 5, 0, 0, TimeSpan.Zero);

    private const string BeforeThis = "20261009095327_PriceUpdateSnapshots";

    private const string ThisMigration = "ReviseSpriteAnalyzePrompt";

    [Fact]
    public async Task MigratingUp_ActivatesTheNormalizedBoundsPrompt()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);
        var untouchedIds = await OtherActiveSlotIds(db);

        await db.Database.MigrateAsync(await MigrationId(db));

        var analyze = Assert.Single(await ActiveSlot(db));
        var expected = SeedPrompts.AnalyzeSpritesV2();
        Assert.Equal(2, analyze.Version);
        Assert.Equal(expected.System, analyze.System);
        Assert.Equal(expected.User, analyze.User);
        Assert.Equal(expected.Schema, analyze.JsonSchema);
        Assert.Equal(expected.Note, analyze.Note);
        Assert.Equal(untouchedIds, await OtherActiveSlotIds(db));
    }

    [Fact]
    public async Task MigratingUp_AllocatesAfterOperatorCreatedVersions()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);
        await InsertOperatorRow(db, version: 9);

        await db.Database.MigrateAsync(await MigrationId(db));

        Assert.Equal(10, Assert.Single(await ActiveSlot(db)).Version);
    }

    [Fact]
    public async Task MigratingDown_RestoresThePreviousRow()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);
        await InsertOperatorRow(db, version: 5);
        await db.Database.MigrateAsync(await MigrationId(db));

        await db.Database.MigrateAsync(BeforeThis);

        Assert.Equal(5, Assert.Single(await ActiveSlot(db)).Version);
    }

    [Fact]
    public async Task V3Up_ActivatesRoleAndGridPrompt()
    {
        await using var db = Context();
        await db.Database.MigrateAsync("20261014064731_AddSpriteAssetRoles");
        var untouchedIds = await OtherActiveSlotIds(db);

        await db.Database.MigrateAsync(db.Database.GetMigrations().Single(m => m.EndsWith("_ReviseSpriteAnalyzePromptV3")));

        var analyze = Assert.Single(await ActiveSlot(db));
        var expected = SeedPrompts.AnalyzeSpritesV3();
        Assert.Equal(3, analyze.Version);
        Assert.Equal(expected.System, analyze.System);
        Assert.Equal(expected.User, analyze.User);
        Assert.Equal(expected.Schema, analyze.JsonSchema);
        Assert.Equal(expected.Note, analyze.Note);
        Assert.Equal(untouchedIds, await OtherActiveSlotIds(db));
    }

    [Fact]
    public async Task V3Up_AllocatesAfterOperatorVersions()
    {
        await using var db = Context();
        await db.Database.MigrateAsync("20261014064731_AddSpriteAssetRoles");
        await InsertOperatorRow(db, version: 9);

        await db.Database.MigrateAsync(db.Database.GetMigrations().Single(m => m.EndsWith("_ReviseSpriteAnalyzePromptV3")));

        Assert.Equal(10, Assert.Single(await ActiveSlot(db)).Version);
    }

    [Fact]
    public async Task V3Down_RestoresPreviousRow()
    {
        await using var db = Context();
        await db.Database.MigrateAsync("20261014064731_AddSpriteAssetRoles");
        await InsertOperatorRow(db, version: 5);
        await db.Database.MigrateAsync(db.Database.GetMigrations().Single(m => m.EndsWith("_ReviseSpriteAnalyzePromptV3")));

        await db.Database.MigrateAsync("20261014064731_AddSpriteAssetRoles");

        Assert.Equal(5, Assert.Single(await ActiveSlot(db)).Version);
    }

    // 접미사로 찾아 타임스탬프를 하드코딩하지 않는다
    private static Task<string> MigrationId(NoxtendDbContext db)
        => Task.FromResult(db.Database.GetMigrations().Single(m => m.EndsWith("_" + ThisMigration)));

    private static Task<List<PromptVersion>> ActiveSlot(NoxtendDbContext db)
        => db.PromptVersions
            .Where(prompt => prompt.Kind == LlmOperationKind.AnalyzeSprites &&
                prompt.Category == AssetCategory.Background &&
                prompt.IsActive)
            .ToListAsync();

    private static Task<List<Guid>> OtherActiveSlotIds(NoxtendDbContext db)
        => db.PromptVersions
            .Where(prompt => prompt.IsActive &&
                (prompt.Kind != LlmOperationKind.AnalyzeSprites || prompt.Category != AssetCategory.Background))
            .Select(prompt => prompt.Id)
            .OrderBy(id => id)
            .ToListAsync();

    // 운영자 행 — 활성 1행 유니크 인덱스 때문에 시드 행 선비활성화
    private static Task InsertOperatorRow(NoxtendDbContext db, int version)
        => db.Database.ExecuteSqlRawAsync(
            "UPDATE PromptVersions SET IsActive = 0 WHERE Kind = {1} AND Category = {2}; " +
            "INSERT INTO PromptVersions " +
            "(Id, Kind, Category, Version, [System], [User], JsonSchema, Note, IsActive, CreatedAt) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9})",
            Guid.NewGuid(), LlmOperationKind.AnalyzeSprites.ToString(),
            AssetCategory.Background.ToString(), version,
            "operator system", "operator user", "{}", "operator row", true, Now);

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString, options => options.EnableRetryOnFailure())
            .Options);
}
