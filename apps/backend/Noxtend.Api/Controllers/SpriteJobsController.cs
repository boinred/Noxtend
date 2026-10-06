using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Application.Sprites;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;

namespace Noxtend.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public sealed class SpriteJobsController(StartSpriteJobHandler start, SpriteCommandsHandler commands,
    IJobRepository jobs, IBlobStorage blobs) : ControllerBase
{
    [HttpPost("sprites")]
    public async Task<IActionResult> StartAsync([FromBody] StartSpriteJobRequest request, CancellationToken ct)
    {
        var settings = request.Settings?.ToDomain();
        if (settings is null) return Invalid(ErrorCode.SpriteSettingsInvalid);
        return Accepted(await start.HandleAsync(new(request.RequestId, request.UploadId, request.SourceJobId,
            request.SourceGeneratedImageId, request.ProviderConfigId, request.Model,
            request.ImageProviderConfigId, request.ImageModel, settings), ct));
    }

    [HttpPut("{id:guid}/sprites/plan")]
    public async Task<IActionResult> UpdatePlanAsync(Guid id, [FromBody] SpritePlanRequest request, CancellationToken ct)
    {
        var plans = request.Assets?.Select(asset => asset?.ToDomain()).ToArray();
        // fingerprint 직렬화 전 유한 ROI·중첩 null 차단
        if (plans is null || plans.Any(plan => plan is null)) return Invalid(ErrorCode.SpritePlanInvalid);
        return Accepted(await commands.UpdatePlanAsync(new(id, request.RequestId, request.ExpectedRevision), plans!, ct));
    }

    [HttpPost("{id:guid}/sprites/plan/approve")]
    public async Task<IActionResult> ApprovePlanAsync(Guid id, [FromBody] SpriteMutationRequest request, CancellationToken ct)
        => Accepted(await commands.ApprovePlanAsync(new(id, request.RequestId, request.ExpectedRevision), ct));

    [HttpPost("{id:guid}/sprites/base/approve")]
    public async Task<IActionResult> ApproveBasesAsync(Guid id, [FromBody] SpriteAssetsRequest request, CancellationToken ct)
        => request.AssetIds is null ? Invalid(ErrorCode.SpritePlanInvalid)
            : Accepted(await commands.ApproveBasesAsync(new(id, request.RequestId, request.ExpectedRevision), request.AssetIds, ct));

    [HttpPost("{id:guid}/sprites/assets/{assetId:guid}/frames/{index:int}/regenerate")]
    public async Task<IActionResult> RegenerateAsync(Guid id, Guid assetId, int index,
        [FromBody] SpriteMutationRequest request, CancellationToken ct)
        => Accepted(await commands.RegenerateAsync(new(id, request.RequestId, request.ExpectedRevision), assetId, index, ct));

    [HttpPost("{id:guid}/sprites/assets/{assetId:guid}/approve")]
    public async Task<IActionResult> ApproveAssetAsync(Guid id, Guid assetId,
        [FromBody] SpriteMutationRequest request, CancellationToken ct)
        => Accepted(await commands.ApproveAssetAsync(new(id, request.RequestId, request.ExpectedRevision), assetId, ct));

    [HttpPost("{id:guid}/sprites/exports")]
    public async Task<IActionResult> ExportAsync(Guid id, [FromBody] SpriteAssetsRequest request, CancellationToken ct)
        => request.AssetIds is null ? Invalid(ErrorCode.SpritePlanInvalid)
            : Accepted(await commands.ExportAsync(new(id, request.RequestId, request.ExpectedRevision), request.AssetIds, ct));

    [HttpGet("{id:guid}/sprites/images/{imageId:guid}")]
    public async Task<IActionResult> GetImageAsync(Guid id, Guid imageId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(id, ct);
        if (job is null) return Missing();
        if (job.ProductionMode != ProductionMode.TwoD || job.Sprites is null) return WrongMode();
        var image = job.Sprites.Images.FirstOrDefault(image => image.Id == imageId);
        if (image is null) return Missing();
        return await DownloadAsync(image.BlobKey, "image/png", $"sprite-{image.Id}.png", ct);
    }

    [HttpGet("{id:guid}/sprites/exports/{exportId:guid}")]
    public async Task<IActionResult> GetExportAsync(Guid id, Guid exportId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(id, ct);
        if (job is null) return Missing();
        if (job.ProductionMode != ProductionMode.TwoD || job.Sprites is null) return WrongMode();
        var export = job.Sprites.Exports.FirstOrDefault(export => export.Id == exportId);
        if (export is not null)
            return await DownloadAsync(export.BlobKey, "application/zip", $"sprites-{export.Id}.zip", ct);
        // 접수된 고정 snapshot은 ZIP 저장 전까지 다운로드 불가
        return job.Tasks.Any(task => task.SpriteExportInput?.ExportId == exportId)
            ? ApiResults.Failure<object>(ErrorCode.SpriteNotReady, "패키지가 완료되지 않았습니다")
            : Missing();
    }

    private async Task<IActionResult> DownloadAsync(string key, string contentType, string filename, CancellationToken ct)
    {
        try
        {
            var stream = await blobs.OpenReadAsync(key, ct);
            Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return File(stream, contentType, filename);
        }
        catch (FileNotFoundException) { return NotFound(); }
    }

    private static IActionResult Accepted(Result<SpriteReceipt> result)
        => ApiResults.From(result, SpriteAcceptedResponse.From, StatusCodes.Status202Accepted);
    private static IActionResult Invalid(string code) => ApiResults.Failure<object>(code, "2D 요청 입력이 유효하지 않습니다");
    private static IActionResult WrongMode() => ApiResults.Failure<object>(ErrorCode.SpriteWrongMode, "2D 배경 작업이 필요합니다");
    private static IActionResult Missing() => ApiResults.Failure<object>(ErrorCode.JobNotFound, "작업에 속한 결과를 찾을 수 없습니다");
}
