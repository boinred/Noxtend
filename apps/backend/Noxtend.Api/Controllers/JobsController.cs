using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Application.Job;
using Noxtend.Application.Mesh;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Api.Controllers;

/// <summary>Design Ref: §4.2 #5~#8</summary>
[ApiController]
[Route("api/jobs")]
public sealed class JobsController(
    StartJobHandler start,
    GetJobHandler get,
    ListJobsHandler list,
    CancelJobHandler cancel,
    DeleteJobHandler delete,
    RetryTaskHandler retry,
    AddMeshProductionHandler addMesh,
    GetReviewHandler getReview,
    AddReviewPartHandler addReviewPart,
    FindOverlapsHandler findOverlaps,
    RemoveReviewPartHandler removeReviewPart,
    MoveReviewPlacementHandler moveReviewPlacement,
    ApproveReviewHandler approveReview,
    ReviewDescriptionsHandler reviewDescriptions,
    GenerateSelectedViewsHandler generateViews,
    ReturnToDescriptionsFromGenerationHandler returnToDescriptions,
    ReplanPartMeshHandler replanPartMesh) : ControllerBase
{
    /// <summary>Design Ref: §4.2 #5 — 202 접수. 워커가 비동기로 집는다.</summary>
    [HttpPost]
    public async Task<IActionResult> StartAsync(
        [FromBody] StartJobRequest request,
        CancellationToken ct)
    {
        if (!TryParseCategory(request.Category, out var category))
        {
            return ApiResults.Failure<JobAcceptedResponse>(
                ErrorCode.JobCategoryInvalid, $"알 수 없는 카테고리입니다: {request.Category}");
        }

        // 성별 파싱 실패는 여기서 걸린다 (JobGenderInvalid). "캐릭터인데 성별 없음" 은 파싱이
        // 아니라 접수 검증(StartJobHandler)의 몫이라 JobGenderRequired 로 나뉜다
        if (!TryParseGender(request.Gender, out var gender))
        {
            return ApiResults.Failure<JobAcceptedResponse>(
                ErrorCode.JobGenderInvalid, $"알 수 없는 성별입니다: {request.Gender}");
        }

        // 배열에 null 원소가 오면 매핑에서 NRE(500)가 난다 — ASP.NET 의 암묵적 required 검증은
        // 속성 누락은 잡아도 원소 자체의 null 은 놓친다. 여기서 걸러 500 대신 봉투 오류로 돌린다
        if (request.PartHints is not null && request.PartHints.Any(hint => hint is null))
        {
            return ApiResults.Failure<JobAcceptedResponse>(
                ErrorCode.JobPartHintInvalid, "파츠 힌트에 빈 항목이 있습니다");
        }

        // DTO → Application 값객체. 힌트 개수·내용 검증은 접수(StartJobHandler)가 한다
        var partHints = request.PartHints?
            .Select(hint => new PartHint(hint.Type, hint.Count, hint.Variant))
            .ToList();

        var result = await start.HandleAsync(
            category, request.UploadId, request.ProviderConfigId, request.Model, ct,
            request.ImageProviderConfigId, request.ImageModel,
            request.MeshProviderConfigId, request.MeshModel,
            gender, partHints, request.RequiresReview);

        return ApiResults.From(result, JobAcceptedResponse.From, StatusCodes.Status202Accepted);
    }

    /// <summary>Design Ref: §4.2 #6 — 폴링 대상.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
        => ApiResults.From(await get.HandleAsync(id, ct), JobResponse.From);

    /// <summary>Design Ref: §4.2 #7 — 홈 두 섹션. status=active | terminal.</summary>
    [HttpGet]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string status = "active",
        [FromQuery] string? category = null,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        var filter = status.Equals("terminal", StringComparison.OrdinalIgnoreCase)
            ? JobListFilter.Terminal
            : JobListFilter.Active;

        AssetCategory? parsedCategory = null;
        if (category is not null)
        {
            if (!TryParseCategory(category, out var wanted))
            {
                return ApiResults.Failure<JobListResponse>(
                    ErrorCode.JobCategoryInvalid, $"알 수 없는 카테고리입니다: {category}");
            }

            parsedCategory = wanted;
        }

        var page = await list.HandleAsync(filter, parsedCategory, limit, ct);

        return Ok(ApiResponse<JobListResponse>.Ok(new JobListResponse(
            page.Items.Select(JobSummaryResponse.From).ToList(), page.Total)));
    }

    /// <summary>
    /// Design Ref: §4.2 #8 — **워커를 기다리지 않고 즉시 반환한다.**
    /// 실행 중이던 공정은 다음 리스 갱신(≤15초)에 스스로 끊는다.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelAsync(Guid id, CancellationToken ct)
        => ApiResults.From(await cancel.HandleAsync(id, ct), JobAcceptedResponse.From);

    /// <summary>
    /// 끝난 작업을 목록에서 지운다.
    ///
    /// **종료 상태만 지운다** — 성공·부분 성공·실패·취소. 진행 중이면 409 다.
    /// 상태 충돌이지 요청 오류가 아니므로, 취소한 뒤 같은 요청을 보내면 성공한다.
    ///
    /// 돌려줄 값이 없어 204 다.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
        => ApiResults.NoContent(await delete.HandleAsync(id, ct));

    /// <summary>
    /// <summary>
    /// 끝난 작업에 3D 를 뒤늦게 붙인다 (사이클 #11 §6).
    ///
    /// **접수와 같은 202 다.** 화면은 기존 폴링으로 이어서 본다 — 응답 모양이 달라지면
    /// 경로마다 다른 갱신 코드를 갖게 된다 (D-10).
    /// </summary>
    [HttpPost("{jobId:guid}/mesh")]
    public async Task<IActionResult> AddMeshAsync(
        Guid jobId,
        [FromBody] AddMeshRequest request,
        CancellationToken ct)
        => ApiResults.From(
            await addMesh.HandleAsync(jobId, request.MeshProviderConfigId, request.MeshModel, ct),
            JobAcceptedResponse.From,
            StatusCodes.Status202Accepted);

    /// Design Ref: §4.2 #3 — 실패한 파츠 방향 하나만 다시 돌린다 (FR-08).
    ///
    /// **공정 단위 어휘다** (C-5). 접수와 같은 202 로, 워커가 비동기로 집는다.
    /// </summary>
    [HttpPost("{jobId:guid}/tasks/{taskId:guid}/retry")]
    public async Task<IActionResult> RetryTaskAsync(Guid jobId, Guid taskId, CancellationToken ct)
        => ApiResults.From(
            await retry.HandleAsync(jobId, taskId, ct),
            JobAcceptedResponse.From,
            StatusCodes.Status202Accepted);

    /// <summary>
    /// 검수 대기 상태 조회 (review-gate §입력→출력).
    ///
    /// Decompose 가 끝나 <c>PendingReview</c> 로 멈춘 작업만 의미가 있지만, 다른 상태의
    /// 작업을 조회해도 거부하지 않는다 — 화면이 폴링 도중 상태가 바뀌는 것을 그대로 본다.
    /// </summary>
    [HttpGet("{jobId:guid}/review")]
    public async Task<IActionResult> GetReviewAsync(Guid jobId, CancellationToken ct)
        => ApiResults.From(await getReview.HandleAsync(jobId, ct), ReviewStateResponse.From);

    /// <summary>
    /// 사각형 하나와 겹치는 파츠 이름 (occludedby-recompute §입력→출력 1).
    ///
    /// 화면이 "무엇을 가리는가" 체크박스를 그리려면 추가 전에 겹침을 알아야 한다.
    /// 순수 조회라 상태를 바꾸지 않는다. 사각형을 본문으로 받아야 해서 POST 다.
    /// </summary>
    [HttpPost("{jobId:guid}/review/parts/overlaps")]
    public async Task<IActionResult> FindOverlapsAsync(
        Guid jobId, [FromBody] FindOverlapsRequest request, CancellationToken ct)
        => ApiResults.From(
            await findOverlaps.HandleAsync(jobId, request.Bounds.ToDomain(), ct),
            names => new { overlapping = names });

    /// <summary>
    /// 검수 화면에서 사람이 사각형으로 파츠를 추가한다 (§입력→출력 1).
    ///
    /// 겹쳐도 추가된다 — 가림 관계는 <c>occludes</c> 가 정한다. 생략하면 겹치는 것
    /// 전부를 가리고, 빈 배열이면 아무것도 안 가린다.
    /// </summary>
    [HttpPost("{jobId:guid}/review/parts")]
    public async Task<IActionResult> AddReviewPartAsync(
        Guid jobId, [FromBody] AddReviewPartRequest request, CancellationToken ct)
        => ApiResults.From(
            await addReviewPart.HandleAsync(
                jobId, request.Name, request.Category, request.Bounds.ToDomain(),
                request.Description, request.Occludes, ct),
            added => ReviewPartResponse.From(added.Part, added.Occludes),
            StatusCodes.Status201Created);

    /// <summary>검수 화면에서 잘못 탐지된 파츠를 제외한다 (§입력→출력).</summary>
    [HttpDelete("{jobId:guid}/review/parts/{partId:guid}")]
    public async Task<IActionResult> RemoveReviewPartAsync(Guid jobId, Guid partId, CancellationToken ct)
        => ApiResults.NoContent(await removeReviewPart.HandleAsync(jobId, partId, ct));

    /// <summary>
    /// 검수 화면에서 상자를 끌어 옮기거나 크기를 바꾼다.
    ///
    /// 좌표만 바뀌므로 <c>PATCH</c> 다. 응답은 검수 상태 전체 — 가림 관계와 재작성 대상이
    /// 함께 달라져 화면이 부분 갱신으로는 못 맞춘다.
    /// </summary>
    [HttpPatch("{jobId:guid}/review/parts/{partId:guid}/placements/{ordinal:int}")]
    public async Task<IActionResult> MoveReviewPlacementAsync(
        Guid jobId, Guid partId, int ordinal,
        [FromBody] BoundsDto request, CancellationToken ct)
        => ApiResults.From(
            await moveReviewPlacement.HandleAsync(jobId, partId, ordinal, request.ToDomain(), ct),
            ReviewStateResponse.From);

    /// <summary>
    /// 전체 승인 (§목표). 미뤄뒀던 Generate 팬아웃이 이 한 번으로 전부 계획된다.
    /// 접수와 같은 202 다 — 워커가 비동기로 집는다.
    /// </summary>
    [HttpPost("{jobId:guid}/review/approve")]
    public async Task<IActionResult> ApproveReviewAsync(Guid jobId, CancellationToken ct)
        => ApiResults.From(
            await approveReview.HandleAsync(jobId, ct),
            JobAcceptedResponse.From,
            StatusCodes.Status202Accepted);

    /// <summary>서술 확정 (review-gate-staged 사이클 1). 미뤄뒀던 Generate 팬아웃 계획 — 202.</summary>
    [HttpPost("{jobId:guid}/review/confirm-descriptions")]
    public async Task<IActionResult> ConfirmDescriptionsAsync(Guid jobId, CancellationToken ct)
        => ApiResults.From(
            await reviewDescriptions.ConfirmAsync(jobId, ct),
            JobAcceptedResponse.From,
            StatusCodes.Status202Accepted);

    /// <summary>서술 단계에서 상자 단계로 되돌린다. 서술은 유지.</summary>
    [HttpPost("{jobId:guid}/review/return-to-boxes")]
    public async Task<IActionResult> ReturnToBoxesAsync(Guid jobId, CancellationToken ct)
        => ApiResults.From(await reviewDescriptions.ReturnToBoxesAsync(jobId, ct), ReviewStateResponse.From);

    /// <summary>서술 단계에서 파츠 서술 하나를 고친다. 응답은 검수 상태 전체.</summary>
    [HttpPut("{jobId:guid}/review/parts/{partId:guid}/description")]
    public async Task<IActionResult> EditReviewDescriptionAsync(
        Guid jobId, Guid partId, [FromBody] EditReviewDescriptionRequest request, CancellationToken ct)
        => ApiResults.From(
            await reviewDescriptions.EditDescriptionAsync(jobId, partId, request.Description, ct),
            ReviewStateResponse.From);

    /// <summary>서술 단계에서 장면 팔레트를 통째로 교체한다.</summary>
    [HttpPut("{jobId:guid}/review/palette")]
    public async Task<IActionResult> EditReviewPaletteAsync(
        Guid jobId, [FromBody] EditReviewPaletteRequest request, CancellationToken ct)
        => ApiResults.From(
            await reviewDescriptions.EditPaletteAsync(
                jobId, [.. request.Palette.Select(e => new PaletteEntry(e.Name, e.Hex))], ct),
            ReviewStateResponse.From);

    /// <summary>정면 생성 완료 후 사용자가 선택한 비정면(좌/후/우) 이미지 생성 요청 (selective-view-generation §2).</summary>
    [HttpPost("{jobId:guid}/generate-views")]
    public async Task<IActionResult> GenerateViewsAsync(
        Guid jobId, [FromBody] GenerateSelectedViewsRequest request, CancellationToken ct)
        => ApiResults.From(
            await generateViews.HandleAsync(jobId, request.Directions, ct),
            JobAcceptedResponse.From,
            StatusCodes.Status202Accepted);

    /// <summary>정면 불만족 시 특정 파츠 서술 수정 복귀 요청 (selective-view-generation §2).</summary>
    [HttpPost("{jobId:guid}/return-to-descriptions")]
    public async Task<IActionResult> ReturnToDescriptionsAsync(
        Guid jobId, [FromBody] ReturnToDescriptionsRequest request, CancellationToken ct)
        => ApiResults.From(
            await returnToDescriptions.HandleAsync(jobId, request.PartId, ct),
            JobAcceptedResponse.From,
            StatusCodes.Status202Accepted);

    /// <summary>
    /// 파츠별 "3D 전송 뷰 자유 선택 + 대칭" (spec 20260917).
    ///
    /// 접수·AddMesh 와 같은 202 다 — 워커가 비동기로 집어 3D 를 실제로 만든다. 대칭
    /// 소스 반전은 이 요청 안에서 이미 끝나 있다(ReplanPartMeshHandler 참고).
    /// </summary>
    [HttpPost("{jobId:guid}/replan-mesh")]
    public async Task<IActionResult> ReplanPartMeshAsync(
        Guid jobId, [FromBody] ReplanPartMeshRequest request, CancellationToken ct)
    {
        if (!TryParseLeftRightPlan(request.LeftRight, out var leftRight))
        {
            return ApiResults.Failure<JobAcceptedResponse>(
                ErrorCode.ReplanMeshSelectionInvalid, $"알 수 없는 좌우 선택입니다: {request.LeftRight}");
        }

        if (!TryParseBackPlan(request.Back, out var back))
        {
            return ApiResults.Failure<JobAcceptedResponse>(
                ErrorCode.ReplanMeshSelectionInvalid, $"알 수 없는 전후 선택입니다: {request.Back}");
        }

        return ApiResults.From(
            await replanPartMesh.HandleAsync(
                jobId, request.PartId, request.MeshProviderConfigId, request.MeshModel, leftRight, back, ct),
            JobAcceptedResponse.From,
            StatusCodes.Status202Accepted);
    }

    // 좌우/전후 선택도 카테고리와 같은 이유로 이름만 받는다 (숫자 문자열 방지)
    private static bool TryParseLeftRightPlan(string? value, out LeftRightPlan parsed)
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value, ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed);
    }

    private static bool TryParseBackPlan(string? value, out BackPlan parsed)
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value, ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed);
    }

    // 카테고리는 이름만 받는다 — 숫자 문자열("99")은 Enum.TryParse 를 통과해 정의되지 않은
    // 값이 백본으로 새고, "0" 은 문서화되지 않은 별칭이 된다. IsDefined 로 정의된 이름만 통과
    // (PromptsController 와 같은 가드 — asset-category-contract 공유 어휘)
    private static bool TryParseCategory(string? category, out AssetCategory parsed)
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(category)
            && !int.TryParse(category, out _)
            && Enum.TryParse(category, ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed);
    }

    // 성별은 선택이다 — 안 보내면(null·빈 문자열) 유효하고 파싱 결과도 null 이다. 값이 오면
    // 카테고리와 같은 가드로 본다: 숫자 문자열("0"·"99")은 Enum.TryParse 를 통과하므로 먼저
    // 거르고, 정의된 이름만 IsDefined 로 통과시킨다 (character-studio §5.1 독립 리뷰 #1)
    private static bool TryParseGender(string? gender, out Gender? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(gender))
        {
            return true;
        }

        // 콤마는 먼저 거른다 — Enum.TryParse 는 "Male,Male" 같은 목록을 조합해 통과시킨다(리뷰 #7)
        if (gender.Contains(',')
            || int.TryParse(gender, out _)
            || !Enum.TryParse<Gender>(gender, ignoreCase: true, out var value)
            || !Enum.IsDefined(value))
        {
            return false;
        }

        parsed = value;
        return true;
    }
}

