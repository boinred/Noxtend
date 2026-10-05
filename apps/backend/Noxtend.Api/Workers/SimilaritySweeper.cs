using Noxtend.Application.Similarity;
using Noxtend.Domain.Ports;

namespace Noxtend.Api.Workers;

/// <summary>
/// 유사도 스위퍼 (§12) — 두 가지를 회수한다: ① Ack 없이 사라진 큐 메시지(XAUTOCLAIM),
/// ② lease 만료된 Running 평가(DB). 워커 shutdown 은 lease 를 남기고 조용히 끝나므로
/// 이 루프가 안전망이다. terminal run 은 건너뛴다.
/// </summary>
public sealed class SimilaritySweeper(
    IServiceScopeFactory scopes,
    ISimilarityQueue queue,
    ILogger<SimilaritySweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                await queue.ReclaimStaleAsync(StaleAfter, stoppingToken);

                await using var scope = scopes.CreateAsyncScope();
                var sweep = scope.ServiceProvider.GetRequiredService<SweepSimilarityHandler>();
                var reclaimed = await sweep.HandleAsync(stoppingToken);
                if (reclaimed > 0)
                {
                    logger.LogInformation("만료된 유사도 평가 {Count}건을 재적재했습니다", reclaimed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "유사도 스위퍼 회차 실패 — 다음 회차에 다시 시도합니다");
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
