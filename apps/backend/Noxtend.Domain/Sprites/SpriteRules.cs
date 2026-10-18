using Noxtend.Domain.Common;
using Noxtend.Domain.Job;

namespace Noxtend.Domain.Sprites;

public static class SpriteRules
{
    public static Result<bool> ValidateSettings(SpriteSettings settings, SpriteCanvas source)
    {
        if (settings is null || !Enum.IsDefined(settings.View) || !Enum.IsDefined(settings.OutputKind)
            || !Enum.IsDefined(settings.Repeat) || settings.TileWidth is not (64 or 128 or 256))
        {
            return Result<bool>.Fail(ErrorCode.SpriteSettingsInvalid, "시점·결과 유형·타일 너비·반복 방향이 유효하지 않습니다");
        }

        if (source is null || source.Width <= 0 || source.Height <= 0
            || (long)source.Width * source.Height > 16_777_216)
        {
            return Result<bool>.Fail(ErrorCode.SpriteSettingsInvalid, "원본은 양수 크기이며 최대 16,777,216픽셀이어야 합니다");
        }

        return Result<bool>.Ok(true);
    }

    public static Result<bool> Validate(
        SpriteSettings settings, SpriteCanvas source, IReadOnlyList<SpriteAssetPlan> assets, SpriteGrid? grid = null)
    {
        var settingsResult = ValidateSettings(settings, source);
        if (!settingsResult.IsSuccess)
        {
            return settingsResult;
        }

        if (assets is null || assets.Count is < 1 or > 12 || assets.Any(a => a is null))
        {
            return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "제작 대상은 1~12개여야 합니다");
        }

