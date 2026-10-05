using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// character-studio slice 4 · §D-04 — 캐릭터 프롬프트 시드를 실제 SQL Server 로 고정한다.
///
/// 🔴 **스코프 함정**(prompt-category-axis §6.1): 시드 SQL 이 (Kind, Category) 로 스코프하지
/// 않으면 기본(NULL) 활성까지 꺼지거나 유니크에 걸려 기동이 죽는다. 필터 유니크·NULL 취급은
/// InMemory 가 재현하지 않으므로 진짜 DB 에서 잡는다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class CharacterPromptSeedMigrationTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(CharacterPromptSeedMigrationTests));

    private static readonly DateTimeOffset Now = new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);

    /// <summary>이 시드 마이그레이션 바로 앞 — 카테고리 열은 있고 캐릭터 시드는 아직 없는 지점.</summary>
    private const string BeforeCharacterSeed = "20260812174123_CharacterGenderPartHints";

    // 캐릭터로 시드하는 세 단계. Analyze 는 포함되지 않는다 — 기본 폴백을 그대로 쓴다 (§D-04)
    private static readonly LlmOperationKind[] SeededKinds =
        [LlmOperationKind.Extract, LlmOperationKind.Decompose, LlmOperationKind.Generate];

    // 세 단계마다 캐릭터 활성 프롬프트가 정확히 하나씩 생긴다.
    // Extract: CharacterPromptSeed v1 → CharacterPromptTuning v2 →
    //          CharacterPromptWorkstreamDE v3 → CharacterPromptWorkstreamE5 v4(결정⑤) →
    //          CharacterPromptWorkstreamE5Trim v5(중복 정리) →
    //          CharacterPromptWorkstreamF v6(긴 신발 발목 분리) →
    //          CharacterPromptWorkstreamG v7(관절 경계 분리 일반화) →
    //          CharacterPromptWorkstreamI v8(벨트 흡수/갑옷 자동 분리, 택소노미 UI 변경) →
    //          CharacterPromptWorkstreamJ v9(중복 예시 정리) →
    //          CharacterPromptWorkstreamK v10(벨트 흡수 결정 반전 — 벨트도 갑옷처럼 자동 분리) →
    //          CharacterExtractJointSegmentNaming v11(workstream M — 중간 구간 네이밍 통일) →
    //          CharacterExtractSoftGarmentsNoJointSplit v12(workstream N — 소프트 의류 관절 분할 금지)
    // Decompose: CharacterPromptWorkstreamDE v4 → CharacterPromptWorkstreamE5Trim v5(중복 정리) →
    //            CharacterPromptWorkstreamH v6(occludedBy 제외 서술) →
    //            CharacterPromptWorkstreamJ v7(죽은 예시 교체, 안전장치 추가) →
    //            CharacterPromptWorkstreamK v8(벨트 occludedBy 예시 되살림) →
    //            CharacterPromptWorkstreamL v9(depthOrder 동률 tie-break)
    // Generate: CharacterPromptWorkstreamDE v4 → CharacterPromptWorkstreamF v5(머리카락 제외 강화) →
    //           CharacterPromptWorkstreamJ v6(죽은 belts 예시 제거) →
    //           CharacterPromptWorkstreamK v7(벨트 다시 별도 파츠 — 바지 파츠는 벨트 없이 그림)
    // 2026-09-04 CharacterHandPoseAndJointArmorWrap — 모델러 피드백으로 Decompose·Generate 가
    //            하나씩 늘었다. Decompose v11(관절 갑주 캡 서술),
    //            Generate v8(손목 아래 장갑의 편 손가락 자세 + 관절 갑주 캡 작화)
    // 2026-09-08 CharacterArmorAnyRegion (armor-any-region) — 세 단계 모두 하나씩 늘었다.
    //            Extract v13(상의·하의 열거 → 힌트된 어떤 파츠 위 원리로 일반화),
    //            Decompose v12·Generate v9(관절 캡 조건을 골든셋 부위 열거 → any joint 원리로
    //            일반화 + Generate 는 서술에 없는 겹침 표시 미작화 규칙 추가)
    // 2026-09-09 CharacterHintScopeWins (①-보강 2·3, 실 API 실측 F96BE0F7) — Extract 만 v14.
    //            베이스바디·머리/머리카락의 강제 반환 문구를 스코프 조건절로 바꾸고,
    //            벨트 자동 분리를 상의 제외 하의·원피스로 좁혔다.
    [Theory]
    [InlineData(LlmOperationKind.Extract, 14)]
    // workstream O 와 그 철회로 하나 늘었다 (9 → 10), 모델러 피드백(손 자세·관절 갑주)으로 다시 하나 늘었다 (10 → 11),
    // armor-any-region 으로 다시 하나 늘었다 (11 → 12)
    [InlineData(LlmOperationKind.Decompose, 12)]
    [InlineData(LlmOperationKind.Generate, 9)]
    public async Task MigratingUp_SeedsExactlyOneActiveCharacterPromptPerStage(LlmOperationKind kind, int expectedVersion)
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var active = await db.PromptVersions
            .Where(p => p.Kind == kind && p.Category == AssetCategory.Character && p.IsActive)
            .ToListAsync();

        var one = Assert.Single(active);
        Assert.Equal(expectedVersion, one.Version);
    }

    // 재시드된 Decompose·Generate 는 성별 실루엣 문구를 담는다 (재정의)
    [Theory]
    [InlineData(LlmOperationKind.Decompose)]
    [InlineData(LlmOperationKind.Generate)]
    public async Task MigratingUp_ReseedsGenderSilhouetteWording(LlmOperationKind kind)
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var active = await db.PromptVersions
            .SingleAsync(p => p.Kind == kind && p.Category == AssetCategory.Character && p.IsActive);

        Assert.Contains("silhouette", active.System, StringComparison.OrdinalIgnoreCase);
    }

    // 🔴 스코프 함정 — 캐릭터 시드가 기본(NULL) 활성을 끄지 않는다
    [Fact]
    public async Task MigratingUp_KeepsDefaultActivePromptsIntact()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        // 기본 슬롯은 네 단계 모두 활성이 유지된다 (Analyze 는 애초에 캐릭터 시드가 없다)
        foreach (var kind in new[] { LlmOperationKind.Analyze, LlmOperationKind.Extract, LlmOperationKind.Decompose, LlmOperationKind.Generate })
        {
            var defaultActive = await db.PromptVersions
                .CountAsync(p => p.Kind == kind && p.Category == null && p.IsActive);

            Assert.Equal(1, defaultActive);
        }
    }

    // Analyze 는 캐릭터 전용 행을 시드하지 않는다 — 폴백으로 기본을 쓴다
    [Fact]
    public async Task MigratingUp_DoesNotSeedCharacterAnalyze()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var characterAnalyze = await db.PromptVersions
            .CountAsync(p => p.Kind == LlmOperationKind.Analyze && p.Category == AssetCategory.Character);

        Assert.Equal(0, characterAnalyze);
    }

    // 운영자가 이미 캐릭터 Extract 를 만들어 둔 환경에서도 배포가 살아남고 활성은 하나로 정리된다
    [Fact]
    public async Task MigratingUp_SurvivesOperatorCreatedCharacterPrompt()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeCharacterSeed);

        // 운영자가 관리자 화면에서 캐릭터 Extract v1 을 만들어 켜 둔 상황
        var operatorRow = PromptVersion.Create(
            LlmOperationKind.Extract, AssetCategory.Character, 1, "운영자 실험", "user", "{}", null, Now);
        operatorRow.Activate();
        db.PromptVersions.Add(operatorRow);
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();

        // 시드·재시드가 운영자 활성을 끄고 자기 행을 켠다 — 활성은 정확히 하나.
        // 운영자 v1 → CharacterPromptSeed v2 → CharacterPromptTuning v3 →
        // CharacterPromptWorkstreamDE v4 → CharacterPromptWorkstreamE5 v5 →
        // CharacterPromptWorkstreamE5Trim v6 → CharacterPromptWorkstreamF v7 →
        // CharacterPromptWorkstreamG v8 → CharacterPromptWorkstreamI v9 →
        // CharacterPromptWorkstreamJ v10 → CharacterPromptWorkstreamK v11 →
        // CharacterExtractJointSegmentNaming v12 →
        // CharacterExtractSoftGarmentsNoJointSplit v13 →
        // CharacterArmorAnyRegion v14 →
        // CharacterHintScopeWins v15
        var active = await db.PromptVersions
            .Where(p => p.Kind == LlmOperationKind.Extract && p.Category == AssetCategory.Character && p.IsActive)
            .ToListAsync();
        var one = Assert.Single(active);
        Assert.Equal(15, one.Version);
        Assert.Contains("separate", one.System, StringComparison.OrdinalIgnoreCase);
    }

    // character-tuning 재시드 문구가 실제로 활성에 담긴다 (A포즈·좌상단·머리 강화)
    [Fact]
    public async Task MigratingUp_ReseedsTuningWording()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var decompose = await db.PromptVersions
            .SingleAsync(p => p.Kind == LlmOperationKind.Decompose && p.Category == AssetCategory.Character && p.IsActive);
        Assert.Contains("TOP-LEFT corner", decompose.System, StringComparison.Ordinal);

        var generate = await db.PromptVersions
            .SingleAsync(p => p.Kind == LlmOperationKind.Generate && p.Category == AssetCategory.Character && p.IsActive);
        Assert.Contains("A-pose", generate.System, StringComparison.Ordinal);

        var extract = await db.PromptVersions
            .SingleAsync(p => p.Kind == LlmOperationKind.Extract && p.Category == AssetCategory.Character && p.IsActive);
        // ①-보강 2 로 조건절화됐다 — 스코프 안일 때만 머리/머리카락을 별개로 낸다
        Assert.Contains("When the head or the hair is in scope, they are two", extract.System, StringComparison.Ordinal);
    }

    // RewriteDescriptions 는 카테고리 무관(Category IS NULL) 기본 슬롯이다 — armor-any-region 의
    // ReseedDefault 가 캐릭터 슬롯(Category = 'Character')과 섞지 않고 NULL 슬롯만 스코프하는지 확인한다
    [Fact]
    public async Task MigratingUp_ReseedsRewriteDescriptionsDefaultSlotOnce()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var active = await db.PromptVersions
            .Where(p => p.Kind == LlmOperationKind.RewriteDescriptions && p.Category == null && p.IsActive)
            .ToListAsync();

        // 마이그레이션 3개(RewriteDescriptionsPromptSeed → …NewParts → …StrictSchema)가 DB
        // Version 1→3 을 채웠다 — SeedPrompts 의 "v4" 표기는 프롬프트 본문 개정 이력이지
        // DB Version 열이 아니다. 이번 재시드가 4번째 마이그레이션이라 DB Version 은 4.
        var one = Assert.Single(active);
        Assert.Equal(4, one.Version);
        Assert.Contains("part hints", one.System, StringComparison.OrdinalIgnoreCase);
    }

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
