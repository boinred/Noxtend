using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;
using Noxtend.Tuning.Application.Prompts;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// prompt-category-axis §8-6·7·10·8c · slice 5 — 카테고리 축을 실제 SQL Server 로 고정한다.
///
/// 필터 유니크와 NULL 취급은 InMemory 가 재현하지 않는다. 활성 유일성·기본 행 보존·
/// 활성 전환 격리를 진짜 DB 에서 한 번 더 잡는다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PromptCategoryPersistenceTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(PromptCategoryPersistenceTests));

    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

    // 마이그레이션이 시드하는 카테고리 활성 행은 캐릭터 Extract·Decompose·Generate 셋뿐이다.
    //
    // character-studio slice 4 이전에는 "카테고리 있는 활성 시드가 하나도 없다" 였다. slice 4 가
    // 캐릭터 세 단계를 의도적으로 시드하면서 이 불변식을 좁혔다 — 이제 "그 셋 외에 다른 카테고리
    // 활성 시드는 없다" 를 고정한다. 훗날 시드가 실수로 다른 전용 프롬프트를 켜면 이 단정이 잡는다.
    // 캐릭터 시드 자체의 스코프·활성 유일성은 CharacterPromptSeedMigrationTests 가 지킨다.
    [Fact]
    public async Task MigratedCategorizedSeeds_AreTheCharacterTripletPlusSixBackgroundSlots()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var activeCount = await db.PromptVersions.CountAsync(p => p.IsActive);
        Assert.True(activeCount > 0);

        var categorized = await db.PromptVersions
            .Where(p => p.IsActive && p.Category != null)
            .Select(p => new { p.Kind, p.Category })
            .ToListAsync();

        // Character triplet and the six independent Background slots
        Assert.Equal(9, categorized.Count);
        Assert.DoesNotContain(categorized, row => row.Category == AssetCategory.Object);
        Assert.Equal(
            [LlmOperationKind.Extract, LlmOperationKind.Decompose, LlmOperationKind.Generate],
            categorized.Where(row => row.Category == AssetCategory.Character)
                .Select(row => row.Kind).OrderBy(k => k));
        Assert.Equal(
            [LlmOperationKind.Analyze, LlmOperationKind.Extract, LlmOperationKind.Decompose,
             LlmOperationKind.Generate, LlmOperationKind.AnalyzeSprites, LlmOperationKind.SimilarityEvaluate],
            categorized.Where(row => row.Category == AssetCategory.Background)
                .Select(row => row.Kind).OrderBy(kind => kind));
    }

    // §8-6 같은 (Kind, Category) 에 활성 둘은 DB 가 거부한다
    [Fact]
    public async Task RejectsSecondActiveInSameCategory()
    {
        await using var db = await FreshMigratedAndClearedAsync();

        db.PromptVersions.AddRange(
            Active(AssetCategory.Character, 1),
            Active(AssetCategory.Character, 2));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // §8-7 (Kind, NULL) 활성 둘도 거부된다 — SQL Server 는 NULL 을 같은 값으로 본다
    [Fact]
    public async Task RejectsSecondActiveInNullSlot()
    {
        await using var db = await FreshMigratedAndClearedAsync();

        db.PromptVersions.AddRange(Active(null, 1), Active(null, 2));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // §8-8c 활성 전환 격리를 실제 SQL Server 로 — 캐릭터를 켜도 기본 활성이 유지된다
    [Fact]
    public async Task ActivatingCharacter_KeepsDefaultActive_OnRealDb()
    {
        await using var db = await FreshMigratedAndClearedAsync();

        var background = Active(null, 1);
        var character = PromptVersion.Create(
            LlmOperationKind.Extract, AssetCategory.Character, 1, "system", "user", "{}", null, Now);
        db.PromptVersions.AddRange(background, character);
        await db.SaveChangesAsync();

        var repo = new EfPromptVersionRepository(db);
        await new ActivatePromptVersionHandler(repo).HandleAsync(character.Id, CancellationToken.None);

        var reloadedBackground = await db.PromptVersions.AsNoTracking()
            .FirstAsync(p => p.Id == background.Id);
        var reloadedCharacter = await db.PromptVersions.AsNoTracking()
            .FirstAsync(p => p.Id == character.Id);

        Assert.True(reloadedBackground.IsActive);   // 배경 회귀 없음
        Assert.True(reloadedCharacter.IsActive);
    }

    // §8-8c 역방향 — 기본 새 버전을 켜도 캐릭터 활성이 유지된다.
    //
    // **이 경로가 GetActiveAsync(kind, null) 을 타는 유일한 실DB 시나리오다** (Design §5.3).
    // null 파라미터가 [Category] IS NULL 로 번역돼 기본 슬롯만 내려야 한다 — 어긋나면
    // 배경 파이프라인 전체가 조용히 깨진다
    [Fact]
    public async Task ActivatingDefault_KeepsCharacterActive_OnRealDb()
    {
        await using var db = await FreshMigratedAndClearedAsync();

        var character = Active(AssetCategory.Character, 1);
        var backgroundV1 = Active(null, 1);
        var backgroundV2 = PromptVersion.Create(
            LlmOperationKind.Extract, null, 2, "system", "user", "{}", null, Now);
        db.PromptVersions.AddRange(character, backgroundV1, backgroundV2);
        await db.SaveChangesAsync();

        var repo = new EfPromptVersionRepository(db);
        await new ActivatePromptVersionHandler(repo).HandleAsync(backgroundV2.Id, CancellationToken.None);

        var reloadedCharacter = await db.PromptVersions.AsNoTracking().FirstAsync(p => p.Id == character.Id);
        var reloadedV1 = await db.PromptVersions.AsNoTracking().FirstAsync(p => p.Id == backgroundV1.Id);
        var reloadedV2 = await db.PromptVersions.AsNoTracking().FirstAsync(p => p.Id == backgroundV2.Id);

        Assert.True(reloadedCharacter.IsActive);    // 캐릭터 회귀 없음
        Assert.False(reloadedV1.IsActive);          // 기본 슬롯만 전환
        Assert.True(reloadedV2.IsActive);
    }

    private static PromptVersion Active(AssetCategory? category, int version)
    {
        var v = PromptVersion.Create(
            LlmOperationKind.Extract, category, version, "system", "user", "{}", null, Now);
        v.Activate();
        return v;
    }

    // 마이그레이션 시드가 남긴 행을 비워 테스트가 정확히 자기 행만 보게 한다
    private async Task<NoxtendDbContext> FreshMigratedAndClearedAsync()
    {
        var db = Context();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM PromptVersions");
        return db;
    }

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
