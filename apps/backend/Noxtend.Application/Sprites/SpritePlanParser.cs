using System.Text.Json;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Sprites;

namespace Noxtend.Application.Sprites;

public sealed record SpriteAnalysisPlan(IReadOnlyList<SpriteAssetPlan> Assets, SpriteGrid? Grid);

public static class SpritePlanParser
{
    public static Result<SpriteAnalysisPlan> Parse(string json, SpriteSettings settings, SpriteCanvas source)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            RequireProperties(root, "view", "outputKind", "grid", "assets");
            var view = root.GetProperty("view").GetString();
            var kind = root.GetProperty("outputKind").GetString();
            if (view != JsonNamingPolicy.CamelCase.ConvertName(settings.View.ToString())
                || kind != JsonNamingPolicy.CamelCase.ConvertName(settings.OutputKind.ToString()))
                return Invalid();
            SpriteGrid? grid = null;
            var gridValue = root.GetProperty("grid");
            if (gridValue.ValueKind != JsonValueKind.Null)
            {
                RequireProperties(gridValue, "cellWidth");
                grid = new(gridValue.GetProperty("cellWidth").GetDouble());
            }
            if (settings.OutputKind == SpriteOutputKind.Tiles && grid is null) return Invalid();
            var assets = root.GetProperty("assets");
            if (assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() is < 1 or > 12) return Invalid();
            var plans = new List<SpriteAssetPlan>();
            foreach (var asset in assets.EnumerateArray())
            {
                RequireProperties(asset, "name", "role", "order", "sourceBounds", "requiresTransparency", "loop", "frameCount", "fps", "motionNotes");
                if (asset.GetProperty("frameCount").GetInt32() is not (4 or 8)) return Invalid();
                var bounds = asset.GetProperty("sourceBounds");
                RequireProperties(bounds, "x", "y", "w", "h");
                var role = asset.GetProperty("role").GetString() switch
                {
                    "layer" => SpriteAssetRole.Layer,
                    "tile" => SpriteAssetRole.Tile,
                    "backdrop" => SpriteAssetRole.Backdrop,
                    "object" => SpriteAssetRole.Object,
                    _ => throw new JsonException("알 수 없는 역할"),
                };
                plans.Add(new(Guid.NewGuid(), asset.GetProperty("name").GetString()!, asset.GetProperty("order").GetInt32(),
                    new Bounds(bounds.GetProperty("x").GetDouble(), bounds.GetProperty("y").GetDouble(),
                        bounds.GetProperty("w").GetDouble(), bounds.GetProperty("h").GetDouble()),
                    asset.GetProperty("requiresTransparency").GetBoolean(), asset.GetProperty("loop").GetBoolean(),
                    asset.GetProperty("frameCount").GetInt32(), asset.GetProperty("fps").GetInt32(),
                    asset.GetProperty("motionNotes").GetString()!, role));
            }
            var validation = SpriteRules.Validate(settings, source, plans, grid);
            return validation.IsSuccess ? Result<SpriteAnalysisPlan>.Ok(new(plans.AsReadOnly(), grid))
                : Result<SpriteAnalysisPlan>.Fail(validation.ErrorCode!, validation.ErrorMessage!);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return Invalid();
        }
    }

    private static void RequireProperties(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(expected.Order()))
            throw new JsonException("분석 schema가 일치하지 않습니다");
    }
    private static Result<SpriteAnalysisPlan> Invalid()
        => Result<SpriteAnalysisPlan>.Fail(ErrorCode.SpritePlanInvalid, "2D 분석 schema·시점·영역이 유효하지 않습니다");
}
