using Noxtend.Domain.Ports;
using SkiaSharp;

namespace Noxtend.Infrastructure.Mesh;

/// <summary>
/// SkiaSharp 기반 <see cref="IImageTranscoder"/>.
///
/// Design Ref: §6.5
///
/// **Skia 를 고른 이유는 Linux 런타임이다.** `System.Drawing` 은 Windows 전용이고,
/// 다른 후보들은 상용 사용에 별도 라이선스를 요구한다. 컨테이너가 Linux 라 네이티브
/// 자산이 함께 실려야 한다.
///
/// **실제 경로에서는 거의 아무 일도 하지 않는다** (§1.4). 지금 생성되는 이미지는 전부
/// JPEG 이고 Tripo 가 그대로 받는다. 이 클래스가 실제로 도는 것은 크기를 잴 때뿐이다.
/// </summary>
public sealed partial class SkiaImageTranscoder : IImageTranscoder
{
    public bool IsUploadable(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" or "image/png" => true,
        _ => false,
    };

    /// <summary>
    /// 픽셀을 전부 디코딩하지 않고 헤더만 읽는다 — 크기만 알면 되는데 20 MB 를
    /// 비트맵으로 펼치면 파드 메모리가 워커 수만큼 배로 든다.
    /// </summary>
    public Task<(int Width, int Height)> MeasureAsync(Stream image, CancellationToken ct)
    {
        using var codec = SKCodec.Create(new SKManagedStream(image));

        if (codec is null)
        {
            throw new InvalidOperationException("이미지를 읽을 수 없습니다");
        }

        return Task.FromResult((codec.Info.Width, codec.Info.Height));
    }

    public Task<Stream> ToPngAsync(Stream image, long maxBytes, CancellationToken ct)
    {
        using var bitmap = SKBitmap.Decode(new SKManagedStream(image))
            ?? throw new InvalidOperationException("이미지를 읽을 수 없습니다");

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        // **줄여서 올리지 않는다.** 품질을 낮춰 통과시키면 우리가 모르는 손실이
        // 결과 mesh 에 남고, 왜 나빠졌는지 나중에 추적할 수 없다
        if (data.Size > maxBytes)
        {
            throw new InvalidOperationException(
                $"PNG 로 바꾼 결과가 상한을 넘습니다: {data.Size} 바이트");
        }

        return Task.FromResult<Stream>(new MemoryStream(data.ToArray()));
    }

    public Task<Stream> FlipHorizontallyAsync(Stream image, string contentType, CancellationToken ct)
    {
        using var bitmap = SKBitmap.Decode(new SKManagedStream(image))
            ?? throw new InvalidOperationException("이미지를 읽을 수 없습니다");

        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        var canvas = surface.Canvas;

        // 가로축 기준 좌우 반전 — 캔버스를 오른쪽 끝으로 옮긴 뒤 x 축을 뒤집어서 그린다
        canvas.Translate(bitmap.Width, 0);
        canvas.Scale(-1, 1);
        canvas.DrawBitmap(bitmap, 0, 0);

        using var snapshot = surface.Snapshot();

        // 들어온 형식 그대로 다시 인코딩한다 — 호출자가 이미 아는 contentType 을 그대로 쓴다
        var format = contentType.ToLowerInvariant() switch
        {
            "image/png" => SKEncodedImageFormat.Png,
            _ => SKEncodedImageFormat.Jpeg,
        };

        using var encoded = snapshot.Encode(format, 100);
        return Task.FromResult<Stream>(new MemoryStream(encoded.ToArray()));
    }

    /// <summary>
    /// 평가 프레임 정규화 (background-similarity-tuning §8.1).
    /// SKCodec origin 으로 EXIF 방향을 읽어 픽셀에 적용한다 — 스마트폰 원본은 회전
    /// 메타데이터만 있는 경우가 많아, 무시하면 평가 모델이 누운 사진을 본다.
    /// </summary>
    public Task<byte[]> NormalizeToFrameAsync(
        Stream image, int frameWidth, int frameHeight, string backgroundHex, CancellationToken ct)
    {
        using var codec = SKCodec.Create(new SKManagedStream(image))
            ?? throw new InvalidOperationException("이미지를 읽을 수 없습니다");
        using var decoded = SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException("이미지를 읽을 수 없습니다");

        using var oriented = ApplyOrigin(decoded, codec.EncodedOrigin);

        // contain — 비율 유지, 짧은 축의 남는 영역은 배경색 (§8.1)
        var scale = Math.Min(
            (float)frameWidth / oriented.Width, (float)frameHeight / oriented.Height);
        var width = oriented.Width * scale;
        var height = oriented.Height * scale;
        var left = (frameWidth - width) / 2;
        var top = (frameHeight - height) / 2;

        using var surface = SKSurface.Create(new SKImageInfo(frameWidth, frameHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColor.Parse(backgroundHex));
        canvas.DrawBitmap(
            oriented,
            new SKRect(left, top, left + width, top + height),
            new SKPaint { IsAntialias = true });

        using var snapshot = surface.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return Task.FromResult(png.ToArray());
    }

    // EXIF origin → 픽셀 회전·반전. 정방향이면 복사 없이 그대로 쓰지 않고 복제한다 —
    // 반환값을 일괄 dispose 하는 호출부 계약을 단순하게 유지한다
    private static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return source.Copy();
        }

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var rotated = new SKBitmap(
            swap ? source.Height : source.Width,
            swap ? source.Width : source.Height);

        using var canvas = new SKCanvas(rotated);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1, source.Width / 2f, 0);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, source.Width / 2f, source.Height / 2f);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1, 0, source.Height / 2f);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.RotateDegrees(90);
                canvas.Translate(0, -source.Height);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.RotateDegrees(-90);
                canvas.Scale(1, -1, source.Width / 2f, source.Height / 2f);
                canvas.Translate(-source.Width, 0);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.RotateDegrees(-90);
                canvas.Translate(-source.Width, 0);
                break;
        }

        canvas.DrawBitmap(source, 0, 0);
        return rotated;
    }
}
