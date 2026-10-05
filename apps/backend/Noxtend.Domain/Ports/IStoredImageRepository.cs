using Noxtend.Domain.Upload;

namespace Noxtend.Domain.Ports;

/// <summary>업로드된 소스 이미지의 메타 저장소. Design Ref: §3.2</summary>
public interface IStoredImageRepository
{
    Task AddAsync(StoredImage image, CancellationToken ct);

    Task<StoredImage?> GetAsync(Guid id, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
