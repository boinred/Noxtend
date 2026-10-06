using System.IO.Compression;
using System.Text.Json;
using Noxtend.Application.Sprites;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Infrastructure.Persistence;
using SkiaSharp;

namespace Noxtend.Tests.Infrastructure;

public sealed class SpritePackageTests
{
    [Fact]
    public async Task Padding_ExtrudesTwoPixelsWithoutChangingRect()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var layout = SpriteRules.SheetPages(new(2, 2), ids).Single();
        await using var output = TemporaryFile();
        await new SkiaImageTranscoder().WriteSpriteSheetAsync(layout,
            (id, _) => Task.FromResult<Stream>(Image(2, 2, id == ids[0] ? SKColors.Red : SKColors.Blue)), output, default);
        Assert.True(output.CanWrite);
        output.Position = 0;
        using var managed = new SKManagedStream(output, false);
        using var bitmap = SKBitmap.Decode(managed);
        Assert.Equal(new SpriteRect(2, 2, 2, 2), layout.Cells[0].Rect);
        Assert.Equal(12, bitmap.Width);
        Assert.Equal(6, bitmap.Height);
        for (var y = 0; y < 6; y++)
            for (var x = 0; x < 12; x++)
                Assert.Equal(x < 6 ? SKColors.Red : SKColors.Blue, bitmap.GetPixel(x, y));
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task Padding_PreservesDistinctEdgesAndTransparentPixels()
    {
        var id = Guid.NewGuid();
        var layout = SpriteRules.SheetPages(new(2, 2), [id]).Single();
        using var original = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        original.SetPixel(0, 0, SKColors.Red);
        original.SetPixel(1, 0, SKColors.Blue);
        original.SetPixel(0, 1, new SKColor(0, 255, 0, 128));
        original.SetPixel(1, 1, SKColors.Transparent);
        using var data = original.Encode(SKEncodedImageFormat.Png, 100);
        await using var output = TemporaryFile();
        await new SkiaImageTranscoder().WriteSpriteSheetAsync(layout,
            (_, _) => Task.FromResult<Stream>(new MemoryStream(data.ToArray())), output, default);
        output.Position = 0;
        using var managed = new SKManagedStream(output, false);
        using var decoded = SKBitmap.Decode(managed);
        for (var y = 0; y < 6; y++)
            for (var x = 0; x < 6; x++)
            {
                var expected = original.GetPixel(x < 3 ? 0 : 1, y < 3 ? 0 : 1);
                var actual = decoded.GetPixel(x, y);
                Assert.Equal(expected.Alpha, actual.Alpha);
                if (expected.Alpha > 0)
                    Assert.Equal(expected, actual);
            }
    }

    [Fact]
    public async Task Sheet_RejectsFrameSizeMismatch()
    {
        await using var output = TemporaryFile();
        await Assert.ThrowsAsync<ProviderBadResponseException>(() => new SkiaImageTranscoder().WriteSpriteSheetAsync(
            SpriteRules.SheetPages(new(2, 2), [Guid.NewGuid()]).Single(),
            (_, _) => Task.FromResult<Stream>(Image(3, 2, SKColors.Red)), output, default));
    }

