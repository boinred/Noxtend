namespace Noxtend.Domain.Upload;

/// <summary>
/// 저장된 소스 이미지.
///
/// Design Ref: §3.1
///
/// <see cref="BlobKey"/> 는 서버가 만든다. 원본 파일명을 경로로 쓰면 사용자가 보낸
/// 문자열이 저장소 경로가 되어 경로 조작이 열린다 (§7).
/// </summary>
public sealed class StoredImage
{
    private StoredImage()
    {
        // EF Core 재구성용
    }

    private StoredImage(
        Guid id,
        string blobKey,
        string originalName,
        string contentType,
        long sizeBytes,
        DateTimeOffset createdAt)
    {
        Id = id;
        BlobKey = blobKey;
        OriginalName = originalName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>저장소 안의 위치. 서버 생성이며 사용자 입력이 섞이지 않는다.</summary>
    public string BlobKey { get; private set; } = string.Empty;

    /// <summary>표시용으로만 쓴다. 경로로 쓰지 않는다.</summary>
    public string OriginalName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static StoredImage Create(
        string blobKey,
        string originalName,
        string contentType,
        long sizeBytes,
        DateTimeOffset now)
        => new(Guid.NewGuid(), blobKey, originalName, contentType, sizeBytes, now);
}
