using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Application.Providers;
using Noxtend.Domain.Provider;

namespace Noxtend.Api.Controllers;

/// <summary>
/// Design Ref: §4.2 #9~#13 · §7
///
/// **어떤 응답에도 평문 키가 없다.** 컨트롤러가 <see cref="ProviderResponse"/> 만
/// 반환하고 엔티티를 직렬화하지 않는 것이 그 보장이다 (§10.4).
/// </summary>
[ApiController]
[Route("api/providers")]
public sealed class ProvidersController(
    ListProvidersHandler list,
    CreateProviderHandler create,
    UpdateProviderHandler update,
    DeleteProviderHandler delete,
    ListProviderModelsHandler models,
    TestProviderHandler test) : ControllerBase
{
    /// <summary>Design Ref: §4.2 #9 — 키 마스킹.</summary>
    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        var configs = await list.HandleAsync(ct);

        return Ok(ApiResponse<IReadOnlyList<ProviderResponse>>.Ok(
            configs.Select(ProviderResponse.From).ToList()));
    }

    /// <summary>
    /// 종류별 사용 용도 — 관리자 폼이 **등록 전에** 읽는다.
    ///
    /// 목록(`GET /api/providers`)은 이미 등록된 공급자의 능력을 싣지만, 폼은 아직
    /// 만들지 않은 종류를 고르는 중이라 그 값이 없다. 화면이 같은 표를 따로 들면
    /// 백엔드가 바뀔 때 두 자리가 어긋나므로 여기서 내보낸다.
    /// </summary>
    [HttpGet("capabilities")]
    public IActionResult ListCapabilities()
        => Ok(ApiResponse<IReadOnlyList<ProviderKindCapabilitiesResponse>>.Ok(
            Enum.GetValues<ProviderKind>()
                .Select(ProviderKindCapabilitiesResponse.From)
                .ToList()));

    /// <summary>Design Ref: §4.2 #10 — 등록.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] ProviderWriteRequest request,
        CancellationToken ct)
    {
        if (!TryParseKind(request.Kind, out var kind))
        {
            return InvalidKind(request.Kind);
        }

        var result = await create.HandleAsync(
            request.DisplayName, kind, request.ApiKey ?? string.Empty, ct);

        return ApiResults.From(result, ProviderResponse.From, StatusCodes.Status201Created);
    }

    /// <summary>Design Ref: §4.2 #11 — `apiKey` 생략 시 기존 키 유지.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] ProviderWriteRequest request,
        CancellationToken ct)
    {
        if (!TryParseKind(request.Kind, out var kind))
        {
            return InvalidKind(request.Kind);
        }

        var result = await update.HandleAsync(
            id, request.DisplayName, kind, request.ApiKey, request.IsEnabled, ct);

        return ApiResults.From(result, ProviderResponse.From);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var result = await delete.HandleAsync(id, ct);

        return result.IsSuccess
            ? NoContent()
            : ApiResults.Failure<object>(result.ErrorCode!, result.ErrorMessage!);
    }

    /// <summary>
    /// Design Ref: §4.2 #14 (신설) — 스튜디오의 모델 드롭다운이 읽는다.
    ///
    /// 실패를 빈 배열로 바꾸지 않는다. 화면은 "연결 확인부터 하세요" 로 막아야 하고,
    /// 그 안내가 맞는지는 오류 코드에 달렸다 (`PROVIDER_CALL_FAILED` 인가
    /// `PROVIDER_NO_VISION_MODELS` 인가).
    /// </summary>
    [HttpGet("{id:guid}/models")]
    public async Task<IActionResult> ListModelsAsync(Guid id, CancellationToken ct)
        => ApiResults.From(
            await models.HandleAsync(id, ct),
            list => (IReadOnlyList<ProviderModelResponse>)
                list.Select(ProviderModelResponse.From).ToList());

    /// <summary>
    /// Design Ref: §4.2 #7 (사이클 #7) — 스튜디오의 **이미지** 모델 드롭다운이 읽는다.
    ///
    /// 텍스트 목록과 경로를 나눈다. 한 목록에 섞으면 사용자가 텍스트 단계에 이미지
    /// 모델을 고를 수 있다.
    /// </summary>
    [HttpGet("{id:guid}/image-models")]
    public async Task<IActionResult> ListImageModelsAsync(Guid id, CancellationToken ct)
        => ApiResults.From(
            await models.HandleImageModelsAsync(id, ct),
            list => (IReadOnlyList<ProviderModelResponse>)
                list.Select(ProviderModelResponse.From).ToList());

    /// <summary>
    /// Design Ref: §10.1 (사이클 #10) — 스튜디오의 **3D** 모델 드롭다운이 읽는다.
    ///
    /// 목록은 검증된 스냅숏 고정이지만 호출은 실제로 나간다 — 키가 살아 있는지
    /// 확인하는 것이 목적이고, 잔액 조회라 credit 을 쓰지 않는다.
    /// </summary>
    [HttpGet("{id:guid}/mesh-models")]
    public async Task<IActionResult> ListMeshModelsAsync(Guid id, CancellationToken ct)
        => ApiResults.From(
            await models.HandleMeshModelsAsync(id, ct),
            list => (IReadOnlyList<ProviderModelResponse>)
                list.Select(ProviderModelResponse.From).ToList());

    /// <summary>Design Ref: §4.2 #13 — 공급자 원문 오류를 그대로 흘리지 않는다.</summary>
    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> TestAsync(Guid id, CancellationToken ct)
        => ApiResults.From(
            await test.HandleAsync(id, ct),
            r => new ProviderTestResponse(
                r.Ok,
                r.LatencyMs,
                r.TextModelCount,
                r.ImageModelCount,
                r.MeshModelCount,
                r.MeshCreditBalance));

    private static bool TryParseKind(string? kind, out ProviderKind parsed)
        => Enum.TryParse(kind, ignoreCase: true, out parsed);

    private IActionResult InvalidKind(string? kind)
        => ApiResults.Failure<ProviderResponse>(
            ErrorCode.ProviderKindInvalid, $"알 수 없는 공급자 종류입니다: {kind}");
}
