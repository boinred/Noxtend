using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 검수 화면에서 사람이 상자를 끌어 옮기거나 크기를 바꾼다
/// (<c>PATCH .../review/parts/{partId}/placements/{ordinal}</c>).
///
/// 응답으로 검수 상태 전체를 돌려준다 — 좌표 하나만 바뀌어도 가림 관계와 재작성 대상이
/// 함께 달라지므로, 화면이 그 셋을 따로 맞추게 두면 어긋난다.
/// </summary>
public sealed class MoveReviewPlacementHandler(IJobRepository jobs)
{
    public async Task<Result<PipelineJob>> HandleAsync(
        Guid jobId, Guid partId, int ordinal, Bounds bounds, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        try
        {
            job.MoveReviewPlacement(partId, ordinal, bounds);
        }
        // 단계 불일치 — InvalidOperationException 하위라 먼저 잡음
        catch (ReviewPhaseMismatchException ex)
        {
            return Result<PipelineJob>.Fail(ErrorCode.ReviewPhaseMismatch, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<PipelineJob>.Fail(ErrorCode.ReviewNotPending, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return Result<PipelineJob>.Fail(ErrorCode.PartNotFound, ex.Message);
        }
        catch (PartValidationException ex)
        {
            return Result<PipelineJob>.Fail(ErrorCode.PartBoundsOutOfRange, ex.Message);
        }

        await jobs.SaveChangesAsync(ct);
        return Result<PipelineJob>.Ok(job);
    }
}
