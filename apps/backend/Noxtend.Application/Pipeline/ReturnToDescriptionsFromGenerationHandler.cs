using Noxtend.Application.Common;
using Noxtend.Application.Job;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// Return to descriptions review phase from generation stage for a given part.
/// </summary>
public sealed class ReturnToDescriptionsFromGenerationHandler(
    IJobRepository jobs,
    JobOrchestrator orchestrator,
    IClock clock)
{
    public async Task<Result<PipelineJob>> HandleAsync(
        Guid jobId,
        Guid partId,
        CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        try
        {
            // Roll back to descriptions review phase and obsolete generated images for the part
            job.ReturnToDescriptionsFromGeneration(partId, clock.Now);
        }
        catch (KeyNotFoundException ex)
        {
            // 존재하지 않는 파츠 ID 예외 처리
            return Result<PipelineJob>.Fail(ErrorCode.PartNotFound, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobAlreadyTerminal, ex.Message);
        }

        await jobs.SaveChangesAsync(ct);
        await orchestrator.StartAsync(job, ct);

        return Result<PipelineJob>.Ok(job);
    }
}
