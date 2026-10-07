using Noxtend.Domain.Job;

namespace Noxtend.Domain.Sprites;

public sealed record SpriteRect(int X, int Y, int Width, int Height);
public sealed record SpriteSheetCell(Guid ImageId, int FrameIndex, SpriteRect Rect);
public sealed record SpriteSheetLayout(int Page, SpriteCanvas Canvas, IReadOnlyList<SpriteSheetCell> Cells);
public sealed record SpriteManifestFrame(Guid ImageId, int Index, string Path,
    string SheetPath, int Page, SpriteRect Rect);
public sealed record SpriteManifestAsset(SpriteApprovedAsset Asset, string BasePath,
    IReadOnlyList<SpriteManifestFrame> Frames);
public sealed record SpriteManifest(int SchemaVersion, Guid JobId, ProductionMode ProductionMode,
    SpriteExportInput Input, string CoordinateOrigin, string CoordinateUnits,
    IReadOnlyList<SpriteManifestAsset> Assets)
{
    public IReadOnlyList<Guid> IncludedAssetIds => Input.Assets.Select(a => a.Id).ToArray();
    public IReadOnlyList<Guid> ExcludedAssetIds => Input.ExcludedAssetIds;
}
