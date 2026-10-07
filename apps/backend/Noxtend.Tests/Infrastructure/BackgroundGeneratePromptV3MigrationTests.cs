using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 배경 Generate v3 프롬프트 마이그레이션 계약.
///
/// 버전은 MAX(Version)+1 로 할당해야 운영자가 만든 프롬프트를 덮어쓰지 않고,
/// 바뀌는 것은 배경 Generate 슬롯 하나뿐이어야 한다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class BackgroundGeneratePromptV3MigrationTests(SqlServerFixture sql)
{
    private readonly string connectionString =
        sql.FreshDatabase(nameof(BackgroundGeneratePromptV3MigrationTests));

    private static readonly DateTimeOffset Now =
        new(2026, 10, 7, 5, 0, 0, TimeSpan.Zero);

    private const string BeforeThis = "20261006113049_SeedSpriteGeneratePrompt";

    private const string ThisMigration = "20261007063456_BackgroundGeneratePromptV3";

    [Fact]
    public async Task MigratingUp_ActivatesTheViewDefinitionGeneratePrompt()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);

        // 다른 Generate 슬롯 불변 — 배경만 건드린다
        var untouchedIds = await OtherGenerateSlotIds(db);

        await db.Database.MigrateAsync(ThisMigration);

        var generate = Assert.Single(await ActiveBackgroundGenerate(db));
        Assert.Equal(SeedPrompts.BackgroundGenerateV3().System, generate.System);

        Assert.Equal(untouchedIds, await OtherGenerateSlotIds(db));
    }

    [Fact]
    public async Task MigratingUp_AllocatesAfterOperatorCreatedVersions()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);
        await InsertBackgroundGenerate(db, version: 9);

        await db.Database.MigrateAsync(ThisMigration);

        Assert.Equal(10, Assert.Single(await ActiveBackgroundGenerate(db)).Version);
    }

    [Fact]
    public async Task MigratingDown_RestoresThePreviousBackgroundRow()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);
        await InsertBackgroundGenerate(db, version: 5);
        await db.Database.MigrateAsync(ThisMigration);

        await db.Database.MigrateAsync(BeforeThis);

        Assert.Equal(5, Assert.Single(await ActiveBackgroundGenerate(db)).Version);
    }

    private static Task<List<PromptVersion>> ActiveBackgroundGenerate(NoxtendDbContext db)
        => db.PromptVersions
            .Where(prompt => prompt.Kind == LlmOperationKind.Generate &&
                prompt.Category == AssetCategory.Background &&
                prompt.IsActive)
            .ToListAsync();

    private static Task<List<Guid>> OtherGenerateSlotIds(NoxtendDbContext db)
        => db.PromptVersions
            .Where(prompt => prompt.IsActive &&
                prompt.Kind == LlmOperationKind.Generate &&
                (prompt.Category == null || prompt.Category != AssetCategory.Background))
            .Select(prompt => prompt.Id)
            .OrderBy(id => id)
            .ToListAsync();

    // 운영자가 만든 종류별 프롬프트 — 버전 충돌 검사의 재료. 활성 행은 슬롯당 하나라 시드 행을 먼저 끈다
    private static Task InsertBackgroundGenerate(NoxtendDbContext db, int version)
        => db.Database.ExecuteSqlRawAsync(
            "UPDATE PromptVersions SET IsActive = 0 WHERE Kind = {1} AND Category = {2}; " +
            "INSERT INTO PromptVersions " +
            "(Id, Kind, Category, Version, [System], [User], JsonSchema, Note, IsActive, CreatedAt) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9})",
            Guid.NewGuid(), LlmOperationKind.Generate.ToString(),
            AssetCategory.Background.ToString(), version,
            "operator system", "operator user", "{}", "operator row", true, Now);

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString, options => options.EnableRetryOnFailure())
            .Options);
}
