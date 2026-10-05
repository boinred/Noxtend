using System.Collections.Concurrent;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.RateLimit;

/// <summary>Design Ref: §8.1 L1-B — 속도 제한 판정(`RateLimitGate`)을 Redis 없이 검증한다.</summary>
public sealed class InMemoryRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<Guid, RateLimitStatus> _byProvider = new();

    public Task<RateLimitStatus?> GetStatusAsync(Guid providerConfigId, CancellationToken ct)
        => Task.FromResult(_byProvider.GetValueOrDefault(providerConfigId));

    public Task UpdateAsync(Guid providerConfigId, RateLimitStatus status, CancellationToken ct)
    {
        _byProvider[providerConfigId] = status;
        return Task.CompletedTask;
    }
}
