using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Noxtend.Domain.Ports;
using StackExchange.Redis;

namespace Noxtend.Infrastructure.Queue;

/// <summary>
/// Redis Streams 기반 <see cref="ISimilarityQueue"/>.
///
/// Design Ref: background-similarity-tuning §12
///
/// **전용 스트림·전용 consumer group** — 기존 `noxtend:tasks:*` 와 공유하지 않는다.
/// 제작 파이프라인의 워커 수·리스 규칙과 평가의 그것이 다르고, 섞이면 한쪽의 밀림이
/// 다른 쪽을 굶긴다. NOGROUP 재생성 규칙은 <see cref="RedisTaskQueue"/> 에서 실측으로
/// 배운 것과 같다 — Redis 재시작 후 그룹이 사라지면 되살리고 계속한다.
/// </summary>
public sealed class RedisSimilarityQueue(
    RedisConnectionProvider connections,
    ILogger<RedisSimilarityQueue> logger) : ISimilarityQueue
{
    private const string StreamKey = "noxtend:similarity:evaluations";
    private const string ConsumerGroup = "similarity-workers";
    private const string EvaluationIdField = "evaluationId";

    private readonly string _consumerName =
        $"similarity-{Environment.MachineName}-{Guid.NewGuid():n}";

    public async Task EnqueueAsync(Guid evaluationId, CancellationToken ct)
    {
        var db = (await connections.GetAsync()).GetDatabase();
        await db.StreamAddAsync(StreamKey, EvaluationIdField, evaluationId.ToString());
    }

    public async IAsyncEnumerable<QueuedEvaluation> ConsumeAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureGroupAsync();

        while (!ct.IsCancellationRequested)
        {
            StreamEntry[] entries;
            try
            {
                var db = (await connections.GetAsync()).GetDatabase();
                entries = await db.StreamReadGroupAsync(
                    StreamKey, ConsumerGroup, _consumerName, ">", count: 1);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP"))
            {
                logger.LogWarning("Consumer group lost, recreating: {Stream}", StreamKey);
                await EnsureGroupAsync();
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Stream read failed: {Stream}", StreamKey);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }

            if (entries.Length == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
                continue;
            }

            foreach (var entry in entries)
            {
                if (TryReadEvaluationId(entry, out var evaluationId))
                {
                    yield return new QueuedEvaluation(evaluationId, entry.Id!);
                }
                else
                {
                    logger.LogWarning("Discarding malformed stream entry {EntryId}", entry.Id);
                    await AckRawAsync(entry.Id!);
                }
            }
        }
    }

    public Task AckAsync(QueuedEvaluation evaluation, CancellationToken ct)
        => AckRawAsync(evaluation.Receipt);

    public async Task<IReadOnlyList<QueuedEvaluation>> ReclaimStaleAsync(
        TimeSpan idleLongerThan, CancellationToken ct)
    {
        var db = (await connections.GetAsync()).GetDatabase();

        StreamPendingMessageInfo[] pending;
        try
        {
            pending = await db.StreamPendingMessagesAsync(
                StreamKey, ConsumerGroup, count: 100, consumerName: RedisValue.Null);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP"))
        {
            await EnsureGroupAsync();
            return [];
        }

        var stale = pending
            .Where(p => p.IdleTimeInMilliseconds >= idleLongerThan.TotalMilliseconds)
            .Select(p => p.MessageId)
            .ToArray();

        if (stale.Length == 0)
        {
            return [];
        }

        var claimed = await db.StreamClaimAsync(
            StreamKey, ConsumerGroup, _consumerName,
            (long)idleLongerThan.TotalMilliseconds, stale);

        var reclaimed = new List<QueuedEvaluation>();
        foreach (var entry in claimed)
        {
            if (TryReadEvaluationId(entry, out var evaluationId))
            {
                reclaimed.Add(new QueuedEvaluation(evaluationId, entry.Id!));
            }
        }

        return reclaimed;
    }

    private async Task AckRawAsync(string entryId)
    {
        var db = (await connections.GetAsync()).GetDatabase();
        await db.StreamAcknowledgeAsync(StreamKey, ConsumerGroup, entryId);
    }

    private async Task EnsureGroupAsync()
    {
        try
        {
            var db = (await connections.GetAsync()).GetDatabase();
            await db.StreamCreateConsumerGroupAsync(StreamKey, ConsumerGroup, "0-0", createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // 정상 — 여러 워커의 동시 기동
        }
    }

    private static bool TryReadEvaluationId(StreamEntry entry, out Guid evaluationId)
    {
        evaluationId = Guid.Empty;
        var field = entry.Values.FirstOrDefault(v => v.Name == EvaluationIdField);
        return field.Value.HasValue && Guid.TryParse(field.Value.ToString(), out evaluationId);
    }
}
