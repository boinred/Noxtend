using Noxtend.Tuning.Domain.Call;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 단가표는 손으로 관리되므로 **모르는 모델을 어떻게 다루는가**가 핵심이다.
/// 0 으로 두면 "공짜로 썼다" 로 읽히고, 그 오해가 단가 누락보다 나쁘다.
/// </summary>
public sealed class ModelPriceBookTests
{
    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Aug = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static ModelPrice Price(
        string model, decimal input, decimal output, DateTimeOffset from,
        int? longFrom = null, decimal? longInput = null, decimal? longOutput = null)
        => ModelPrice.Create(model, input, output, longFrom, longInput, longOutput, from, "테스트");

    private static ModelPriceBook Book(params ModelPrice[] prices) => new(prices);

    [Fact]
    public void KnownModel_Estimates()
    {
        // claude-opus-5: $5/1M 입력, $25/1M 출력
        var book = Book(Price("claude-opus-5", 5m, 25m, Jan));

        Assert.Equal(5m + 2.5m, book.Estimate("claude-opus-5", Aug, 1_000_000, 100_000));
    }

    [Fact]
    public void DateSuffixedModel_MatchesTheFamily()
    {
        // 공급자가 `claude-opus-4-5-20251101` 처럼 날짜를 붙여 돌려주는 경우가 있다
        var book = Book(Price("claude-opus-4-5", 5m, 25m, Jan));

        Assert.Equal(5m, book.Estimate("claude-opus-4-5-20251101", Aug, 1_000_000, 0));
    }

    [Fact]
    public void LongestModelNameWins()
    {
        // `-luna` 는 `-sol` 보다 25배 싸다. 계열로 뭉뚱그리면 합계가 통째로 틀린다
        var book = Book(
            Price("gpt-5", 1.25m, 10m, Jan),
            Price("gpt-5.6-sol", 5m, 30m, Jan),
            Price("gpt-5.6-luna", 0.20m, 1.20m, Jan));

        Assert.Equal(0.020m, book.Estimate("gpt-5.6-luna", Aug, 100_000, 0));
        Assert.Equal(0.5m, book.Estimate("gpt-5.6-sol", Aug, 100_000, 0));
        Assert.Equal(0.125m, book.Estimate("gpt-5", Aug, 100_000, 0));
    }

    [Fact]
    public void UnknownVariant_DoesNotFallBackToShorterName()
    {
        // `gpt-5.6-nova` 가 `gpt-5` 에 걸리면 25배 싼 값이 조용히 계산된다.
        // OpenAI 는 접미사로 단가가 다른 모델을 가르므로 날짜 접미사만 인정한다
        var book = Book(Price("gpt-5", 1.25m, 10m, Jan));

        Assert.Null(book.Estimate("gpt-5.6-nova", Aug, 100_000, 0));
        Assert.False(book.IsKnown("gpt-5.9", Aug));
    }

    [Fact]
    public void UnknownModel_ReturnsNullNotZero()
    {
        var book = Book(Price("claude-opus-5", 5m, 25m, Jan));

        Assert.Null(book.Estimate("some-unlisted-model", Aug, 1_000_000, 1_000_000));
        Assert.False(book.IsKnown("some-unlisted-model", Aug));
    }

    [Fact]
    public void MissingTokens_ReturnNull()
    {
        // 토큰 수를 못 받은 호출이 있다 — 그때 0원으로 세면 합계가 조용히 낮아진다
        var book = Book(Price("claude-opus-5", 5m, 25m, Jan));

        Assert.Null(book.Estimate("claude-opus-5", Aug, null, null));
    }

    [Fact]
    public void LongContextInput_UsesTheHigherTier()
    {
        // 272K 위는 단가가 뛴다. 짧은 단가로 계산하면 실제보다 싸게 나온다
        var book = Book(Price("gpt-5.6-luna", 0.20m, 1.20m, Jan, 272_000, 0.40m, 1.80m));

        Assert.Equal(0.40m, book.Estimate("gpt-5.6-luna", Aug, 1_000_000, 0));
        Assert.Equal(0.20m, book.Estimate("gpt-5.6-luna", Aug, 271_999, 0) * 1_000_000m / 271_999m);
    }

