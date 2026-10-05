using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Job;

/// <summary>
/// 공정이 끝날 때 다음에 무엇을 할지 정한다.
///
/// Design Ref: §2.2 · §8.2 #15~17
///
/// **오케스트레이터를 두 벌 만들지 않는다** (§1.1).
///
/// 준비 판정 자체는 `PipelineJob.IsReadyToRun` 이 갖는다 — 스위퍼도 같은 판정이
/// 필요하기 때문이다 (사이클 #5 에서 드러난 결함).
///
/// 카테고리를 읽지 않는다 (§2.4) — 캐릭터·소품·배경이 같은 백본을 쓴다.
/// </summary>
public sealed class JobOrchestrator(ITaskQueue queue, IJobRepository jobs, IClock clock)
{
    /// <summary>
    /// 공정 하나가 종료 상태가 된 직후에 호출한다.
    ///
    /// 순서가 중요하다. 종료 판정을 먼저 하므로, 한 공정이 실패했을 때 남은 대기 공정을
    /// 적재하지 않는다 — 실패한 작업의 뒷단계를 돌리는 것은 낭비이자 오염이다.
    /// </summary>
    public async Task OnTaskCompletedAsync(PipelineJob job, CancellationToken ct)
    {
        // 현재 공정 결과의 DB 확정
        await jobs.SaveChangesAsync(ct);

        // 병렬 생성 워커 전체의 최신 공정 상태
        var latest = await jobs.ReloadAsync(job.Id, ct);
        if (latest is null)
        {
            return;
        }

        // 팬아웃 지점 (사이클 #7 §2.2 · §2.3 A-2). 분해가 끝나야 파츠 수를 알므로
        // 접수 시점에 계획할 수 없었다.
        //
        // **종료 판정보다 앞이다.** 뒤에 두면 분해 성공 직후 "남은 공정이 없다" 로
        // 작업이 성공 확정되고, 생성 공정은 종료된 작업에 붙지 못한다.
        //
        // 계획 규칙과 멱등성은 도메인이 갖는다 — 여기서 조건을 다시 판단하면
        // 그 규칙이 두 곳에 생긴다
        latest.PlanReadyFollowUpTasks();

        latest.ReconcileFromTasks(clock.Now);

        if (latest.IsTerminal)
        {
            await jobs.SaveChangesAsync(ct);
            return;
        }

        await EnqueueReadyTasksAsync(latest, ct);
    }

    /// <summary>
    /// 작업 접수 직후 첫 공정을 적재한다.
    ///
    /// 접수와 완료가 같은 "준비된 공정을 적재한다" 규칙을 쓴다. 첫 공정만 특별 취급하면
    /// 의존 판정이 두 곳에 생기고, 단계가 늘 때 둘이 어긋난다.
    /// </summary>
    public Task StartAsync(PipelineJob job, CancellationToken ct) => EnqueueReadyTasksAsync(job, ct);

    private async Task EnqueueReadyTasksAsync(PipelineJob job, CancellationToken ct)
    {
        var now = clock.Now;
        var ready = job.Tasks.Where(t => job.IsReadyToRun(t, now)).ToList();

        foreach (var task in ready)
        {
            await queue.EnqueueAsync(task.Id, task.Kind, ct);
        }

        await jobs.SaveChangesAsync(ct);
    }

}
