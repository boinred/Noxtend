using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Upload;

namespace Noxtend.Infrastructure.Persistence.Repositories;

/// <summary>Design Ref: §3.2 · §9.3</summary>
public sealed class EfStoredImageRepository(NoxtendDbContext db) : IStoredImageRepository
{
    public async Task AddAsync(StoredImage image, CancellationToken ct)
        => await db.StoredImages.AddAsync(image, ct);

    public Task<StoredImage?> GetAsync(Guid id, CancellationToken ct)
        => db.StoredImages.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