    /// <summary>
    /// **이 표가 존재하는 이유.** 행이 하나뿐이면 인상분이 과거 지출까지 소급되어
    /// 지난달 비용이 조용히 부풀어 오른다.
    /// </summary>
    [Fact]
    public void PriceIncrease_DoesNotRewriteThePast()
    {
        var book = Book(
            Price("gpt-5.6-luna", 0.20m, 1.20m, Jan),
            Price("gpt-5.6-luna", 0.25m, 1.50m, Aug));

        var july = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        var september = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(0.20m, book.Estimate("gpt-5.6-luna", july, 1_000_000, 0));
        Assert.Equal(0.25m, book.Estimate("gpt-5.6-luna", september, 1_000_000, 0));
    }

    /// <summary>
    /// 이 표가 생기기 전에 쌓인 호출을 통째로 미등록 처리하지 않는다 —
    /// 그때의 단가로는 가장 오래된 행이 유일한 근거다.
    /// </summary>
    [Fact]
    public void CallOlderThanEveryRow_UsesTheEarliestRow()
    {
        var book = Book(
            Price("gpt-5.6-luna", 0.20m, 1.20m, Aug),
            Price("gpt-5.6-luna", 0.25m, 1.50m, Aug.AddMonths(1)));

        Assert.Equal(0.20m, book.Estimate("gpt-5.6-luna", Jan, 1_000_000, 0));
    }

    [Theory]
    [InlineData(272_000, 0.40, null)]
    [InlineData(272_000, null, 1.80)]
    [InlineData(null, 0.40, 1.80)]
    public void HalfFilledLongTier_IsRejected(int? longFrom, double? longInput, double? longOutput)
    {
        // 반쪽 행은 조용히 짧은 단가로 계산되어 실제보다 싼 값을 낸다
        Assert.Throws<ModelPriceInvalidException>(() => ModelPrice.Create(
            "gpt-5.6-luna", 0.20m, 1.20m, longFrom,
            (decimal?)longInput, (decimal?)longOutput, Jan, "반쪽"));
    }

    [Fact]
    public void NegativePrice_IsRejected()
    {
        Assert.Throws<ModelPriceInvalidException>(
            () => ModelPrice.Create("gpt-5", -1m, 10m, null, null, null, Jan, "음수"));
    }

    // ─── 사이클 #7 — 장당 과금 (§3.3 · Plan V-13) ───

    private static ModelPrice ImagePrice(string model, decimal perImage, DateTimeOffset from)
        => ModelPrice.CreatePerImage(model, perImage, from, "테스트");

    [Fact]
    public void ImageCall_IsPricedPerImage()
    {
        // **이것이 사이클 #7 의 조용한 함정이었다** (Plan V-13). 이전 코드는 토큰 둘 다
        // null 이면 무조건 null 을 냈는데, 이미지 호출이 정확히 그 모양이라 파츠 이미지
        // 비용이 전부 "단가 미등록" 으로 사라졌을 것이다
        var book = Book(ImagePrice("gpt-image-1", 0.04m, Jan));

        Assert.Equal(0.04m, book.Estimate("gpt-image-1", Aug, null, null, images: 1));
        Assert.Equal(0.12m, book.Estimate("gpt-image-1", Aug, null, null, images: 3));
    }

    [Fact]
    public void ImageCall_WithoutPerImagePrice_IsUnknownNotZero()
    {
        // 토큰 단가만 있는 행에 이미지 호출이 걸리면 0 이 아니라 미등록이다 —
        // 0 으로 두면 "공짜로 그렸다" 로 읽힌다 (Plan FR-20 · 원칙 ③)
        var book = Book(Price("gpt-image-1", 5m, 25m, Jan));

        Assert.Null(book.Estimate("gpt-image-1", Aug, null, null, images: 1));
    }

