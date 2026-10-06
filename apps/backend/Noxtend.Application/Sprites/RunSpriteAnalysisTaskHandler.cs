using System.Text.Json;
using System.Text.Json.Serialization;
using Noxtend.Application.Common;
using Noxtend.Application.Generation;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Prompt;

namespace Noxtend.Application.Sprites;

public sealed class RunSpriteAnalysisTaskHandler(
    IStoredImageRepository images, IBlobStorage blobs, ILlmProviderFactory providerFactory,
    IPromptCatalog prompts, TaskExecution execution, JobOptions options, RateLimitGate rateLimitGate) : ITaskHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public Task<RunTaskOutcome> HandleAsync(Guid taskId, CancellationToken ct)
        => execution.RunAsync(taskId, new(options.Lease, options.LeaseRenew, options.MaxAttempts), AnalyzeAsync, Classify, ct);

    private async Task<Action<PipelineJob>> AnalyzeAsync(PipelineJob job, PipelineTask task, CancellationToken ct)
    {
        if (task.Kind != TaskKind.AnalyzeSprites || job.Sprites is null)
            throw new ProviderBadResponseException("2D 분석 공정이 필요합니다");
        var prompt = await prompts.GetActiveAsync(LlmOperationKind.AnalyzeSprites, job.Category, ct)
            ?? throw new ProviderCallFailedException("2D 분석 활성 프롬프트가 없습니다");
        var image = await images.GetAsync(job.SourceImageId, ct)
            ?? throw new ProviderCallFailedException("입력 이미지가 없습니다");
        await using var stream = await blobs.OpenReadAsync(image.BlobKey, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var variables = new Dictionary<string, string>
        {
            ["settings"] = JsonSerializer.Serialize(job.Sprites.Settings, Json),
            ["sourceCanvas"] = JsonSerializer.Serialize(job.Sprites.SourceCanvas, Json),
        };
        var providerId = task.ProviderConfigId ?? throw new ProviderCallFailedException("분석 공급자가 없습니다");
        var model = task.Model ?? throw new ProviderCallFailedException("분석 모델이 없습니다");
        var provider = await providerFactory.CreateAsync(providerId, model, ct);
        var request = new LlmRequest(LlmCallContext.ForTask(job.Id, task.Id, task.Kind, prompt.VersionId, providerId, model),
            PromptTemplate.Render(prompt.System, variables), PromptTemplate.Render(prompt.User, variables),
            [new LlmImage("original", new ImageContent(buffer.ToArray(), image.ContentType))], prompt.JsonSchema);
        await rateLimitGate.WaitIfNeededAsync(providerId, ct);
        var response = await provider.CompleteAsync(request, ct);
        await rateLimitGate.RecordAsync(providerId, new(response.RateLimitRemainingRequests, response.RateLimitResetAfter,
            response.RateLimitRemainingTokens, response.RateLimitResetTokensAfter,
            RateLimitHeaders.SumTokens(response.InputTokens, response.OutputTokens)), ct);
        var parsed = SpritePlanParser.Parse(response.RawJson, job.Sprites.Settings, job.Sprites.SourceCanvas);
        if (!parsed.IsSuccess) throw new ProviderBadResponseException(parsed.ErrorMessage!);
        return current => current.ReplaceSpritePlan(parsed.Value!, current.Sprites!.ReviewRevision);
    }

    private static TaskFailure? Classify(Exception exception) => exception switch
    {
        ProviderBadResponseException => TaskFailure.Retry(ErrorCode.SpritePlanInvalid),
        ProviderCallFailedException call => call.IsTransient ? TaskFailure.Retry(ErrorCode.ProviderCallFailed)
            : TaskFailure.Fail(ErrorCode.ProviderCallFailed),
        _ => null,
    };
}
