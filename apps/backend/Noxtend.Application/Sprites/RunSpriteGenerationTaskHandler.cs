using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Noxtend.Application.Common;
using Noxtend.Application.Generation;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Prompt;
using Noxtend.Domain.Sprites;

namespace Noxtend.Application.Sprites;

public sealed class RunSpriteGenerationTaskHandler(
    IJobRepository jobs, IStoredImageRepository images, IBlobStorage blobs, IImageProviderFactory providerFactory,
    IPromptCatalog prompts, IImageTranscoder transcoder, TaskExecution execution, IClock clock,
    JobOptions jobOptions, GenerationOptions options, RateLimitGate rateLimitGate,
    ILogger<RunSpriteGenerationTaskHandler> logger) : ITaskHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public async Task<RunTaskOutcome> HandleAsync(Guid taskId, CancellationToken ct)
    {
        string? savedKey = null;
        Guid jobId = default;
        try
        {
            return await execution.RunAsync(taskId, new(options.Lease, options.LeaseRenew, jobOptions.MaxAttempts),
                async (job, task, token) =>
                {
                    jobId = job.Id;
                    return await GenerateAsync(job, task, key => savedKey = key, token);
                }, Classify, ct);
        }
        finally
        {
            if (savedKey is not null)
            {
                try
                {
                    // 저장 후 응답 유실 가능성까지 실제 공개 이력으로 판정
                    var persisted = await jobs.ReloadAsync(jobId, CancellationToken.None);
                    if (persisted?.Sprites?.Images.Any(image => image.BlobKey == savedKey) != true)
                        await blobs.DeleteAsync(savedKey, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "2D 공정 {TaskId} 결과 공개 여부 또는 Blob 정리 확인 실패", taskId);
                }
            }
        }
    }

    private async Task<Action<PipelineJob>> GenerateAsync(
        PipelineJob job, PipelineTask task, Action<string> saved, CancellationToken ct)
    {
        if (task.Kind != TaskKind.GenerateSprite || task.SpriteInput is not { } input || job.Sprites is not { } state)
            throw new ProviderBadResponseException("2D 프레임 고정 입력이 필요합니다");
        var plan = input.Plan;
        var settings = state.Settings;
        var sourceCanvas = state.SourceCanvas;
        var generationCanvas = input.GenerationCanvas;
        var outputCanvas = input.Canvas;
        var phase = (double)input.FrameIndex / (plan.Loop ? plan.FrameCount : 1);
        var prompt = await prompts.GetActiveAsync(LlmOperationKind.GenerateSprite, job.Category, ct)
            ?? throw new ProviderCallFailedException("2D 생성 활성 프롬프트가 없습니다");
        var source = await images.GetAsync(job.SourceImageId, ct)
            ?? throw new ProviderCallFailedException("원본 이미지가 없습니다");
        var references = new List<ReferenceImage>
        {
            new(await ReadAsync(source.BlobKey, source.ContentType, ct), ReferenceRole.Original),
        };
        if (input.FrameIndex > 0)
        {
            var baseImage = state.Images.SingleOrDefault(image => image.Id == input.BaseImageId
                && image.AssetId == input.AssetId && image.FrameIndex == 0 && image.PlanRevision == input.PlanRevision)
                ?? throw new ProviderBadResponseException("고정 기준 이미지가 없습니다");
            references.Add(new(await ReadAsync(baseImage.BlobKey, baseImage.ContentType, ct), ReferenceRole.SpriteBase));
        }
        var variables = new Dictionary<string, string>
        {
            ["settings"] = JsonSerializer.Serialize(settings, Json),
            ["asset"] = JsonSerializer.Serialize(plan, Json),
            ["frame"] = JsonSerializer.Serialize(new { index = input.FrameIndex, count = plan.Loop ? plan.FrameCount : 1,
                phase, input.BaseImageId, generationCanvas, anchor = settings.OutputKind == SpriteOutputKind.Layers
                    ? new SpriteAnchor(0, 0) : new SpriteAnchor(outputCanvas.Width / 2d, outputCanvas.Height / 2d), input.Transform }, Json),
            ["sourceCanvas"] = JsonSerializer.Serialize(sourceCanvas, Json),
            ["outputCanvas"] = JsonSerializer.Serialize(outputCanvas, Json),
        };
        var providerId = task.ProviderConfigId ?? throw new ProviderCallFailedException("이미지 공급자가 없습니다");
        var model = task.Model ?? throw new ProviderCallFailedException("이미지 모델이 없습니다");
        var provider = await providerFactory.CreateAsync(providerId, model, ct);
        var request = new ImageRequest(new(job.Id, task.Id, null, prompt.VersionId, providerId, model, TaskKind.GenerateSprite),
            PromptTemplate.Render($"{prompt.System}\n\n{prompt.User}", variables), references,
            $"{generationCanvas.Width}x{generationCanvas.Height}", plan.RequiresTransparency ? ImageBackground.Transparent : ImageBackground.Opaque);
        await rateLimitGate.WaitIfNeededAsync(providerId, ct);
        var result = await provider.GenerateAsync(request, ct);
        await rateLimitGate.RecordAsync(providerId, new(result.RateLimitRemainingRequests, result.RateLimitResetAfter,
            result.RateLimitRemainingTokens, result.RateLimitResetTokensAfter,
            RateLimitHeaders.SumTokens(result.InputTokens, result.OutputTokens)), ct);
        if (result.ImageCount != 1 || !string.Equals(result.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
            throw new ProviderBadResponseException("PNG 이미지 한 장이 필요합니다");
        using var raw = new MemoryStream(result.Bytes, writable: false);
        var inspected = await transcoder.InspectSpriteAsync(raw, Math.Min(options.MaxImageBytes, 32L * 1024 * 1024), 16_777_216, ct);
        if (inspected.ContentType != "image/png" || inspected.Width != generationCanvas.Width || inspected.Height != generationCanvas.Height)
            throw new ProviderBadResponseException("생성 PNG 형식 또는 캔버스가 일치하지 않습니다");
        raw.Position = 0;
        var layout = settings.OutputKind == SpriteOutputKind.Tiles && settings.View == SpriteView.Isometric
            ? SpriteTileLayout.Diamond : SpriteTileLayout.Square;
        await using var normalized = await transcoder.NormalizeSpriteAsync(raw, outputCanvas, input.Transform,
            plan.RequiresTransparency, layout, ct);
        var key = await blobs.SaveAsync(normalized, "image/png", ct);
        saved(key);
        var image = SpriteImage.Create(task.Id, input, key, clock.Now);
        return current =>
        {
            if (!current.TryAttachSpriteImage(image))
                throw new InvalidOperationException("현재 슬롯에 생성 결과를 연결할 수 없습니다");
        };
    }

    private async Task<ImageContent> ReadAsync(string key, string contentType, CancellationToken ct)
    {
        await using var stream = await blobs.OpenReadAsync(key, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return new(buffer.ToArray(), contentType);
    }

    private static TaskFailure? Classify(Exception exception) => exception switch
    {
        ProviderBadResponseException => TaskFailure.Fail(ErrorCode.ProviderBadResponse),
        ProviderCallFailedException call => call.IsTransient ? TaskFailure.Retry(ErrorCode.ProviderCallFailed)
            : TaskFailure.Fail(ErrorCode.ProviderCallFailed),
        _ => null,
    };
}
