namespace Noxtend.Domain.Sprites;

public sealed record SpriteFrameInput(Guid AssetId, SpriteAssetPlan Plan, int FrameIndex, int PlanRevision,
    Guid? BaseImageId, SpriteCanvas GenerationCanvas, SpriteCanvas Canvas, SpriteTransform Transform);
public sealed record SpriteApprovedAsset(Guid Id, string Name, int Order, int Fps,
    bool Loop, SpriteAnchor Anchor, SpriteRepeat Repeat, SpriteTileLayout Layout,
    Guid BaseImageId, IReadOnlyList<Guid> ImageIds);
public sealed record SpriteAssetApproval(int PlanRevision, SpriteApprovedAsset Snapshot);
public sealed record SpriteExportInput(int SchemaVersion, Guid ExportId, int ReviewRevision,
    SpriteView View, SpriteOutputKind OutputKind, SpriteCanvas SourceCanvas,
    SpriteCanvas OutputCanvas, IReadOnlyList<SpriteApprovedAsset> Assets,
    IReadOnlyList<Guid> ExcludedAssetIds);
