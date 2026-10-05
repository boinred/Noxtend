using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// B-10 — 분해 프롬프트 v3 전환 (D-09 · NFR-05).
///
/// **버전 번호를 고정값으로 박으면 배포가 죽는다.** 프롬프트 화면이 같은 표에 행을 발급하므로
/// 운영자가 이미 그 번호를 써 버린 환경이 있다. `structured-palette` 에서 실제로 겪었고,
/// 그때 `IX_PromptVersions_Kind_Version` 에 걸려 API 가 기동하지 못했다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class DecomposePromptMigrationTests(SqlServerFixture sql)
{
    // xUnit 이 테스트마다 클래스를 새로 만든다 — 이 필드가 곧 "테스트당 DB 하나" 다.
    // `Context()` 안에서 뽑으면 한 테스트가 서로 다른 DB 를 보게 된다
    private readonly string connectionString = sql.FreshDatabase(nameof(DecomposePromptMigrationTests));

    private static readonly DateTimeOffset Now = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);

    /// <summary>이 마이그레이션 바로 앞 — 여기까지 돌려 두고 운영자가 손댄 상황을 만든다.</summary>
    private const string BeforeDecomposePlacements = "20260811032007_PartPlacements";

    [Fact]
    public async Task MigratingUp_LeavesExactlyOneActiveDecomposePrompt()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var active = Assert.Single(await ActiveDecomposePrompts(db));

        Assert.Contains("placements", active.System, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>운영자가 이미 다음 번호를 써 버린 환경에서도 배포가 살아남는다.</summary>
    [Fact]
    public async Task MigratingUp_SurvivesVersionNumbersAnOperatorAlreadyUsed()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeDecomposePlacements);

        // 이 지점에는 Category 컬럼이 없다 — EF INSERT 는 [Category] 를 넣어 죽는다.
        // 그 시점 스키마에 맞춘 raw SQL 로 넣는다 (Design §5.2)
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO PromptVersions (Id, Kind, Version, [System], [User], JsonSchema, Note, IsActive, CreatedAt) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})",
            Guid.NewGuid(), nameof(LlmOperationKind.Decompose), 3, "운영자 실험", "user", "{}",
            "관리자 화면에서 만든 행", false, Now);

        await db.Database.MigrateAsync();

        var active = Assert.Single(await ActiveDecomposePrompts(db));
        Assert.Contains("placements", active.System, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, active.Version);
    }

    // 기본(NULL) 슬롯의 활성만 본다 — character-studio slice 4 가 캐릭터 Decompose 활성을
    // 따로 시드하므로, 카테고리로 스코프하지 않으면 이 기본 전환 테스트가 그 행까지 세어 깨진다
    private static Task<List<PromptVersion>> ActiveDecomposePrompts(NoxtendDbContext db)
        => db.PromptVersions
            .Where(p => p.Kind == LlmOperationKind.Decompose && p.Category == null && p.IsActive)
            .ToListAsync();

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
