using Noxtend.Domain.Common;

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
        SpriteSettings settings, SpriteCanvas source, IReadOnlyList<SpriteAssetPlan> assets)
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

        var backmost = assets.Min(a => a.Order);
        var totalFrames = 0;
        foreach (var asset in assets)
        {
            var bounds = asset.SourceBounds;
            if (string.IsNullOrWhiteSpace(asset.Name) || bounds is null
                || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y)
                || !double.IsFinite(bounds.W) || !double.IsFinite(bounds.H)
                || bounds.X < 0 || bounds.Y < 0 || bounds.W <= 0 || bounds.H <= 0
                || bounds.X + bounds.W > 1 || bounds.Y + bounds.H > 1)
            {
                return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "대상 이름과 원본 내부의 유한한 영역이 필요합니다");
            }

            if ((asset.Loop && asset.FrameCount is not (4 or 8)) || asset.Fps is < 1 or > 30
                || asset.MotionNotes is null || asset.MotionNotes.Length > 500)
            {
                return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "루프는 4·8프레임, FPS는 1~30, 동작 설명은 최대 500자여야 합니다");
            }

            // 오름차순은 뒤에서 앞으로, 다이아몬드는 셀 밖 투명 영역 필수
            var needsTransparency = settings.OutputKind == SpriteOutputKind.Layers
                ? asset.Order != backmost
                : settings.View == SpriteView.Isometric;
            if (needsTransparency && !asset.RequiresTransparency)
            {
                return Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "앞쪽 레이어와 다이아몬드 타일은 투명이 필요합니다");
            }

            // 정적 대상은 기본 FrameCount와 무관하게 기준 이미지 한 장
            totalFrames += asset.Loop ? asset.FrameCount : 1;
        }

        return totalFrames <= 64
            ? Result<bool>.Ok(true)
            : Result<bool>.Fail(ErrorCode.SpritePlanInvalid, "기준 이미지를 포함해 최대 64프레임까지 승인할 수 있습니다");
    }

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