/// <summary>
/// Design Ref: §4.2 #5
///
/// <paramref name="Model"/> — 공급자가 아니라 **작업**이 모델을 정한다. 스튜디오가
/// `GET /api/providers/{id}/models` 에서 고른 값을 그대로 보낸다.
/// </summary>
public sealed record StartJobRequest(
    string Category,
    Guid UploadId,
    Guid ProviderConfigId,
    string Model,
    /// <summary>
    /// 파츠를 그릴 공급자·모델 (사이클 #7 §4.2 #1).
    ///
    /// **선택이다** — 비우면 앞 세 단계로 끝나는 작업이 된다. 텍스트 모델 목록과 다른
    /// 엔드포인트에서 고른 값을 보낸다 (`/api/providers/{id}/image-models`).
    /// </summary>
    Guid? ImageProviderConfigId = null,
    string? ImageModel = null,
    /// <summary>
    /// 파츠를 3D 로 만들 공급자·모델 (사이클 #10 §10.2).
    ///
    /// **선택이다** — 비우면 이미지까지만 도는 기존 작업과 같다. 3D 를 고르려면
    /// 이미지 생성도 함께 골라야 한다. 그것이 3D 의 입력이기 때문이다.
    /// </summary>
    Guid? MeshProviderConfigId = null,
    string? MeshModel = null,
    /// <summary>
    /// 캐릭터 성별 (character-studio §5). **선택이다** — 캐릭터만 필수이고 그 규칙은 접수
    /// 검증이 지킨다. 파싱 실패는 <c>JobGenderInvalid</c>, 캐릭터인데 없음은 <c>JobGenderRequired</c>.
    /// </summary>
    string? Gender = null,
    /// <summary>
    /// 파츠 힌트 (character-studio §5). **선택이다.** 개수는 1~20, 범위 밖은 <c>JobPartHintInvalid</c>.
    /// </summary>
    IReadOnlyList<PartHintDto>? PartHints = null,
    /// <summary>
    /// 검수 게이트 opt-in (review-gate §목표). true 면 분해 직후 자동 팬아웃 대신
    /// 검수 대기(PendingReview)로 멈추고, <c>POST .../review/approve</c> 로만 진행한다.
    /// </summary>
    bool RequiresReview = false);