        if (assets.Select(a => a.Id).Distinct().Count() != assets.Count
            || assets.Select(a => a.Order).Distinct().Count() != assets.Count)
        {
            return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "대상 ID와 순서는 중복될 수 없습니다");
        }

        if (grid is not null && (settings.OutputKind == SpriteOutputKind.Layers
            || !double.IsFinite(grid.CellWidth) || grid.CellWidth <= 0 || grid.CellWidth > 1))
        {
            return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "격자는 타일 작업의 0 초과 1 이하 칸 너비여야 합니다");
        }

        var backmost = assets.Min(a => a.Order);
        if (assets.Count(a => RoleOf(settings, a) == SpriteAssetRole.Backdrop) > 1
            || assets.Any(a => RoleOf(settings, a) == SpriteAssetRole.Backdrop && a.Order != backmost))
        {
            return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "배경은 최대 하나이며 가장 뒤에 있어야 합니다");
        }
        var totalFrames = 0;
        foreach (var asset in assets)
        {
            if (string.IsNullOrWhiteSpace(asset.Name) || !InsideSource(asset.SourceBounds))
            {
                return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "대상 이름과 원본 내부의 유한한 영역이 필요합니다");
            }

            if ((asset.Loop && asset.FrameCount is not (4 or 8)) || asset.Fps is < 1 or > 30
                || asset.MotionNotes is null || asset.MotionNotes.Length > 500)
            {
                return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "루프는 4·8프레임, FPS는 1~30, 동작 설명은 최대 500자여야 합니다");
            }

            var role = RoleOf(settings, asset);
            if (!Enum.IsDefined(role) || (settings.OutputKind == SpriteOutputKind.Layers
                ? role != SpriteAssetRole.Layer : role == SpriteAssetRole.Layer))
            {
                return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "결과 유형에 허용되지 않는 에셋 역할입니다");
            }
            if (asset.Placements is not null && role != SpriteAssetRole.Object)
            {
                return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "오브젝트만 자리를 가질 수 있습니다");
            }
            if (asset.Placements is { } placements && (placements.Count is < 1 or > 20 || !placements.All(InsideSource)))
            {
                return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "오브젝트 자리는 원본 내부 영역 1~20개여야 합니다");
            }

            var needsTransparency = role == SpriteAssetRole.Layer && asset.Order != backmost
                || role == SpriteAssetRole.Tile && settings.View == SpriteView.Isometric;
            if (needsTransparency && !asset.RequiresTransparency)
            {
                return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "앞쪽 레이어와 다이아몬드 타일은 투명이 필요합니다");
            }
            if (role == SpriteAssetRole.Object && !asset.RequiresTransparency)
            {
                return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "오브젝트는 투명이 필요합니다");
            }
            if (role == SpriteAssetRole.Backdrop && asset.RequiresTransparency)
            {
                return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "배경은 불투명이어야 합니다");
            }

            // 정적 대상은 기본 FrameCount와 무관하게 기준 이미지 한 장
            totalFrames += asset.Loop ? asset.FrameCount : 1;
        }

        var cellWidth = CellWidth(grid, assets, settings);
        foreach (var asset in assets.Where(a => RoleOf(settings, a) is SpriteAssetRole.Object or SpriteAssetRole.Backdrop))
        {
            var output = AssetOutput(settings, source, cellWidth, asset);
            if (!output.IsSuccess)
                return Result<bool>.Fail(output.ErrorCode!, output.ErrorMessage!);
        }

        return totalFrames <= 64
            ? Result<bool>.Ok(true)
            : Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "기준 이미지를 포함해 최대 64프레임까지 승인할 수 있습니다");
    }

    private static bool InsideSource(Bounds? bounds)
        => bounds is not null && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.W) && double.IsFinite(bounds.H)
            && bounds.X >= 0 && bounds.Y >= 0 && bounds.W > 0 && bounds.H > 0
            && bounds.X + bounds.W <= 1 && bounds.Y + bounds.H <= 1;

    public static SpriteAssetRole RoleOf(SpriteSettings settings, SpriteAssetPlan plan)
        => plan.Role ?? (settings.OutputKind == SpriteOutputKind.Layers ? SpriteAssetRole.Layer : SpriteAssetRole.Tile);

    public static SpriteAssetPlan NormalizeRole(SpriteSettings settings, SpriteAssetPlan plan)
        => plan.Role == (settings.OutputKind == SpriteOutputKind.Layers ? SpriteAssetRole.Layer : SpriteAssetRole.Tile)
            ? plan with { Role = null } : plan;

    public static double? CellWidth(SpriteGrid? grid, IReadOnlyList<SpriteAssetPlan> plans, SpriteSettings settings)
    {
        if (grid is not null)
            return grid.CellWidth;
        var widths = plans.Where(p => RoleOf(settings, p) == SpriteAssetRole.Tile)
            .Select(p => p.SourceBounds.W).Order().ToArray();
        if (widths.Length == 0)
            return null;
        return (widths[(widths.Length - 1) / 2] + widths[widths.Length / 2]) / 2;
    }

    public static Result<(SpriteCanvas Canvas, double Scale)> AssetOutput(
        SpriteSettings settings, SpriteCanvas source, double? cellWidth, SpriteAssetPlan plan)
    {
        var role = RoleOf(settings, plan);
        if (role is SpriteAssetRole.Tile or SpriteAssetRole.Layer)
            return Result<(SpriteCanvas, double)>.Ok((OutputCanvas(settings, source), 1));
        if (cellWidth is null || !double.IsFinite(cellWidth.Value) || cellWidth <= 0 || cellWidth > 1)
            return Result<(SpriteCanvas, double)>.Fail(ErrorCode.SpritePlanInvalid, "격자 칸 너비가 필요합니다");

        var worldScale = settings.TileWidth / (cellWidth.Value * source.Width);
        var width = source.Width * worldScale;
        var height = source.Height * worldScale;
        var scale = 1.0;
        if (role == SpriteAssetRole.Object)
        {
            width *= plan.SourceBounds.W;
            height *= plan.SourceBounds.H;
        }
        else
        {
            scale = Math.Min(1, 1024 / Math.Max(width, height));
            width *= scale;
            height *= scale;
        }
        width = Math.Round(width, MidpointRounding.AwayFromZero);
        height = Math.Round(height, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width is < 1 or > 1024 || height is < 1 or > 1024)
            return Result<(SpriteCanvas, double)>.Fail(ErrorCode.SpritePlanInvalid,
                role == SpriteAssetRole.Object ? "오브젝트 출력 크기는 1~1024픽셀이어야 합니다" : "배경 출력 크기는 1~1024픽셀이어야 합니다");
        return Result<(SpriteCanvas, double)>.Ok((new((int)width, (int)height), scale));
    }

    public static Result<SpriteAssetCanvas> AssetCanvas(SpriteSettings settings, SpriteCanvas source,
        SpriteCanvas jobGeneration, double? cellWidth, SpriteAssetPlan plan, IReadOnlyList<SpriteCanvas> generationSizes)
    {
        var output = AssetOutput(settings, source, cellWidth, plan);
        if (!output.IsSuccess)
            return Result<SpriteAssetCanvas>.Fail(output.ErrorCode!, output.ErrorMessage!);
        var generation = RoleOf(settings, plan) is SpriteAssetRole.Tile or SpriteAssetRole.Layer
            ? jobGeneration : GenerationCanvas(output.Value.Canvas, generationSizes);
        return Result<SpriteAssetCanvas>.Ok(new(output.Value.Canvas, generation,
            Transform(generation, output.Value.Canvas), output.Value.Scale));
    }

    public static SpriteAnchor Anchor(SpriteAssetRole role, SpriteCanvas output)
        => role is SpriteAssetRole.Layer or SpriteAssetRole.Backdrop
            ? new(0, 0) : new(output.Width / 2.0, output.Height / 2.0);

    public static SpriteCanvas OutputCanvas(SpriteSettings settings, SpriteCanvas source)
    {
        if (settings.OutputKind == SpriteOutputKind.Tiles)
        {
            return new(settings.TileWidth,
                settings.View == SpriteView.Isometric ? settings.TileWidth / 2 : settings.TileWidth);
        }

        var scale = Math.Min(1.0, 1024.0 / Math.Max(source.Width, source.Height));
        return new(
            Math.Max(1, (int)Math.Round(source.Width * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(source.Height * scale, MidpointRounding.AwayFromZero)));
    }

    public static SpriteCanvas GenerationCanvas(SpriteCanvas output, IReadOnlyList<SpriteCanvas> supported)
    {
        if (output is null || output.Width <= 0 || output.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(output));
        if (supported is null || supported.Count == 0
            || supported.Any(size => size is null || size.Width <= 0 || size.Height <= 0))
            throw new ArgumentException("확인된 양수 생성 크기가 필요합니다", nameof(supported));

        var ratio = (double)output.Width / output.Height;
        return supported.MinBy(size => Math.Abs(Math.Log((double)size.Width / size.Height / ratio)))!;
    }

    public static SpriteTransform Transform(SpriteCanvas generation, SpriteCanvas output)
    {
        var scale = Math.Min((double)output.Width / generation.Width, (double)output.Height / generation.Height);
        return new(scale, (output.Width - generation.Width * scale) / 2,
            (output.Height - generation.Height * scale) / 2);
    }

    public static IReadOnlyList<SpriteSheetLayout> SheetPages(SpriteCanvas frame, IReadOnlyList<Guid> imageIds)
    {
        if (frame is null || frame.Width is < 1 or > 4092 || frame.Height is < 1 or > 4092)
            throw new ArgumentOutOfRangeException(nameof(frame));
        ArgumentNullException.ThrowIfNull(imageIds);
        var strideX = frame.Width + 4;
        var strideY = frame.Height + 4;
        var columns = 4096 / strideX;
        var capacity = columns * (4096 / strideY);
        var pages = new List<SpriteSheetLayout>();
        for (var start = 0; start < imageIds.Count; start += capacity)
        {
            var count = Math.Min(capacity, imageIds.Count - start);
            var cells = Enumerable.Range(0, count).Select(i => new SpriteSheetCell(
                imageIds[start + i], start + i, new SpriteRect(
                    i % columns * strideX + 2, i / columns * strideY + 2, frame.Width, frame.Height))).ToArray();
            pages.Add(new(pages.Count, new(Math.Min(count, columns) * strideX,
                ((count + columns - 1) / columns) * strideY), cells));
        }
        return pages;
    }
}
