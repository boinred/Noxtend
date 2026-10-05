using SkiaSharp;

namespace Noxtend.Tests;

/// <summary>
/// 실제 픽셀이 있는 테스트 이미지.
///
/// 헤더만 흉내낸 바이트로는 정규화 경로가 돌지 않는다 — 최소 해상도 검사가 디코더를
/// 거치므로, 가짜 바이트는 "읽을 수 없습니다" 로 끝난다.
/// </summary>
public static class TestImages
{
    public static MemoryStream Jpeg(int width, int height)
        => Encode(width, height, SKEncodedImageFormat.Jpeg);

    public static MemoryStream Png(int width, int height)
        => Encode(width, height, SKEncodedImageFormat.Png);

    private static MemoryStream Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);

        using var data = bitmap.Encode(format, 90);
        return new MemoryStream(data.ToArray());
    }
}
