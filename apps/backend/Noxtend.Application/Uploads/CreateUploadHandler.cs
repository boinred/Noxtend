using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Upload;
using Noxtend.Domain.Validation;

namespace Noxtend.Application.Uploads;

/// <summary>
/// 소스 이미지 업로드.
///
/// Design Ref: §2.2 업로드 · §8.2 #10
///
/// **서버가 다시 검증한다.** 프론트에 같은 규칙이 있지만 그것은 즉시 피드백용이고,
/// 클라이언트를 신뢰하지 않는다 (§2.2). 저장 순서는 Blob 먼저, 메타 나중이다 —
/// 반대로 하면 저장에 실패했을 때 가리키는 대상이 없는 메타가 남는다.
/// </summary>
public sealed class CreateUploadHandler(
    IBlobStorage blobs,
    IStoredImageRepository images,
    IClock clock)
{
    public async Task<Result<StoredImage>> HandleAsync(
        Stream content,
        string originalName,
        string? contentType,
        long sizeBytes,
        CancellationToken ct)
    {
        var rejection = UploadRules.Validate(contentType, sizeBytes);
        if (rejection is { } reason)
        {
            return Result<StoredImage>.Fail(ToCode(reason), ToMessage(reason));
        }

        var blobKey = await blobs.SaveAsync(content, contentType!, ct);

        var image = StoredImage.Create(blobKey, originalName, contentType!, sizeBytes, clock.Now);
        await images.AddAsync(image, ct);
        await images.SaveChangesAsync(ct);

        return Result<StoredImage>.Ok(image);
    }

    private static string ToCode(UploadRejection reason) => reason switch
    {
        UploadRejection.Empty => ErrorCode.UploadEmpty,
        UploadRejection.UnsupportedType => ErrorCode.UploadUnsupportedType,
        UploadRejection.TooLarge => ErrorCode.UploadTooLarge,
        _ => ErrorCode.UploadUnsupportedType,
    };

    private static string ToMessage(UploadRejection reason) => reason switch
    {
        UploadRejection.Empty => "빈 파일입니다",
        UploadRejection.UnsupportedType => "PNG · JPG · WEBP 만 올릴 수 있습니다",
        UploadRejection.TooLarge => $"최대 {UploadRules.MaxImageBytes / 1024 / 1024} MB 까지 올릴 수 있습니다",
        _ => "업로드할 수 없는 파일입니다",
    };
}
