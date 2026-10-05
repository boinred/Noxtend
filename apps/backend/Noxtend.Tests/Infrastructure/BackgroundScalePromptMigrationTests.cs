using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Infrastructure;

/// <summary>Background Analyze/Extract numeric scale prompt migration contract.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class BackgroundScalePromptMigrationTests(SqlServerFixture sql)
{
    private readonly string connectionString =
        sql.FreshDatabase(nameof(BackgroundScalePromptMigrationTests));

    private static readonly DateTimeOffset Now =
        new(2026, 8, 25, 6, 30, 0, TimeSpan.Zero);

    private const string BeforeThis = "20260825005323_BackgroundGeneratePromptV2";

    // 이 마이그레이션까지만 올린다. 헤드까지 가면 뒤에 붙은 캐릭터 프롬프트
    // 마이그레이션이 Character 슬롯을 바꿔, "배경만 건드린다" 는 이 검사가 그 변경을
    // 위반으로 읽는다 — 검사 대상은 이 마이그레이션 하나다
    private const string ThisMigration = "20260825054133_BackgroundScaleCalibrationPrompts";

    [Fact]
    public async Task MigratingUp_ActivatesNumericScalePromptsForBackgroundOnly()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);

        // Unrelated active-slot baseline
        var untouchedIds = await db.PromptVersions
            .Where(prompt => prompt.IsActive &&
                (prompt.Category == null || prompt.Category == AssetCategory.Character) &&
                (prompt.Kind == LlmOperationKind.Analyze || prompt.Kind == LlmOperationKind.Extract))
            .Select(prompt => prompt.Id)
            .OrderBy(id => id)
            .ToListAsync();

        await db.Database.MigrateAsync(ThisMigration);

        var analyze = Assert.Single(await Active(db, LlmOperationKind.Analyze));
        var extract = Assert.Single(await Active(db, LlmOperationKind.Extract));
        Assert.Contains("heightMeters", analyze.JsonSchema);
        Assert.Contains("physically separable object", analyze.System);
        Assert.Contains("character-for-character", extract.System);

        // Default and Character slot isolation
        var currentUntouchedIds = await db.PromptVersions
            .Where(prompt => prompt.IsActive &&
                (prompt.Category == null || prompt.Category == AssetCategory.Character) &&
                (prompt.Kind == LlmOperationKind.Analyze || prompt.Kind == LlmOperationKind.Extract))
            .Select(prompt => prompt.Id)
            .OrderBy(id => id)
            .ToListAsync();
        Assert.Equal(untouchedIds, currentUntouchedIds);
    }

    [Fact]
    public async Task MigratingUp_AllocatesAfterOperatorCreatedVersions()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);
        await InsertBackgroundPrompt(db, LlmOperationKind.Analyze, version: 7);
        await InsertBackgroundPrompt(db, LlmOperationKind.Extract, version: 11);

        await db.Database.MigrateAsync(ThisMigration);

        Assert.Equal(8, Assert.Single(await Active(db, LlmOperationKind.Analyze)).Version);
        Assert.Equal(12, Assert.Single(await Active(db, LlmOperationKind.Extract)).Version);
    }

    [Fact]
    public async Task MigratingDown_RestoresPreviousBackgroundRows()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);
        await InsertBackgroundPrompt(db, LlmOperationKind.Analyze, version: 3);
        await InsertBackgroundPrompt(db, LlmOperationKind.Extract, version: 4);
        await db.Database.MigrateAsync(ThisMigration);

        await db.Database.MigrateAsync(BeforeThis);

        Assert.Equal(3, Assert.Single(await Active(db, LlmOperationKind.Analyze)).Version);
        Assert.Equal(4, Assert.Single(await Active(db, LlmOperationKind.Extract)).Version);
    }

    private static Task<List<PromptVersion>> Active(NoxtendDbContext db, LlmOperationKind kind)
        => db.PromptVersions
            .Where(prompt => prompt.Kind == kind &&
                prompt.Category == AssetCategory.Background &&
                prompt.IsActive)
            .ToListAsync();

    // Operator-authored categorized prompt fixture
    private static Task InsertBackgroundPrompt(NoxtendDbContext db, LlmOperationKind kind, int version)
        => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO PromptVersions " +
            "(Id, Kind, Category, Version, [System], [User], JsonSchema, Note, IsActive, CreatedAt) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9})",
            Guid.NewGuid(), kind.ToString(), AssetCategory.Background.ToString(), version,
            "operator system", "operator user", "{}", "operator row", true, Now);

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString, options => options.EnableRetryOnFailure())
            .Options);
}
