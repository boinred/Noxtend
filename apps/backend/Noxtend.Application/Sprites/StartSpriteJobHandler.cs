using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Noxtend.Application.Job;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;
using Noxtend.Domain.Upload;

namespace Noxtend.Application.Sprites;

public sealed class StartSpriteJobHandler(
    IJobRepository jobs, IStoredImageRepository images, IBlobStorage blobs,
    IProviderConfigRepository providers, IModelCatalog catalog, IPromptCatalog prompts,
    IImageTranscoder transcoder, JobOrchestrator orchestrator, IClock clock, ILogger<StartSpriteJobHandler> logger)
{
    private const long MaxBytes = 12 * 1024 * 1024;

    public async Task<Result<SpriteReceipt>> HandleAsync(StartSpriteJobCommand command, CancellationToken ct)
    {
        var upload = command.UploadId is not null;
        var sourcePair = command.SourceJobId is not null && command.SourceGeneratedImageId is not null;
        if (command.RequestId == Guid.Empty || command.UploadId == Guid.Empty
            || command.SourceJobId == Guid.Empty || command.SourceGeneratedImageId == Guid.Empty
            || upload == sourcePair || (command.SourceJobId is null) != (command.SourceGeneratedImageId is null)
            || string.IsNullOrWhiteSpace(command.Model) || string.IsNullOrWhiteSpace(command.ImageModel))
            return Fail(ErrorCode.SpriteSettingsInvalid, "업로드 또는 원본 작업·결과 쌍 중 하나와 모델이 필요합니다");
        command = command with { Model = command.Model.Trim(), ImageModel = command.ImageModel.Trim() };
        var validation = SpriteRules.ValidateSettings(command.Settings, new(1, 1));
        if (!validation.IsSuccess) return Fail(validation.ErrorCode!, validation.ErrorMessage!);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command)));

        // 원본 삭제 이후에도 접수 응답 재사용
        if (await jobs.GetSpriteRequestAsync(command.RequestId, ct) is { } accepted)
            return Receipt(accepted, fingerprint);

        var provider = await providers.GetAsync(command.ProviderConfigId, ct);
        if (provider is null) return Fail(ErrorCode.JobProviderNotFound, "분석 공급자가 없습니다");
        if (!provider.IsEnabled) return Fail(ErrorCode.JobProviderDisabled, "분석 공급자가 중지되었습니다");
        if (!(await catalog.ListAsync(provider.Id, ct)).Any(m => m.Id == command.Model))
            return Fail(ErrorCode.JobModelUnavailable, "분석 모델을 사용할 수 없습니다");
        var imageProvider = await providers.GetAsync(command.ImageProviderConfigId, ct);
        if (imageProvider is null) return Fail(ErrorCode.JobImageProviderNotFound, "이미지 공급자가 없습니다");
        if (!imageProvider.IsEnabled) return Fail(ErrorCode.JobImageProviderDisabled, "이미지 공급자가 중지되었습니다");
        var model = (await catalog.ListImageModelsAsync(imageProvider.Id, ct)).FirstOrDefault(m => m.Id == command.ImageModel);
        if (model?.Sprite is not { SupportsTransparency: true } capability || capability.Sizes is null
            || capability.Sizes.Count == 0 || capability.Sizes.Any(s => s is null || s.Width <= 0 || s.Height <= 0))
            return Fail(ErrorCode.JobImageModelUnavailable, "투명 지원과 생성 크기가 확인된 이미지 모델이 필요합니다");
        if (await prompts.GetActiveAsync(LlmOperationKind.AnalyzeSprites, AssetCategory.Background, ct) is null)
            return Fail(ErrorCode.PromptNotActive, "2D 분석 활성 프롬프트가 없습니다");

        StoredImage? sourceImage = null;
        string? sourceKey;
        if (upload)
        {
            sourceImage = await images.GetAsync(command.UploadId!.Value, ct);
            sourceKey = sourceImage?.BlobKey;
        }
        else
        {
            var source = await jobs.GetAsync(command.SourceJobId!.Value, ct);
            sourceKey = source?.GeneratedImages.FirstOrDefault(i => i.Id == command.SourceGeneratedImageId)?.BlobKey
                ?? source?.Sprites?.Images.FirstOrDefault(i => i.Id == command.SourceGeneratedImageId)?.BlobKey;
        }
        if (sourceKey is null) return Fail(ErrorCode.JobUploadNotFound, "작업에 속한 원본 이미지를 찾을 수 없습니다");

        using var content = new MemoryStream();
        SpriteImageInfo info;
        try
        {
            await using var source = await blobs.OpenReadAsync(sourceKey, ct);
            // 비seek 원본에도 적용하는 바이트 상한
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, MaxBytes + 1 - content.Length)), ct);
                if (read == 0) break;
                await content.WriteAsync(buffer.AsMemory(0, read), ct);
                if (content.Length > MaxBytes) return Fail(ErrorCode.UploadTooLarge, "입력 이미지는 최대 12 MiB입니다");
            }
            content.Position = 0;
            info = await transcoder.InspectSpriteAsync(content, MaxBytes, 16_777_216, ct);
        }
        catch (ProviderBadResponseException)
        {
            return Fail(ErrorCode.UploadUnsupportedType, "PNG·JPEG·WebP의 유효한 이미지와 최대 16,777,216픽셀이 필요합니다");
        }
        catch (FileNotFoundException)
        {
            return Fail(ErrorCode.JobUploadNotFound, "원본 이미지 파일이 없습니다");
        }
        if (sourceImage is not null && !string.Equals(sourceImage.ContentType, info.ContentType, StringComparison.OrdinalIgnoreCase))
            return Fail(ErrorCode.UploadUnsupportedType, "업로드 MIME과 실제 이미지 형식이 일치해야 합니다");
        var sourceCanvas = new SpriteCanvas(info.Width, info.Height);
        var generation = SpriteRules.GenerationCanvas(SpriteRules.OutputCanvas(command.Settings, sourceCanvas), capability.Sizes);
        string? copiedKey = null;
        if (sourceImage is null)
        {
            // 기존 작업 결과만 원본 작업의 삭제 수명에서 분리
            content.Position = 0;
            copiedKey = await blobs.SaveAsync(content, info.ContentType, ct);
            sourceImage = StoredImage.Create(copiedKey, "sprite-input", info.ContentType, content.Length, clock.Now);
        }
        var created = PipelineJob.CreateSprites(sourceImage.Id, imageProvider.Id, command.ImageModel,
            command.Settings, sourceCanvas, generation, clock.Now);
        var job = created.Value!;
        var task = job.PlanTask(TaskKind.AnalyzeSprites, 0, providerConfigId: provider.Id, model: command.Model);
        task.BindRequest(command.RequestId);
        var receipt = new SpriteReceipt(job.Id, job.Status, job.Sprites!.ReviewRevision, [task.Id]);
        job.AcceptSpriteRequest(SpriteAcceptedRequest.Create(command.RequestId, job.Id, SpriteRequestKind.Create, fingerprint, receipt));
        var saveAttempted = false;
        try
        {
            // StoredImage·작업·공정·접수 응답의 단일 SaveChanges
            if (copiedKey is not null) await images.AddAsync(sourceImage, ct);
            await jobs.AddAsync(job, ct);
            saveAttempted = true;
            await jobs.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            // SQL 경쟁에서 진 요청이 생성한 독립 복사본만 제거
            if (copiedKey is not null) await DeleteCopyAsync(copiedKey, job.Id);
            var winner = await jobs.GetSpriteRequestAsync(command.RequestId, ct);
            return winner is null ? Fail(ErrorCode.SpriteRevisionConflict, "접수 상태가 변경되었습니다") : Receipt(winner, fingerprint);
        }
        catch (Exception)
        {
            if (copiedKey is not null)
            {
                var unreferenced = !saveAttempted;
                if (saveAttempted)
                {
                    try
                    {
                        // 취소·응답 유실과 분리한 실제 커밋 확인
                        var persisted = await jobs.GetSpriteRequestAsync(command.RequestId, CancellationToken.None);
                        unreferenced = persisted?.JobId != job.Id;
                    }
                    catch (Exception lookupError)
                    {
                        logger.LogWarning(lookupError,
                            "Could not verify sprite admission {JobId}; retaining input {BlobKey}", job.Id, copiedKey);
                    }
                }
                if (unreferenced) await DeleteCopyAsync(copiedKey, job.Id);
            }
            throw;
        }
        await orchestrator.StartAsync(job, ct);
        return Result<SpriteReceipt>.Ok(receipt);
    }

    private async Task DeleteCopyAsync(string key, Guid jobId)
    {
        try
        {
            await blobs.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception cleanupError)
        {
            logger.LogWarning(cleanupError, "Orphaned sprite input after failed admission {JobId}: {BlobKey}", jobId, key);
        }
    }

    private static Result<SpriteReceipt> Receipt(SpriteAcceptedRequest request, string fingerprint)
        => request.Kind == SpriteRequestKind.Create && request.Fingerprint == fingerprint
            ? Result<SpriteReceipt>.Ok(request.Receipt)
            : Fail(ErrorCode.SpriteRevisionConflict, "같은 요청 ID에 다른 본문을 사용할 수 없습니다");
    private static Result<SpriteReceipt> Fail(string code, string message) => Result<SpriteReceipt>.Fail(code, message);
}
