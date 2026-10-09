namespace Noxtend.Tuning.Domain.Call;

/// <summary>
/// 모델 하나의 단가 — **시행일 하나당 한 행**.
///
/// 같은 모델에 여러 행이 쌓인다. 공급자가 단가를 올리면 새 행을 추가하고, 오타를
/// 고칠 때는 해당 행을 수정한다. 이 구분이 이 표가 존재하는 이유다 — 행이 하나뿐이면
/// 인상분이 과거 지출까지 소급되어 지난달 비용이 조용히 부풀어 오른다.
///
/// **코드가 아니라 여기가 정본이다.** 마이그레이션이 초기값을 심지만
/// (<c>SeedModelPrices</c>), 이후 수정은 관리자 화면에서 한다 — 프롬프트와 같은 구조다.
/// </summary>
public sealed class ModelPrice
{
    private ModelPrice()
    {
        // EF Core 재구성용
    }

    private ModelPrice(
        Guid id,
        string model,
        decimal inputPerMillion,
        decimal outputPerMillion,
        int? longContextFrom,
        decimal? longInputPerMillion,
        decimal? longOutputPerMillion,
        DateTimeOffset effectiveFrom,
        string note,
        decimal? perImage)
    {
        PerImage = perImage;
        Id = id;
        Model = model;
        InputPerMillion = inputPerMillion;
        OutputPerMillion = outputPerMillion;
        LongContextFrom = longContextFrom;
        LongInputPerMillion = longInputPerMillion;
        LongOutputPerMillion = longOutputPerMillion;
        EffectiveFrom = effectiveFrom;
        Note = note;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// 모델 id. 날짜 접미사는 떼고 계열명만 적는다 — `claude-opus-4-5-20251101` 은
    /// `claude-opus-4-5` 행에 걸린다 (<see cref="ModelPriceBook"/> 참고).
    /// </summary>
    public string Model { get; private set; } = string.Empty;

    public string? Provider { get; private set; }
    public bool AllowHistoricalFallback { get; private set; } = true;
    public string? SourceEvidenceJson { get; private set; }

    /// <summary>100만 토큰당 USD.</summary>
    public decimal InputPerMillion { get; private set; }

    public decimal OutputPerMillion { get; private set; }

    /// <summary>
    /// 장문 단가가 시작되는 입력 토큰 수. `null` 이면 구간 구분이 없다.
    ///
    /// OpenAI 는 272K 를 넘는 입력에 단가를 올린다. Anthropic 쪽은 확인하지 못해
    /// 초기값에서 비워 두었다 — 확인되면 화면에서 채우면 된다.
    /// </summary>
    public int? LongContextFrom { get; private set; }

    public decimal? LongInputPerMillion { get; private set; }
    public decimal? LongOutputPerMillion { get; private set; }

    /// <summary>이 단가가 적용되기 시작한 시점. 이 시각 **이후**의 호출에 쓰인다.</summary>
    public DateTimeOffset EffectiveFrom { get; private set; }

    /// <summary>출처나 변경 이유. 손으로 관리되는 표라 근거가 남아야 한다.</summary>
    public string Note { get; private set; } = string.Empty;

    /// <summary>
    /// 이미지 1장당 USD. 토큰 과금 모델이면 <c>null</c> (사이클 #7 · Plan D-8).
    ///
    /// **토큰 칸에 억지로 넣지 않는 이유**: 이미지 모델은 장·해상도·품질당 과금이라
    /// "100만 토큰당" 이라는 단위 자체가 성립하지 않는다. 환산해 넣으면 단가표를 읽는
    /// 사람이 그 값을 토큰 단가로 오해한다.
    /// </summary>
    public decimal? PerImage { get; private set; }

    public static ModelPrice Create(
        string model,
        decimal inputPerMillion,
        decimal outputPerMillion,
        int? longContextFrom,
        decimal? longInputPerMillion,
        decimal? longOutputPerMillion,
        DateTimeOffset effectiveFrom,
        string note,
        decimal? perImage = null,
        string? provider = null)
    {
        var price = new ModelPrice(
            Guid.NewGuid(), model.Trim(), inputPerMillion, outputPerMillion,
            longContextFrom, longInputPerMillion, longOutputPerMillion, effectiveFrom, note.Trim(),
            perImage);

        price.Provider = provider?.Trim().ToLowerInvariant();
        price.Validate();
        return price;
    }

    public static ModelPrice CreateCollected(
        string model, decimal inputPerMillion, decimal outputPerMillion,
        int? longContextFrom, decimal? longInputPerMillion, decimal? longOutputPerMillion,
        DateTimeOffset effectiveFrom, string note, decimal? perImage,
        string provider, string sourceEvidenceJson)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(sourceEvidenceJson))
        {
            throw new ModelPriceInvalidException("수집 단가는 공급자와 근거가 필요합니다");
        }

