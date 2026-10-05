using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Tuning.Application.Prompts;

namespace Noxtend.Api.Controllers;

/// <summary>
/// Design Ref: §4.2 #16~19 — 프롬프트 버전.
///
/// **이 컨트롤러가 사이클 #5 의 핵심 가치를 나른다.** 프롬프트를 배포 없이 고치고
/// 되돌릴 수 있어야 반복 주기가 분 단위가 된다 (FR-08·FR-09).
/// </summary>
[ApiController]
[Route("api/prompts")]
public sealed class PromptsController(
    ListActivePromptsHandler listActive,
    GetPromptGridHandler grid,
    ListPromptVersionsHandler listVersions,
    CreatePromptVersionHandler create,
    ActivatePromptVersionHandler activate) : ControllerBase
{
    /// <summary>
    /// [목적] 프롬프트 관리 화면에서 단계별로 지금 켜져 있는 버전 목록을 조회합니다.
    /// [핵심 동작] 카테고리 구분 없이 활성 상태인 버전만 골라 평면 목록으로 돌려줍니다 — 격자로 가공하기 전의 원본입니다.
    /// [반환] 활성 프롬프트 버전 목록(PromptVersionResponse 배열).
    /// Design Ref: §4.2 #16.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListActiveAsync(CancellationToken ct)
    {
        var active = await listActive.HandleAsync(ct);

        return Ok(ApiResponse<IReadOnlyList<PromptVersionResponse>>.Ok(
            active.Select(PromptVersionResponse.From).ToList()));
    }

    /// <summary>
    /// [목적] 프롬프트 관리 화면의 그리드(격자) 뷰에서 단계별·카테고리별 현황을 조회합니다.
    /// [핵심 동작] 단순 목록이 아니라, 전용 프롬프트가 없는 칸은 폴백(기본값) 규칙을 적용해
    /// 각 칸을 [전용 / 폴백 / 실행불가] 상태로 가공합니다. 이 판정은 작업 접수 차단과 같은 규칙을 씁니다.
    /// [반환] (단계 × 카테고리) 격자 — 행마다 각 칸의 상태·버전이 담긴 PromptGridResponse.
    /// Design Ref: prompt-category-axis §12.1.
    /// </summary>
    [HttpGet("grid")]
    public async Task<IActionResult> GetGridAsync(CancellationToken ct)
    {
        var rows = await grid.HandleAsync(ct);

        return Ok(ApiResponse<PromptGridResponse>.Ok(PromptGridResponse.From(rows)));
    }

    /// <summary>
    /// [목적] 프롬프트 편집 화면에서 한 단계의 버전 이력(과거 수정 내역)을 조회합니다.
    /// [핵심 동작] <c>category</c> 를 생략하면 기본 슬롯의 이력을, 값을 주면 그 카테고리 전용 이력만 가릅니다 —
    /// 슬롯을 섞지 않아야 편집 화면의 이력이 뒤섞이지 않습니다.
    /// [반환] 최신순 프롬프트 버전 목록(PromptVersionResponse 배열).
    /// Design Ref: §4.2 #17 · prompt-category-axis §7.1.
    /// </summary>
    [HttpGet("{kind}/versions")]
    public async Task<IActionResult> ListVersionsAsync(
        string kind, [FromQuery] string? category, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var parsed))
        {
            return InvalidKind(kind);
        }

        if (!TryParseCategory(category, out var parsedCategory))
        {
            return InvalidCategory(category);
        }

        var versions = await listVersions.HandleAsync(parsed, parsedCategory, ct);

        return Ok(ApiResponse<IReadOnlyList<PromptVersionResponse>>.Ok(
            versions.Select(PromptVersionResponse.From).ToList()));
    }

    /// <summary>
    /// [목적] 프롬프트 편집 화면에서 새 버전을 저장합니다(수정은 덮어쓰기가 아니라 새 버전 쌓기).
    /// [핵심 동작] 저장이 곧 활성화가 아닙니다 — 항상 비활성으로 만들어지고, 켜는 것은 별도 호출입니다.
    /// 그래야 편집 중 실수가 즉시 운영에 나가지 않습니다. <c>category</c> 생략 시 기본 슬롯에 저장합니다.
    /// [반환] 방금 만들어진 프롬프트 버전(201). 변수 오타 등은 400 으로 거부됩니다.
    /// Design Ref: §4.2 #18 · FR-09.
    /// </summary>
    [HttpPost("{kind}/versions")]
    public async Task<IActionResult> CreateAsync(
        string kind,
        [FromBody] PromptWriteRequest request,
        CancellationToken ct)
    {
        if (!TryParseKind(kind, out var parsed))
        {
            return InvalidKind(kind);
        }

        if (!TryParseCategory(request.Category, out var parsedCategory))
        {
            return InvalidCategory(request.Category);
        }

        var result = await create.HandleAsync(
            parsed, parsedCategory, request.System, request.User, request.JsonSchema, request.Note, ct);

        return ApiResults.From(
            result, PromptVersionResponse.From, StatusCodes.Status201Created);
    }

    /// <summary>
    /// [목적] 프롬프트 편집 화면에서 특정 버전을 "켜기" 합니다 — 롤백(이전 버전으로 되돌리기)도 이 동작입니다.
    /// [핵심 동작] 같은 단계·카테고리의 이전 활성을 내리고 대상 버전을 켭니다. 대상 행이 자기 카테고리를 알므로
    /// 카테고리끼리 서로 끄지 않습니다(격리).
    /// [반환] 켜진 프롬프트 버전. 이미 켜져 있으면 그대로(멱등), 없는 id 는 404.
    /// Design Ref: §4.2 #19 · FR-09.
    /// </summary>
    [HttpPost("versions/{id:guid}/activate")]
    public async Task<IActionResult> ActivateAsync(Guid id, CancellationToken ct)
        => ApiResults.From(await activate.HandleAsync(id, ct), PromptVersionResponse.From);

    private static bool TryParseKind(string? kind, out LlmOperationKind parsed)
        => Enum.TryParse(kind, ignoreCase: true, out parsed);

    // 생략/null 은 기본 슬롯이라 성공이고 parsed 는 null. 값이 있는데 못 읽으면 실패.
    // asset-category-contract 의 공유 어휘라 JobCategoryInvalid 를 그대로 쓴다
    private static bool TryParseCategory(string? category, out AssetCategory? parsed)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            parsed = null;
            return true;
        }

        // 숫자 문자열("99")은 Enum.TryParse 를 통과해 정의되지 않은 값이 컬럼에 영속되고,
        // "0" 은 문서화되지 않은 별칭이 된다. 이름만 받는다 — IsDefined 로 정의된 값만 통과
        if (!int.TryParse(category, out _)
            && Enum.TryParse<AssetCategory>(category, ignoreCase: true, out var wanted)
            && Enum.IsDefined(wanted))
        {
            parsed = wanted;
            return true;
        }

        parsed = null;
        return false;
    }

    private IActionResult InvalidKind(string? kind)
        => ApiResults.Failure<PromptVersionResponse>(
            ErrorCode.PromptVersionNotFound, $"알 수 없는 단계입니다: {kind}");

    private IActionResult InvalidCategory(string? category)
        => ApiResults.Failure<PromptVersionResponse>(
            ErrorCode.JobCategoryInvalid, $"알 수 없는 카테고리입니다: {category}");
}
