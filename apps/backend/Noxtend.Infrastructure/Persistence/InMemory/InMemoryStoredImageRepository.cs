using System.Collections.Concurrent;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Upload;

namespace Noxtend.Infrastructure.Persistence.InMemory;

/// <summary>Design Ref: §8.1 L1-B — 업로드 유스케이스를 DB 없이 검증한다.</summary>
public sealed class InMemoryStoredImageRepository : IStoredImageRepository
{
    private readonly ConcurrentDictionary<Guid, StoredImage> _images = new();

    public Task AddAsync(StoredImage image, CancellationToken ct)
    {
        _images[image.Id] = image;
        return Task.CompletedTask;
    }

    public Task<StoredImage?> GetAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_images.GetValueOrDefault(id));

    /// <summary>업로드 하나를 지우고 그 저장소 키를 돌려준다. 없으면 <c>null</c>.</summary>
    public string? Remove(Guid id)
        => _images.TryRemove(id, out var image) ? image.BlobKey : null;

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