    [Fact]
    public void UnknownImageModel_IsNull()
    {
        Assert.Null(ModelPriceBook.Empty.Estimate("gpt-image-1", Aug, null, null, images: 1));
    }

    [Fact]
    public void CallWithNeitherTokensNorImages_IsNull()
    {
        // 값을 하나도 못 받은 호출 — 0원으로 세면 합계가 조용히 낮아진다
        var book = Book(ImagePrice("gpt-image-1", 0.04m, Jan));

        Assert.Null(book.Estimate("gpt-image-1", Aug, null, null, images: null));
    }

    [Fact]
    public void TokenEstimates_AreUnchangedByTheImageBranch()
    {
        // 회귀 (§8.1 #16) — 기존 토큰 계산 경로가 그대로여야 한다
        var book = Book(Price("claude-opus-5", 5m, 25m, Jan));

        Assert.Equal(5m + 2.5m, book.Estimate("claude-opus-5", Aug, 1_000_000, 100_000, images: null));
    }

    // ─── 토큰 기반 이미지 과금 (D-8 재검토) ───

    [Fact]
    public void ImageCall_WithTokens_UsesTheTokenRates()
    {
        // 두 공급자 모두 실제로는 토큰 과금이다. 장당 정액은 "1024px · 참조 없음" 한
        // 조합에서만 맞는데, D-5 때문에 참조 원본을 **항상** 보내므로 입력 토큰이 더 붙는다
        var book = Book(Price("gemini-3.1-flash-lite-image", 0.30m, 60m, Jan));

        // 입력 1,120(참조 이미지) + 출력 1,120(1024px 결과)
        var cost = book.Estimate(
            "gemini-3.1-flash-lite-image", Aug,
            inputTokens: 1_120, outputTokens: 1_120, images: 1);

        Assert.Equal((1_120 * 0.30m + 1_120 * 60m) / 1_000_000m, cost);
    }

    [Fact]
    public void ImageCall_PrefersTokenRatesOverPerImage()
    {
        // 행이 **어느 과금 모델인지 선언**한다. 토큰 단가가 있으면 실측이 우선이고,
        // 장당 정액만 있는 행은 그 값을 쓴다 — 운영자가 표를 보고 예측할 수 있어야 한다
        var book = Book(ModelPrice.Create(
            "gpt-image-2", 10m, 40m, null, null, null, Jan, "토큰 + 정액", perImage: 0.05m));

        var cost = book.Estimate("gpt-image-2", Aug, 1_000_000, 0, images: 1);

        Assert.Equal(10m, cost);
    }

    [Fact]
    public void ImageCall_FallsBackToPerImage_WhenTheProviderGaveNoTokens()
    {
        // 토큰을 안 주는 공급자·모델이 있을 수 있다. 그때는 정액이 유일한 근거다
        var book = Book(ImagePrice("gpt-image-2", 0.05m, Jan));

        Assert.Equal(0.05m, book.Estimate("gpt-image-2", Aug, null, null, images: 1));
    }

    [Fact]
    public void ImageCall_WithTokensButNoRates_IsUnknownNotZero()
    {
        // 토큰은 왔는데 단가 행이 장당도 토큰도 없으면 계산할 근거가 없다.
        // 0 으로 뭉개면 "공짜로 그렸다" 로 읽힌다 (FR-20)
        var book = ModelPriceBook.Empty;

        Assert.Null(book.Estimate("gemini-3.1-flash-image", Aug, 1_120, 1_120, images: 1));
    }

    [Fact]
    public void PerImagePriceRow_RejectsANegativeValue()
    {
        Assert.Throws<ModelPriceInvalidException>(
            () => ModelPrice.CreatePerImage("gpt-image-1", -0.04m, Jan, "음수"));
    }
}
