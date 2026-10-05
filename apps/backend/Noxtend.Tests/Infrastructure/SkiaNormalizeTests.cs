using Noxtend.Infrastructure.Mesh;
using SkiaSharp;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 평가 프레임 정규화 (background-similarity-tuning §8.1).
///
/// **reference 와 render 가 같은 프레임이어야 비교가 공정하다** — 원본은 임의 비율의
/// 사진이라 contain 배치와 배경 채움이 없으면 평가 모델이 서로 다른 구도를 본다.
/// </summary>
public sealed class SkiaNormalizeTests
{
    [Fact]
    public async Task Normalize_ContainsTheImageInASquareFrameWithBackground()
    {
        // 4×2 빨강 — 가로가 긴 사진
        using var bitmap = new SKBitmap(4, 2);
        bitmap.Erase(SKColors.Red);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        var normalized = await new SkiaImageTranscoder().NormalizeToFrameAsync(
            new MemoryStream(data.ToArray()), 8, 8, "#f5f5f4", CancellationToken.None);

        using var result = SKBitmap.Decode(normalized);
        Assert.Equal(8, result.Width);
        Assert.Equal(8, result.Height);

        // 가운데 띠는 빨강, 위아래 여백은 배경색
        Assert.Equal(SKColor.Parse("#ff0000"), result.GetPixel(4, 4));
        Assert.Equal(SKColor.Parse("#f5f5f4"), result.GetPixel(4, 0));
        Assert.Equal(SKColor.Parse("#f5f5f4"), result.GetPixel(4, 7));
    }

    /// <summary>세로형 프레임 — 렌더가 원본 비율이면 reference 도 그 프레임을 따른다.</summary>
    [Fact]
    public async Task Normalize_FollowsAPortraitFrame()
    {
        using var bitmap = new SKBitmap(4, 8);   // 이미 프레임과 같은 비율
        bitmap.Erase(SKColors.Red);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        var normalized = await new SkiaImageTranscoder().NormalizeToFrameAsync(
            new MemoryStream(data.ToArray()), 4, 8, "#f5f5f4", CancellationToken.None);

        using var result = SKBitmap.Decode(normalized);
        Assert.Equal(4, result.Width);
        Assert.Equal(8, result.Height);
        Assert.Equal(SKColor.Parse("#ff0000"), result.GetPixel(2, 4));   // 여백 없이 가득
    }
}
