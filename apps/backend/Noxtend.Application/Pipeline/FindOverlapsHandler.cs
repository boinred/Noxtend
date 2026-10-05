using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 사각형 하나와 겹치는 파츠 이름 조회 (occludedby-recompute §입력→출력 1 ·
/// <c>POST .../review/parts/overlaps</c>).
///
/// **순수 조회다.** 화면이 "무엇을 가리는가" 체크박스를 그리려면 추가하기 전에 겹침을
/// 알아야 한다. 저장하지 않으므로 <c>SaveChangesAsync</c> 를 부르지 않는다.
///
/// 가드는 추가 경로와 같다 — 여기서 통과한 좌표가 곧바로 추가로 이어지므로, 검사가
/// 다르면 화면이 그린 목록으로 추가했는데 거부당하는 일이 생긴다.
/// </summary>
public sealed class FindOverlapsHandler(IJobRepository jobs)
{
    public async Task<Result<IReadOnlyList<string>>> HandleAsync(
        Guid jobId, Bounds bounds, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<IReadOnlyList<string>>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        if (job.Status != JobStatus.PendingReview)
        {
            return Result<IReadOnlyList<string>>.Fail(
                ErrorCode.ReviewNotPending, $"검수 대기 상태가 아닙니다. 현재 상태: {job.Status}");
        }

        if (!bounds.IsWithinFrame())
        {
            return Result<IReadOnlyList<string>>.Fail(
                ErrorCode.PartBoundsOutOfRange, "좌표가 화면을 벗어났습니다.");
        }

        return Result<IReadOnlyList<string>>.Ok(job.FindOverlappingPartNames(bounds));
    }
}
