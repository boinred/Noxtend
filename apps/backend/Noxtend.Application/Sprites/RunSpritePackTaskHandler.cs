using Microsoft.Extensions.Logging;
using Noxtend.Application.Common;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;

namespace Noxtend.Application.Sprites;

public sealed class RunSpritePackTaskHandler(IJobRepository jobs, IBlobStorage blobs,
    SpritePackageWriter writer, TaskExecution execution, IClock clock, JobOptions options,
    ILogger<RunSpritePackTaskHandler> logger) : ITaskHandler
{
    public async Task<RunTaskOutcome> HandleAsync(Guid taskId, CancellationToken ct)
    {
        string? savedKey = null;
        Guid jobId = default;
        var path = Path.Combine(Path.GetTempPath(), $"noxtend-sprites-{Guid.NewGuid():N}.zip");
        try
        {
            return await execution.RunAsync(taskId, new(options.Lease, options.LeaseRenew, options.MaxAttempts),
                async (job, task, token) =>
                {
                    jobId = job.Id;
                    if (task.Kind != TaskKind.PackSprites || task.SpriteExportInput is not { } input || job.Sprites is null)
                        throw new ProviderBadResponseException("내보내기 고정 입력이 필요합니다");
                    await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                        8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var manifest = await writer.WriteAsync(job.Id, input, (id, cancellation) =>
                    {
                        var image = job.Sprites.Images.SingleOrDefault(i => i.Id == id
                            && input.Assets.Any(a => a.Id == i.AssetId && a.ImageIds.Contains(id)))
                            ?? throw new ProviderBadResponseException("작업 소유의 승인 이미지가 필요합니다");
                        return blobs.OpenReadAsync(image.BlobKey, cancellation);
                    }, file, token);
                    token.ThrowIfCancellationRequested();
                    file.Position = 0;
                    savedKey = await blobs.SaveAsync(file, "application/zip", token);
                    var export = SpriteExport.Create(task.Id, input, manifest, savedKey, clock.Now);
                    return current =>
                    {
                        if (!current.TryAttachSpriteExport(export))
                            throw new InvalidOperationException("현재 내보내기에 패키지를 연결할 수 없습니다");
                    };
                }, exception => exception switch
                {
                    ProviderBadResponseException => TaskFailure.Fail(ErrorCode.ProviderBadResponse),
                    IOException => TaskFailure.Retry("SPRITE_PACK_FAILED"),
                    _ => null,
                }, ct);
        }
        finally
        {
            try { File.Delete(path); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "2D 공정 {TaskId} 패키지 임시 파일 정리 실패", taskId);
            }
            if (savedKey is not null)
            {
                try
                {
                    // 저장 응답 유실 시 공개된 패키지 보존
                    var persisted = await jobs.ReloadAsync(jobId, CancellationToken.None);
                    if (persisted?.Sprites?.Exports.Any(export => export.BlobKey == savedKey) != true)
                        await blobs.DeleteAsync(savedKey, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "2D 공정 {TaskId} 패키지 공개 여부 또는 Blob 정리 확인 실패", taskId);
                }
            }
        }
    }
}
