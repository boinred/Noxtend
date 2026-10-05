using Noxtend.Application.Common;
using Noxtend.Application.Generation;
using Noxtend.Domain.Common;
using Noxtend.Application.Stages;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Prompt;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 텍스트 공정 하나를 실행한다 — 워커가 큐에서 꺼낸 뒤 부르는 유스케이스.
///
/// Design Ref: §2.2 파이프라인 실행 · §2.0 (사이클 #7)
///
/// **사이클 #7 에서 실행 골격이 <see cref="TaskExecution"/> 으로 빠졌다.** 툼스톤 ·
/// <c>Claim</c> · 리스 갱신 · 취소 감시 · 재시도 한도 · 통지는 공정의 성질이지 단계의
/// 성질이 아니라, 이미지 경로와 공유해야 어긋나지 않는다. 여기 남은 것은 **단계 지식과
/// 공급자 호출** 둘뿐이다.
///
/// **단계를 모른다** (사이클 #5 G-1). 무엇을 묻고 어떻게 해석할지는 `IStage` 가 안다.
/// </summary>
public sealed class RunTaskHandler(
    IStoredImageRepository images,
    IBlobStorage blobs,
    ILlmProviderFactory providerFactory,
    IPromptCatalog prompts,
    StageRegistry stages,
    TaskExecution execution,
    JobOptions options,
    RateLimitGate rateLimitGate) : ITaskHandler
{
    public Task<RunTaskOutcome> HandleAsync(Guid taskId, CancellationToken ct)
        => execution.RunAsync(
            taskId,
            new TaskExecutionPolicy(options.Lease, options.LeaseRenew, options.MaxAttempts),
            RunStageAsync,
            Classify,
            ct);

    /// <summary>
    /// 단계를 돌려 **반영 함수**를 돌려준다.
    ///
    /// **해석과 검증이 성공 확정보다 먼저다.** 뒤에 두면 파싱 실패가 이미 종료된
    /// 공정을 실패시키지 못하고 조용히 넘어간다 (사이클 #5 에서 잡은 결함).
    /// 확정과 반영의 순서는 <see cref="TaskExecution"/> 이 지킨다.
    /// </summary>
    private async Task<Action<PipelineJob>> RunStageAsync(
        PipelineJob job, PipelineTask task, CancellationToken ct)
    {
        var stage = stages.For(task.Kind);
        var rawJson = await CallProviderAsync(job, task, stage, ct);

        return stage.Interpret(job, rawJson);
    }

    /// <summary>
    /// 텍스트 단계의 예외를 실패 코드와 재시도 여부로 옮긴다.
    ///
    /// **호출 자체가 실패한 경우와 응답 내용이 계약을 어긴 경우가 나뉜다.** 앞의 것 중
    /// 일시적인 것(500·429)은 남의 사정이라 다시 걸어보고, 인증 실패나 크레딧 부족은
    /// 몇 번을 걸어도 같으므로 즉시 확정한다. 뒤의 것은 LLM 출력이 비결정적이라
    /// 다시 돌리면 대개 성공한다.
    /// </summary>
    private static TaskFailure? Classify(Exception ex) => ex switch
    {
        // 형식은 맞는데 조립에 필요한 필드가 없다. 프롬프트가 잘못됐다면 몇 번을
        // 돌려도 같겠지만, 모델이 한 번 흘린 것일 수도 있다 — 한도가 그것을 가른다
        SceneValidationException => TaskFailure.Retry(ErrorCode.SceneIncomplete),

        // 유효성 위반 (FR-05). 공급자가 계약을 어긴 것이지 호출이 실패한 게 아니다
        PartValidationException part => TaskFailure.Retry(ToErrorCode(part.Error)),

        ProviderBadResponseException => TaskFailure.Retry(ErrorCode.ProviderBadResponse),

        ProviderCallFailedException call => call.IsTransient
            ? TaskFailure.Retry(ErrorCode.ProviderCallFailed)
            : TaskFailure.Fail(ErrorCode.ProviderCallFailed),

        _ => null,
    };

    private static string ToErrorCode(PartValidationError error) => error switch
    {
        PartValidationError.NameMismatch => ErrorCode.PartNameMismatch,
        PartValidationError.Duplicate => ErrorCode.PartDuplicate,
        PartValidationError.BoundsOutOfRange => ErrorCode.PartBoundsOutOfRange,
        PartValidationError.UnknownReference => ErrorCode.PartUnknownReference,
        PartValidationError.DepthDuplicate => ErrorCode.PartDepthDuplicate,
        _ => ErrorCode.ProviderBadResponse,
    };

    /// <summary>
    /// 프롬프트를 가져와 렌더하고 공급자를 부른다.
    ///
    /// **단계별 분기가 없다.** `IStage` 가 변수를 만들고 응답을 해석하므로, 여기서 하는
    /// 일은 어느 단계든 같다 — 프롬프트 조회 · 렌더 · 이미지 로드 · 호출.
    /// </summary>
    private async Task<string> CallProviderAsync(
        PipelineJob job,
        PipelineTask task,
        IStage stage,
        CancellationToken ct)
    {
        var prompt = await prompts.GetActiveAsync(LlmOperation.FromTask(task.Kind), job.Category, ct)
                     ?? throw new ProviderCallFailedException(
                         $"{task.Kind} 단계에 활성 프롬프트가 없습니다");

        var variables = stage.BuildVariables(job);

        var image = stage.NeedsImage ? await LoadImageAsync(job.SourceImageId, ct) : null;

        // 키 복호화와 어댑터 생성이 Infrastructure 안에서 끝난다.
        // 여기가 id 만 넘기는 것이 §2.2 의 보안 경계다 — 모델은 비밀이 아니라 값으로 넘긴다
        var providerConfigId = task.ProviderConfigId
            ?? throw new ProviderCallFailedException("공급자가 지정되지 않았습니다");
        var model = task.Model
            ?? throw new ProviderCallFailedException("모델이 지정되지 않았습니다");

        var provider = await providerFactory.CreateAsync(providerConfigId, model, ct);

        var request = new LlmRequest(
            LlmCallContext.ForTask(job.Id, task.Id, task.Kind, prompt.VersionId, providerConfigId, model),
            PromptTemplate.Render(prompt.System, variables),
            PromptTemplate.Render(prompt.User, variables),
            image is null ? [] : [new LlmImage("original", image)],
            prompt.JsonSchema);

        // 사전 예방 (text-generation-rate-limiting) — 이미지 경로(RunGenerationTaskHandler)와
        // 같은 순서. 남은 횟수가 0으로 기록돼 있으면 초기화 시각까지 여기서 기다린다
        await rateLimitGate.WaitIfNeededAsync(providerConfigId, ct);

        var result = await provider.CompleteAsync(request, ct);

        // 공급자가 이번 호출로 알려준 최신 값을 기록한다 — 다음 호출이 이걸 본다.
        // TPM(토큰)도 RPM(요청)과 독립으로 같이 싣는다 — OpenAI 는 둘 중 먼저 닿는
        // 쪽에서 429 를 낸다. CallTokens 는 다음 대기 판단의 토큰 여유분 기준이 된다
        await rateLimitGate.RecordAsync(
            providerConfigId,
            new RateLimitHeaders(
                result.RateLimitRemainingRequests,
                result.RateLimitResetAfter,
                result.RateLimitRemainingTokens,
                result.RateLimitResetTokensAfter,
                RateLimitHeaders.SumTokens(result.InputTokens, result.OutputTokens)),
            ct);

        return result.RawJson;
    }

    private async Task<ImageContent> LoadImageAsync(Guid sourceImageId, CancellationToken ct)
    {
        var image = await images.GetAsync(sourceImageId, ct)
                    ?? throw new ProviderCallFailedException("소스 이미지를 찾을 수 없습니다");

        await using var stream = await blobs.OpenReadAsync(image.BlobKey, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);

        return new ImageContent(buffer.ToArray(), image.ContentType);
    }
}
