using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 검수 대기 상태 조회 (review-gate §입력→출력 — <c>GET .../review</c>).
///
/// 컨트롤러가 <see cref="PipelineJob.Status"/>·<see cref="PipelineJob.Parts"/> 를 그대로
/// 봉투에 담는다 — 파츠 목록 자체가 이미 검수 화면이 그릴 데이터다.
/// </summary>
public sealed class GetReviewHandler(IJobRepository jobs)
{
    public async Task<Result<PipelineJob>> HandleAsync(Guid jobId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        return Result<PipelineJob>.Ok(job);
    }
}
