using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Queue;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// Design Ref: §8.4 · Plan D-10 — 공정 종류마다 스트림이 하나인데 확인(ACK)은 한 곳으로만 갔다.
///
/// **`InMemoryTaskQueue` 로는 이 결함을 볼 수 없다.** 그쪽은 영수증마다 종류를 따로 들고
/// 있어서 계약이 깨져 있어도 올바르게 동작한다. 스트림이 실제로 나뉘는 것은 Redis 뿐이라
/// 컨테이너를 띄운다.
///
/// Reconstruct 를 얹기 전에 고친다. 지금도 Generate 는 틀린 스트림에 확인하고 있고,
/// 종류가 하나 더 늘면 미확인 메시지가 그만큼 더 쌓인다.
/// </summary>
[Collection(RedisCollection.Name)]
public sealed class RedisTaskQueueTests(RedisFixture redis)
{
    /// <summary>
    /// 꺼낸 메시지를 확인하면 **그 종류의** 스트림에서 사라져야 한다.
    ///
    /// 미확인 목록에 남으면 스위퍼가 회수해 같은 공정을 다시 돌린다 — 생성 공정이라면
    /// 이미지를 한 번 더 만들어 실제로 돈이 나간다.
    /// </summary>
    [Fact]
    public async Task AckingAGenerateMessage_LeavesNothingPendingOnTheGenerateStream()
    {
        await redis.ResetAsync();
        var queue = Queue();

        await queue.EnqueueAsync(Guid.NewGuid(), TaskKind.Generate, default);
        var queued = await FirstAsync(queue, TaskKind.Generate);

        await queue.AckAsync(queued, default);

        Assert.Equal(0, await PendingCountAsync(TaskKind.Generate));
    }

    /// <summary>
    /// 확인은 남의 스트림을 건드리지 않는다.
    ///
    /// Generate 를 확인하는데 Extract 스트림으로 가면, 정작 Generate 는 미확인으로 남고
    /// Extract 는 영문 모를 확인을 받는다. **두 스트림을 함께 봐야 드러난다** — 한쪽만 보면
    /// 종류가 하나뿐이던 시절의 코드도 통과한다.
    /// </summary>
    [Fact]
    public async Task AckingOneKind_LeavesAnotherKindsMessageUntouched()
    {
        await redis.ResetAsync();
        var queue = Queue();

        await queue.EnqueueAsync(Guid.NewGuid(), TaskKind.Extract, default);
        await queue.EnqueueAsync(Guid.NewGuid(), TaskKind.Generate, default);

        await FirstAsync(queue, TaskKind.Extract);
        var generate = await FirstAsync(queue, TaskKind.Generate);

        await queue.AckAsync(generate, default);

        Assert.Equal(0, await PendingCountAsync(TaskKind.Generate));
        Assert.Equal(1, await PendingCountAsync(TaskKind.Extract));
    }

    /// <summary>꺼낸 메시지는 자기가 어느 종류였는지 안다 — 확인이 그 값을 쓴다.</summary>
    [Fact]
    public async Task ConsumedMessage_CarriesItsKind()
    {
        await redis.ResetAsync();
        var queue = Queue();

        await queue.EnqueueAsync(Guid.NewGuid(), TaskKind.Decompose, default);

        Assert.Equal(TaskKind.Decompose, (await FirstAsync(queue, TaskKind.Decompose)).Kind);
    }

