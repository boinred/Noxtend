using Microsoft.Extensions.Logging;
using Noxtend.Application.Common;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Prompt;

namespace Noxtend.Application.Generation;

/// <summary>
/// 파츠 이미지 공정 하나를 실행한다.
///
/// Design Ref: §2.0 · §2.2 · §9.1
///
/// **실행 골격을 <see cref="TaskExecution"/> 과 공유한다.** 툼스톤 · <c>Claim</c> ·
/// 리스 갱신 · 취소 감시 · 재시도 한도 · 통지는 공정의 성질이지 단계의 성질이 아니다.
/// 여기 있는 것은 **단계 지식과 공급자 호출** 둘뿐이고, 그것이 텍스트 경로와 다른 전부다.
///
/// 순서가 규칙이다: **Blob 저장이 성공 확정보다 앞이고, 파츠 연결은 뒤다.**
/// 저장이 실패하면 공정이 실패해야 하는데, 성공 확정 뒤에 저장하면 이미 종료된 공정을
/// 실패시킬 수 없다 (사이클 #5 가 `IStage` 2단계 규약으로 막은 결함과 같은 모양).
/// </summary>
public sealed class RunGenerationTaskHandler(
    IStoredImageRepository images,
    IBlobStorage blobs,
    IImageProviderFactory providerFactory,
    IPromptCatalog prompts,
    GenerationStage stage,
    TaskExecution execution,
    IClock clock,
    JobOptions jobOptions,
    GenerationOptions options,
    RateLimitGate rateLimitGate,
    ILogger<RunGenerationTaskHandler> logger) : ITaskHandler
{
    public Task<RunTaskOutcome> HandleAsync(Guid taskId, CancellationToken ct)
        => execution.RunAsync(
            taskId,
            // 리스는 생성 전용이고 재시도 한도는 공유한다 (§2.3 A-7)
            new TaskExecutionPolicy(options.Lease, options.LeaseRenew, jobOptions.MaxAttempts),
            GenerateAsync,
            Classify,
            ct);

    private async Task<Action<PipelineJob>> GenerateAsync(
        PipelineJob job, PipelineTask task, CancellationToken ct)
    {
        var part = ResolvePart(job, task);

        var prompt = await prompts.GetActiveAsync(LlmOperationKind.Generate, job.Category, ct)
                     ?? throw new ProviderCallFailedException(
                         "Generate 단계에 활성 프롬프트가 없습니다");

        var providerConfigId = task.ProviderConfigId
            ?? throw new ProviderCallFailedException("이미지 공급자가 지정되지 않았습니다");
        var model = task.Model
            ?? throw new ProviderCallFailedException("이미지 모델이 지정되지 않았습니다");

        // 배포 전 계획된 생성 공정은 방향이 없다 — 정면으로 완주시키는 호환 경로
        var viewDirection = task.ViewDirection ?? ViewDirection.Front;
        var variables = stage.BuildVariables(job, part, viewDirection);

        // 참조 목록 (Plan D-5 · workstream B §5.4). 정면에 의존하는 공정만 정면 결과도 참조한다
        var references = await LoadReferencesAsync(job, task, ct);

        // 키 복호화와 어댑터 생성이 Infrastructure 안에서 끝난다 — 여기가 id 만 넘기는
        // 것이 보안 경계다 (NFR-05)
        var provider = await providerFactory.CreateAsync(providerConfigId, model, ct);

        var request = new ImageRequest(
            new ImageCallContext(
                job.Id, task.Id, part.Id, prompt.VersionId, providerConfigId, model),
            // 이미지 공급자는 system/user 를 나누지 않는다 — 하나로 합쳐 보낸다
            PromptTemplate.Render($"{prompt.System}\n\n{prompt.User}", variables),
            references,
            options.Size);

        // 사전 예방 (generation-rate-limiting §3①) — 남은 횟수가 0으로 기록돼 있으면
        // 초기화 시각까지 여기서 기다린다. 정보가 없거나 여유가 있으면 곧바로 진행한다
        await rateLimitGate.WaitIfNeededAsync(providerConfigId, ct);

        var result = await provider.GenerateAsync(request, ct);

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

        // **검증이 저장보다 먼저다.** 쓸 수 없는 바이트를 Blob 에 올리면 지워지지 않는다
        stage.Validate(result);

        // **저장이 성공 확정보다 먼저다.** 키는 서버가 만든다 (C-1 · NFR-08)
        var blobKey = await SaveAsync(result, ct);

        // 반영은 순수 대입이다 — TaskExecution 이 성공 확정 뒤에 부른다
        var now = clock.Now;
        return target => target.AttachGeneratedImage(
            part.Id, task.Id, blobKey, result.ContentType, result.Bytes.Length, now);
    }

    /// <summary>
    /// 이 공정이 그리는 파츠.
    ///
    /// 공정이 파츠를 가리키는 방향이므로(§3.1) 여기서 되찾는다. 못 찾으면 계획과 파츠가
    /// 어긋난 것이라 다시 걸어도 같다 — 확정 실패다.
    /// </summary>
    private static AssetPart ResolvePart(PipelineJob job, PipelineTask task)
    {
        if (task.PartId is not { } partId)
        {
            throw new ProviderCallFailedException("생성 공정에 파츠가 지정되지 않았습니다");
        }

        return job.Parts.FirstOrDefault(p => p.Id == partId)
            ?? throw new ProviderCallFailedException($"작업에 없는 파츠입니다: {partId}");
    }

    /// <summary>
    /// 이 공정이 보낼 참조 목록을 순서대로 만든다 (workstream B §5.4, 2026-08-15 개정).
    ///
    /// **판정 기준은 "정면 이미지가 존재하는가"가 아니라 "이 공정이 정면 공정에 의존하도록
    /// 계획됐는가"다.** 존재 여부로 판정한 첫 버전은 레이스가 있었다 — 배경·소품은 넷이
    /// 분해 하나에 병렬로 의존하므로(체이닝 없음) 워커 타이밍에 따라 정면이 다른 방향보다
    /// 먼저 끝날 수 있고, 그러면 비정면이 이미 저장된 정면 이미지를 우연히 주웠다.
    ///
    /// `DependsOnTaskId` 는 계획 시점에 고정되고 이후 안 바뀌므로(`PipelineTask.cs`) 이
    /// 판정은 타이밍과 무관하게 항상 맞다 — 배경·소품 비정면은 분해를 가리켜 절대 참이
    /// 되지 않고, 캐릭터 비정면만 정면 공정을 가리킨다(§5.3). 카테고리 분기는 여전히
    /// `PlanGenerationFanOut` 한 곳뿐이다(§2.4·§3 "국소화" 유지).
    /// </summary>
    private async Task<IReadOnlyList<ReferenceImage>> LoadReferencesAsync(
        PipelineJob job, PipelineTask task, CancellationToken ct)
    {
        var references = new List<ReferenceImage>();

        var frontTask = task.DependsOnTaskId is { } dependsOnTaskId
            ? job.Tasks.FirstOrDefault(t =>
                t.Id == dependsOnTaskId && t.Kind == TaskKind.Generate
                && t.ViewDirection == ViewDirection.Front)
            : null;

        if (frontTask is not null)
        {
            // 동률(같은 클럭 틱에 재시도가 몰리는 경우)이면 CreatedAt 만으로는 어느 행이
            // 오는지 EF/컬렉션 로드 순서에 달린다 — Id 로 결정적 타이브레이크한다(독립 리뷰 지적)
            var frontImage = job.GeneratedImages
                .Where(image => image.TaskId == frontTask.Id)
                .OrderByDescending(image => image.CreatedAt)
                .ThenByDescending(image => image.Id)
                .FirstOrDefault();

            if (frontImage is not null)
            {
                var frontContent = await LoadBlobAsync(frontImage.BlobKey, frontImage.ContentType, ct);
                references.Add(new ReferenceImage(frontContent, ReferenceRole.FrontView));
            }
            else
            {
                // 정면 의존 공정인데 이미지가 없다 — IsReadyToRun 이 정면 Succeeded 를
                // 보장하므로 정상 경로에서는 일어나지 않는다. 로그 없이 원본만으로
                // 조용히 저하시키면 일관성 기능이 꺼진 채로 결과는 정상처럼 보인다(독립 리뷰 지적)
                logger.LogWarning(
                    "공정 {TaskId} 가 정면 공정 {FrontTaskId} 에 의존하지만 생성 이미지가 없습니다 " +
                    "— 원본만으로 저하합니다",
                    task.Id, frontTask.Id);
            }
        }

        if (await LoadOriginalAsync(job.SourceImageId, ct) is { } original)
        {
            references.Add(new ReferenceImage(original, ReferenceRole.Original));
        }

        return references;
    }

    private async Task<ImageContent?> LoadOriginalAsync(Guid sourceImageId, CancellationToken ct)
    {
        var image = await images.GetAsync(sourceImageId, ct);
        if (image is null)
        {
            return null;   // 원본이 사라졌어도 장면 명세와 서술만으로 그린다
        }

        return await LoadBlobAsync(image.BlobKey, image.ContentType, ct);
    }

    private async Task<ImageContent> LoadBlobAsync(string blobKey, string contentType, CancellationToken ct)
    {
        await using var stream = await blobs.OpenReadAsync(blobKey, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);

        return new ImageContent(buffer.ToArray(), contentType);
    }

    private async Task<string> SaveAsync(ImageResult result, CancellationToken ct)
    {
        using var stream = new MemoryStream(result.Bytes);
        return await blobs.SaveAsync(stream, result.ContentType, ct);
    }

    /// <summary>
    /// 생성 단계의 예외를 실패 코드와 재시도 여부로 옮긴다.
    ///
    /// <see cref="GenerationException"/> 은 판정을 이미 들고 온다 — 크기 초과만 즉시
    /// 실패라는 사실이 <see cref="GenerationStage"/> 한 곳에 남는다.
    /// </summary>
    private static TaskFailure? Classify(Exception ex) => ex switch
    {
        GenerationException generation => new TaskFailure(generation.Code, generation.Disposition),

        ProviderBadResponseException => TaskFailure.Retry(ErrorCode.GenerationEmptyResponse),

        ProviderCallFailedException call => call.IsTransient
            ? TaskFailure.Retry(ErrorCode.ProviderCallFailed)
            : TaskFailure.Fail(ErrorCode.ProviderCallFailed),

        _ => null,
    };
}
