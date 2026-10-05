using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 검수 화면에서 잘못 탐지된 파츠를 제외한다 (review-gate §입력→출력 —
/// <c>DELETE .../review/parts/{partId}</c>).
/// </summary>
public sealed class RemoveReviewPartHandler(IJobRepository jobs)
{
    public async Task<Result<Unit>> HandleAsync(Guid jobId, Guid partId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<Unit>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        try
        {
            job.RemoveReviewPart(partId);
        }
        // 단계 불일치 — InvalidOperationException 하위라 먼저 잡음
        catch (ReviewPhaseMismatchException ex)
        {
            return Result<Unit>.Fail(ErrorCode.ReviewPhaseMismatch, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<Unit>.Fail(ErrorCode.ReviewNotPending, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return Result<Unit>.Fail(ErrorCode.PartNotFound, ex.Message);
        }

        await jobs.SaveChangesAsync(ct);
        return Result<Unit>.Ok(Unit.Value);
    }
}
