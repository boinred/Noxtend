using Noxtend.Domain.Job;

namespace Noxtend.Domain.Sprites;

public enum SpriteView { SideView, TopDown, Isometric }
public enum SpriteOutputKind { Layers, Tiles }
public enum SpriteAssetRole { Layer, Tile, Backdrop, Object }
public enum SpriteRepeat { X, Y, Both }
public enum SpriteTileLayout { Square, Diamond }
public enum SpriteRequestKind { Create, UpdatePlan, ApprovePlan, ApproveBases, RegenerateFrame, ApproveAsset, Export,
    CreateDraft, EnhanceSource, SaveSourcePrompt, GenerateSource, SelectSource }
public enum SpritePhase { SourceDraft, Analyzing, PlanReview, BaseGeneration, BaseReview, FrameGeneration, FrameReview, ExportReady, Packaging, Completed }

public sealed record SpriteImageInfo(int Width, int Height, bool HasTransparentPixels, bool HasVisiblePixels, string ContentType);
public sealed record SpriteCanvas(int Width, int Height);
public sealed record SpriteGrid(double CellWidth);
public sealed record SpriteAssetCanvas(SpriteCanvas Output, SpriteCanvas Generation, SpriteTransform Transform, double Scale);
public sealed record SpriteAnchor(double X, double Y);
public sealed record SpriteTransform(double Scale, double OffsetX, double OffsetY);
public sealed record SpriteSettings(SpriteView View, SpriteOutputKind OutputKind,
    int TileWidth = 128, SpriteRepeat Repeat = SpriteRepeat.Both);
public sealed record SpriteAssetPlan(Guid Id, string Name, int Order, Bounds SourceBounds,
    bool RequiresTransparency, bool Loop = false, int FrameCount = 8, int Fps = 8,
    string MotionNotes = "", SpriteAssetRole? Role = null, IReadOnlyList<Bounds>? Placements = null)
{
    // 자리 목록은 참조 대신 순서 있는 값 비교
    public bool Equals(SpriteAssetPlan? other)
        => other is not null && Id == other.Id && Name == other.Name && Order == other.Order
            && SourceBounds == other.SourceBounds && RequiresTransparency == other.RequiresTransparency
            && Loop == other.Loop && FrameCount == other.FrameCount && Fps == other.Fps
            && MotionNotes == other.MotionNotes && Role == other.Role
            && (Placements is null ? other.Placements is null
                : other.Placements is not null && Placements.SequenceEqual(other.Placements));

    public override int GetHashCode() => HashCode.Combine(Id, Name, Order, SourceBounds, Role, Placements?.Count);
}
public sealed record SpriteReceipt(Guid JobId, JobStatus Status, int Revision,
    IReadOnlyList<Guid> TaskIds);
