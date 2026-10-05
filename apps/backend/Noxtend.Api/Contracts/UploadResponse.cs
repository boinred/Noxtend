using Noxtend.Domain.Upload;

namespace Noxtend.Api.Contracts;

/// <summary>
/// Design Ref: §4.2 #2 · §10.4 — 엔티티를 직렬화하지 않는다.
///
/// <c>BlobKey</c> 가 여기 없는 것이 계약이다. 저장소 내부 경로를 클라이언트가 알 이유가
/// 없고, 알면 그것으로 직접 접근을 시도하게 된다.
/// </summary>
public sealed record UploadResponse(
    Guid Id,
    string OriginalName,
    string ContentType,
    long SizeBytes)
{
    public static UploadResponse From(StoredImage image)
        => new(image.Id, image.OriginalName, image.ContentType, image.SizeBytes);
}
