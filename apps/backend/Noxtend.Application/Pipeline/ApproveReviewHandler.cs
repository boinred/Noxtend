using Noxtend.Application.Common;
using Noxtend.Application.Job;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 검수 게이트 전체 승인 (review-gate §목표 · <c>POST .../review/approve</c>).
///
/// **새 팬아웃 로직을 만들지 않는다.** <see cref="PipelineJob.ApproveReview"/> 가
/// 도메인 안에서 미뤄뒀던 <see cref="PipelineJob.PlanReadyFollowUpTasks"/> 를 다시 부르고,
/// 이 핸들러는 접수(<c>StartJobHandler</c>)·재시도(<c>RetryTaskHandler</c>)와 같은
/// <see cref="JobOrchestrator.StartAsync"/> 로 큐에 적재한다 — 기존 팬아웃 경로 재사용.
/// </summary>
public sealed class ApproveReviewHandler(IJobRepository jobs, JobOrchestrator orchestrator, IClock clock)
{
    public async Task<Result<PipelineJob>> HandleAsync(Guid jobId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        try
        {
            job.ApproveReview(clock.Now);
        }
        // 단계 불일치 — InvalidOperationException 하위라 먼저 잡음
        catch (ReviewPhaseMismatchException ex)
        {
            return Result<PipelineJob>.Fail(ErrorCode.ReviewPhaseMismatch, ex.Message);
        }
        catch (InvalidOperationException ex) when (!job.RequiresReview)
        {
            return Result<PipelineJob>.Fail(ErrorCode.ReviewNotRequired, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<PipelineJob>.Fail(ErrorCode.ReviewNotPending, ex.Message);
        }

        await jobs.SaveChangesAsync(ct);
        await orchestrator.StartAsync(job, ct);

        return Result<PipelineJob>.Ok(job);
    }
}
