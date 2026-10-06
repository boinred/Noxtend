using System.Security.Cryptography;
using System.Text.Json;
using Noxtend.Application.Job;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;

namespace Noxtend.Application.Sprites;

public sealed class SpriteCommandsHandler(IJobRepository jobs, JobOrchestrator orchestrator,
    IProviderConfigRepository providers, IModelCatalog catalog)
{
    public Task<Result<SpriteReceipt>> UpdatePlanAsync(SpriteCommandContext context, IReadOnlyList<SpriteAssetPlan> plans, CancellationToken ct)
        => ApplyAsync(context, SpriteRequestKind.UpdatePlan, null, null, plans?.OrderBy(p => p?.Id).ToArray(),
            job => NoTasks(job.ReplaceSpritePlan(plans!, context.ExpectedRevision)), false, ct);

    public Task<Result<SpriteReceipt>> ApprovePlanAsync(SpriteCommandContext context, CancellationToken ct)
        => ApplyAsync(context, SpriteRequestKind.ApprovePlan, null, null, null,
            job => Frames(job, job.ApproveSpritePlan(context.ExpectedRevision), context.RequestId), true, ct);

    public Task<Result<SpriteReceipt>> ApproveBasesAsync(SpriteCommandContext context, IReadOnlyList<Guid> assetIds, CancellationToken ct)
        => ApplyAsync(context, SpriteRequestKind.ApproveBases, null, null, assetIds?.Order().ToArray(),
            job => Frames(job, job.ApproveSpriteBases(assetIds!, context.ExpectedRevision), context.RequestId), true, ct);

    public Task<Result<SpriteReceipt>> RegenerateAsync(SpriteCommandContext context, Guid assetId, int index, CancellationToken ct)
        => ApplyAsync(context, SpriteRequestKind.RegenerateFrame, assetId, index, null, job =>
        {
            var input = job.RegenerateSpriteFrame(assetId, index, context.ExpectedRevision);
            return input.IsSuccess ? Result<IReadOnlyList<PipelineTask>>.Ok(job.PlanSpriteFrames([input.Value!], context.RequestId))
                : Result<IReadOnlyList<PipelineTask>>.Fail(input.ErrorCode!, input.ErrorMessage!);
        }, true, ct);

    public Task<Result<SpriteReceipt>> ApproveAssetAsync(SpriteCommandContext context, Guid assetId, CancellationToken ct)
        => ApplyAsync(context, SpriteRequestKind.ApproveAsset, assetId, null, null,
            job => NoTasks(job.ApproveSpriteAsset(assetId, context.ExpectedRevision)), false, ct);

    public Task<Result<SpriteReceipt>> ExportAsync(SpriteCommandContext context, IReadOnlyList<Guid> assetIds, CancellationToken ct)
        => ApplyAsync(context, SpriteRequestKind.Export, null, null, assetIds?.Order().ToArray(), job =>
        {
            var input = job.CaptureSpriteExport(assetIds!, context.ExpectedRevision);
            if (!input.IsSuccess) return Result<IReadOnlyList<PipelineTask>>.Fail(input.ErrorCode!, input.ErrorMessage!);
            var task = job.PlanTask(TaskKind.PackSprites, job.Tasks.Count);
            task.BindSpriteExport(input.Value!);
            task.BindRequest(context.RequestId);
            return Result<IReadOnlyList<PipelineTask>>.Ok([task]);
        }, false, ct);

    private async Task<Result<SpriteReceipt>> ApplyAsync(SpriteCommandContext context, SpriteRequestKind kind,
        Guid? assetId, int? index, object? body, Func<PipelineJob, Result<IReadOnlyList<PipelineTask>>> mutate,
        bool generates, CancellationToken ct)
    {
        if (context.JobId == Guid.Empty || context.RequestId == Guid.Empty || context.ExpectedRevision < 0)
            return Result<SpriteReceipt>.Fail(ErrorCode.SpriteSettingsInvalid, "작업·요청 ID와 revision이 필요합니다");
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { kind, context.JobId, assetId, index, context.ExpectedRevision, body })));
        // 중복 접수는 현재 revision·모델 상태보다 우선
        if (await jobs.GetSpriteRequestAsync(context.RequestId, ct) is { } accepted)
            return Receipt(accepted, context.JobId, kind, fingerprint);
        var job = await jobs.GetAsync(context.JobId, ct);
        // 요청 조회와 작업 조회 사이의 동시 접수 확정
        if (await jobs.GetSpriteRequestAsync(context.RequestId, ct) is { } freshlyAccepted)
            return Receipt(freshlyAccepted, context.JobId, kind, fingerprint);
        if (job is null) return Result<SpriteReceipt>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        if (job.ProductionMode != ProductionMode.TwoD || job.Sprites is null)
            return Result<SpriteReceipt>.Fail(ErrorCode.SpriteWrongMode, "2D 배경 작업이 필요합니다");
        if (context.ExpectedRevision != job.Sprites.ReviewRevision)
            return Result<SpriteReceipt>.Fail(ErrorCode.SpriteRevisionConflict, "검수 revision이 변경되었습니다");
        if (job.Status == JobStatus.Canceled)
            return Result<SpriteReceipt>.Fail(ErrorCode.JobAlreadyTerminal, "취소된 작업은 다시 열 수 없습니다");
        if (generates)
        {
            var provider = job.ImageProviderConfigId is { } id ? await providers.GetAsync(id, ct) : null;
            if (provider is null || !provider.IsEnabled)
                return Result<SpriteReceipt>.Fail(ErrorCode.JobImageProviderDisabled, "이미지 공급자를 사용할 수 없습니다");
            var model = (await catalog.ListImageModelsAsync(provider.Id, ct)).FirstOrDefault(m => m.Id == job.ImageModel);
            if (model?.Sprite is not { SupportsTransparency: true } capability
                || capability.Sizes?.Any(s => s.Width == job.Sprites.GenerationCanvas.Width
                    && s.Height == job.Sprites.GenerationCanvas.Height) != true)
                return Result<SpriteReceipt>.Fail(ErrorCode.JobImageModelUnavailable, "투명 지원과 생성 크기가 확인된 이미지 모델이 필요합니다");
        }
        var result = mutate(job);
        if (!result.IsSuccess) return Result<SpriteReceipt>.Fail(result.ErrorCode!, result.ErrorMessage!);
        var receipt = new SpriteReceipt(job.Id, job.Status, job.Sprites.ReviewRevision,
            Array.AsReadOnly(result.Value!.Select(t => t.Id).ToArray()));
        job.AcceptSpriteRequest(SpriteAcceptedRequest.Create(context.RequestId, job.Id, kind, fingerprint, receipt));
        try { await jobs.SaveChangesAsync(ct); }
        catch (ConcurrencyConflictException)
        {
            // 추적 캐시 제거 후 승자 접수 또는 새 revision 충돌
            await jobs.ReloadAsync(context.JobId, ct);
            var winner = await jobs.GetSpriteRequestAsync(context.RequestId, ct);
            return winner is null
                ? Result<SpriteReceipt>.Fail(ErrorCode.SpriteRevisionConflict, "접수 상태가 변경되었습니다")
                : Receipt(winner, context.JobId, kind, fingerprint);
        }
        await orchestrator.StartAsync(job, ct);
        return Result<SpriteReceipt>.Ok(receipt);
    }

    private static Result<SpriteReceipt> Receipt(SpriteAcceptedRequest request, Guid jobId, SpriteRequestKind kind, string fingerprint)
        => request.JobId == jobId && request.Kind == kind && request.Fingerprint == fingerprint
            ? Result<SpriteReceipt>.Ok(request.Receipt)
            : Result<SpriteReceipt>.Fail(ErrorCode.SpriteRequestConflict, "같은 요청 ID에 다른 본문을 사용할 수 없습니다");
    private static Result<IReadOnlyList<PipelineTask>> NoTasks(Result<bool> result)
        => result.IsSuccess ? Result<IReadOnlyList<PipelineTask>>.Ok([])
            : Result<IReadOnlyList<PipelineTask>>.Fail(result.ErrorCode!, result.ErrorMessage!);
    private static Result<IReadOnlyList<PipelineTask>> Frames(PipelineJob job,
        Result<IReadOnlyList<SpriteFrameInput>> result, Guid requestId)
        => result.IsSuccess ? Result<IReadOnlyList<PipelineTask>>.Ok(job.PlanSpriteFrames(result.Value!, requestId))
            : Result<IReadOnlyList<PipelineTask>>.Fail(result.ErrorCode!, result.ErrorMessage!);
}
