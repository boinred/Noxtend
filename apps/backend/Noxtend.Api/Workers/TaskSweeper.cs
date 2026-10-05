using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Application.Job;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Api.Workers;

/// <summary>
/// 주기적으로 유실된 공정을 되살린다.
///
/// Design Ref: §2.2 스위퍼 · §3.3
///
/// 두 가지를 한다. **DB 쪽** 은 리스가 만료된 공정을 되돌리고 재적재하며,
/// **Redis 쪽** 은 Ack 없이 남은 메시지를 회수한다. 둘은 다른 층의 유실이라 하나로
/// 합칠 수 없다 — 워커가 죽으면 전자, 워커가 메시지를 물고 멈추면 후자다.
/// </summary>
public sealed class TaskSweeper(
    IServiceScopeFactory scopeFactory,
    ITaskQueue queue,
    JobOptions options,
    GenerationOptions generation,
    MeshGenerationOptions mesh,
    ILogger<TaskSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.SweepIntervalSeconds);

        // 기동 직후 한 번 쉰다. 워커들이 스트림에 붙기 전에 쓸면 방금 적재된 공정을
        // "미적재" 로 보고 중복으로 넣는다
        await Task.Delay(interval, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 스위퍼가 죽으면 복구 장치가 사라진다. 어떤 실패에도 루프는 계속된다
                logger.LogError(ex, "Sweep failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<SweepStaleTasksHandler>();

        var report = await handler.HandleAsync(ct);

        // 브로커 쪽 유실 — **스트림은 종류마다 하나이므로 종류마다 회수한다** (§8.4).
        // 한 종류만 쓸던 시절에는 나머지 스트림의 미확인 메시지가 영원히 남았다
        var reclaimed = 0;

        foreach (var step in ReclaimPlan.For(options, generation, mesh))
        {
            reclaimed += await queue.ReclaimStaleAsync(step.Kind, step.IdleLongerThan, ct);
        }

        if (report.Requeued > 0 || report.Failed > 0 || reclaimed > 0)
        {
            logger.LogInformation(
                "Sweep: requeued={Requeued} failed={Failed} reclaimed={Reclaimed}",
                report.Requeued, report.Failed, reclaimed);
        }
    }
}
