using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Queue;

/// <summary>
/// 인프라 없는 <see cref="ISimilarityQueue"/> — InMemoryTaskQueue 와 같은 규칙.
/// 미확인 메시지를 따로 들고 <see cref="ReclaimStaleAsync"/> 가 되돌린다 —
/// Redis 어댑터의 XAUTOCLAIM 동작이 여기서 먼저 검증된다.
/// </summary>
public sealed class InMemorySimilarityQueue(TimeProvider? timeProvider = null) : ISimilarityQueue
{
    private readonly Channel<QueuedEvaluation> _channel = Channel.CreateUnbounded<QueuedEvaluation>();
    private readonly ConcurrentDictionary<string, PendingEntry> _unacked = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private long _receiptCounter;

    /// <summary>테스트가 "무엇이 적재되었는가" 를 확인하는 창구.</summary>
    public IReadOnlyCollection<Guid> Enqueued => [.. _enqueued];

    private readonly ConcurrentQueue<Guid> _enqueued = new();

    public Task EnqueueAsync(Guid evaluationId, CancellationToken ct)
    {
        var receipt = Interlocked.Increment(ref _receiptCounter).ToString();
        _enqueued.Enqueue(evaluationId);
        _channel.Writer.TryWrite(new QueuedEvaluation(evaluationId, receipt));
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<QueuedEvaluation> ConsumeAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct))
        {
            while (_channel.Reader.TryRead(out var queued))
            {
                _unacked[queued.Receipt] = new PendingEntry(queued, _time.GetUtcNow());
                yield return queued;
            }
        }
    }

    public Task AckAsync(QueuedEvaluation evaluation, CancellationToken ct)
    {
        _unacked.TryRemove(evaluation.Receipt, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QueuedEvaluation>> ReclaimStaleAsync(
        TimeSpan idleLongerThan, CancellationToken ct)
    {
        var threshold = _time.GetUtcNow() - idleLongerThan;
        var reclaimed = new List<QueuedEvaluation>();

        foreach (var (receipt, entry) in _unacked)
        {
            if (entry.DeliveredAt > threshold)
            {
                continue;
            }

            if (_unacked.TryRemove(receipt, out _))
            {
                _channel.Writer.TryWrite(entry.Evaluation);
                reclaimed.Add(entry.Evaluation);
            }
        }

        return Task.FromResult<IReadOnlyList<QueuedEvaluation>>(reclaimed);
    }

    private sealed record PendingEntry(QueuedEvaluation Evaluation, DateTimeOffset DeliveredAt);
}
