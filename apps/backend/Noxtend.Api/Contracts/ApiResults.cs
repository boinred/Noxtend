using Microsoft.AspNetCore.Mvc;
using Noxtend.Application.Common;
using Noxtend.Domain.Common;

namespace Noxtend.Api.Contracts;

/// <summary>
/// 오류 코드 → HTTP 상태 매핑.
///
/// Design Ref: §4.0 — 매핑이 한 곳에 있어야 컨트롤러마다 같은 코드를 다른 상태로
/// 내보내는 일이 생기지 않는다. 유스케이스는 코드만 정하고 전송 계층은 여기서 붙는다.
/// </summary>
public static class ApiResults
{
    private static readonly Dictionary<string, int> StatusByCode = new()
    {
        [ErrorCode.PriceUpdateInvalid] = StatusCodes.Status400BadRequest,
        [ErrorCode.PriceUpdateNotFound] = StatusCodes.Status404NotFound,
        [ErrorCode.PriceUpdateExpired] = StatusCodes.Status409Conflict,
        [ErrorCode.PriceUpdateConflict] = StatusCodes.Status409Conflict,
        [ErrorCode.PriceUpdateRequestConflict] = StatusCodes.Status409Conflict,
        [ErrorCode.SpriteWrongMode] = StatusCodes.Status409Conflict,
        [ErrorCode.SpriteRequestConflict] = StatusCodes.Status409Conflict,
        [ErrorCode.SpriteRevisionConflict] = StatusCodes.Status409Conflict,
        [ErrorCode.SpriteBusy] = StatusCodes.Status409Conflict,
        [ErrorCode.SpriteNotReady] = StatusCodes.Status409Conflict,
        [ErrorCode.SpriteAssetNotFound] = StatusCodes.Status404NotFound,
        [ErrorCode.SpriteSettingsInvalid] = StatusCodes.Status400BadRequest,
        [ErrorCode.SpritePlanInvalid] = StatusCodes.Status400BadRequest,
        [ErrorCode.SpriteFrameInvalid] = StatusCodes.Status400BadRequest,

        [ErrorCode.UploadEmpty] = StatusCodes.Status400BadRequest,
        [ErrorCode.UploadUnsupportedType] = StatusCodes.Status400BadRequest,
        [ErrorCode.UploadTooLarge] = StatusCodes.Status400BadRequest,

        [ErrorCode.JobUploadNotFound] = StatusCodes.Status400BadRequest,
        [ErrorCode.JobProviderNotFound] = StatusCodes.Status400BadRequest,
        [ErrorCode.JobProviderDisabled] = StatusCodes.Status400BadRequest,

        [ErrorCode.JobNotFound] = StatusCodes.Status404NotFound,
        [ErrorCode.ProviderNotFound] = StatusCodes.Status404NotFound,

        // 상태 충돌이지 요청 오류가 아니다 — 같은 요청이 조금 전이었다면 성공했다
        [ErrorCode.JobAlreadyTerminal] = StatusCodes.Status409Conflict,

        // 같은 이유로 409 다 — 작업이 끝나면 같은 요청이 성공한다.
        // 400 으로 나가면 화면이 "요청이 잘못됐다" 로 읽어 사용자에게 고칠 것을 찾게 한다
        [ErrorCode.JobActiveCannotDelete] = StatusCodes.Status409Conflict,

        // 공급자 오류는 우리 잘못도 클라이언트 잘못도 아니다
        [ErrorCode.ProviderCallFailed] = StatusCodes.Status502BadGateway,
        [ErrorCode.ProviderBadResponse] = StatusCodes.Status502BadGateway,

        // ─── 사이클 #5 ───
        [ErrorCode.PromptVersionNotFound] = StatusCodes.Status404NotFound,
        [ErrorCode.GoldenNotFound] = StatusCodes.Status404NotFound,

        [ErrorCode.PromptUnknownVariable] = StatusCodes.Status400BadRequest,
        [ErrorCode.PromptSchemaInvalid] = StatusCodes.Status400BadRequest,
        [ErrorCode.GoldenImageNotFound] = StatusCodes.Status400BadRequest,
        [ErrorCode.JobModelUnavailable] = StatusCodes.Status400BadRequest,

        // 설정 문제다 — 요청은 정상이고 서버가 준비되지 않았다
        [ErrorCode.PromptNotActive] = StatusCodes.Status409Conflict,

        // 공급자에 쓸 모델이 없다. 키는 유효하므로 인증 실패와 구별된다
        [ErrorCode.ProviderNoVisionModels] = StatusCodes.Status400BadRequest,

        // ─── 사이클 #10 — 3D 재구성 ───
        [ErrorCode.JobMeshProviderNotFound] = StatusCodes.Status400BadRequest,
        [ErrorCode.JobMeshProviderDisabled] = StatusCodes.Status400BadRequest,
        [ErrorCode.JobMeshModelUnavailable] = StatusCodes.Status400BadRequest,
        [ErrorCode.JobMeshRequiresImages] = StatusCodes.Status400BadRequest,
        [ErrorCode.MeshInputMissing] = StatusCodes.Status400BadRequest,
        [ErrorCode.ReplanMeshConflict] = StatusCodes.Status409Conflict,
        [ErrorCode.GeneratedMeshNotFound] = StatusCodes.Status404NotFound,

        // **요청 오류가 아니다.** 서버가 외부 상태를 모르는 것이라, 운영자가 확인하면
        // 같은 요청이 통과할 수 있다 (§10.5)
        [ErrorCode.MeshSubmissionUnknown] = StatusCodes.Status409Conflict,

        // ─── 사이클 #11 ───
        // 요청이 틀린 게 아니라 작업 상태가 맞지 않는다. 조금 전이었다면 통과했을 수 있다
        [ErrorCode.JobMeshNotApplicable] = StatusCodes.Status409Conflict,

        // ─── background-similarity-tuning §10.2 ───
        [ErrorCode.SimilarityNotReady] = StatusCodes.Status409Conflict,
        [ErrorCode.SimilarityRunNotFound] = StatusCodes.Status404NotFound,
        [ErrorCode.SimilarityConflict] = StatusCodes.Status409Conflict,
        [ErrorCode.SimilarityRenderInvalid] = StatusCodes.Status400BadRequest,
        [ErrorCode.SimilarityRenderTooLarge] = StatusCodes.Status413PayloadTooLarge,
        [ErrorCode.SimilarityEvaluationInvalid] = StatusCodes.Status502BadGateway,
        [ErrorCode.SimilarityIterationLimit] = StatusCodes.Status409Conflict,
        [ErrorCode.SceneRevisionStale] = StatusCodes.Status409Conflict,
        // ─── review-gate ───
        // 상태 충돌이다 — 승인 이후·검수 게이트 미적용 작업에 검수 편집을 걸면 409
        [ErrorCode.ReviewNotPending] = StatusCodes.Status409Conflict,
        [ErrorCode.ReviewNotRequired] = StatusCodes.Status409Conflict,
        [ErrorCode.PartNotOverlapping] = StatusCodes.Status400BadRequest,
        [ErrorCode.PartNameDuplicate] = StatusCodes.Status409Conflict,
        [ErrorCode.PartNotFound] = StatusCodes.Status404NotFound,
        // ─── review-gate-staged ─── 단계 충돌은 상태 충돌 관례(409), 입력 위반은 400
        [ErrorCode.ReviewPhaseMismatch] = StatusCodes.Status409Conflict,
        [ErrorCode.PartDescriptionEmpty] = StatusCodes.Status400BadRequest,
        [ErrorCode.PaletteInvalid] = StatusCodes.Status400BadRequest,
    };

