using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Application.Uploads;
using Noxtend.Application.Sprites;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Validation;

namespace Noxtend.Api.Controllers;

/// <summary>
/// Design Ref: §4.2 #2~#4
///
/// **서버가 다시 검증한다.** 프론트에 같은 규칙이 있지만 클라이언트를 신뢰하지 않는다 (§2.2).
/// </summary>
[ApiController]
[Route("api/uploads")]
public sealed class UploadsController(
    CreateUploadHandler createUpload,
    IStoredImageRepository images,
    IBlobStorage blobs,
    GenerateSpriteSourceHandler generateSpriteSource) : ControllerBase
{
    /// <summary>Design Ref: §4.2 #2 — multipart/form-data, 필드 `file`.</summary>
    [HttpPost]
    // Kestrel 기본 상한(약 28 MB)보다 낮게 잡아 도메인 규칙이 실질 상한이 되게 한다.
    // 여유를 조금 두는 이유는 multipart 경계·헤더가 본문에 더해지기 때문이다
    [RequestSizeLimit(UploadRules.MaxImageBytes + 1024 * 1024)]
    public async Task<IActionResult> CreateAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null)
        {
            return ApiResults.Failure<UploadResponse>(ErrorCode.UploadEmpty, "파일이 없습니다");
        }

        await using var content = file.OpenReadStream();

        var result = await createUpload.HandleAsync(
            content, file.FileName, file.ContentType, file.Length, ct);

        return ApiResults.From(result, UploadResponse.From, StatusCodes.Status201Created);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateAsync(GenerateSpriteSourceRequest request, CancellationToken ct)
    {
        var result = await generateSpriteSource.HandleAsync(
            new(request.RequestId, request.Prompt, request.ImageProviderConfigId, request.ImageModel), ct);
        return ApiResults.From(result, UploadResponse.From, StatusCodes.Status201Created);
    }

    /// <summary>Design Ref: §4.2 #3 — 메타 조회.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var image = await images.GetAsync(id, ct);

        return image is null
            ? ApiResults.Failure<UploadResponse>(ErrorCode.JobUploadNotFound, "업로드를 찾을 수 없습니다")
            : Ok(ApiResponse<UploadResponse>.Ok(UploadResponse.From(image)));
    }

    /// <summary>
    /// Design Ref: §4.2 #4 — 이미지 스트림.
    ///
    /// 봉투를 쓰지 않는 유일한 엔드포인트다. 바이너리를 JSON 에 담을 수 없고,
    /// 화면이 `&lt;img src&gt;` 로 직접 건다.
    /// </summary>
    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> GetContentAsync(Guid id, CancellationToken ct)
    {
        var image = await images.GetAsync(id, ct);
        if (image is null)
        {
            return NotFound();
        }

        var stream = await blobs.OpenReadAsync(image.BlobKey, ct);
        return File(stream, image.ContentType);
    }
}
