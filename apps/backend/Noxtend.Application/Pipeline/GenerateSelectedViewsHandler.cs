using Noxtend.Application.Common;
using Noxtend.Application.Job;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 정면 생성 완료 후 사용자가 선택한 비정면(좌/후/우) 이미지 생성을 추가 계획 및 큐 적재 (selective-view-generation §2).
/// </summary>
public sealed class GenerateSelectedViewsHandler(IJobRepository jobs, JobOrchestrator orchestrator)
{
    public async Task<Result<PipelineJob>> HandleAsync(
        Guid jobId,
        IReadOnlyList<string> directionStrings,
        CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        // 입력 문자열 방향 목록을 domain ViewDirection enum 값으로 파싱
        var directions = new List<ViewDirection>();
        foreach (var dirStr in directionStrings)
        {
            if (Enum.TryParse<ViewDirection>(dirStr, ignoreCase: true, out var parsed))
            {
                directions.Add(parsed);
            }
        }

        if (directions.Count == 0)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobPartHintInvalid, "유효한 생성 요청 방향이 없습니다");
        }

        if (job.Status is JobStatus.Failed or JobStatus.Canceled)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobAlreadyTerminal, "성공 또는 부분 성공한 작업에 대해서만 비정면 뷰를 생성할 수 있습니다");
        }

        // 정면 성공 완료 후 선택된 비정면 방향 공정들을 추가로 계획
        var planned = job.PlanSelectedViews(directions);
        if (planned.Count == 0)
        {
            return Result<PipelineJob>.Ok(job);
        }

        await jobs.SaveChangesAsync(ct);
        await orchestrator.StartAsync(job, ct);

        return Result<PipelineJob>.Ok(job);
    }
}