    [Fact]
    public async Task Package_RejectsUnnormalizedJpeg()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Red);
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
        await using var output = TemporaryFile();
        await Assert.ThrowsAsync<ProviderBadResponseException>(() => new SpritePackageWriter(new SkiaImageTranscoder()).WriteAsync(
            Guid.NewGuid(), Input(SpriteOutputKind.Layers),
            (_, _) => Task.FromResult<Stream>(new MemoryStream(data.ToArray())), output, default));
    }

    [Theory]
    [InlineData(SpriteOutputKind.Layers)]
    [InlineData(SpriteOutputKind.Tiles)]
    public async Task Zip_ContainsManifestBasesFramesAndSheets(SpriteOutputKind kind)
    {
        var input = Input(kind);
        var jobId = Guid.NewGuid();
        await using var output = TemporaryFile();
        var manifest = await new SpritePackageWriter(new SkiaImageTranscoder()).WriteAsync(jobId, input,
            (_, _) => Task.FromResult<Stream>(Image(2, 2, SKColors.Red)), output, default);
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal(ProductionMode.TwoD, manifest.ProductionMode);
        Assert.Equal("topLeft", manifest.CoordinateOrigin);
        Assert.Equal("pixels", manifest.CoordinateUnits);
        Assert.Equal(input.Assets.Select(a => a.Id), manifest.IncludedAssetIds);
        Assert.Equal(input.ExcludedAssetIds, manifest.ExcludedAssetIds);
        var persisted = SpriteJsonSerializer.Deserialize<SpriteManifest>(SpriteJsonSerializer.Serialize(manifest));
        Assert.Equal(manifest.IncludedAssetIds, persisted.IncludedAssetIds);
        Assert.Equal(input.Assets[0].ImageIds, persisted.Assets[0].Asset.ImageIds);
        var persistedInput = SpriteJsonSerializer.Deserialize<SpriteExportInput>(SpriteJsonSerializer.Serialize(input));
        Assert.Equal(input.Assets[0].ImageIds, persistedInput.Assets[0].ImageIds);
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read, true);
        Assert.Equal(10, zip.Entries.Count);
        var entries = zip.Entries.Select(e => e.FullName).ToArray();
        Assert.DoesNotContain(entries, x => x.Contains("../") || Path.IsPathRooted(x) || x.Contains('\\'));
        foreach (var asset in manifest.Assets)
        {
            Assert.Equal(asset.Asset.Loop ? 4 : 1, asset.Frames.Count);
            Assert.Equal(Enumerable.Range(0, asset.Frames.Count), asset.Frames.Select(f => f.Index));
            Assert.StartsWith(kind == SpriteOutputKind.Layers ? "layers/" : "tiles/", asset.BasePath);
            foreach (var path in asset.Frames.Select(f => f.Path).Append(asset.BasePath))
            {
                using var source = zip.GetEntry(path)!.Open();
                using var png = SKBitmap.Decode(source);
                Assert.Equal(input.OutputCanvas.Width, png.Width);
                Assert.Equal(input.OutputCanvas.Height, png.Height);
            }
            foreach (var frame in asset.Frames)
            {
                using var source = zip.GetEntry(frame.SheetPath)!.Open();
                using var png = SKBitmap.Decode(source);
                Assert.Equal(SKColors.Red, png.GetPixel(frame.Rect.X, frame.Rect.Y));
                Assert.Equal(input.OutputCanvas.Width, frame.Rect.Width);
                Assert.Equal(input.OutputCanvas.Height, frame.Rect.Height);
            }
        }
        using var json = zip.GetEntry("manifest.json")!.Open();
        using var doc = await JsonDocument.ParseAsync(json);
        Assert.Equal("topLeft", doc.RootElement.GetProperty("coordinateOrigin").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("includedAssetIds").GetArrayLength());
        Assert.Equal(input.ExcludedAssetIds[0], doc.RootElement.GetProperty("excludedAssetIds")[0].GetGuid());
        Assert.DoesNotContain("blobKey", doc.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Users/", doc.RootElement.GetRawText());
    }

    [Fact]
    public async Task PackageLimit_StopsAt256MiBWithoutPublishingSuccess()
    {
        using var bounded = new SpritePackageWriter.BoundedWriteStream(Stream.Null, default);
        var chunk = new byte[8192];
        for (var i = 0; i < 256 * 1024 * 1024 / chunk.Length; i++)
            await bounded.WriteAsync(chunk.AsMemory());
        Assert.Equal(256L * 1024 * 1024, bounded.Position);
        await Assert.ThrowsAsync<InvalidDataException>(() => bounded.WriteAsync(new byte[1].AsMemory()).AsTask());
        Assert.Throws<InvalidDataException>(() => bounded.WriteByte(1));
        Assert.Equal(256L * 1024 * 1024, bounded.Position);
    }

    [Fact]
    public async Task Package_RejectsActualSizeMismatch()
    {
        await using var output = TemporaryFile();
        await Assert.ThrowsAsync<ProviderBadResponseException>(() => new SpritePackageWriter(new SkiaImageTranscoder()).WriteAsync(
            Guid.NewGuid(), Input(SpriteOutputKind.Layers), (_, _) => Task.FromResult<Stream>(Image(3, 2, SKColors.Red)), output, default));
    }

    [Fact]
    public async Task Package_CancellationDoesNotReturnManifest()
    {
        await using var output = TemporaryFile();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SpritePackageWriter(new SkiaImageTranscoder()).WriteAsync(
            Guid.NewGuid(), Input(SpriteOutputKind.Layers), (_, _) => throw new InvalidOperationException(), output, cts.Token));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task StaticAsset_WithExtraFramesIsRejected()
    {
        var input = Input(SpriteOutputKind.Layers);
        input = input with { Assets = [input.Assets[0] with { Loop = false }] };
        await using var output = TemporaryFile();
        await Assert.ThrowsAsync<ArgumentException>(() => new SpritePackageWriter(new SkiaImageTranscoder()).WriteAsync(
            Guid.NewGuid(), input, (_, _) => throw new InvalidOperationException(), output, default));
        Assert.Equal(0, output.Length);
    }

    private static SpriteExportInput Input(SpriteOutputKind kind)
    {
        var loopIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var staticId = Guid.NewGuid();
        return new(1, Guid.NewGuid(), 1, SpriteView.SideView, kind, new(2, 2), new(2, 2),
            [new(Guid.NewGuid(), "loop", 0, 8, true, new(0, 0), SpriteRepeat.Both, SpriteTileLayout.Square, loopIds[0], loopIds),
             new(Guid.NewGuid(), "static", 1, 8, false, new(0, 0), SpriteRepeat.Both, SpriteTileLayout.Square, staticId, [staticId])],
            [Guid.NewGuid()]);
    }

    private static FileStream TemporaryFile()
        => new(Path.Combine(Path.GetTempPath(), $"sprite-{Guid.NewGuid():N}.zip"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 8192, FileOptions.DeleteOnClose);

    private static MemoryStream Image(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new(data.ToArray());
    }
}
