using System.Buffers.Binary;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;
using Noxtend.Infrastructure.Mesh;
using SkiaSharp;

namespace Noxtend.Tests.Infrastructure;

public sealed class SpritePixelTests
{
    private readonly IImageTranscoder _transcoder = new SkiaImageTranscoder();
    private const long MaxBytes = 12 * 1024 * 1024;
    private const long MaxPixels = 16_777_216;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpaqueCheckerboard_IsNotTransparency(bool checkerboard)
    {
        using var source = Image(4, 4, (x, y) => checkerboard && (x + y) % 2 == 0
            ? SKColors.Gray : SKColors.White);
        var info = await _transcoder.InspectSpriteAsync(source, MaxBytes, MaxPixels, default);
        Assert.False(info.HasTransparentPixels);
        Assert.True(info.HasVisiblePixels);
        Assert.True(source.CanRead);
        source.Position = 0;
        await Assert.ThrowsAsync<ProviderBadResponseException>(() => _transcoder.NormalizeSpriteAsync(
            source, new(8, 8), new(1, 2, 2), true, SpriteTileLayout.Square, default));
        source.Position = 0;
        await Assert.ThrowsAsync<ProviderBadResponseException>(() => _transcoder.NormalizeSpriteAsync(
            source, new(8, 4), new(1, 2, 0), true, SpriteTileLayout.Diamond, default));
    }

