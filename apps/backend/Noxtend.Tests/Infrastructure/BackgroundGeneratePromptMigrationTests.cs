using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 배경 Generate v2 전환 (download-view-consistency §3.4 · SC-06).
///
/// **배경 행은 이번이 처음이다** — 지금까지 배경은 (Generate, NULL) 기본 행을 폴백으로
/// 썼다. v2 는 Category='Background' 로 심어 배경만 갈아타고, 기본 행과 캐릭터 행은
/// 무접촉이어야 한다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class BackgroundGeneratePromptMigrationTests(SqlServerFixture sql)
{
    private readonly string connectionString =
        sql.FreshDatabase(nameof(BackgroundGeneratePromptMigrationTests));

    private static readonly DateTimeOffset Now = new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

    /// <summary>이 마이그레이션 바로 앞 — 운영자가 손댄 상황을 만들 지점.</summary>
    private const string BeforeThis = "20260818140000_CharacterPromptWorkstreamJ";

    [Fact]
    public async Task MigratingUp_ActivatesTheRotationContractForBackgroundOnly()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        // 배경 활성은 v2 하나 — 회전 계약이 실려 있다
        var background = Assert.Single(await ActiveGeneratePrompts(db, "Background"));
        Assert.Contains("reference image 1 is the FRONT view", background.System);

        // 기본(NULL) 행과 캐릭터 행은 무접촉이다 — 기본은 여전히 v1 활성
        var fallback = Assert.Single(await ActiveGeneratePrompts(db, null));
        Assert.DoesNotContain("reference image 1", fallback.System);
        Assert.Single(await ActiveGeneratePrompts(db, "Character"));
    }

    /// <summary>운영자가 이미 Background 행을 발급한 환경에서도 배포가 살아남는다 (B-08).</summary>
    [Fact]
    public async Task MigratingUp_SurvivesABackgroundRowAnOperatorAlreadyMade()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeThis);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO PromptVersions (Id, Kind, Category, Version, [System], [User], JsonSchema, Note, IsActive, CreatedAt) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9})",
            Guid.NewGuid(), nameof(LlmOperationKind.Generate), "Background", 1, "운영자 실험", "user", "{}",
            "관리자 화면에서 만든 행", true, Now);

        await db.Database.MigrateAsync();

        // 활성은 v2 하나뿐이고, 번호는 운영자 행 다음이다
        var active = Assert.Single(await ActiveGeneratePrompts(db, "Background"));
        Assert.Equal(2, active.Version);
        Assert.Contains("reference image 1 is the FRONT view", active.System);
    }

    // ─── 설정 ───

    private async Task<List<PromptVersion>> ActiveGeneratePrompts(NoxtendDbContext db, string? category)
        => await db.PromptVersions
            .Where(p => p.Kind == LlmOperationKind.Generate && p.IsActive)
            .Where(p => category == null ? p.Category == null : p.Category.ToString() == category)
            .ToListAsync();

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure())
            .Options);
}
