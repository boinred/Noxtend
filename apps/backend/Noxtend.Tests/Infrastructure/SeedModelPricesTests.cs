using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Image;
using Noxtend.Infrastructure.Llm;
using Noxtend.Tuning.Domain.Call;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 기존 이미지 모델 단가를 검증하고 미확인 sprite 단가는 null 로 유지한다.
///
/// 두 목록 다 손으로 관리되는데 서로를 모른다. 어긋나면 사용자는 아무 경고 없이 모델을
/// 고르고, 그 작업의 생성 공정이 통째로 "단가 미등록" 이 된다 — 실제로 파츠 9개 × 방향
/// 4개 = 36건이 그렇게 사라졌다. 화면은 정직하게 미등록이라 말하지만, 그때는 이미 돈을
/// 쓴 뒤다.
///
/// **텍스트 모델은 대상이 아니다.** <see cref="OpenAiModels"/> 와 <see cref="AnthropicModels"/>
/// 는 런타임에 공급자 API 로 목록을 받으므로 정적으로 셀 수 없다. 이미지 카탈로그만
/// 하드코딩된 허용목록이라 이 검사가 가능하다.
/// </summary>
public sealed class SeedModelPricesTests
{
    private const string UnconfirmedSpritePriceModel = "gpt-image-2.5-sunburst";

    /// <summary>단가 시행일 이후의 아무 시각 — 어느 행이 걸리는지가 아니라 걸리는지를 본다.</summary>
    private static readonly DateTimeOffset After = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> CatalogImageModels()
    {
        var data = new TheoryData<string>();

        // Fake 목록은 뺀다 — 청구되지 않는 모델이라 단가 행이 있으면 그것이 거짓말이다
        foreach (var kind in Enum.GetValues<ProviderKind>())
        {
            foreach (var model in ImageModels.For(kind))
            {
                data.Add(model.Id);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogImageModels))]
    public void EveryCatalogImageModel_HasConfirmedCostOrExplicitUnknown(string model)
    {
        var book = SeededBook();

        // 행의 존재가 아니라 **비용이 나오는가**를 묻는다. 모델명은 걸리는데 장당 단가도
        // 토큰 단가도 없는 행이면 호출은 여전히 미등록이 된다 (FR-20)
        var cost = book.Estimate(model, After, inputTokens: 2_075, outputTokens: 1_377, images: 1);

        if (model == UnconfirmedSpritePriceModel)
        {
            Assert.False(book.IsKnown(model, After));
            Assert.Null(cost);
            return;
        }

        Assert.NotNull(cost);
        Assert.True(cost > 0m, $"{model} 의 단가 행이 0원을 낸다 — 0 은 '공짜로 썼다' 로 읽힌다");
    }

    /// <summary>마이그레이션들이 심는 행 전부 — 어느 목록에 있느냐는 배포 순서 문제일 뿐이다.</summary>
    private static IEnumerable<SeedModelPrices.Row> AllSeededRows =>
        SeedModelPrices.All
            .Concat(SeedModelPrices.Images)
            .Concat(SeedModelPrices.MissingImagePrices);

    private static ModelPriceBook SeededBook()
        => new(AllSeededRows.Select(row =>
            ModelPrice.Create(
                row.Model, row.Input, row.Output,
                row.LongFrom, row.LongInput, row.LongOutput,
                SeedModelPrices.EffectiveFrom, row.Note, row.PerImage)));
}
