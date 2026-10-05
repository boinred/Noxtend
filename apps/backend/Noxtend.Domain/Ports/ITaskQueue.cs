using Noxtend.Domain.Job;

namespace Noxtend.Domain.Ports;

/// <summary>
/// 공정 디스패치.
///
/// Design Ref: §3.2 — **이 Port 가 있는 이유는 교체 가능성이 아니라 어휘 격리다.**
/// 브로커는 Redis Streams 로 확정됐지만, Application 이 XADD·XREADGROUP·XACK 를 알면
/// 큐를 바꿀 때 유스케이스가 바뀐다.
///
/// 단계(공정 종류)마다 스트림이 하나씩이므로 <see cref="TaskKind"/> 가 모든 메서드에
/// 들어온다. 종류가 늘어도 인터페이스는 그대로다 (§10.4).
///
/// **큐는 디스패치 수단이지 정본이 아니다** (§1.2). 메시지를 잃어도 작업을 잃지 않는다 —
/// 스위퍼가 DB 를 보고 재적재한다.
/// </summary>
public interface ITaskQueue
{
    Task EnqueueAsync(Guid taskId, TaskKind kind, CancellationToken ct);

    IAsyncEnumerable<QueuedTask> ConsumeAsync(TaskKind kind, CancellationToken ct);

    Task AckAsync(QueuedTask task, CancellationToken ct);

    /// <summary>
    /// 오래 붙들려 있던 메시지를 회수한다. 워커가 크래시해 Ack 없이 사라진 경우다.
    /// </summary>
    /// <returns>회수한 메시지 수.</returns>
    Task<int> ReclaimStaleAsync(TaskKind kind, TimeSpan idleLongerThan, CancellationToken ct);
}

/// <summary>
/// 큐에서 꺼낸 공정. <see cref="ReceiptId"/> 는 Ack 에 필요한 브로커 측 식별자이며
/// 도메인은 그 내용을 해석하지 않는다.
///
/// <see cref="Kind"/> 를 함께 나르는 이유는 **확인이 어느 스트림으로 갈지 정하기
/// 때문이다** (§8.4). 종류마다 스트림이 하나인데 영수증만 들고 있으면 돌아갈 곳을 모른다.
/// </summary>
public sealed record QueuedTask(Guid TaskId, TaskKind Kind, string ReceiptId);