    public static IActionResult From<TValue, TDto>(
        Result<TValue> result,
        Func<TValue, TDto> toDto,
        int successStatus = StatusCodes.Status200OK)
    {
        if (!result.IsSuccess)
        {
            return Failure<TDto>(result.ErrorCode!, result.ErrorMessage!);
        }

        return new ObjectResult(ApiResponse<TDto>.Ok(toDto(result.Value!)))
        {
            StatusCode = successStatus,
        };
    }

    /// <summary>
    /// 돌려줄 값이 없는 성공 — 204 No Content.
    ///
    /// **봉투를 씌우지 않는다.** `{"data": true}` 는 그 `true` 가 무엇을 뜻하는지
    /// 묻게 만드는데 답이 "성공했다" 뿐이라면 상태 코드가 이미 말하고 있다.
    /// </summary>
    public static IActionResult NoContent(Result<Unit> result)
        => result.IsSuccess
            ? new StatusCodeResult(StatusCodes.Status204NoContent)
            : Failure<object>(result.ErrorCode!, result.ErrorMessage!);

    public static IActionResult Failure<TDto>(string code, string message)
        => new ObjectResult(ApiResponse<TDto>.Fail(code, message))
        {
            // 모르는 코드를 500 으로 내리면 클라이언트가 재시도해야 할지 알 수 없다.
            // 400 이 기본인 이유는 알려지지 않은 거부는 대개 요청 문제이기 때문이다
            StatusCode = StatusByCode.GetValueOrDefault(code, StatusCodes.Status400BadRequest),
        };
}
