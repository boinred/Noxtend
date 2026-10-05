using Microsoft.AspNetCore.Mvc;
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
    DeleteModelPriceHandler delete) : ControllerBase
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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
        => ApiResults.From(await delete.HandleAsync(id, ct), _ => true);
}
