using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Noxtend.Application.Job;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;

namespace Noxtend.Application.Sprites;

public sealed class SpriteSourceCommandsHandler(
    IJobRepository jobs, IStoredImageRepository images, IBlobStorage blobs,
    IProviderConfigRepository providers, IModelCatalog catalog, IPromptCatalog prompts,
    IImageTranscoder transcoder, JobOrchestrator orchestrator, IClock clock, ILogger<SpriteSourceCommandsHandler> logger)
{
    public async Task<Result<SpriteReceipt>> CreateDraftAsync(CreateSpriteDraftCommand command, CancellationToken ct)
    {
        if (command.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(command.Model) || string.IsNullOrWhiteSpace(command.ImageModel))
            return Fail(ErrorCode.SpriteSettingsInvalid, "요청 ID와 모델이 필요합니다");
        if (!SpriteSourceDraft.ValidDescription(command.Description))
            return Fail(ErrorCode.SpriteSettingsInvalid, "설명은 1~1000자여야 합니다");
        command = command with { Model = command.Model.Trim(), ImageModel = command.ImageModel.Trim() };
        var validation = SpriteRules.ValidateSettings(command.Settings, new(1, 1));
        if (!validation.IsSuccess) return Fail(validation.ErrorCode!, validation.ErrorMessage!);
        var fingerprint = Fingerprint(new { kind = SpriteRequestKind.CreateDraft, command });

        if (await jobs.GetSpriteRequestAsync(command.RequestId, ct) is { } accepted)
            return Receipt(accepted, accepted.JobId, SpriteRequestKind.CreateDraft, fingerprint);

        var provider = await providers.GetAsync(command.ProviderConfigId, ct);
        if (provider is null) return Fail(ErrorCode.JobProviderNotFound, "텍스트 공급자가 없습니다");
        if (!provider.IsEnabled) return Fail(ErrorCode.JobProviderDisabled, "텍스트 공급자가 중지되었습니다");
        if (!(await catalog.ListAsync(provider.Id, ct)).Any(m => m.Id == command.Model))
            return Fail(ErrorCode.JobModelUnavailable, "텍스트 모델을 사용할 수 없습니다");
        var imageProvider = await providers.GetAsync(command.ImageProviderConfigId, ct);
        if (imageProvider is null) return Fail(ErrorCode.JobImageProviderNotFound, "이미지 공급자가 없습니다");
        if (!imageProvider.IsEnabled) return Fail(ErrorCode.JobImageProviderDisabled, "이미지 공급자가 중지되었습니다");
        var sizes = await SizesAsync(imageProvider.Id, command.ImageModel, ct);
        if (sizes is null) return Fail(ErrorCode.JobImageModelUnavailable, "투명 지원과 생성 크기가 확인된 이미지 모델이 필요합니다");
        foreach (var kind in new[] { LlmOperationKind.EnhanceSpriteSource, LlmOperationKind.GenerateSpriteSource, LlmOperationKind.AnalyzeSprites })
            if (await prompts.GetActiveAsync(kind, AssetCategory.Background, ct) is null)
                return Fail(ErrorCode.PromptNotActive, "2D 원본 준비·분석 활성 프롬프트가 없습니다");

        // 원본 확정 전 잠정 캔버스
        var provisional = SpriteSourceSizes.Pick(sizes);
        var generation = SpriteRules.GenerationCanvas(SpriteRules.OutputCanvas(command.Settings, provisional), sizes);
        var created = PipelineJob.CreateSpriteDraft(imageProvider.Id, command.ImageModel, command.Settings, provisional,
            generation, new(command.Description, null, provider.Id, command.Model, []), clock.Now);
        if (!created.IsSuccess) return Fail(created.ErrorCode!, created.ErrorMessage!);
        var job = created.Value!;
        var task = job.PlanSourceEnhance(command.Description, command.RequestId);
        if (!task.IsSuccess) return Fail(task.ErrorCode!, task.ErrorMessage!);
        var receipt = new SpriteReceipt(job.Id, job.Status, job.Sprites!.ReviewRevision, [task.Value!.Id]);
        job.AcceptSpriteRequest(SpriteAcceptedRequest.Create(command.RequestId, job.Id, SpriteRequestKind.CreateDraft, fingerprint, receipt));
        try
        {
            // 작업·공정·접수 응답의 단일 SaveChanges
            await jobs.AddAsync(job, ct);
            await jobs.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            var winner = await jobs.GetSpriteRequestAsync(command.RequestId, ct);
            return winner is null ? Fail(ErrorCode.SpriteRevisionConflict, "접수 상태가 변경되었습니다")
                : Receipt(winner, winner.JobId, SpriteRequestKind.CreateDraft, fingerprint);
        }
        await orchestrator.StartAsync(job, ct);
        return Result<SpriteReceipt>.Ok(receipt);
    }

    public Task<Result<SpriteReceipt>> EnhanceAsync(SpriteCommandContext context, string description, CancellationToken ct)
        => ApplyAsync(context, SpriteRequestKind.EnhanceSource, description, true,
            job => Task.FromResult(One(job.PlanSourceEnhance(description, context.RequestId))), null, ct);

    public Task<Result<SpriteReceipt>> SavePromptAsync(SpriteCommandContext context, string prompt, CancellationToken ct)
        => ApplyAsync(context, SpriteRequestKind.SaveSourcePrompt, prompt, false, job =>
        {
            var saved = job.SaveSourcePrompt(prompt);
            return Task.FromResult(saved.IsSuccess ? Result<IReadOnlyList<PipelineTask>>.Ok([])
                : Result<IReadOnlyList<PipelineTask>>.Fail(saved.ErrorCode!, saved.ErrorMessage!));
        }, null, ct);

    public Task<Result<SpriteReceipt>> GenerateAsync(SpriteCommandContext context, string prompt, CancellationToken ct)
        => ApplyAsync(context, SpriteRequestKind.GenerateSource, prompt, false,
            job => Task.FromResult(One(job.PlanSourceGeneration(prompt, context.RequestId))), null, ct);

    public Task<Result<SpriteReceipt>> SelectAsync(SpriteCommandContext context, Guid candidateId, CancellationToken ct)
    {
        string? copiedKey = null;
        return ApplyAsync(context, SpriteRequestKind.SelectSource, candidateId, true, async job =>
        {
            var sprites = job.Sprites!;
            if (sprites.Phase != SpritePhase.SourceDraft || sprites.SourceDraft is not { } draft)
                return Reject(ErrorCode.SpriteNotReady, "원본 준비 단계가 아닙니다");
            var candidate = draft.Candidates.FirstOrDefault(c => c.Id == candidateId);
            if (candidate is null) return Reject(ErrorCode.JobUploadNotFound, "작업에 속한 원본 후보를 찾을 수 없습니다");
            var sizes = await SizesAsync(job.ImageProviderConfigId!.Value, job.ImageModel!, ct);
            if (sizes is null) return Reject(ErrorCode.JobImageModelUnavailable, "투명 지원과 생성 크기가 확인된 이미지 모델이 필요합니다");
            var inspected = await SpriteSourceInspector.InspectAsync(blobs, transcoder, candidate.BlobKey, candidate.ContentType, ct);
            if (!inspected.IsSuccess) return Reject(inspected.ErrorCode!, inspected.ErrorMessage!);
            // EXIF 방향 적용 후 실제 크기로 캔버스 재계산
            var actual = new SpriteCanvas(inspected.Value!.Info.Width, inspected.Value.Info.Height);
            var generation = SpriteRules.GenerationCanvas(SpriteRules.OutputCanvas(sprites.Settings, actual), sizes);
            var image = await SpriteSourceInspector.CopyAsync(blobs, inspected.Value, "sprite-source", clock.Now, ct);
            copiedKey = image.BlobKey;
            var task = job.SelectSource(image.Id, actual, generation, context.RequestId);
            if (task.IsSuccess) await images.AddAsync(image, ct);
            return One(task);
        }, () => copiedKey is null ? Task.CompletedTask : DeleteCopyAsync(copiedKey, context.JobId), ct);
    }

    private async Task<Result<SpriteReceipt>> ApplyAsync(SpriteCommandContext context, SpriteRequestKind kind, object body,
        bool usesText, Func<PipelineJob, Task<Result<IReadOnlyList<PipelineTask>>>> mutate, Func<Task>? discard, CancellationToken ct)
    {
        if (context.JobId == Guid.Empty || context.RequestId == Guid.Empty || context.ExpectedRevision < 0)
            return Fail(ErrorCode.SpriteSettingsInvalid, "작업·요청 ID와 revision이 필요합니다");
        var fingerprint = Fingerprint(new { kind, context.JobId, context.ExpectedRevision, body });
        // 중복 접수는 현재 revision·모델 상태보다 우선
        if (await jobs.GetSpriteRequestAsync(context.RequestId, ct) is { } accepted)
            return Receipt(accepted, context.JobId, kind, fingerprint);
        var job = await jobs.GetAsync(context.JobId, ct);
        // 요청 조회와 작업 조회 사이의 동시 접수 확정
        if (await jobs.GetSpriteRequestAsync(context.RequestId, ct) is { } freshlyAccepted)
            return Receipt(freshlyAccepted, context.JobId, kind, fingerprint);
        if (job is null) return Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        if (job.ProductionMode != ProductionMode.TwoD || job.Sprites is null)
            return Fail(ErrorCode.SpriteWrongMode, "2D 배경 작업이 필요합니다");
        if (context.ExpectedRevision != job.Sprites.ReviewRevision)
            return Fail(ErrorCode.SpriteRevisionConflict, "검수 revision이 변경되었습니다");
        if (job.Status == JobStatus.Canceled)
            return Fail(ErrorCode.JobAlreadyTerminal, "취소된 작업은 다시 열 수 없습니다");
        // 작업 생성 시 고른 텍스트 모델의 사용 시점 재검증
        if (usesText && job.Sprites is { Phase: SpritePhase.SourceDraft, SourceDraft: { } draft })
        {
            var provider = await providers.GetAsync(draft.TextProviderConfigId, ct);
            if (provider is null || !provider.IsEnabled)
                return Fail(ErrorCode.JobProviderDisabled, "텍스트 공급자를 사용할 수 없습니다");
        }
        // 생성 접수 시점의 이미지 공급자·모델·잠정 생성 크기 재검증
        if (kind == SpriteRequestKind.GenerateSource && job.Sprites.Phase == SpritePhase.SourceDraft)
        {
            var imageProvider = job.ImageProviderConfigId is { } id ? await providers.GetAsync(id, ct) : null;
            if (imageProvider is null || !imageProvider.IsEnabled)
                return Fail(ErrorCode.JobImageProviderDisabled, "이미지 공급자를 사용할 수 없습니다");
            var sizes = await SizesAsync(imageProvider.Id, job.ImageModel!, ct);
            if (sizes is null || !sizes.Any(s => s.Width == job.Sprites.SourceCanvas.Width && s.Height == job.Sprites.SourceCanvas.Height))
                return Fail(ErrorCode.JobImageModelUnavailable, "투명 지원과 생성 크기가 확인된 이미지 모델이 필요합니다");
        }
        var saveAttempted = false;
        SpriteReceipt receipt;
        try
        {
            var result = await mutate(job);
            if (!result.IsSuccess)
            {
                if (discard is not null) await discard();
                return Fail(result.ErrorCode!, result.ErrorMessage!);
            }
            receipt = new SpriteReceipt(job.Id, job.Status, job.Sprites.ReviewRevision,
                Array.AsReadOnly(result.Value!.Select(t => t.Id).ToArray()));
            job.AcceptSpriteRequest(SpriteAcceptedRequest.Create(context.RequestId, job.Id, kind, fingerprint, receipt));
            saveAttempted = true;
            await jobs.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException) when (saveAttempted)
        {
            if (discard is not null) await discard();
            // 추적 캐시 제거 후 승자 접수 또는 새 revision 충돌
            await jobs.ReloadAsync(context.JobId, ct);
            var winner = await jobs.GetSpriteRequestAsync(context.RequestId, ct);
            return winner is null ? Fail(ErrorCode.SpriteRevisionConflict, "접수 상태가 변경되었습니다")
                : Receipt(winner, context.JobId, kind, fingerprint);
        }
        catch (Exception) when (discard is not null)
        {
            var unreferenced = !saveAttempted;
            if (saveAttempted)
            {
                try
                {
                    // 취소·응답 유실과 분리한 실제 커밋 확인
                    unreferenced = await jobs.GetSpriteRequestAsync(context.RequestId, CancellationToken.None) is null;
                }
                catch (Exception lookupError)
                {
                    logger.LogWarning(lookupError, "Could not verify sprite source selection {JobId}; retaining copy", context.JobId);
                }
            }
            if (unreferenced) await discard();
            throw;
        }
        await orchestrator.StartAsync(job, ct);
        return Result<SpriteReceipt>.Ok(receipt);
    }

    private async Task<IReadOnlyList<SpriteCanvas>?> SizesAsync(Guid imageProviderId, string imageModel, CancellationToken ct)
    {
        var model = (await catalog.ListImageModelsAsync(imageProviderId, ct)).FirstOrDefault(m => m.Id == imageModel);
        return model?.Sprite is { SupportsTransparency: true, Sizes: { Count: > 0 } sizes }
            && sizes.All(s => s is not null && s.Width > 0 && s.Height > 0) ? sizes : null;
    }

    private async Task DeleteCopyAsync(string key, Guid jobId)
    {
        try
        {
            await blobs.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception cleanupError)
        {
            logger.LogWarning(cleanupError, "Orphaned sprite source copy {JobId}: {BlobKey}", jobId, key);
        }
    }

    private static string Fingerprint(object value)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static Result<SpriteReceipt> Receipt(SpriteAcceptedRequest request, Guid jobId, SpriteRequestKind kind, string fingerprint)
        => request.JobId == jobId && request.Kind == kind && request.Fingerprint == fingerprint
            ? Result<SpriteReceipt>.Ok(request.Receipt)
            : Fail(ErrorCode.SpriteRequestConflict, "같은 요청 ID에 다른 본문을 사용할 수 없습니다");
    private static Result<IReadOnlyList<PipelineTask>> One(Result<PipelineTask> result)
        => result.IsSuccess ? Result<IReadOnlyList<PipelineTask>>.Ok([result.Value!])
            : Result<IReadOnlyList<PipelineTask>>.Fail(result.ErrorCode!, result.ErrorMessage!);
    private static Result<IReadOnlyList<PipelineTask>> Reject(string code, string message)
        => Result<IReadOnlyList<PipelineTask>>.Fail(code, message);
    private static Result<SpriteReceipt> Fail(string code, string message) => Result<SpriteReceipt>.Fail(code, message);
}
