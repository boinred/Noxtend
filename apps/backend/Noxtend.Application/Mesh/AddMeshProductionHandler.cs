using Noxtend.Application.Job;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Mesh;

/// <summary>
/// 끝난 작업에 3D 를 뒤늦게 붙인다.
///
/// Design Ref: §5.2 · Plan FR-01~FR-05
///
/// **이미지를 다시 만들지 않는 것이 이 유스케이스의 값어치다.** 전체 재실행 비용의
/// 99.5% 가 이미 갖고 있는 이미지이므로, 3D 공정만 큐에 들어가야 한다.
///
/// `RetryTaskHandler` 와 같은 골격이다 — 조회 · 도메인 판정 · 저장 · 적재.
/// </summary>
public sealed class AddMeshProductionHandler(
    IJobRepository jobs,
    MeshSelectionValidator selection,
    JobOrchestrator orchestrator,
    IClock clock)
{
    public async Task<Result<PipelineJob>> HandleAsync(
        Guid jobId, Guid providerConfigId, string? model, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);

        if (job is null)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        // 접수와 같은 규칙을 쓴다 (D-11) — 나눠 두면 한쪽에서만 막히는 조합이 생긴다
        if (await selection.ValidateAsync(providerConfigId, model, ct) is { } error)
        {
            return Result<PipelineJob>.Fail(error.Code, error.Message);
        }

        // 대상 판정은 도메인이 한다. 여기서 다시 판단하면 규칙이 두 곳에 생긴다
        if (!job.AddMeshProduction(providerConfigId, model!, clock.Now))
        {
            return Result<PipelineJob>.Fail(
                ErrorCode.JobMeshNotApplicable,
                "이 작업에는 3D 를 붙일 수 없습니다. 목록을 새로 불러오세요");
        }

        await jobs.SaveChangesAsync(ct);

        // 계획된 3D 공정을 큐에 넣는다. 3D 는 의존이 없어 즉시 준비 상태이고,
        // 이미 끝난 이미지 공정은 `IsReadyToRun` 이 걸러 다시 들어가지 않는다
        await orchestrator.StartAsync(job, ct);

        return Result<PipelineJob>.Ok(job);
    }
}
