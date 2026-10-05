using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Noxtend.Api.Workers;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Queue;

namespace Noxtend.Tests.Api;

/// <summary>
/// "경합은 오류가 아니다"가 실제로 지켜지는지 고정한다(merge-gate 2차 리뷰 B3).
///
/// `EfJobRepository.SaveChangesAsync`가 `DbUpdateConcurrencyException`을
/// `ConcurrencyConflictException`으로 감싸면서, `TaskWorker`의 기존
/// `catch (DbUpdateConcurrencyException)`이 죽은 코드가 될 뻔했다(F2). `when`
/// 필터로 고쳤는데, 그 필터를 나중에 누가 지우거나 잘못 고치면 조용히
/// 회귀한다 — 경합이 `LogError`로 잘못 기록되기 시작해도 테스트 없이는 아무도
/// 모른다.
/// </summary>
public sealed class TaskWorkerConcurrencyTests
{
    [Fact]
    public async Task ConcurrencyConflict_LogsAsInformation_NotAsError()
    {
        var queue = new InMemoryTaskQueue();
        var taskId = Guid.NewGuid();
        await queue.EnqueueAsync(taskId, TaskKind.Reconstruct, CancellationToken.None);

        var logger = new CapturingLogger<TaskWorker>();
        var handler = new ThrowingHandler(
            new ConcurrencyConflictException("충돌", new DbUpdateConcurrencyException()));

        await using var services = new ServiceCollection().BuildServiceProvider();

        var worker = new TaskWorker(
            TaskKind.Reconstruct, _ => handler, services.GetRequiredService<IServiceScopeFactory>(),
            queue, logger);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // 워커가 큐에서 하나를 꺼내 실패시키고 Ack 할 때까지 기다린다(폴링, 최대 2초)
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (logger.Entries.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Information && e.Message.Contains("settled by another worker"));
    }

    private sealed class ThrowingHandler(Exception exception) : ITaskHandler
    {
        public Task<RunTaskOutcome> HandleAsync(Guid taskId, CancellationToken ct) => throw exception;
    }

    /// <summary>레벨·메시지만 기록하는 최소 로거 — 순서·개수를 검증할 수 있으면 충분하다.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
