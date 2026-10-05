using Noxtend.Domain.Provider;

namespace Noxtend.Domain.Ports;

/// <summary>공급자 설정 저장소. Design Ref: §3.2</summary>
public interface IProviderConfigRepository
{
    Task AddAsync(ProviderConfig config, CancellationToken ct);

    Task<ProviderConfig?> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ProviderConfig>> ListAsync(CancellationToken ct);

    Task RemoveAsync(ProviderConfig config, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