    [Theory]
    [InlineData(16_777_217, 1)]
    [InlineData(4096, 4097)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public async Task OversizedHeader_RejectsBeforePixelAllocation(int width, int height)
    {
        using var fixture = Image(1, 1, (_, _) => SKColors.Red);
        var bytes = fixture.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), Crc(bytes.AsSpan(12, 17)));
        using var source = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<ProviderBadResponseException>(() =>
            _transcoder.InspectSpriteAsync(source, MaxBytes, long.MaxValue, default));
        if (width == 4096)
            Assert.Contains("픽셀 상한", error.Message);
    }

    [Fact]
    public async Task AllTransparentImage_IsRejected()
    {
        using var source = Image(4, 4, (_, _) => SKColors.Transparent);
        await Assert.ThrowsAsync<ProviderBadResponseException>(() =>
            _transcoder.InspectSpriteAsync(source, MaxBytes, MaxPixels, default));
        source.Position = 0;
        await Assert.ThrowsAsync<ProviderBadResponseException>(() => _transcoder.NormalizeSpriteAsync(
            source, new(4, 4), new(1, 0, 0), true, SpriteTileLayout.Square, default));
    }

    [Fact]
    public async Task Normalize_PreservesAlphaAndFixedOrigin()
    {
        foreach (var otherX in new[] { 0, 5 })
        {
            using var source = Image(6, 4, (x, y) => x == 2 && y == 1
                ? new SKColor(255, 0, 0, 128)
                : x == otherX && y == 3 ? SKColors.Blue : SKColors.Transparent);
            await using var output = await _transcoder.NormalizeSpriteAsync(
                source, new(10, 8), new(1, 1, 2), true, SpriteTileLayout.Square, default);
            Assert.Equal(0, output.Position);
            Assert.True(source.CanRead);
            using var decoded = SKBitmap.Decode(output);
            Assert.Equal(128, decoded.GetPixel(3, 3).Alpha);
            Assert.Equal(255, decoded.GetPixel(otherX + 1, 5).Alpha);
            Assert.Equal(0, decoded.GetPixel(0, 0).Alpha);
            Assert.Equal(10, decoded.Width);
            Assert.Equal(8, decoded.Height);
        }
    }

    [Fact]
    public async Task Normalize_UsesProvidedContainScale()
    {
        using var source = Image(8, 4, (x, y) => x == 4 && y == 2 ? SKColors.Red : SKColors.Transparent);
        await using var output = await _transcoder.NormalizeSpriteAsync(
            source, new(16, 16), new(2, 0, 4), true, SpriteTileLayout.Square, default);
        using var decoded = SKBitmap.Decode(output);
        Assert.Equal(SKColors.Red, decoded.GetPixel(8, 8));
        Assert.Equal(0, decoded.GetPixel(8, 3).Alpha);
    }

    [Fact]
    public async Task DiamondCell_HasTransparentCornersAndVisibleCenter()
    {
        using var source = Image(8, 4, (x, y) => x == 0 && y == 0 ? SKColors.Transparent : SKColors.Red);
        await using var output = await _transcoder.NormalizeSpriteAsync(
            source, new(8, 4), new(1, 0, 0), true, SpriteTileLayout.Diamond, default);
        using var decoded = SKBitmap.Decode(output);
        foreach (var (x, y) in new[] { (0, 0), (7, 0), (0, 3), (7, 3) })
            Assert.Equal(0, decoded.GetPixel(x, y).Alpha);
        Assert.Equal(255, decoded.GetPixel(4, 2).Alpha);
    }

    [Fact]
    public async Task DiamondMask_RejectsContentEntirelyOutsideCell()
    {
        using var source = Image(8, 4, (x, y) => x == 0 && y == 0 ? SKColors.Red : SKColors.Transparent);
        await Assert.ThrowsAsync<ProviderBadResponseException>(() => _transcoder.NormalizeSpriteAsync(
            source, new(8, 4), new(1, 0, 0), true, SpriteTileLayout.Diamond, default));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task Inspect_DecodesSupportedFormats(SKEncodedImageFormat format)
    {
        using var source = Image(5, 3, (_, _) => SKColors.Red, format);
        var info = await _transcoder.InspectSpriteAsync(source, MaxBytes, MaxPixels, default);
        Assert.Equal(new SpriteImageInfo(5, 3, false, true), info);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task ExifOrientation_AppliesToInspectionAndPixels(int origin)
    {
        using var jpeg = Image(40, 20, (x, y) => y < 10
            ? x < 20 ? SKColors.Red : SKColors.Blue
            : x < 20 ? SKColors.Green : SKColors.Yellow, SKEncodedImageFormat.Jpeg);
        var bytes = jpeg.ToArray();
        byte[] exif = [0xff, 0xe1, 0, 34, 69, 120, 105, 102, 0, 0,
            73, 73, 42, 0, 8, 0, 0, 0, 1, 0,
            0x12, 1, 3, 0, 1, 0, 0, 0, (byte)origin, 0, 0, 0, 0, 0, 0, 0];
        using var source = new MemoryStream([.. bytes[..2], .. exif, .. bytes[2..]]);
        var swap = origin >= 5;
        var width = swap ? 20 : 40;
        var height = swap ? 40 : 20;
        var info = await _transcoder.InspectSpriteAsync(source, MaxBytes, MaxPixels, default);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
        source.Position = 0;
        await using var output = await _transcoder.NormalizeSpriteAsync(
            source, new(width, height), new(1, 0, 0), false, SpriteTileLayout.Square, default);
        using var decoded = SKBitmap.Decode(output);
        foreach (var (x, y) in new[] { (0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1) })
            Assert.Equal(255, decoded.GetPixel(x, y).Alpha);
        SKColor[][] expected = [
            [SKColors.Red, SKColors.Blue, SKColors.Green, SKColors.Yellow],
            [SKColors.Blue, SKColors.Red, SKColors.Yellow, SKColors.Green],
            [SKColors.Yellow, SKColors.Green, SKColors.Blue, SKColors.Red],
            [SKColors.Green, SKColors.Yellow, SKColors.Red, SKColors.Blue],
            [SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow],
            [SKColors.Green, SKColors.Red, SKColors.Yellow, SKColors.Blue],
            [SKColors.Yellow, SKColors.Blue, SKColors.Green, SKColors.Red],
            [SKColors.Blue, SKColors.Yellow, SKColors.Red, SKColors.Green]];
        source.Position = 0;
        var frame = await _transcoder.NormalizeToFrameAsync(source, width, height, "#FFFFFF", default);
        using var existingDecoded = SKBitmap.Decode(frame);
        var points = new[] { (5, 5), (width - 6, 5), (5, height - 6), (width - 6, height - 6) };
        for (var i = 0; i < points.Length; i++)
        {
            var (x, y) = points[i];
            foreach (var actual in new[] { decoded.GetPixel(x, y), existingDecoded.GetPixel(x, y) })
            {
                Assert.InRange(Math.Abs(actual.Red - expected[origin - 1][i].Red), 0, 20);
                Assert.InRange(Math.Abs(actual.Green - expected[origin - 1][i].Green), 0, 20);
                Assert.InRange(Math.Abs(actual.Blue - expected[origin - 1][i].Blue), 0, 20);
            }
        }
    }

    [Fact]
    public async Task Inspect_RejectsNonImageAndTruncatedPayload()
    {
        using var bogusPng = new MemoryStream("image/png is only a label"u8.ToArray());
        await Assert.ThrowsAsync<ProviderBadResponseException>(() =>
            _transcoder.InspectSpriteAsync(bogusPng, MaxBytes, MaxPixels, default));
        using var fixture = Image(8, 8, (_, _) => SKColors.Red);
        using var truncated = new MemoryStream(fixture.ToArray()[..45]);
        await Assert.ThrowsAsync<ProviderBadResponseException>(() =>
            _transcoder.InspectSpriteAsync(truncated, MaxBytes, MaxPixels, default));
    }

    [Fact]
    public async Task Inspect_BoundsNonSeekableReadAndKeepsCallerStreamOpen()
    {
        using var source = new NonSeekableStream(new byte[1000]);
        await Assert.ThrowsAsync<ProviderBadResponseException>(() =>
            _transcoder.InspectSpriteAsync(source, 100, MaxPixels, default));
        Assert.Equal(101, source.BytesRead);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task Inspect_AllowsExactByteLimit()
    {
        using var source = Image(4, 4, (_, _) => SKColors.Red);
        var info = await _transcoder.InspectSpriteAsync(source, source.Length, MaxPixels, default);
        Assert.True(info.HasVisiblePixels);
        source.Position = 0;
        await Assert.ThrowsAsync<ProviderBadResponseException>(() =>
            _transcoder.InspectSpriteAsync(source, source.Length - 1, MaxPixels, default));
    }

    [Fact]
    public async Task Inspect_EnforcesCallerPixelLimit()
    {
        using var source = Image(4, 4, (_, _) => SKColors.Red);
        await Assert.ThrowsAsync<ProviderBadResponseException>(() =>
            _transcoder.InspectSpriteAsync(source, MaxBytes, 15, default));
    }

    [Fact]
    public async Task Cancellation_IsNotImageValidationFailure()
    {
        using var source = Image(4, 4, (_, _) => SKColors.Red);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _transcoder.InspectSpriteAsync(source, MaxBytes, MaxPixels, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _transcoder.NormalizeSpriteAsync(
            source, new(4, 4), new(1, 0, 0), false, SpriteTileLayout.Square, cts.Token));
    }

    [Theory]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(double.PositiveInfinity, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(1, double.NaN, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(2, 0, 0)]
    public async Task Normalize_RejectsInvalidOrClippingTransform(double scale, double x, double y)
    {
        using var source = Image(4, 4, (_, _) => SKColors.Red);
        await Assert.ThrowsAsync<ProviderBadResponseException>(() => _transcoder.NormalizeSpriteAsync(
            source, new(4, 4), new(scale, x, y), false, SpriteTileLayout.Square, default));
    }

    private static MemoryStream Image(int width, int height, Func<int, int, SKColor> pixel,
        SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, pixel(x, y));
        using var data = bitmap.Encode(format, 100);
        return new MemoryStream(data.ToArray());
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0);
        }
        return ~crc;
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var result = base.ReadAsync(buffer, ct);
            BytesRead += result.Result;
            return result;
        }
    }
}
