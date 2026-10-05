using System.Collections.Concurrent;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;

namespace Noxtend.Infrastructure.Persistence.InMemory;

/// <summary>Design Ref: §8.1 L1-B — 공급자 선택 검증(작업 접수)을 DB 없이 돌린다.</summary>
public sealed class InMemoryProviderConfigRepository : IProviderConfigRepository
{
    private readonly ConcurrentDictionary<Guid, ProviderConfig> _configs = new();

    public Task AddAsync(ProviderConfig config, CancellationToken ct)
    {
        _configs[config.Id] = config;
        return Task.CompletedTask;
    }

    public Task<ProviderConfig?> GetAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_configs.GetValueOrDefault(id));

    public Task<IReadOnlyList<ProviderConfig>> ListAsync(CancellationToken ct)
    {
        IReadOnlyList<ProviderConfig> result = _configs.Values
            .OrderBy(c => c.DisplayName)
            .ToList();

        return Task.FromResult(result);
    }

    public Task RemoveAsync(ProviderConfig config, CancellationToken ct)
    {
        _configs.TryRemove(config.Id, out _);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
