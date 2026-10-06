using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;
using SkiaSharp;

namespace Noxtend.Infrastructure.Mesh;

public sealed partial class SkiaImageTranscoder
{
    private const long SpriteMaxBytes = 32 * 1024 * 1024;
    private const long SpriteMaxPixels = 16_777_216;

    public async Task<SpriteImageInfo> InspectSpriteAsync(
        Stream image, long maxBytes, long maxPixels, CancellationToken ct)
    {
        var (bitmap, _, info) = await DecodeSpriteAsync(image, maxBytes, maxPixels, ct);
        using (bitmap)
        {
            return info;
        }
    }

    public async Task<Stream> NormalizeSpriteAsync(Stream image, SpriteCanvas canvas, SpriteTransform transform,
        bool requireTransparency, SpriteTileLayout layout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (canvas is null || transform is null || !Enum.IsDefined(layout))
            throw new ProviderBadResponseException("스프라이트 캔버스·변환·레이아웃이 유효하지 않습니다");
        ValidateSpriteSize(canvas.Width, canvas.Height, SpriteMaxPixels);
        if (layout == SpriteTileLayout.Diamond && canvas.Width != (long)canvas.Height * 2)
            throw new ProviderBadResponseException("다이아몬드 캔버스는 2:1 비율이어야 합니다");

        var (bitmap, origin, info) = await DecodeSpriteAsync(image, SpriteMaxBytes, SpriteMaxPixels, ct);
        using var decoded = bitmap;

        // 패딩·마스크 이전의 실제 공급자 알파
        if (requireTransparency && !info.HasTransparentPixels)
            throw new ProviderBadResponseException("원본 이미지에 투명 픽셀이 없습니다");

        var right = transform.OffsetX + info.Width * transform.Scale;
        var bottom = transform.OffsetY + info.Height * transform.Scale;
        if (!double.IsFinite(transform.Scale) || transform.Scale <= 0
            || !double.IsFinite(transform.OffsetX) || !double.IsFinite(transform.OffsetY)
            || transform.OffsetX < 0 || transform.OffsetY < 0
            || !double.IsFinite(right) || !double.IsFinite(bottom)
            || right > canvas.Width + 0.000001 || bottom > canvas.Height + 0.000001)
            throw new ProviderBadResponseException("고정 변환이 캔버스 내부에 이미지를 배치하지 않습니다");

        ct.ThrowIfCancellationRequested();
        using var oriented = origin == SKEncodedOrigin.TopLeft ? null : ApplyOrigin(decoded, origin);
        using var output = new SKBitmap(new SKImageInfo(
            canvas.Width, canvas.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var drawing = new SKCanvas(output))
        {
            drawing.Clear(SKColors.Transparent);
            if (layout == SpriteTileLayout.Diamond)
            {
                using var builder = new SKPathBuilder();
                builder.MoveTo(canvas.Width / 2f, 0);
                builder.LineTo(canvas.Width, canvas.Height / 2f);
                builder.LineTo(canvas.Width / 2f, canvas.Height);
                builder.LineTo(0, canvas.Height / 2f);
                builder.Close();
                using var diamond = builder.Detach();
                drawing.ClipPath(diamond, SKClipOperation.Intersect, false);
            }

            drawing.DrawBitmap(oriented ?? decoded, new SKRect(
                (float)transform.OffsetX, (float)transform.OffsetY, (float)right, (float)bottom),
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }

        if (!SpriteAlpha(output, ct).HasVisiblePixels)
            throw new ProviderBadResponseException("정규화 결과에 보이는 픽셀이 없습니다");
        using var png = output.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new ProviderBadResponseException("스프라이트 PNG를 인코딩할 수 없습니다");
        if (png.Size > SpriteMaxBytes)
            throw new ProviderBadResponseException("스프라이트 PNG가 바이트 상한을 넘습니다");
        ct.ThrowIfCancellationRequested();
        return new MemoryStream(png.ToArray());
    }

    private static async Task<(SKBitmap Bitmap, SKEncodedOrigin Origin, SpriteImageInfo Info)> DecodeSpriteAsync(
        Stream image, long maxBytes, long maxPixels, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (maxBytes <= 0 || maxPixels <= 0)
            throw new ProviderBadResponseException("이미지 검증 상한은 양수여야 합니다");
        maxBytes = Math.Min(maxBytes, SpriteMaxBytes);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await image.ReadAsync(chunk.AsMemory(0,
                (int)Math.Min(chunk.Length, maxBytes - buffer.Length + 1)), ct);
            if (count == 0)
                break;
            if (buffer.Length + count > maxBytes)
                throw new ProviderBadResponseException("이미지가 바이트 상한을 넘습니다");
            buffer.Write(chunk, 0, count);
        }

        buffer.Position = 0;
        using var managed = new SKManagedStream(buffer, false);
        using var codec = SKCodec.Create(managed)
            ?? throw new ProviderBadResponseException("이미지를 읽을 수 없습니다");
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp))
            throw new ProviderBadResponseException("PNG·JPEG·WebP 이미지만 지원합니다");

        // 압축된 입력의 64비트 픽셀 상한, 디코딩 메모리 할당 전 검증
        ValidateSpriteSize(codec.Info.Width, codec.Info.Height, maxPixels);
        ct.ThrowIfCancellationRequested();
        var bitmap = new SKBitmap(new SKImageInfo(
            codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        try
        {
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                throw new ProviderBadResponseException("이미지 픽셀을 완전히 읽을 수 없습니다");
            var (transparent, visible) = SpriteAlpha(bitmap, ct);
            if (!visible)
                throw new ProviderBadResponseException("이미지에 보이는 픽셀이 없습니다");
            var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            return (bitmap, codec.EncodedOrigin, new SpriteImageInfo(
                swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height, transparent, visible));
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void ValidateSpriteSize(int width, int height, long maxPixels)
    {
        if (width <= 0 || height <= 0 || (long)width * height > Math.Min(maxPixels, SpriteMaxPixels))
            throw new ProviderBadResponseException("이미지 크기가 픽셀 상한을 넘거나 유효하지 않습니다");
    }

    private static (bool HasTransparentPixels, bool HasVisiblePixels) SpriteAlpha(SKBitmap bitmap, CancellationToken ct)
    {
        var transparent = false;
        var visible = false;
        for (var y = 0; y < bitmap.Height; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (var x = 0; x < bitmap.Width; x++)
            {
                var alpha = bitmap.GetPixel(x, y).Alpha;
                transparent |= alpha < 255;
                visible |= alpha > 0;
                if (transparent && visible)
                    return (true, true);
            }
        }
        return (transparent, visible);
    }
}
