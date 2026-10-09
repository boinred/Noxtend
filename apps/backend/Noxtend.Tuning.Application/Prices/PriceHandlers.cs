using Noxtend.Domain.Common;
using Noxtend.Tuning.Domain.Call;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Tuning.Application.Prices;

/// <summary>단가 목록. 관리자 화면이 읽는다.</summary>
public sealed class ListModelPricesHandler(IModelPriceRepository prices)
{
    public Task<IReadOnlyList<ModelPrice>> HandleAsync(CancellationToken ct)
        => prices.ListAsync(ct);
}

/// <summary>
/// 단가 행 하나의 입력값.
///
/// 장문 구간 셋은 함께 오거나 함께 비어야 한다 — 도메인이 강제한다.
/// </summary>
public sealed record ModelPriceInput(
    string Model,
    decimal InputPerMillion,
    decimal OutputPerMillion,
    int? LongContextFrom,
    decimal? LongInputPerMillion,
    decimal? LongOutputPerMillion,
    DateTimeOffset EffectiveFrom,
    string? Note,
    /// <summary>이미지 1장당 USD. 토큰 과금 모델이면 null (사이클 #7 · Plan D-8).</summary>
    decimal? PerImage = null,
    string? Provider = null);

/// <summary>
/// 새 단가 행.
///
/// **공급자가 단가를 바꿨을 때 쓰는 길이다.** 기존 행을 고치면 과거 호출의 비용까지
/// 다시 계산되어 지난달 지출이 조용히 바뀐다. 인상은 새 행, 오타는 수정이다.
/// </summary>
public sealed class CreateModelPriceHandler(IModelPriceRepository prices)
{
    public async Task<Result<ModelPrice>> HandleAsync(ModelPriceInput input, CancellationToken ct)
    {
        var model = input.Model.Trim();

        if (await prices.ExistsAsync(model, input.EffectiveFrom, null, ct))
        {
            return Result<ModelPrice>.Fail(
                ErrorCode.PriceDuplicate,
                $"'{model}' 의 {input.EffectiveFrom:yyyy-MM-dd} 시행 단가가 이미 있습니다");
        }

        try
        {
            var price = ModelPrice.Create(
                model, input.InputPerMillion, input.OutputPerMillion,
                input.LongContextFrom, input.LongInputPerMillion, input.LongOutputPerMillion,
                input.EffectiveFrom, input.Note ?? string.Empty, input.PerImage, input.Provider);

            await prices.AddAsync(price, ct);
            await prices.SaveChangesAsync(ct);

            return Result<ModelPrice>.Ok(price);
        }
        catch (ModelPriceInvalidException ex)
        {
            return Result<ModelPrice>.Fail(ErrorCode.PriceInvalid, ex.Message);
        }
    }
}

/// <summary>
/// 기존 행 수정 — 오타 정정용.
///
/// 모델명은 바꾸지 않는다. 바꿔야 한다면 그것은 다른 모델이고, 새 행이 맞다.
/// </summary>
public sealed class UpdateModelPriceHandler(IModelPriceRepository prices)
{
    public async Task<Result<ModelPrice>> HandleAsync(
        Guid id, ModelPriceInput input, CancellationToken ct)
    {
        if (await prices.GetAsync(id, ct) is not { } price)
        {
            return Result<ModelPrice>.Fail(ErrorCode.PriceNotFound, "단가 행을 찾을 수 없습니다");
        }

        // 시행일을 옮기면 다른 행과 겹칠 수 있다 — 자기 자신은 빼고 본다
        if (await prices.ExistsAsync(price.Model, input.EffectiveFrom, id, ct))
        {
            return Result<ModelPrice>.Fail(
                ErrorCode.PriceDuplicate,
                $"'{price.Model}' 의 {input.EffectiveFrom:yyyy-MM-dd} 시행 단가가 이미 있습니다");
        }

        try
        {
            price.Update(
                input.InputPerMillion, input.OutputPerMillion,
                input.LongContextFrom, input.LongInputPerMillion, input.LongOutputPerMillion,
                input.EffectiveFrom, input.Note ?? string.Empty, input.PerImage, input.Provider);

            await prices.SaveChangesAsync(ct);

            return Result<ModelPrice>.Ok(price);
        }
        catch (ModelPriceInvalidException ex)
        {
            return Result<ModelPrice>.Fail(ErrorCode.PriceInvalid, ex.Message);
        }
    }
}

/// <summary>
/// 단가 행 삭제.
///
/// 지우면 그 구간의 호출이 이전 행(또는 미등록)으로 계산된다. 잘못 넣은 행을
/// 되돌리는 용도다 — 내역은 지우지 않으므로 되돌릴 수 있다.
/// </summary>
public sealed class DeleteModelPriceHandler(IModelPriceRepository prices)
{
    public async Task<Result<bool>> HandleAsync(Guid id, CancellationToken ct)
    {
        if (await prices.GetAsync(id, ct) is not { } price)
        {
            return Result<bool>.Fail(ErrorCode.PriceNotFound, "단가 행을 찾을 수 없습니다");
        }

        prices.Remove(price);
        await prices.SaveChangesAsync(ct);

        return Result<bool>.Ok(true);
    }
}