    /// <summary>
    /// **Redis 가 재시작해도 큐가 스스로 낫는다.**
    ///
    /// 실서버에서 났다. Redis 를 재시작하니 스트림과 컨슈머 그룹이 함께 사라졌는데,
    /// 소비 루프가 그것을 일시적 장애로 읽고 2초마다 같은 실패를 되풀이했다 —
    /// 오류 578건이 쌓이는 동안 한 건도 소비하지 못했고, **API 를 다시 시작하기
    /// 전까지 낫지 않았다.**
    ///
    /// 그 상태에서 작업을 넣으면 접수는 202 로 성공하고 공정은 pending 으로 쌓이며
    /// 화면에는 "진행 중" 으로 보인다. 예외도 안 난다.
    ///
    /// `FLUSHALL` 이 그 순간을 그대로 재현한다.
    /// </summary>
    [Fact]
    public async Task QueueRecovers_AfterTheConsumerGroupDisappears()
    {
        await redis.ResetAsync();
        var queue = Queue();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));

        // **루프 하나가 처음부터 끝까지 살아 있어야 한다.** 중간에 새 열거를 열면
        // `ConsumeAsync` 진입부가 그룹을 다시 만들어, 정작 문제인 "돌고 있는 루프" 를
        // 비껴간다 — 처음 쓴 검사가 고치기 전에도 통과한 이유가 그것이었다
        var seen = new System.Collections.Concurrent.ConcurrentQueue<Guid>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var queued in queue.ConsumeAsync(TaskKind.Analyze, timeout.Token))
            {
                seen.Enqueue(queued.TaskId);
            }
        });

        var before = Guid.NewGuid();
        await queue.EnqueueAsync(before, TaskKind.Analyze, default);
        await WaitForAsync(seen, before);

        // Redis 재시작 — 스트림도 그룹도 사라진다. 위 루프는 그대로 돌고 있다
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        await using (multiplexer)
        {
            await multiplexer.GetServer(multiplexer.GetEndPoints()[0]).FlushAllDatabasesAsync();
        }

        var after = Guid.NewGuid();
        await queue.EnqueueAsync(after, TaskKind.Analyze, default);

        // **그 루프가 NOGROUP 에 갇히면 이 대기가 끝나지 않는다.** 실서버에서 난 그대로다
        await WaitForAsync(seen, after);

        await timeout.CancelAsync();
        await Task.WhenAny(consumer, Task.Delay(2000));
    }

    /// <summary>루프가 그 id 를 꺼낼 때까지 기다린다. 못 꺼내면 던진다.</summary>
    private static async Task WaitForAsync(
        System.Collections.Concurrent.ConcurrentQueue<Guid> seen, Guid expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            if (seen.Contains(expected))
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException($"루프가 {expected} 를 꺼내지 못했습니다");
    }

    /// <summary>회수도 같은 자리다 — 스위퍼가 NOGROUP 으로 계속 실패하면 안 된다.</summary>
    [Fact]
    public async Task Reclaim_DoesNotThrow_AfterTheConsumerGroupDisappears()
    {
        await redis.ResetAsync();
        var queue = Queue();

        await queue.EnqueueAsync(Guid.NewGuid(), TaskKind.Extract, default);
        await FirstAsync(queue, TaskKind.Extract);

        var multiplexer = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        await using (multiplexer)
        {
            await multiplexer.GetServer(multiplexer.GetEndPoints()[0]).FlushAllDatabasesAsync();
        }

        // 거둘 것이 없으니 0 이다. 던지면 스위퍼가 매 회차 실패한다
        Assert.Equal(0, await queue.ReclaimStaleAsync(TaskKind.Extract, TimeSpan.Zero, default));
    }

    private RedisTaskQueue Queue()
        => new(new RedisConnectionProvider(redis.ConnectionString),
               NullLogger<RedisTaskQueue>.Instance);

    /// <summary>
    /// 소비는 무한 열거라 하나만 받고 끊는다. 컨테이너가 굳어 테스트가 멈추는 일이
    /// 없도록 시간 제한을 둔다.
    /// </summary>
    private static async Task<QueuedTask> FirstAsync(RedisTaskQueue queue, TaskKind kind)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await foreach (var queued in queue.ConsumeAsync(kind, timeout.Token))
        {
            return queued;
        }

        throw new InvalidOperationException($"{kind} 스트림에서 메시지를 받지 못했습니다");
    }

    /// <summary>확인되지 않은 채 소비자 그룹이 물고 있는 메시지 수.</summary>
    private async Task<long> PendingCountAsync(TaskKind kind)
    {
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        await using (multiplexer)
        {
            var stream = $"noxtend:tasks:{kind.ToString().ToLowerInvariant()}";
            return (await multiplexer.GetDatabase().StreamPendingAsync(stream, "workers"))
                .PendingMessageCount;
        }
    }
}

/// <summary>
/// 큐 계약 테스트가 함께 쓰는 Redis 한 대.
///
/// SQL Server 와 같은 이유로 공유한다(<see cref="SqlServerFixture"/>) — 클래스마다
/// 띄우면 호스트 메모리를 놓고 개발용 클러스터와 다툰다.
///
/// **다만 이쪽은 데이터베이스를 나눠 격리할 수 없다.** 스트림 이름이 공정 종류로
/// 고정이라 어느 DB 에 붙어도 같은 키다. 테스트마다 비우는 쪽을 택한다.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer container =
        new RedisBuilder("redis:7-alpine").Build();

    /// <summary>FLUSHDB 를 쓰려면 관리 명령이 열려 있어야 한다.</summary>
    public string ConnectionString => $"{container.GetConnectionString()},allowAdmin=true";

    public Task InitializeAsync() => container.StartAsync();

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    /// <summary>앞 테스트가 남긴 스트림과 소비자 그룹을 지운다.</summary>
    public async Task ResetAsync()
    {
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        await using (multiplexer)
        {
            var endpoint = multiplexer.GetEndPoints().Single();
            await multiplexer.GetServer(endpoint).FlushDatabaseAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "redis";
}
