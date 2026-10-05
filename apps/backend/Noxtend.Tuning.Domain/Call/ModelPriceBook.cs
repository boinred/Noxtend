namespace Noxtend.Tuning.Domain.Call;

/// <summary>
/// 단가표 한 벌을 들고 호출 비용을 낸다.
///
/// **행 목록을 통째로 받는 이유**는 비용 계산이 SQL 로 표현되지 않기 때문이다 —
/// 모델 매칭과 시행일 선택이 문자열 규칙이라 메모리에서 한다. 호출 한 건마다 DB 를
/// 왕복하지 않도록 한 번 읽어 이 객체를 만들고 목록 전체에 재사용한다.
///
/// 모르는 모델은 **0 이 아니라 `null`** 이다. 0 으로 두면 "공짜로 썼다" 로 읽히고,
/// 그 오해가 단가 누락보다 나쁘다.
/// </summary>
public sealed class ModelPriceBook
{
    private readonly IReadOnlyList<ModelPrice> prices;

    public ModelPriceBook(IEnumerable<ModelPrice> prices)
    {
        // 매칭이 "가장 긴 모델명 우선" 이므로 미리 정렬해 호출마다 다시 정렬하지 않는다
        this.prices = [.. prices.OrderByDescending(p => p.Model.Length)];
    }

    public static ModelPriceBook Empty { get; } = new([]);

    /// <summary>
    /// 호출 한 건의 비용. 단가를 모르면 <c>null</c>.
    /// </summary>
    /// <param name="images">
    /// 생성한 이미지 수 (사이클 #7 §2.3 A-9). 텍스트 호출이면 <c>null</c>.
    ///
    /// **호출자가 과금 모델을 판단하게 하지 않는다.** 그렇게 하면 그 판단이 화면·집계·
    /// 내역 세 곳에 흩어진다. 단가표를 아는 객체가 하나이므로 여기가 그 지식의 자리다.
    /// </param>
    public decimal? Estimate(
        string model, DateTimeOffset at, int? inputTokens, int? outputTokens, int? images = null)
    {
        // **이 분기가 사이클 #7 의 조용한 함정이었다** (Plan V-13). 이전에는 토큰 둘 다
        // null 이면 무조건 null 을 냈는데, 이미지 호출이 정확히 그 모양이라 파츠 이미지
        // 비용이 전부 "단가 미등록" 으로 사라졌을 것이다.
        //
        // 셋 다 없는 경우는 여전히 null 이다 — 0원으로 세면 합계가 조용히 낮아진다
        if (inputTokens is null && outputTokens is null && images is null)
        {
            return null;
        }

        if (Lookup(model, at) is not { } price)
        {
            return null;
        }

        // 이미지 호출의 과금 모델은 **단가 행이 선언한다.**
        //
        // Plan D-8 은 "이미지는 장당 과금" 을 전제로 `PerImage` 를 만들었는데, 실제로는
        // 두 공급자 다 토큰으로 매긴다 — 장당 정액은 "1024px · 참조 없음" 한 조합에서만
        // 맞는 환산값이다. **D-5 때문에 참조 원본을 항상 보내므로 입력 토큰이 더 붙고,**
        // 그만큼 정액은 하한이 된다.
        //
        // 토큰 단가가 있는 행이면 공급자가 준 실측 토큰을 쓰고, 정액만 있는 행은 그 값을
        // 쓴다 — 운영자가 표를 보고 어느 쪽으로 계산되는지 예측할 수 있어야 한다.
        if (images is not null)
        {
            var hasTokenRates = price.InputPerMillion > 0 || price.OutputPerMillion > 0;
            var hasTokens = inputTokens is not null || outputTokens is not null;

            if (!hasTokenRates || !hasTokens)
            {
                // 정액도 없으면 0 이 아니라 미등록이다 (FR-20)
                return price.PerImage is { } perImage ? images * perImage : null;
            }
        }

        var isLong = price.LongContextFrom is { } threshold && inputTokens >= threshold;

        var input = isLong ? price.LongInputPerMillion!.Value : price.InputPerMillion;
        var output = isLong ? price.LongOutputPerMillion!.Value : price.OutputPerMillion;

        return ((inputTokens ?? 0) * input + (outputTokens ?? 0) * output) / 1_000_000m;
    }

    /// <summary>단가를 아는 모델인가. 화면이 "단가 미등록" 을 구분해 보여주려고 쓴다.</summary>
    public bool IsKnown(string model, DateTimeOffset at) => Lookup(model, at) is not null;

    /// <summary>
    /// 모델과 시각으로 단가 행을 찾는다.
    ///
    /// **모델 매칭.** 공급자가 `claude-opus-4-5-20251101` 처럼 날짜를 붙여 돌려주므로
    /// 접두사로 찾되, 남는 부분이 날짜일 때만 인정한다. 아무 접두사나 받으면
    /// `gpt-5.6-nova` 같은 새 변종이 `gpt-5` 에 걸려 25배 싼 값으로 조용히 계산된다 —
    /// OpenAI 는 접미사로 **단가가 다른** 모델을 가르기 때문에 Anthropic 보다 위험하다.
    ///
    /// **시행일 선택.** 그 시각 이전에 시작된 행 중 가장 최근 것을 쓴다. 호출이 모든
    /// 행보다 오래됐으면 **가장 오래된 행**으로 소급한다 — 이 표가 생기기 전의 호출을
    /// 통째로 미등록 처리하는 것보다 낫고, 그때의 단가로는 그 값이 유일한 근거다.
    /// </summary>
    private ModelPrice? Lookup(string model, DateTimeOffset at)
    {
        // prices 는 모델명 긴 순으로 정렬돼 있다 — 처음 걸린 모델명이 가장 구체적이다
        var matchedModel = prices.FirstOrDefault(p => Matches(model, p.Model))?.Model;

        if (matchedModel is null)
        {
            return null;
        }

        var candidates = prices.Where(p => p.Model == matchedModel).ToList();

        return candidates
                   .Where(p => p.EffectiveFrom <= at)
                   .MaxBy(p => p.EffectiveFrom)
               ?? candidates.MinBy(p => p.EffectiveFrom);
    }

    /// <summary>모델 id 가 이 단가 행에 걸리는가 — 정확히 같거나, 날짜 접미사만 더 붙었거나.</summary>
    private static bool Matches(string model, string key)
    {
        if (!model.StartsWith(key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = model[key.Length..];
        return rest.Length == 0
               || (rest.Length > 1 && rest[0] == '-' && rest[1..].All(char.IsAsciiDigit));
    }
}
