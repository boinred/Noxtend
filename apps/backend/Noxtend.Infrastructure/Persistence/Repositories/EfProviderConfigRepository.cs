using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;

namespace Noxtend.Infrastructure.Persistence.Repositories;

/// <summary>Design Ref: §3.2 · §9.3</summary>
public sealed class EfProviderConfigRepository(NoxtendDbContext db) : IProviderConfigRepository
{
    public async Task AddAsync(ProviderConfig config, CancellationToken ct)
        => await db.ProviderConfigs.AddAsync(config, ct);

    public Task<ProviderConfig?> GetAsync(Guid id, CancellationToken ct)
        => db.ProviderConfigs.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<ProviderConfig>> ListAsync(CancellationToken ct)
        => await db.ProviderConfigs.OrderBy(c => c.DisplayName).ToListAsync(ct);

    public Task RemoveAsync(ProviderConfig config, CancellationToken ct)
    {
        db.ProviderConfigs.Remove(config);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
