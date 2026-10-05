using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 작업 조회 — 폴링 대상. Design Ref: §4.2 #6
///
/// 화면이 새로고침·북마크로 다시 열 수 있는 근거다. 10분 이상 걸리는 작업에서
/// 사용자가 페이지를 떠나는 것은 예외가 아니라 정상이다 (FR-13 · §5.2).
/// </summary>
public sealed class GetJobHandler(IJobRepository jobs, IMeshRunRepository meshRuns)
{
    public async Task<Result<JobDetails>> HandleAsync(Guid jobId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);

        if (job is null)
        {
            return Result<JobDetails>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        // **진행률은 실행 기록이 정본이다** (§8.5). 공정에 사본을 두면 둘이 어긋나고,
        // 어느 쪽이 맞는지 화면이 알 수 없다. 3D 를 만들지 않는 작업은 빈 목록이라
        // 이 조회가 사실상 공짜다
        var runs = job.ProducesMeshes
            ? await meshRuns.ListLatestByJobAsync(jobId, ct)
            : [];

        return Result<JobDetails>.Ok(new JobDetails(job, runs));
    }
}

/// <summary>
/// 상세 화면이 한 번에 읽는 것.
///
/// Design Ref: §8.5
///
/// **작업 애그리게이트 밖의 값을 하나 더 얹는 자리다.** 실행 기록은 별도 애그리게이트라
/// 작업을 읽는 것만으로는 따라오지 않는데, 화면에는 진행률이 필요하다.
/// </summary>
public sealed record JobDetails(PipelineJob Job, IReadOnlyList<Noxtend.Domain.Mesh.MeshRun> MeshRuns);
