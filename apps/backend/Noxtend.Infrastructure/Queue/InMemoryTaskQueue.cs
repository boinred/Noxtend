using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Queue;

/// <summary>
/// 인프라 없는 <see cref="ITaskQueue"/> 구현.
///
/// Design Ref: §2.0 · §8.1 — 오케스트레이터와 스위퍼가 Redis 없이 검증되어야 한다.
/// 단계마다 채널을 하나씩 두어 Redis Streams 의 "종류마다 스트림" 구조를 그대로 흉내낸다.
///
/// 미확인 메시지를 따로 들고 있는 것이 핵심이다. Ack 없이 사라진 메시지를
/// <see cref="ReclaimStaleAsync"/> 가 되돌리는 동작은 Redis 어댑터가 XAUTOCLAIM 으로
/// 할 일과 같다 — 여기서 검증되면 거기서 회귀가 드러난다.
/// </summary>
public sealed class InMemoryTaskQueue(TimeProvider? timeProvider = null) : ITaskQueue
{
    private readonly ConcurrentDictionary<TaskKind, Channel<QueuedTask>> _channels = new();
    private readonly ConcurrentDictionary<string, PendingEntry> _unacked = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private long _receiptCounter;

    /// <summary>테스트가 "무엇이 적재되었는가" 를 확인하는 창구.</summary>
    public IReadOnlyCollection<(Guid TaskId, TaskKind Kind)> Enqueued => _enqueued;

    private readonly ConcurrentQueue<(Guid TaskId, TaskKind Kind)> _enqueued = new();

    public Task EnqueueAsync(Guid taskId, TaskKind kind, CancellationToken ct)
    {
        var receiptId = Interlocked.Increment(ref _receiptCounter).ToString();
        var queued = new QueuedTask(taskId, kind, receiptId);

        _enqueued.Enqueue((taskId, kind));
        ChannelFor(kind).Writer.TryWrite(queued);

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<QueuedTask> ConsumeAsync(
        TaskKind kind,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var reader = ChannelFor(kind).Reader;

        while (await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out var queued))
            {
                // 꺼낸 시점부터 미확인이다. Ack 가 오지 않으면 회수 대상이 된다
                _unacked[queued.ReceiptId] = new PendingEntry(queued, kind, _time.GetUtcNow());
                yield return queued;
            }
        }
    }

    public Task AckAsync(QueuedTask task, CancellationToken ct)
    {
        _unacked.TryRemove(task.ReceiptId, out _);
        return Task.CompletedTask;
    }

    public Task<int> ReclaimStaleAsync(TaskKind kind, TimeSpan idleLongerThan, CancellationToken ct)
    {
        var threshold = _time.GetUtcNow() - idleLongerThan;
        var reclaimed = 0;

        foreach (var (receiptId, entry) in _unacked)
        {
            if (entry.Kind != kind || entry.DeliveredAt > threshold)
            {
                continue;
            }

            if (_unacked.TryRemove(receiptId, out _))
            {
                ChannelFor(kind).Writer.TryWrite(entry.Task);
                reclaimed++;
            }
        }

        return Task.FromResult(reclaimed);
    }

    private Channel<QueuedTask> ChannelFor(TaskKind kind)
        => _channels.GetOrAdd(kind, _ => Channel.CreateUnbounded<QueuedTask>());

    private sealed record PendingEntry(QueuedTask Task, TaskKind Kind, DateTimeOffset DeliveredAt);
}
