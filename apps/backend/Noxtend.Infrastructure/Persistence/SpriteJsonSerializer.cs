using System.Text.Json;
using System.Text.Json.Serialization;
using Noxtend.Domain.Job;
using Noxtend.Domain.Sprites;

namespace Noxtend.Infrastructure.Persistence;

internal static class SpriteJsonSerializer
{
    private sealed record Envelope<T>(int SchemaVersion, T Value);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string Serialize<T>(T value)
    {
        Validate(value);
        return JsonSerializer.Serialize(new Envelope<T>(1, value), Options);
    }

    public static T Deserialize<T>(string json)
    {
        var envelope = JsonSerializer.Deserialize<Envelope<T>>(json, Options);
        if (envelope is null || envelope.SchemaVersion != 1)
            throw new JsonException("지원하지 않는 sprite 저장 schemaVersion입니다");
        Validate(envelope.Value);
        return envelope.Value;
    }

    public static string ValidateBlobKey(string key)
    {
        if (!SafePath(key) || key.Length > 512)
            throw new JsonException("유효하지 않은 sprite Blob 참조입니다");
        return key;
    }

    private static void Validate(object? value)
    {
        var valid = value switch
        {
            SpriteSettings s => SpriteRules.ValidateSettings(s, new(1, 1)).IsSuccess,
            SpriteCanvas c => c.Width > 0 && c.Height > 0 && (long)c.Width * c.Height <= 16_777_216,
            SpriteAnchor a => double.IsFinite(a.X) && double.IsFinite(a.Y),
            SpriteTransform t => double.IsFinite(t.Scale) && t.Scale > 0
                && double.IsFinite(t.OffsetX) && double.IsFinite(t.OffsetY),
            SpriteAssetPlan p => p.Id != Guid.Empty && SpriteRules.Validate(
                new(SpriteView.SideView, SpriteOutputKind.Layers), new(1, 1), [p]).IsSuccess,
            SpriteAssetApproval a => a.PlanRevision > 0 && Valid(a.Snapshot),
            SpriteApprovedAsset a => a.Id != Guid.Empty && !string.IsNullOrWhiteSpace(a.Name)
                && a.Fps is >= 1 and <= 30 && Valid(a.Anchor) && Enum.IsDefined(a.Repeat)
                && Enum.IsDefined(a.Layout) && Ids(a.ImageIds)
                && (a.Loop ? a.ImageIds.Count is 4 or 8 : a.ImageIds.Count == 1)
                && a.ImageIds[0] == a.BaseImageId,
            SpriteFrameInput i => Valid(i.Plan) && i.AssetId == i.Plan.Id && i.PlanRevision > 0
                && i.FrameIndex >= 0 && i.FrameIndex < (i.Plan.Loop ? i.Plan.FrameCount : 1)
                && (i.FrameIndex == 0 ? i.BaseImageId is null : i.BaseImageId is { } id && id != Guid.Empty)
                && Valid(i.GenerationCanvas) && Valid(i.Canvas) && Valid(i.Transform),
            SpriteExportInput i => i.SchemaVersion == 1 && i.ExportId != Guid.Empty && i.ReviewRevision >= 0
                && Enum.IsDefined(i.View) && Enum.IsDefined(i.OutputKind) && Valid(i.SourceCanvas)
                && Valid(i.OutputCanvas) && i.Assets is { Count: > 0 and <= 12 }
                && i.Assets.All(Valid) && i.Assets.Sum(a => a.ImageIds.Count) <= 64
                && Ids(i.Assets.Select(a => a.Id).ToArray()) && i.ExcludedAssetIds is not null
                && Ids(i.ExcludedAssetIds) && !i.Assets.Any(a => i.ExcludedAssetIds.Contains(a.Id)),
            SpriteReceipt r => r.JobId != Guid.Empty && Enum.IsDefined(r.Status) && r.Revision >= 0 && Ids(r.TaskIds),
            SpriteManifest m => m.SchemaVersion == 1 && m.JobId != Guid.Empty
                && m.ProductionMode == ProductionMode.TwoD && Valid(m.Input)
                && m.CoordinateOrigin == "topLeft" && m.CoordinateUnits == "pixels"
                && m.Assets is not null && m.Assets.Count == m.Input.Assets.Count
                && m.Assets.All(a => a is not null && Valid(a.Asset) && SafePath(a.BasePath)
                    && m.Input.Assets.Any(input => input.Id == a.Asset.Id
                        && input.ImageIds.SequenceEqual(a.Asset.ImageIds))
                    && a.Frames is not null && a.Frames.All(f => f is not null)
                    && a.Frames.Count == a.Asset.ImageIds.Count
                    && a.Frames.Select(f => f.Index).SequenceEqual(Enumerable.Range(0, a.Frames.Count))
                    && a.Frames.All(f => f is not null && f.ImageId == a.Asset.ImageIds[f.Index]
                        && SafePath(f.Path) && SafePath(f.SheetPath) && f.Page >= 0 && f.Rect is not null
                        && f.Rect.X >= 0 && f.Rect.Y >= 0 && f.Rect.Width > 0 && f.Rect.Height > 0)),
            _ => false,
        };
        if (!valid) throw new JsonException("유효하지 않은 sprite 저장 snapshot입니다");
    }

    private static bool Valid(object? value)
    {
        Validate(value);
        return true;
    }

    private static bool Ids(IReadOnlyList<Guid>? ids)
        => ids is not null && ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count;

    private static bool SafePath(string? path)
        => !string.IsNullOrWhiteSpace(path) && !path.StartsWith('/') && !path.Contains('\\')
            && !path.Contains(':') && !path.Any(char.IsControl) && path.Split('/').All(segment => segment is not ("" or "." or ".."));
}
