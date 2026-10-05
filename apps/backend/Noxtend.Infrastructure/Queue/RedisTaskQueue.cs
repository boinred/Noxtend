using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using StackExchange.Redis;

namespace Noxtend.Infrastructure.Queue;

/// <summary>
/// Redis Streams 기반 <see cref="ITaskQueue"/>.
///
/// Design Ref: §2.2 · §3.2 · §10.1
///
/// **스트림은 단계마다 하나다** (<c>noxtend:tasks:extract</c>). 종류가 늘어도
/// 인터페이스는 그대로이고 워커와 스트림만 는다 (§10.4).
///
/// XREADGROUP 의 소비자 그룹이 "한 메시지를 한 워커만 집는다" 를 보장한다. Ack 없이
/// 사라진 메시지는 pending 목록에 남고 <see cref="ReclaimStaleAsync"/> 가 XAUTOCLAIM
/// 으로 회수한다 — <c>InMemoryTaskQueue</c> 가 같은 동작을 흉내내므로 이 경로는
/// 인프라 없이도 검증돼 있다 (§8.2 #18).
///
/// 큐를 잃어도 작업을 잃지 않는다 (§1.2). 정본은 DB 이고 스위퍼가 재적재한다.
/// </summary>
public sealed class RedisTaskQueue(
    RedisConnectionProvider connections,
    ILogger<RedisTaskQueue> logger) : ITaskQueue
{
    private const string ConsumerGroup = "workers";
    private const string TaskIdField = "taskId";

    private readonly string _consumerName = $"worker-{Environment.MachineName}-{Guid.NewGuid():n}";

    public async Task EnqueueAsync(Guid taskId, TaskKind kind, CancellationToken ct)
    {
        var db = (await connections.GetAsync()).GetDatabase();
        await db.StreamAddAsync(StreamKey(kind), TaskIdField, taskId.ToString());
    }

    public async IAsyncEnumerable<QueuedTask> ConsumeAsync(
        TaskKind kind,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var streamKey = StreamKey(kind);
        await EnsureGroupAsync(streamKey);

        while (!ct.IsCancellationRequested)
        {
            StreamEntry[] entries;
            try
            {
                var db = (await connections.GetAsync()).GetDatabase();
                entries = await db.StreamReadGroupAsync(
                    streamKey, ConsumerGroup, _consumerName, ">", count: 1);
            }
            // **NOGROUP 은 지연이 아니다.** Redis 가 재시작하면 스트림과 그룹이 함께
            // 사라지는데, 그것을 일시적 장애로 다루면 2초마다 같은 실패를 영원히
            // 되풀이한다 — 큐가 죽은 채로 접수만 성공하고 아무도 꺼내지 않는다.
            //
            // 실제로 났다. Redis 를 재시작한 뒤 오류 578건이 쌓이는 동안 워커는
            // 한 건도 소비하지 못했고, API 를 다시 시작하기 전까지 낫지 않았다
            catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP"))
            {
                logger.LogWarning("Consumer group lost, recreating: {Stream}", streamKey);
                await EnsureGroupAsync(streamKey);
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Redis 장애는 지연이지 유실이 아니다. 잠깐 쉬고 다시 붙는다 (§6)
                logger.LogWarning(ex, "Stream read failed: {Stream}", streamKey);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }

            if (entries.Length == 0)
            {
                // 폴링 간격. 블로킹 읽기를 쓰면 취소 응답이 느려진다
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
                continue;
            }

            foreach (var entry in entries)
            {
                if (TryReadTaskId(entry, out var taskId))
                {
                    yield return new QueuedTask(taskId, kind, entry.Id!);
                }
                else
                {
                    // 해석할 수 없는 메시지를 남겨두면 pending 목록이 영원히 자란다
                    logger.LogWarning("Discarding malformed stream entry {EntryId}", entry.Id);
                    await AckRawAsync(streamKey, entry.Id!);
                }
            }
        }
    }

    public async Task AckAsync(QueuedTask task, CancellationToken ct)
    {
        // 어느 스트림에서 왔는지 알아야 Ack 할 수 있다 — 메시지가 자기 종류를 나른다.
        // 고정값이던 시절에는 Generate 확인이 Extract 로 가서 미확인 메시지가 쌓였고,
        // 스위퍼가 그것을 회수해 같은 이미지를 다시 만들었다
        await AckRawAsync(StreamKey(task.Kind), task.ReceiptId);
    }

    public async Task<int> ReclaimStaleAsync(
        TaskKind kind,
        TimeSpan idleLongerThan,
        CancellationToken ct)
    {
        var db = (await connections.GetAsync()).GetDatabase();
        var streamKey = StreamKey(kind);

        StreamPendingMessageInfo[] pending;
        try
        {
            pending = await db.StreamPendingMessagesAsync(
                streamKey, ConsumerGroup, count: 100, consumerName: RedisValue.Null);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP"))
        {
            // 소비 루프와 같은 이유다 — 그룹이 사라졌으면 되살리고, 이번 회차는 넘긴다.
            // 되살릴 대기 메시지도 함께 사라졌으므로 지금 거둘 것은 없다
            await EnsureGroupAsync(streamKey);
            return 0;
        }

        var stale = pending
            .Where(p => p.IdleTimeInMilliseconds >= idleLongerThan.TotalMilliseconds)
            .Select(p => p.MessageId)
            .ToArray();

        if (stale.Length == 0)
        {
            return 0;
        }

        // 죽은 워커가 물고 있던 메시지를 이 워커 앞으로 옮긴다
        await db.StreamClaimAsync(
            streamKey, ConsumerGroup, _consumerName,
            (long)idleLongerThan.TotalMilliseconds, stale);

        return stale.Length;
    }

    /// <summary>Design Ref: §10.1 — noxtend:tasks:{kind}</summary>
    private static string StreamKey(TaskKind kind) => $"noxtend:tasks:{kind.ToString().ToLowerInvariant()}";

    private async Task AckRawAsync(string streamKey, string entryId)
    {
        var db = (await connections.GetAsync()).GetDatabase();
        await db.StreamAcknowledgeAsync(streamKey, ConsumerGroup, entryId);
    }

    /// <summary>
    /// 컨슈머 그룹을 보장한다.
    ///
    /// 이미 있으면 Redis 가 BUSYGROUP 을 내는데, 여러 워커가 동시에 기동하는 정상
    /// 상황이므로 오류로 다루지 않는다.
    ///
    /// **기동 때 한 번으로는 부족하다.** Redis 가 재시작하면 그룹이 사라지고, 그 뒤로는
    /// 읽기와 회수가 모두 NOGROUP 으로 실패한다. 그래서 두 경로가 이것을 다시 부른다.
    /// </summary>
    private async Task EnsureGroupAsync(string streamKey)
    {
        try
        {
            var db = (await connections.GetAsync()).GetDatabase();
            await db.StreamCreateConsumerGroupAsync(streamKey, ConsumerGroup, "0-0", createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // 정상
        }
    }

    private static bool TryReadTaskId(StreamEntry entry, out Guid taskId)
    {
        taskId = Guid.Empty;

        var field = entry.Values.FirstOrDefault(v => v.Name == TaskIdField);
        return field.Value.HasValue && Guid.TryParse(field.Value.ToString(), out taskId);
    }
}
