namespace Noxtend.Domain.Ports;

/// <summary>
/// 유사도 평가 전용 큐 (§12) — 기존 <see cref="ITaskQueue"/> 와 consumer group 을
/// 공유하지 않는다. 제작 파이프라인의 워커 수·리스 규칙과 평가의 그것이 다르고,
/// 섞이면 한쪽의 밀림이 다른 쪽을 굶긴다.
///
/// 큐를 잃어도 평가를 잃지 않는다 — 정본은 DB(Pending 상태)이고 스위퍼가 재적재한다.
/// </summary>
public interface ISimilarityQueue
{
    Task EnqueueAsync(Guid evaluationId, CancellationToken ct);

    IAsyncEnumerable<QueuedEvaluation> ConsumeAsync(CancellationToken ct);

    Task AckAsync(QueuedEvaluation evaluation, CancellationToken ct);

    /// <summary>Ack 없이 사라진 메시지 회수 — 워커 크래시 경로.</summary>
    Task<IReadOnlyList<QueuedEvaluation>> ReclaimStaleAsync(TimeSpan idleLongerThan, CancellationToken ct);
}

/// <summary><paramref name="Receipt"/> 는 구현이 Ack 에 쓰는 불투명한 값이다.</summary>
public sealed record QueuedEvaluation(Guid EvaluationId, string Receipt);
