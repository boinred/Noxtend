using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Job;

/// <summary>
/// 유실된 공정을 되살린다.
///
/// Design Ref: §2.2 스위퍼 · §8.2 #18~21
///
/// **이것이 "작업의 정본은 DB 다" 를 실제 보장으로 바꾸는 장치다** (§1.2).
/// Redis 가 메시지를 잃어도, 워커가 크래시해도, DB 만 살아 있으면 작업은 결국 끝난다.
/// 스위퍼가 없으면 큐 장애가 곧 작업 유실이 된다 (R-10).
/// </summary>
public sealed class SweepStaleTasksHandler(
    IJobRepository jobs,
    ITaskQueue queue,
    IClock clock,
    JobOptions options)
{
    public async Task<SweepReport> HandleAsync(CancellationToken ct)
    {
        var now = clock.Now;
        var candidates = await jobs.ListSweepCandidatesAsync(now, ct);

        var requeued = 0;
        var failed = 0;

        foreach (var job in candidates)
        {
            foreach (var task in job.Tasks)
            {
                // 종료 상태 공정은 건드리지 않는다 (#21). 성공한 결과를 되돌리거나
                // 취소된 공정을 되살리면 스위퍼가 복구 장치가 아니라 오염원이 된다
                if (task.IsTerminal)
                {
                    continue;
                }

                if (task.IsLeaseExpired(now))
                {
                    if (task.AttemptCount >= options.MaxAttempts)
                    {
                        // 재시도 한도 초과 — 되돌리지 않고 확정한다 (#20).
                        // 무한히 되살리면 실패가 영원히 진행 중으로 보인다
                        task.Fail("재시도 한도를 초과했습니다", now);
                        failed++;
                        continue;
                    }

                    task.ReleaseForRetry();
                    await queue.EnqueueAsync(task.Id, task.Kind, ct);
                    requeued++;
                }
                else if (job.IsReadyToRun(task, now))
                {
                    // 대기 중인데 살아 있는 워커가 없다. 적재가 실패했을 수 있으므로 다시 넣는다 (#19).
                    // 중복 배달은 안전하다 — 워커가 집을 때 상태를 다시 확인한다
                    //
                    // **의존을 확인하는 것이 핵심이다.** 공정이 하나뿐일 땐 대기 = 준비였지만,
                    // 셋이 되면 앞 공정이 끝나기 전의 뒤 공정도 대기 상태다. 그것을 적재하면
                    // 워커가 집어서 "장면 명세가 없습니다" 로 실패시킨다
                    await queue.EnqueueAsync(task.Id, task.Kind, ct);
                    requeued++;
                }
            }

            // 공정을 실패로 확정했으면 작업 상태도 따라간다
            job.ReconcileFromTasks(now);
        }

        await jobs.SaveChangesAsync(ct);
        return new SweepReport(requeued, failed);
    }
}

public sealed record SweepReport(int Requeued, int Failed);
