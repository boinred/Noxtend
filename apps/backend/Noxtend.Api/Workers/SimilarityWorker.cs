using Noxtend.Application.Similarity;
using Noxtend.Domain.Ports;

namespace Noxtend.Api.Workers;

/// <summary>
/// 유사도 평가 소비 루프 (§12) — 전용 스트림, 제작 파이프라인과 분리.
///
/// Design Ref: background-similarity-tuning §12
///
/// **Ack 는 처리 뒤에** — 핸들러가 중복 배달에 안전(DB 상태가 정본)하므로
/// at-least-once 로 충분하다. 예외로 Ack 를 놓친 메시지는 pending 에 남고
/// 회수 경로가 되살린다. 종료(shutdown)는 리스를 남기고 조용히 끝낸다.
/// </summary>
public sealed class SimilarityWorker(
    IServiceScopeFactory scopes,
    ISimilarityQueue queue,
    ILogger<SimilarityWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ConsumeAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<EvaluateSimilarityHandler>();
                await handler.HandleAsync(message.EvaluationId, stoppingToken);

                await queue.AckAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Ack 하지 않는다 — pending 에 남아 회수된다 (§12)
                logger.LogError(ex, "유사도 평가 처리 실패: {EvaluationId}", message.EvaluationId);
            }
        }
    }
}
