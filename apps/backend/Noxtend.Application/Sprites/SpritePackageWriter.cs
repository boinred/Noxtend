using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;

namespace Noxtend.Application.Sprites;

public sealed class SpritePackageWriter(IImageTranscoder transcoder)
{
    private const long MaxImageBytes = 32L * 1024 * 1024;

    public async Task<SpriteManifest> WriteAsync(Guid jobId, SpriteExportInput input,
        Func<Guid, CancellationToken, Task<Stream>> openFrame, Stream output, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Validate(jobId, input);
        using var bounded = new BoundedWriteStream(output, ct);
        var assets = new List<SpriteManifestAsset>();
        var manifest = new SpriteManifest(1, jobId, ProductionMode.TwoD, input, "topLeft", "pixels", assets);
        using (var zip = new ZipArchive(bounded, ZipArchiveMode.Create, true))
        {
            foreach (var asset in input.Assets.OrderBy(a => a.Order))
            {
                ct.ThrowIfCancellationRequested();
                var basePath = $"{(input.OutputKind == SpriteOutputKind.Layers ? "layers" : "tiles")}/asset-{asset.Id}.png";
                await WriteImageAsync(zip, basePath, asset.BaseImageId, input.OutputCanvas, openFrame, ct);
                var pages = SpriteRules.SheetPages(input.OutputCanvas, asset.ImageIds);
                var frames = new List<SpriteManifestFrame>();
                foreach (var page in pages)
                {
                    var sheetPath = $"sheets/asset-{asset.Id}-{page.Page:000}.png";
                    foreach (var cell in page.Cells)
                    {
                        var path = $"frames/asset-{asset.Id}/frame-{cell.FrameIndex:000}.png";
                        await WriteImageAsync(zip, path, cell.ImageId, input.OutputCanvas, openFrame, ct);
                        frames.Add(new(cell.ImageId, cell.FrameIndex, path, sheetPath, page.Page, cell.Rect));
                    }
                    await using var sheetOutput = zip.CreateEntry(sheetPath, CompressionLevel.NoCompression).Open();
                    await transcoder.WriteSpriteSheetAsync(page, openFrame, sheetOutput, ct);
                }
                assets.Add(new(asset, basePath, frames));
            }
            await using var json = zip.CreateEntry("manifest.json", CompressionLevel.Optimal).Open();
            await JsonSerializer.SerializeAsync(json, manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }, ct);
        }
        // ZIP 중앙 디렉터리까지 상한 검증 후 성공 반환
        ct.ThrowIfCancellationRequested();
        return manifest;
    }

    private async Task WriteImageAsync(ZipArchive zip, string path, Guid imageId, SpriteCanvas canvas,
        Func<Guid, CancellationToken, Task<Stream>> openFrame, CancellationToken ct)
    {
        await using (var source = await openFrame(imageId, ct))
        {
            var info = await transcoder.InspectSpriteAsync(source, MaxImageBytes, 16_777_216, ct);
            if (info.Width != canvas.Width || info.Height != canvas.Height)
                throw new ProviderBadResponseException("실제 프레임 크기가 내보내기 캔버스와 다릅니다");
        }
        // Blob은 불변, 검증 뒤 비탐색 스트림도 다시 열어 순차 복사
        await using var image = await openFrame(imageId, ct);
        var header = new byte[8];
        await image.ReadExactlyAsync(header, ct);
        if (!header.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new ProviderBadResponseException("내보내기 프레임은 정규화 PNG여야 합니다");
        await using var entry = zip.CreateEntry(path, CompressionLevel.NoCompression).Open();
        await entry.WriteAsync(header, ct);
        var buffer = new byte[8192];
        long bytes = header.Length;
        int count;
        while ((count = await image.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            bytes += count;
            if (bytes > MaxImageBytes)
                throw new ProviderBadResponseException("프레임이 바이트 상한을 넘습니다");
            await entry.WriteAsync(buffer.AsMemory(0, count), ct);
        }
    }

    private static void Validate(Guid jobId, SpriteExportInput input)
    {
        if (jobId == Guid.Empty || input is null || input.SchemaVersion != 1 || input.ExportId == Guid.Empty
            || input.ReviewRevision < 0 || !Enum.IsDefined(input.View) || !Enum.IsDefined(input.OutputKind)
            || input.OutputCanvas is null || input.OutputCanvas.Width is < 1 or > 1024
            || input.OutputCanvas.Height is < 1 or > 1024 || input.SourceCanvas is null
            || input.SourceCanvas.Width <= 0 || input.SourceCanvas.Height <= 0
            || (long)input.SourceCanvas.Width * input.SourceCanvas.Height > 16_777_216
            || input.Assets is null || input.Assets.Count is < 1 or > 12 || input.Assets.Any(a => a is null))
            throw new ArgumentException("내보내기 입력이 유효하지 않습니다", nameof(input));
        if (input.Assets.Select(a => a.Id).Distinct().Count() != input.Assets.Count
            || input.Assets.Select(a => a.Order).Distinct().Count() != input.Assets.Count
            || input.ExcludedAssetIds is null || input.ExcludedAssetIds.Any(id => id == Guid.Empty)
            || input.ExcludedAssetIds.Distinct().Count() != input.ExcludedAssetIds.Count
            || input.Assets.Any(a => input.ExcludedAssetIds.Contains(a.Id)))
            throw new ArgumentException("포함·제외 대상이 유효하지 않습니다", nameof(input));
        foreach (var asset in input.Assets)
        {
            if (asset.Id == Guid.Empty || string.IsNullOrWhiteSpace(asset.Name) || asset.Fps is < 1 or > 30
                || asset.Anchor is null || !double.IsFinite(asset.Anchor.X) || !double.IsFinite(asset.Anchor.Y)
                || !Enum.IsDefined(asset.Repeat) || !Enum.IsDefined(asset.Layout) || asset.ImageIds is null
                || (asset.Loop ? asset.ImageIds.Count is not (4 or 8) : asset.ImageIds.Count != 1)
                || asset.ImageIds.Any(id => id == Guid.Empty) || asset.ImageIds.Distinct().Count() != asset.ImageIds.Count
                || asset.ImageIds[0] != asset.BaseImageId)
                throw new ArgumentException("승인된 대상·프레임이 유효하지 않습니다", nameof(input));
        }
        if (input.Assets.Sum(a => a.ImageIds.Count) > 64)
            throw new ArgumentException("최대 64프레임까지 내보낼 수 있습니다", nameof(input));
    }

    internal sealed class BoundedWriteStream(Stream output, CancellationToken ct) : Stream
    {
        private const long MaxBytes = 256L * 1024 * 1024;
        private long _written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => output.CanWrite;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() { ct.ThrowIfCancellationRequested(); output.Flush(); }
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            ct.ThrowIfCancellationRequested();
            return output.FlushAsync(cancellationToken);
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            output.Write(buffer);
            _written += buffer.Length;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Check(buffer.Length);
            await output.WriteAsync(buffer, cancellationToken);
            _written += buffer.Length;
        }
        private void Check(int count)
        {
            ct.ThrowIfCancellationRequested();
            if (count > MaxBytes - _written)
                throw new InvalidDataException("스프라이트 ZIP이 256 MiB 상한을 넘습니다");
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
