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

public sealed class RunSpriteSourceGenerationTaskHandler(
    IJobRepository jobs, IBlobStorage blobs, IImageProviderFactory providerFactory,
    IPromptCatalog prompts, IImageTranscoder transcoder, TaskExecution execution, IClock clock,
    JobOptions jobOptions, GenerationOptions options, RateLimitGate rateLimitGate,
    ILogger<RunSpriteSourceGenerationTaskHandler> logger) : ITaskHandler
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
                    if (persisted?.Sprites?.SourceDraft?.Candidates.Any(candidate => candidate.BlobKey == savedKey) != true)
                        await blobs.DeleteAsync(savedKey, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "원본 후보 공정 {TaskId} 결과 공개 여부 또는 Blob 정리 확인 실패", taskId);
                }
            }
        }
    }

    private async Task<Action<PipelineJob>> GenerateAsync(
        PipelineJob job, PipelineTask task, Action<string> saved, CancellationToken ct)
    {
        if (task.Kind != TaskKind.GenerateSpriteSource || task.SourceInput?.Prompt is not { } fixedPrompt
            || job.Sprites is not { } state)
            throw new ProviderCallFailedException("원본 생성 고정 입력이 필요합니다");
        var prompt = await prompts.GetActiveAsync(LlmOperationKind.GenerateSpriteSource, job.Category, ct)
            ?? throw new ProviderCallFailedException("원본 생성 활성 프롬프트가 없습니다");
        var variables = new Dictionary<string, string>
        {
            ["prompt"] = fixedPrompt,
            ["settings"] = JsonSerializer.Serialize(state.Settings, Json),
        };
        var providerId = task.ProviderConfigId ?? throw new ProviderCallFailedException("이미지 공급자가 없습니다");
        var model = task.Model ?? throw new ProviderCallFailedException("이미지 모델이 없습니다");
        var provider = await providerFactory.CreateAsync(providerId, model, ct);
        // 원본 확정 전 SourceCanvas는 모델 생성 크기로 잠정 고정된 값
        var size = state.SourceCanvas;
        var request = new ImageRequest(new(job.Id, task.Id, null, prompt.VersionId, providerId, model, TaskKind.GenerateSpriteSource),
            PromptTemplate.Render($"{prompt.System}\n\n{prompt.User}", variables), [],
            $"{size.Width}x{size.Height}", ImageBackground.Opaque);
        await rateLimitGate.WaitIfNeededAsync(providerId, ct);
        var result = await provider.GenerateAsync(request, ct);
        await rateLimitGate.RecordAsync(providerId, new(result.RateLimitRemainingRequests, result.RateLimitResetAfter,
            result.RateLimitRemainingTokens, result.RateLimitResetTokensAfter,
            RateLimitHeaders.SumTokens(result.InputTokens, result.OutputTokens)), ct);
        // 장수·형식 위반은 유료 재호출을 막기 위해 재시도 없이 실패
        if (result.ImageCount != 1) throw new ProviderBadResponseException("이미지 한 장이 필요합니다");
        using var raw = new MemoryStream(result.Bytes, writable: false);
        // 선택 단계와 같은 상한으로 선택 불가 후보 차단
        var inspected = await transcoder.InspectSpriteAsync(raw, SpriteSourceInspector.MaxBytes, 16_777_216, ct);
        raw.Position = 0;
        var key = await blobs.SaveAsync(raw, inspected.ContentType, ct);
        saved(key);
        var candidate = new SpriteSourceCandidate(Guid.NewGuid(), key, inspected.ContentType,
            inspected.Width, inspected.Height, fixedPrompt, clock.Now);
        // 현재 공정이 아니거나 상한이면 false, 저장 Blob은 finally에서 정리
        return current => current.TryAddSourceCandidate(task.Id, candidate);
    }

    private static TaskFailure? Classify(Exception exception) => exception switch
    {
        ProviderBadResponseException => TaskFailure.Fail(ErrorCode.ProviderBadResponse),
        ProviderCallFailedException call => call.IsTransient ? TaskFailure.Retry(ErrorCode.ProviderCallFailed)
            : TaskFailure.Fail(ErrorCode.ProviderCallFailed),
        _ => null,
    };
}
