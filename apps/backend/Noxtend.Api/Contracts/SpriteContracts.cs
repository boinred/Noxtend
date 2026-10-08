using System.Text.Json.Serialization;
using Noxtend.Domain.Job;
using Noxtend.Domain.Sprites;

namespace Noxtend.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerateSpriteSourceRequest(Guid RequestId, string Prompt, Guid ImageProviderConfigId, string ImageModel);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartSpriteJobRequest(Guid RequestId, Guid? UploadId, Guid? SourceJobId,
    Guid? SourceGeneratedImageId, Guid ProviderConfigId, string Model,
    Guid ImageProviderConfigId, string ImageModel, SpriteSettingsRequest? Settings);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SpriteSettingsRequest(string View, string OutputKind, int TileWidth = 128, string Repeat = "both")
{
    public SpriteSettings? ToDomain()
        => TryEnum<SpriteView>(View, out var view) && TryEnum<SpriteOutputKind>(OutputKind, out var output)
            && TryEnum<SpriteRepeat>(Repeat, out var repeat) ? new(view, output, TileWidth, repeat) : null;

    private static bool TryEnum<T>(string? value, out T parsed) where T : struct, Enum
    {
        parsed = default;
        return Enum.GetNames<T>().Any(name => name.Equals(value, StringComparison.OrdinalIgnoreCase))
            && Enum.TryParse(value, true, out parsed);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SpriteMutationRequest(Guid RequestId, int ExpectedRevision);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SpriteAssetsRequest(Guid RequestId, int ExpectedRevision, IReadOnlyList<Guid>? AssetIds);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SpritePlanRequest(Guid RequestId, int ExpectedRevision, IReadOnlyList<SpriteAssetPlanRequest>? Assets);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SpriteAssetPlanRequest(Guid Id, string Name, int Order, SpriteBoundsRequest? SourceBounds,
    bool RequiresTransparency, bool Loop = false, int FrameCount = 8, int Fps = 8, string MotionNotes = "")
{
    public SpriteAssetPlan? ToDomain()
        => Id != Guid.Empty && SourceBounds is { } bounds
            && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.W) && double.IsFinite(bounds.H)
            ? new(Id, Name, Order, new Bounds(bounds.X, bounds.Y, bounds.W, bounds.H),
                RequiresTransparency, Loop, FrameCount, Fps, MotionNotes) : null;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SpriteBoundsRequest(double X, double Y, double W, double H);

public sealed record SpriteAcceptedResponse(Guid Id, string Status, int Revision, IReadOnlyList<Guid> TaskIds)
{
    public static SpriteAcceptedResponse From(SpriteReceipt receipt)
        => new(receipt.JobId, JobResponse.Wire(receipt.Status), receipt.Revision, receipt.TaskIds);
}

public sealed record SpriteResponse(SpriteSettingsResponse Settings, SpriteCanvasResponse SourceCanvas,
    SpriteCanvasResponse GenerationCanvas, SpriteCanvasResponse OutputCanvas, SpriteTransformResponse Transform,
    string Phase, int ReviewRevision, Guid? CompletedExportId, IReadOnlyList<SpriteAssetResponse> Assets,
    IReadOnlyList<SpriteImageResponse> Images, IReadOnlyList<SpriteExportResponse> Exports)
{
    public static SpriteResponse From(SpritePipelineState state)
        => new(new(JobResponse.Wire(state.Settings.View), JobResponse.Wire(state.Settings.OutputKind),
                state.Settings.TileWidth, JobResponse.Wire(state.Settings.Repeat)),
            SpriteCanvasResponse.From(state.SourceCanvas), SpriteCanvasResponse.From(state.GenerationCanvas),
            SpriteCanvasResponse.From(state.OutputCanvas),
            new(state.Transform.Scale, state.Transform.OffsetX, state.Transform.OffsetY),
            JobResponse.Wire(state.Phase), state.ReviewRevision,
            state.Exports.Any(export => export.Id == state.CompletedExportId) ? state.CompletedExportId : null,
            state.Assets.OrderBy(asset => asset.Plan.Order).Select(SpriteAssetResponse.From).ToArray(),
            state.Images.Select(image => new SpriteImageResponse(image.Id, image.TaskId, image.AssetId,
                image.FrameIndex, image.PlanRevision, image.BaseImageId, image.Width, image.Height,
                image.ContentType, image.CreatedAt)).ToArray(),
            state.Exports.Select(export => new SpriteExportResponse(export.Id, export.TaskId,
                export.Input.ReviewRevision, export.IsCurrent, export.CreatedAt,
                export.Input.Assets.Select(asset => asset.Id).ToArray(), export.Input.ExcludedAssetIds)).ToArray());
}

public sealed record SpriteSettingsResponse(string View, string OutputKind, int TileWidth, string Repeat);
public sealed record SpriteCanvasResponse(int Width, int Height)
{
    public static SpriteCanvasResponse From(SpriteCanvas canvas) => new(canvas.Width, canvas.Height);
}
public sealed record SpriteTransformResponse(double Scale, double OffsetX, double OffsetY);
public sealed record SpriteAnchorResponse(double X, double Y);
public sealed record SpriteFrameResponse(int Index, Guid? CurrentTaskId, Guid? CurrentImageId);
public sealed record SpriteAssetResponse(Guid Id, SpriteAssetPlanRequest Plan, int PlanRevision,
    SpriteAnchorResponse Anchor, Guid? ApprovedBaseImageId, SpriteApprovalResponse? Approval,
    IReadOnlyList<SpriteFrameResponse> Frames)
{
    public static SpriteAssetResponse From(SpriteAsset asset)
    {
        var plan = asset.Plan;
        return new(asset.Id, new(plan.Id, plan.Name, plan.Order,
                new(plan.SourceBounds.X, plan.SourceBounds.Y, plan.SourceBounds.W, plan.SourceBounds.H),
                plan.RequiresTransparency, plan.Loop, plan.FrameCount, plan.Fps, plan.MotionNotes),
            asset.PlanRevision, new(asset.Anchor.X, asset.Anchor.Y), asset.ApprovedBaseImageId,
            asset.Approval is { } approval ? new(approval.PlanRevision,
                SpriteApprovedAssetResponse.From(approval.Snapshot)) : null,
            asset.Frames.Select(frame => new SpriteFrameResponse(frame.Index, frame.CurrentTaskId, frame.CurrentImageId)).ToArray());
    }
}
public sealed record SpriteApprovalResponse(int PlanRevision, SpriteApprovedAssetResponse Snapshot);
public sealed record SpriteApprovedAssetResponse(Guid Id, string Name, int Order, int Fps, bool Loop,
    SpriteAnchorResponse Anchor, string Repeat, string Layout, Guid BaseImageId, IReadOnlyList<Guid> ImageIds)
{
    public static SpriteApprovedAssetResponse From(SpriteApprovedAsset asset)
        => new(asset.Id, asset.Name, asset.Order, asset.Fps, asset.Loop, new(asset.Anchor.X, asset.Anchor.Y),
            JobResponse.Wire(asset.Repeat), JobResponse.Wire(asset.Layout), asset.BaseImageId, asset.ImageIds);
}
public sealed record SpriteImageResponse(Guid Id, Guid TaskId, Guid AssetId, int FrameIndex, int PlanRevision,
    Guid? BaseImageId, int Width, int Height, string ContentType, DateTimeOffset CreatedAt);
public sealed record SpriteExportResponse(Guid Id, Guid TaskId, int ReviewRevision, bool IsCurrent,
    DateTimeOffset CreatedAt, IReadOnlyList<Guid> IncludedAssetIds, IReadOnlyList<Guid> ExcludedAssetIds);
public sealed record SpriteSummaryResponse(string Phase, int AssetCount, int ApprovedAssetCount, int ImageCount, int ExportCount)
{
    public static SpriteSummaryResponse From(SpritePipelineState state)
        => new(JobResponse.Wire(state.Phase), state.Assets.Count, state.Assets.Count(asset => asset.Approval is not null),
            state.Images.Count, state.Exports.Count);
}
