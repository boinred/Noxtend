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
using Noxtend.Domain.Sprites;

namespace Noxtend.Application.Sprites;

public sealed class RunSpriteSourceEnhanceTaskHandler(
    ILlmProviderFactory providerFactory, IPromptCatalog prompts, TaskExecution execution,
    JobOptions options, RateLimitGate rateLimitGate) : ITaskHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public Task<RunTaskOutcome> HandleAsync(Guid taskId, CancellationToken ct)
        => execution.RunAsync(taskId, new(options.Lease, options.LeaseRenew, options.MaxAttempts), EnhanceAsync, Classify, ct);

    private async Task<Action<PipelineJob>> EnhanceAsync(PipelineJob job, PipelineTask task, CancellationToken ct)
    {
        if (task.Kind != TaskKind.EnhanceSpriteSource || task.SourceInput?.Description is not { } description
            || job.Sprites is not { } state)
            throw new ProviderCallFailedException("원본 보완 고정 입력이 필요합니다");
        var prompt = await prompts.GetActiveAsync(LlmOperationKind.EnhanceSpriteSource, job.Category, ct)
            ?? throw new ProviderCallFailedException("원본 보완 활성 프롬프트가 없습니다");
        var variables = new Dictionary<string, string>
        {
            ["description"] = description,
            ["settings"] = JsonSerializer.Serialize(state.Settings, Json),
        };
        var providerId = task.ProviderConfigId ?? throw new ProviderCallFailedException("보완 공급자가 없습니다");
        var model = task.Model ?? throw new ProviderCallFailedException("보완 모델이 없습니다");
        var provider = await providerFactory.CreateAsync(providerId, model, ct);
        var request = new LlmRequest(LlmCallContext.ForTask(job.Id, task.Id, task.Kind, prompt.VersionId, providerId, model),
            PromptTemplate.Render(prompt.System, variables), PromptTemplate.Render(prompt.User, variables),
            [], prompt.JsonSchema);
        await rateLimitGate.WaitIfNeededAsync(providerId, ct);
        var response = await provider.CompleteAsync(request, ct);
        await rateLimitGate.RecordAsync(providerId, new(response.RateLimitRemainingRequests, response.RateLimitResetAfter,
            response.RateLimitRemainingTokens, response.RateLimitResetTokensAfter,
            RateLimitHeaders.SumTokens(response.InputTokens, response.OutputTokens)), ct);
        var text = ParsePrompt(response.RawJson);
        // 현재 공정이 아니면 TryApplySourcePrompt가 반영 없이 false
        return current => current.TryApplySourcePrompt(task.Id, text);
    }

    private static string ParsePrompt(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement is { ValueKind: JsonValueKind.Object } root
                && root.TryGetProperty("prompt", out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString()?.Trim() is { } text && SpriteSourceDraft.ValidPrompt(text))
                return text;
        }
        catch (JsonException) { }
        throw new ProviderBadResponseException("보완 응답은 1~4000자 prompt 문자열이 필요합니다");
    }

    private static TaskFailure? Classify(Exception exception) => exception switch
    {
        ProviderBadResponseException => TaskFailure.Retry(ErrorCode.ProviderBadResponse),
        ProviderCallFailedException call => call.IsTransient ? TaskFailure.Retry(ErrorCode.ProviderCallFailed)
            : TaskFailure.Fail(ErrorCode.ProviderCallFailed),
        _ => null,
    };
}
