using Noxtend.Infrastructure.Mesh;
using SkiaSharp;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 좌우 대칭 이미지를 실제로 반전한다 (spec 20260917).
///
/// **도메인이 아니라 이 계층에서 반전한다.** `MeshRunInput.ViewDirection`(슬롯)과
/// `GeneratedImage.ViewDirection`(원본 방향)이 다를 때만 이 자리에서 뒤집으면 되므로,
/// 도메인 타입은 하나도 바뀌지 않는다.
/// </summary>
public sealed class SkiaImageTranscoderTests
{
    [Fact]
    public async Task FlipHorizontallyAsync_SwapsLeftAndRightHalves()
    {
        var transcoder = new SkiaImageTranscoder();

        // 왼쪽 절반은 빨강, 오른쪽 절반은 파랑 — 반전 여부를 픽셀로 확인할 수 있다
        using var source = HalfRedHalfBlue(width: 4, height: 4);

        await using var flipped = await transcoder.FlipHorizontallyAsync(
            source, "image/png", default);

        using var bitmap = SKBitmap.Decode(new SKManagedStream(flipped))
            ?? throw new InvalidOperationException("반전 결과를 읽을 수 없습니다");

        Assert.Equal(SKColors.Blue, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(3, 0));
    }

    private static Stream HalfRedHalfBlue(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Red);
        canvas.DrawRect(width / 2f, 0, width / 2f, height, new SKPaint { Color = SKColors.Blue });

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(data.ToArray());
    }
}
