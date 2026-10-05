using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 추가 결과 — 파츠 하나와, 그 추가가 만든 작업 수준 변화
/// (occludedby-recompute §입력→출력 1 응답 예시).
/// </summary>
public sealed record ReviewPartAdded(
    AssetPart Part,
    IReadOnlyList<string> Occludes,
    IReadOnlyList<string> DescriptionsStale);

/// <summary>
/// 검수 화면에서 사람이 사각형으로 파츠 하나를 추가한다 (review-gate §목표 ·
/// <c>POST .../review/parts</c>).
///
/// **도메인 예외를 여기서만 코드로 바꾼다.** <see cref="PipelineJob.AddReviewPart"/> 가
/// 예외로 실패를 표현하는 이유는 그것이 Decompose 단계와 같은 유효성 규칙
/// (<see cref="PartValidationException"/>)을 재사용하기 때문이다 — 전송 계층에서
/// <see cref="Result{T}"/> 로 접는 것은 이 핸들러의 몫이다 (§4.0).
/// </summary>
public sealed class AddReviewPartHandler(IJobRepository jobs)
{
    public async Task<Result<ReviewPartAdded>> HandleAsync(
        Guid jobId,
        string name,
        string? category,
        Bounds bounds,
        string? description,
        IReadOnlyList<string>? occludes,
        CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<ReviewPartAdded>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        AssetPart part;
        try
        {
            part = job.AddReviewPart(name, category, bounds, description, occludes);
        }
        // 단계 불일치 — InvalidOperationException 하위라 먼저 잡음
        catch (ReviewPhaseMismatchException ex)
        {
            return Result<ReviewPartAdded>.Fail(ErrorCode.ReviewPhaseMismatch, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ReviewPartAdded>.Fail(ErrorCode.ReviewNotPending, ex.Message);
        }
        catch (PartValidationException ex)
        {
            var code = ex.Error switch
            {
                PartValidationError.NotOverlapping => ErrorCode.PartNotOverlapping,
                PartValidationError.Duplicate => ErrorCode.PartNameDuplicate,
                _ => ErrorCode.PartBoundsOutOfRange,
            };
            return Result<ReviewPartAdded>.Fail(code, ex.Message);
        }

        await jobs.SaveChangesAsync(ct);

        // 이 파츠가 가리게 된 것들 — 도메인이 방금 갱신한 결과를 그대로 읽는다
        var occluded = job.Parts
            .Where(other => other.OccludedBy.Contains(part.Name))
            .Select(other => other.Name)
            .ToArray();

        return Result<ReviewPartAdded>.Ok(new ReviewPartAdded(part, occluded, job.DescriptionsStale));
    }
}
