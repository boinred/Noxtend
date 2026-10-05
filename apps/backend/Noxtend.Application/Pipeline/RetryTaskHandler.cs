using Noxtend.Application.Job;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 실패한 결과물 공정 하나를 다시 돌린다 — 이미지 생성 또는 3D 재구성.
///
/// Design Ref: §4.2 #3 · Plan FR-08
///
/// **공정 단위 어휘다** (C-5). 생성 공정과 파츠는 1:1 이므로 파츠 단위로도 부를 수
/// 있지만, 그러면 같은 것을 두 이름으로 부르게 된다.
///
/// **비싼 성공분을 다시 만들지 않는 것이 목적이다.** 파츠 9개 중 2개가 실패했을 때
/// 작업을 통째로 다시 돌리면 성공한 7장을 버리고 그만큼 다시 지불한다.
/// </summary>
public sealed class RetryTaskHandler(
    IJobRepository jobs,
    IMeshRunRepository meshRuns,
    JobOrchestrator orchestrator)
{
    public async Task<Result<PipelineJob>> HandleAsync(
        Guid jobId, Guid taskId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        // **제출 결과를 모르는 3D 공정은 막는다** (§10.5 · Plan D-06).
        //
        // 다시 돌리면 이미 만들어졌을지 모르는 유료 작업을 하나 더 만든다. 운영자가
        // 공급자 대시보드에서 중복 여부를 확인한 뒤에만 앞으로 나아가야 하는데, 그
        // 확인 절차가 아직 없으므로 여기서 확정 거절한다
        if (await meshRuns.GetLatestByTaskAsync(taskId, ct) is
            { Status: Domain.Mesh.MeshRunStatus.SubmissionUnknown })
        {
            return Result<PipelineJob>.Fail(
                ErrorCode.MeshSubmissionUnknown,
                "제출 결과를 확인할 수 없습니다. 운영자 확인이 필요합니다");
        }

        // 대상 판정은 도메인이 한다 — "결과물 공정인가 · 실패했는가" 가 규칙이고,
        // 여기서 다시 판단하면 그 규칙이 두 곳에 생긴다
        if (!job.RetryOutputTask(taskId))
        {
            return Result<PipelineJob>.Fail(
                ErrorCode.TaskNotRetryable,
                "실패한 이미지 생성 또는 3D 제작 공정만 다시 돌릴 수 있습니다");
        }

        await jobs.SaveChangesAsync(ct);

        // 되돌린 공정을 큐에 넣는다. 준비 판정은 오케스트레이터가 쓰는 것과 같은
        // `IsReadyToRun` 이므로, 분해가 성공해 있는 한 이 공정만 적재된다
        await orchestrator.StartAsync(job, ct);

        return Result<PipelineJob>.Ok(job);
    }
}
