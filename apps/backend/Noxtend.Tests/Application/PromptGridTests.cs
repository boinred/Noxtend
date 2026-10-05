using Noxtend.Api.Contracts;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Persistence.InMemory;
using Noxtend.Tuning.Application.Prompts;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Application;

/// <summary>
/// prompt-category-axis §12.1 — 서버가 (단계 × 카테고리) 유효 활성 격자를 계산한다.
///
/// **폴백을 서버에서만 판정한다.** 프론트가 전용/기본 규칙을 재구현하면 §3.2 에서 기각한
/// 이중화가 다시 생긴다. 각 칸은 {전용 있음 / 기본으로 폴백 / 실행 불가} 중 하나이고,
/// 이 판정은 StartJobHandler 의 접수 차단과 같은 폴백 규칙(TuningPromptCatalog)을 탄다.
/// </summary>
public sealed class PromptGridTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-13T00:00:00Z");

    // 전용 활성이 있으면 그 칸은 전용 — 자기 버전을 가리킨다
    [Fact]
    public async Task Cell_WithDedicatedActive_IsDedicated()
    {
        var handler = await BuildAsync(
            (LlmOperationKind.Extract, AssetCategory.Character, 3));

        var cell = CellOf(await handler.HandleAsync(CancellationToken.None),
            LlmOperationKind.Extract, AssetCategory.Character);

        Assert.Equal(PromptCellStatus.Dedicated, cell.Status);
        Assert.Equal(3, cell.Version);
    }

    // 전용이 없고 기본만 있으면 폴백 — 기본 버전을 가리킨다
    [Fact]
    public async Task Cell_WithOnlyDefaultActive_FallsBack()
    {
        var handler = await BuildAsync(
            (LlmOperationKind.Extract, null, 5));

        var cell = CellOf(await handler.HandleAsync(CancellationToken.None),
            LlmOperationKind.Extract, AssetCategory.Character);

        Assert.Equal(PromptCellStatus.Fallback, cell.Status);
        Assert.Equal(5, cell.Version); // 폴백 대상인 기본 버전
    }

    // 전용도 기본도 없으면 실행 불가
    [Fact]
    public async Task Cell_WithNothing_IsUnavailable()
    {
        var handler = await BuildAsync(); // 아무 것도 심지 않는다

        var cell = CellOf(await handler.HandleAsync(CancellationToken.None),
            LlmOperationKind.Extract, AssetCategory.Object);

        Assert.Equal(PromptCellStatus.Unavailable, cell.Status);
        Assert.Null(cell.Version);
    }

    // 기본 열은 폴백이 없다 — 자기 활성이 있으면 전용, 없으면 실행 불가뿐
    [Fact]
    public async Task DefaultColumn_NeverFallsBack()
    {
        var handler = await BuildAsync(
            (LlmOperationKind.Extract, null, 1));

        var defaultCell = CellOf(await handler.HandleAsync(CancellationToken.None),
            LlmOperationKind.Extract, category: null);

        Assert.Equal(PromptCellStatus.Dedicated, defaultCell.Status);
    }

    // 격자는 모든 단계 × (기본 + 세 카테고리) 를 덮는다 — 화면이 빠진 칸을 만들지 않는다
    [Fact]
    public async Task Grid_CoversEveryKindAndColumn()
    {
        var rows = await (await BuildAsync()).HandleAsync(CancellationToken.None);

        // LLM 을 부르는 슬롯 전부 — 파이프라인 다섯(재서술 포함) + 유사도 평가.
        // Reconstruct 는 없다 (§7.1·§15.1)
        Assert.Equal(6, rows.Count);
        foreach (var row in rows)
        {
            var categories = row.Columns.Select(c => c.Category).ToList();
            Assert.Contains((AssetCategory?)null, categories);
            Assert.Contains(AssetCategory.Character, categories);
            Assert.Contains(AssetCategory.Object, categories);
            Assert.Contains(AssetCategory.Background, categories);
        }
    }

    // 같은 kind 의 카테고리가 여럿 활성이어도 각 칸이 독립으로 나온다 (Map 덮어쓰기 방지)
    [Fact]
    public async Task Cell_SameKindDifferentCategories_DoNotOverwrite()
    {
        var handler = await BuildAsync(
            (LlmOperationKind.Extract, AssetCategory.Character, 1),
            (LlmOperationKind.Extract, AssetCategory.Background, 2));

        var rows = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(PromptCellStatus.Dedicated,
            CellOf(rows, LlmOperationKind.Extract, AssetCategory.Character).Status);
        Assert.Equal(PromptCellStatus.Dedicated,
            CellOf(rows, LlmOperationKind.Extract, AssetCategory.Background).Status);
        // 전용을 안 심은 Object 는 기본이 없으니 실행 불가
        Assert.Equal(PromptCellStatus.Unavailable,
            CellOf(rows, LlmOperationKind.Extract, AssetCategory.Object).Status);
    }

    // 응답 계약(§2.4): 상태는 dedicated/fallback/unavailable, 기본 열 카테고리는 null
    [Fact]
    public async Task Response_WiresStatusAndNullDefaultCategory()
    {
        var handler = await BuildAsync((LlmOperationKind.Extract, null, 1));
        var response = PromptGridResponse.From(await handler.HandleAsync(CancellationToken.None));

        var extract = response.Rows.Single(r => r.Kind == "extract");
        var defaultCell = extract.Cells.Single(c => c.Category is null);
        var characterCell = extract.Cells.Single(c => c.Category == "character");

        Assert.Equal("dedicated", defaultCell.Status);
        Assert.Equal("fallback", characterCell.Status); // 기본이 있어 카테고리는 폴백
    }

    private static PromptGridCell CellOf(
        IReadOnlyList<PromptGridRow> rows, LlmOperationKind kind, AssetCategory? category)
        => rows.Single(r => r.Kind == kind).Columns.Single(c => c.Category == category).Cell;

    // 실제 어댑터(폴백 소유)로 조립한다 — 격자가 그 규칙을 재구현하지 않는지 검증하려면 진짜를 태워야 한다
    private static async Task<GetPromptGridHandler> BuildAsync(
        params (LlmOperationKind Kind, AssetCategory? Category, int Version)[] rows)
    {
        var repo = new InMemoryPromptVersionRepository();
        foreach (var (kind, category, version) in rows)
        {
            var v = PromptVersion.Create(kind, category, version, "system", "user", "{}", null, Now);
            v.Activate();
            await repo.AddAsync(v, CancellationToken.None);
        }

        await repo.SaveChangesAsync(CancellationToken.None);
        return new GetPromptGridHandler(new TuningPromptCatalog(repo), repo);
    }
}
