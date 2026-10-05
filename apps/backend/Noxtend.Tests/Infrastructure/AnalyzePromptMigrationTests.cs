using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// B-13 — Analyze 프롬프트 버전 전환을 **실제로 돌려** 확인한다.
///
/// `PromptVersions` 에는 종류별로 활성 행 하나만 허용하는 filtered unique index 가 있다.
/// 새 행을 먼저 넣고 옛 행을 끄면 그 인덱스에 걸려 배포가 죽는다 — 순서가 계약이라
/// 코드를 읽는 것만으로는 지켜졌는지 알 수 없다.
///
/// InMemory 공급자로는 마이그레이션이 돌지 않아 컨테이너를 띄운다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AnalyzePromptMigrationTests(SqlServerFixture sql)
{
    // xUnit 이 테스트마다 클래스를 새로 만든다 — 이 필드가 곧 "테스트당 DB 하나" 다.
    // `Context()` 안에서 뽑으면 한 테스트가 서로 다른 DB 를 보게 된다
    private readonly string connectionString = sql.FreshDatabase(nameof(AnalyzePromptMigrationTests));

    [Fact]
    public async Task MigratingUp_LeavesExactlyOneActiveDefaultAnalyzePrompt()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var active = await ActiveAnalyzePrompts(db);

        Assert.Equal(2, Assert.Single(active).Version);
    }

    /// <summary>
    /// 되돌리면 v1 이 다시 선다.
    ///
    /// **다만 프롬프트만 되돌리는 것은 롤백이 아니다** (Design §15.3). `AnalyzeStage` 는
    /// v2 모양만 파싱하므로, v1 이 다시 활성이 되면 그 응답은 전부 계약 위반이 된다.
    /// 여기서 보는 것은 마이그레이션이 데이터를 원래대로 되돌리는지 하나뿐이다.
    /// </summary>
    [Fact]
    public async Task MigratingDown_RestoresV1AsTheOnlyActivePrompt()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync(BeforeStructuredPalettePrompt);

        // 되돌린 지점에는 Category 컬럼이 없다 — EF 로 읽으면 SELECT 에 [Category] 가 껴
        // Invalid column name 으로 죽는다. Version 만 raw SQL 로 뽑는다
        var versions = await ActiveAnalyzeVersionsRaw(db);

        Assert.Equal(1, Assert.Single(versions));
    }

    /// <summary>
    /// 운영자가 관리자 화면에서 이미 같은 버전 번호를 써 버린 환경.
    ///
    /// **개발 DB 에서 실제로 배포가 죽었다.** 누군가 Analyze v2·v3 를 만들어 두었고
    /// (`Check: 실패 기록 검증용`, `G-1 검증`), 마이그레이션이 `Version = 2` 를 고정값으로
    /// 박은 탓에 `IX_PromptVersions_Kind_Version` 에 걸렸다.
    ///
    /// 버전 번호는 프롬프트 화면이 발급하는 값이라 마이그레이션이 소유할 수 없다.
    /// 단가표에서 같은 교훈을 얻고도 여기에 적용하지 않았다.
    /// </summary>
    [Fact]
    public async Task MigratingUp_SurvivesVersionNumbersAnOperatorAlreadyUsed()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeStructuredPalettePrompt);

        // 이 지점에는 Category 컬럼이 없다 — EF INSERT 는 [Category] 를 넣어 죽는다.
        // 그 시점 스키마에 맞춘 raw SQL 로 넣는다 (Design §5.2)
        await InsertPromptRaw(db, LlmOperationKind.Analyze, 2, "운영자 실험");
        await db.Database.MigrateAsync();

        // 배포가 살아남고, 활성인 것은 구조화 팔레트 프롬프트 하나여야 한다
        var active = Assert.Single(await ActiveAnalyzePrompts(db));
        Assert.Contains("hex", active.System, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, active.Version);
    }

    private static readonly DateTimeOffset Now =
        new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    private const string BeforeStructuredPalettePrompt = "20260810075802_ImageCatalogPrices";

    private static Task<List<PromptVersion>> ActiveAnalyzePrompts(NoxtendDbContext db)
        => db.PromptVersions
            .Where(p => p.Kind == LlmOperationKind.Analyze && p.Category == null && p.IsActive)
            .ToListAsync();

    // Category 컬럼이 없던 지점에서도 읽히게 raw SQL 로 활성 버전만 뽑는다.
    // 스칼라 SqlQuery 는 컬럼 이름이 Value 여야 한다
    private static Task<List<int>> ActiveAnalyzeVersionsRaw(NoxtendDbContext db)
        => db.Database
            .SqlQueryRaw<int>(
                "SELECT Version AS Value FROM PromptVersions WHERE Kind = {0} AND IsActive = 1",
                nameof(LlmOperationKind.Analyze))
            .ToListAsync();

    // Category 컬럼이 생기기 전 스키마에 맞춘 삽입. IsActive=0 으로 넣는다
    private static Task InsertPromptRaw(NoxtendDbContext db, LlmOperationKind kind, int version, string system)
        => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO PromptVersions (Id, Kind, Version, [System], [User], JsonSchema, Note, IsActive, CreatedAt) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})",
            Guid.NewGuid(), kind.ToString(), version, system, "user", "{}", "관리자 화면에서 만든 행", false, Now);

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
