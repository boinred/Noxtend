using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;
using Noxtend.Domain.Upload;

namespace Noxtend.Application.Sprites;

public sealed record SpriteSourceContent(byte[] Bytes, SpriteImageInfo Info);

// 업로드·기존 결과·원본 후보가 공유하는 원본 검증
public static class SpriteSourceInspector
{
    public const long MaxBytes = 12 * 1024 * 1024;

    public static async Task<Result<SpriteSourceContent>> InspectAsync(IBlobStorage blobs, IImageTranscoder transcoder,
        string key, string? expectedContentType, CancellationToken ct)
    {
        using var content = new MemoryStream();
        SpriteImageInfo info;
        try
        {
            await using var source = await blobs.OpenReadAsync(key, ct);
            // 비seek 원본에도 적용하는 바이트 상한
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, MaxBytes + 1 - content.Length)), ct);
                if (read == 0) break;
                await content.WriteAsync(buffer.AsMemory(0, read), ct);
                if (content.Length > MaxBytes) return Fail(ErrorCode.UploadTooLarge, "입력 이미지는 최대 12 MiB입니다");
            }
            content.Position = 0;
            info = await transcoder.InspectSpriteAsync(content, MaxBytes, 16_777_216, ct);
        }
        catch (ProviderBadResponseException)
        {
            return Fail(ErrorCode.UploadUnsupportedType, "PNG·JPEG·WebP의 유효한 이미지와 최대 16,777,216픽셀이 필요합니다");
        }
        catch (FileNotFoundException)
        {
            return Fail(ErrorCode.JobUploadNotFound, "원본 이미지 파일이 없습니다");
        }
        if (expectedContentType is not null && !string.Equals(expectedContentType, info.ContentType, StringComparison.OrdinalIgnoreCase))
            return Fail(ErrorCode.UploadUnsupportedType, "업로드 MIME과 실제 이미지 형식이 일치해야 합니다");
        return Result<SpriteSourceContent>.Ok(new(content.ToArray(), info));
    }

    // 원본 작업·후보의 삭제 수명에서 분리한 독립 복사본
    public static async Task<StoredImage> CopyAsync(IBlobStorage blobs, SpriteSourceContent content,
        string originalName, DateTimeOffset now, CancellationToken ct)
    {
        using var stream = new MemoryStream(content.Bytes, writable: false);
        var key = await blobs.SaveAsync(stream, content.Info.ContentType, ct);
        return StoredImage.Create(key, originalName, content.Info.ContentType, content.Bytes.Length, now);
    }

    private static Result<SpriteSourceContent> Fail(string code, string message) => Result<SpriteSourceContent>.Fail(code, message);
}
