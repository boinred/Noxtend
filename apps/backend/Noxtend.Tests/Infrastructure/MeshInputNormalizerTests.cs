using Noxtend.Application.Common;
using Noxtend.Application.Mesh;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Mesh;
using SkiaSharp;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 올리기 전에 입력을 다듬는다.
///
/// Design Ref: §6.5 · §7.2 · Plan FR-05 · NFR-03
///
/// **검증이 업로드 앞에 있어야 한다.** 뒤에 두면 거절될 파일 네 장을 다 올린 뒤에야
/// 알게 되고, 그 왕복이 재시도마다 반복된다.
/// </summary>
public sealed class MeshInputNormalizerTests
{
    private static readonly MeshGenerationOptions Options = new();

    [Fact]
    public async Task Jpeg_GoesUpUntouched()
    {
        var normalizer = Normalizer();

        var prepared = await normalizer.PrepareAsync(
            Image(512, 512, SKEncodedImageFormat.Jpeg), "image/jpeg", ViewDirection.Front, default);

        // JPEG 을 다시 인코딩하면 세대 손실만 쌓인다 — Tripo 가 그대로 받는다
        Assert.Equal("image/jpeg", prepared.ContentType);
        Assert.Equal("front.jpg", prepared.SafeFileName);
    }

    [Fact]
    public async Task Png_GoesUpUntouched()
    {
        var normalizer = Normalizer();

        var prepared = await normalizer.PrepareAsync(
            Image(512, 512, SKEncodedImageFormat.Png), "image/png", ViewDirection.Right, default);

        Assert.Equal("image/png", prepared.ContentType);
        Assert.Equal("right.png", prepared.SafeFileName);
    }

    /// <summary>
    /// WebP 만 바꾼다 (§6.5).
    ///
    /// Tripo File Upload 문서는 JPEG/PNG 만 명시한다. 생성 계약은 WebP 를 허용하므로
    /// 언젠가 들어오는데, 그때 이 자리가 없으면 업로드가 거절된다.
    /// </summary>
    [Fact]
    public async Task Webp_BecomesPng()
    {
        var normalizer = Normalizer();

        var prepared = await normalizer.PrepareAsync(
            Image(512, 512, SKEncodedImageFormat.Webp), "image/webp", ViewDirection.Back, default);

        Assert.Equal("image/png", prepared.ContentType);
        Assert.Equal("back.png", prepared.SafeFileName);
    }

    /// <summary>
    /// **파일명에 방향만 넣는다** (§7.2 · §13.1).
    ///
    /// 파츠 이름이나 사용자 파일명을 넣으면 우리 쪽 어휘가 공급자 로그에 남는다.
    /// </summary>
    [Theory]
    [InlineData(ViewDirection.Front, "front.jpg")]
    [InlineData(ViewDirection.Right, "right.jpg")]
    [InlineData(ViewDirection.Back, "back.jpg")]
    [InlineData(ViewDirection.Left, "left.jpg")]
    public async Task FileName_IsTheDirectionAlone(ViewDirection direction, string expected)
    {
        var normalizer = Normalizer();

        var prepared = await normalizer.PrepareAsync(
            Image(512, 512, SKEncodedImageFormat.Jpeg), "image/jpeg", direction, default);

        Assert.Equal(expected, prepared.SafeFileName);
    }

    [Fact]
    public async Task ImageBelowMinimumResolution_IsRejected()
    {
        var normalizer = Normalizer();

        var failure = await Assert.ThrowsAsync<MeshInputRejectedException>(
            () => normalizer.PrepareAsync(
                Image(128, 128, SKEncodedImageFormat.Jpeg), "image/jpeg", ViewDirection.Front, default));

        // 작은 입력은 Tripo 가 받아도 형편없는 mesh 를 내고 credit 은 그대로 나간다
        Assert.Equal("MESH_INPUT_REJECTED", failure.FailureCode);
    }

    [Fact]
    public async Task ImageOverTheSizeCap_IsRejected()
    {
        var normalizer = Normalizer(new MeshGenerationOptions { MaxInputImageBytes = 64 });

        await Assert.ThrowsAsync<MeshInputRejectedException>(
            () => normalizer.PrepareAsync(
                Image(512, 512, SKEncodedImageFormat.Jpeg), "image/jpeg", ViewDirection.Front, default));
    }

    [Fact]
    public async Task UnknownContentType_IsRejected()
    {
        var normalizer = Normalizer();

        await Assert.ThrowsAsync<MeshInputRejectedException>(
            () => normalizer.PrepareAsync(
                Image(512, 512, SKEncodedImageFormat.Jpeg), "image/gif", ViewDirection.Front, default));
    }

    // ─── 설정 ───

    private static MeshInputNormalizer Normalizer(MeshGenerationOptions? options = null)
        => new(new SkiaImageTranscoder(), options ?? Options);

    /// <summary>실제 픽셀이 있는 이미지 — 헤더만 흉내내면 디코딩 경로가 안 돈다.</summary>
    private static Stream Image(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);

        using var data = bitmap.Encode(format, 90);
        return new MemoryStream(data.ToArray());
    }
}
