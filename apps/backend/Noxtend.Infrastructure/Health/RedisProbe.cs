using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Noxtend.Infrastructure.Health;

/// <summary>
/// Design Ref: §4.2 #1 — the "redis" entry of /health.
///
/// The multiplexer is resolved lazily rather than injected: a connection failure at
/// startup would otherwise take the whole API down, and §6 requires Redis being
/// unavailable to degrade to a delay, not an outage.
/// </summary>
public sealed class RedisProbe(
    Func<Task<IConnectionMultiplexer>> multiplexerFactory,
    ILogger<RedisProbe> logger) : IDependencyProbe
{
    public string Name => "redis";

    public async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        try
        {
            var multiplexer = await multiplexerFactory();
            await multiplexer.GetDatabase().PingAsync();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Health probe failed: redis");
            return false;
        }
    }
}
