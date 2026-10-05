using StackExchange.Redis;

namespace Noxtend.Infrastructure.Queue;

/// <summary>
/// Shared, lazily-established Redis connection.
///
/// Design Ref: §6 — "Redis 불가" must degrade to a delay, not an outage. Connecting
/// eagerly at startup would make an unreachable broker fail the whole API instead.
///
/// A plain <see cref="Lazy{T}"/> is not enough: it caches the faulted task, so one
/// failed attempt at boot would keep the connection dead for the process lifetime.
/// A failed attempt is therefore discarded and the next caller retries.
/// </summary>
public sealed class RedisConnectionProvider(string connectionString) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnectionMultiplexer? _multiplexer;

    public async Task<IConnectionMultiplexer> GetAsync()
    {
        if (_multiplexer is { IsConnected: true })
        {
            return _multiplexer;
        }

        await _gate.WaitAsync();
        try
        {
            // Re-check inside the gate — a concurrent caller may have connected already
            if (_multiplexer is { IsConnected: true })
            {
                return _multiplexer;
            }

            var previous = _multiplexer;
            _multiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);

            if (previous is not null)
            {
                await previous.DisposeAsync();
            }

            return _multiplexer;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.DisposeAsync();
        }

        _gate.Dispose();
    }
}