        var price = Create(model, inputPerMillion, outputPerMillion, longContextFrom,
            longInputPerMillion, longOutputPerMillion, effectiveFrom, note, perImage, provider);
        price.AllowHistoricalFallback = false;
        price.SourceEvidenceJson = sourceEvidenceJson;
        return price;
    }

    /// <summary>
    /// 장당 과금 행 (사이클 #7).
    ///
    /// 토큰 단가를 0 으로 둔다 — 이미지 모델은 토큰으로 과금하지 않으므로 그 칸에
    /// 의미 있는 값이 없다. <see cref="ModelPriceBook.Estimate"/> 는 이미지 호출에
    /// <see cref="PerImage"/> 만 보므로 이 0 이 계산에 섞이지 않는다.
    /// </summary>
    public static ModelPrice CreatePerImage(
        string model, decimal perImage, DateTimeOffset effectiveFrom, string note)
        => Create(model, 0m, 0m, null, null, null, effectiveFrom, note, perImage);

    /// <summary>
    /// 값을 갈아끼운다 — 오타 수정용.
    ///
    /// 인상에는 쓰지 말아야 한다. 수정은 과거 호출의 비용까지 다시 계산되게 하므로,
    /// 공급자가 단가를 바꾼 경우는 **새 행**이 맞다.
    /// </summary>
    public void Update(
        decimal inputPerMillion,
        decimal outputPerMillion,
        int? longContextFrom,
        decimal? longInputPerMillion,
        decimal? longOutputPerMillion,
        DateTimeOffset effectiveFrom,
        string note,
        decimal? perImage = null,
        string? provider = null)
    {
        // 구형 수정 입력의 공급자·서버 소유 메타데이터 보존
        Provider = provider?.Trim().ToLowerInvariant() ?? Provider;
        PerImage = perImage;
        InputPerMillion = inputPerMillion;
        OutputPerMillion = outputPerMillion;
        LongContextFrom = longContextFrom;
        LongInputPerMillion = longInputPerMillion;
        LongOutputPerMillion = longOutputPerMillion;
        EffectiveFrom = effectiveFrom;
        Note = note.Trim();

        Validate();
    }

    /// <summary>
    /// 장문 구간은 **셋이 함께 있거나 셋 다 없어야 한다.** 하나만 빠지면 계산할 수 없는
    /// 반쪽 행이 되고, 그런 행은 조용히 짧은 단가로 계산되어 실제보다 싼 값을 낸다.
    /// </summary>
    private void Validate()
    {
        if (Provider is not null && Provider is not ("openai" or "anthropic" or "google" or "tripo" or "meshy"))
        {
            throw new ModelPriceInvalidException("지원하지 않는 공급자입니다");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new ModelPriceInvalidException("모델 id 가 비어 있습니다");
        }

        if (InputPerMillion < 0 || OutputPerMillion < 0)
        {
            throw new ModelPriceInvalidException("단가는 음수일 수 없습니다");
        }

        var longFields = new[]
        {
            LongContextFrom is not null,
            LongInputPerMillion is not null,
            LongOutputPerMillion is not null,
        };

        if (longFields.Distinct().Count() != 1)
        {
            throw new ModelPriceInvalidException(
                "장문 구간은 시작 토큰 수·입력 단가·출력 단가를 모두 채우거나 모두 비워야 합니다");
        }

        if (LongContextFrom is <= 0)
        {
            throw new ModelPriceInvalidException("장문 구간 시작 토큰 수는 0보다 커야 합니다");
        }

        if (LongInputPerMillion < 0 || LongOutputPerMillion < 0)
        {
            throw new ModelPriceInvalidException("장문 단가는 음수일 수 없습니다");
        }

        if (PerImage < 0)
        {
            throw new ModelPriceInvalidException("장당 단가는 음수일 수 없습니다");
        }
    }
}

/// <summary>단가 행이 계산 가능한 형태가 아닐 때.</summary>
public sealed class ModelPriceInvalidException(string message) : Exception(message);