/// <summary>
/// 파츠 힌트 한 줄의 API 표현 (character-studio §5.1).
///
/// <paramref name="Type"/> 상위 종류(예: "팔찌"), <paramref name="Count"/> 개수,
/// <paramref name="Variant"/> 변형(예: "손목형", 없으면 null). 컨트롤러가 Application 값객체
/// <c>PartHint</c> 로 옮긴다.
/// </summary>
public sealed record PartHintDto(string Type, int Count, string? Variant);

/// <summary>
/// 끝난 작업에 붙일 3D 선택 (사이클 #11).
///
/// 이미지는 이미 있으므로 여기서는 3D 만 받는다.
/// </summary>
public sealed record AddMeshRequest(Guid MeshProviderConfigId, string? MeshModel);

/// <summary>
/// 정면 생성 완료 후 사용자가 선택한 비정면 이미지 방향 목록 요청 DTO (selective-view-generation §2).
/// </summary>
public sealed record GenerateSelectedViewsRequest(IReadOnlyList<string> Directions);

/// <summary>
/// 정면 불만족 시 특정 파츠 서술 수정 복귀 요청 DTO (selective-view-generation §2).
/// </summary>
public sealed record ReturnToDescriptionsRequest(Guid PartId);

/// <summary>
/// 파츠별 "3D 전송 뷰 자유 선택 + 대칭" 요청 DTO (spec 20260917).
///
/// <paramref name="LeftRight"/>/<paramref name="Back"/>는 <see cref="LeftRightPlan"/>/
/// <see cref="BackPlan"/> 이름 문자열이다("Skip", "MirrorFromLeft" 등).
/// </summary>
public sealed record ReplanPartMeshRequest(
    Guid PartId, Guid MeshProviderConfigId, string? MeshModel, string LeftRight, string Back);
