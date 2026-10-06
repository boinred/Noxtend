using Noxtend.Domain.Job;

namespace Noxtend.Domain.Sprites;

public enum SpriteView { SideView, TopDown, Isometric }
public enum SpriteOutputKind { Layers, Tiles }
public enum SpriteRepeat { X, Y, Both }
public enum SpriteTileLayout { Square, Diamond }
public enum SpriteRequestKind { Create, UpdatePlan, ApprovePlan, ApproveBases, RegenerateFrame, ApproveAsset, Export }
public enum SpritePhase { Analyzing, PlanReview, BaseGeneration, BaseReview, FrameGeneration, FrameReview, ExportReady, Packaging, Completed }

public sealed record SpriteImageInfo(int Width, int Height, bool HasTransparentPixels, bool HasVisiblePixels);
public sealed record SpriteCanvas(int Width, int Height);
public sealed record SpriteAnchor(double X, double Y);
public sealed record SpriteTransform(double Scale, double OffsetX, double OffsetY);
public sealed record SpriteSettings(SpriteView View, SpriteOutputKind OutputKind,
    int TileWidth = 128, SpriteRepeat Repeat = SpriteRepeat.Both);
public sealed record SpriteAssetPlan(Guid Id, string Name, int Order, Bounds SourceBounds,
    bool RequiresTransparency, bool Loop = false, int FrameCount = 8, int Fps = 8,
    string MotionNotes = "");
public sealed record SpriteReceipt(Guid JobId, JobStatus Status, int Revision,
    IReadOnlyList<Guid> TaskIds);
