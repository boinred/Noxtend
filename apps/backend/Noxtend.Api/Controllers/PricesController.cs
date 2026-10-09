using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Noxtend.Domain.Common;
using Noxtend.Api.Contracts;
using Noxtend.Tuning.Application.Prices;

namespace Noxtend.Api.Controllers;

/// <summary>
/// 모델 단가.
///
/// 전에는 코드 안의 `static Dictionary` 라 단가 하나를 고치려면 재배포해야 했다.
/// 공급자가 단가를 바꿀 때마다 배포가 필요한 구조는 결국 낡은 표를 방치하게 만든다.
///
/// **인상은 POST, 오타는 PUT.** 기존 행을 고치면 과거 호출의 비용까지 다시 계산되므로
/// 공급자가 단가를 바꾼 경우는 새 행이어야 한다 (§ModelPrice).
/// </summary>
[ApiController]
[Route("api/prices")]
public sealed class PricesController(
    ListModelPricesHandler list,
    CreateModelPriceHandler create,
    UpdateModelPriceHandler update,
    DeleteModelPriceHandler delete,
    CollectPriceUpdateHandler collectUpdates,
    ApplyPriceUpdateHandler applyUpdates) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        var prices = await list.HandleAsync(ct);

        return Ok(ApiResponse<IReadOnlyList<ModelPriceResponse>>.Ok(
            prices.Select(ModelPriceResponse.From).ToList()));
    }

    /// <summary>새 단가 행 — 공급자가 단가를 바꿨을 때.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] ModelPriceRequest request, CancellationToken ct)
        => ApiResults.From(
            await create.HandleAsync(request.ToInput(), ct),
            ModelPriceResponse.From,
            StatusCodes.Status201Created);

    /// <summary>기존 행 수정 — 오타 정정. 과거 호출의 비용도 함께 바뀐다.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] ModelPriceRequest request, CancellationToken ct)
        => ApiResults.From(
            await update.HandleAsync(id, request.ToInput(), ct), ModelPriceResponse.From);

    [HttpPost("update-previews")]
    public async Task<IActionResult> CollectUpdateAsync([FromBody] JsonElement request, CancellationToken ct)
    {
        if (!TryIds(request, "providerConfigIds", out var ids)) return InvalidUpdate();
        return ApiResults.From(await collectUpdates.HandleAsync(ids, ct), PriceUpdatePreviewResponse.From);
    }

    [HttpPost("update-previews/{id:guid}/apply")]
    public async Task<IActionResult> ApplyUpdateAsync(Guid id, [FromBody] JsonElement request, CancellationToken ct)
    {
        if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("requestId", out var requestId)
            || requestId.ValueKind != JsonValueKind.String || !Guid.TryParse(requestId.GetString(), out var parsedRequestId)
            || !TryIds(request, "candidateIds", out var ids)) return InvalidUpdate();
        DateTimeOffset? effective = null;
        if (request.TryGetProperty("effectiveFrom", out var date) && date.ValueKind != JsonValueKind.Null)
        {
            if (date.ValueKind != JsonValueKind.String || !date.TryGetDateTimeOffset(out var parsedDate)) return InvalidUpdate();
            effective = parsedDate.ToUniversalTime();
        }
        return ApiResults.From(await applyUpdates.HandleAsync(id, new(parsedRequestId, ids, effective), ct), receipt => receipt);
    }

    private static bool TryIds(JsonElement request, string name, out IReadOnlyList<Guid>? ids)
    {
        ids = null;
        if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return false;
        var parsed = new List<Guid>();
        foreach (var value in array.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var id)) return false;
            parsed.Add(id);
        }
        ids = parsed;
        return true;
    }

    private static IActionResult InvalidUpdate()
        => ApiResults.Failure<object>(ErrorCode.PriceUpdateInvalid, "단가 업데이트 입력이 유효하지 않습니다");

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
        => ApiResults.From(await delete.HandleAsync(id, ct), _ => true);
}
