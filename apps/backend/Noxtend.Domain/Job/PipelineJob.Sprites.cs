using Noxtend.Domain.Common;
using Noxtend.Domain.Sprites;

namespace Noxtend.Domain.Job;

public sealed partial class PipelineJob
{
    public ProductionMode ProductionMode { get; private set; } = ProductionMode.ThreeD;
    public SpritePipelineState? Sprites { get; private set; }

    public static Result<PipelineJob> CreateSprites(
        Guid sourceImageId, Guid imageProviderConfigId, string imageModel,
        SpriteSettings settings, SpriteCanvas sourceCanvas, SpriteCanvas generationCanvas, DateTimeOffset now)
    {
        var validation = SpriteRules.ValidateSettings(settings, sourceCanvas);
        if (!validation.IsSuccess)
        {
            return Result<PipelineJob>.Fail(validation.ErrorCode!, validation.ErrorMessage!);
        }

        if (generationCanvas is null || generationCanvas.Width <= 0 || generationCanvas.Height <= 0)
        {
            return Result<PipelineJob>.Fail(ErrorCode.SpriteSettingsInvalid, "생성 캔버스는 양수 크기여야 합니다");
        }

        var job = Create(AssetCategory.Background, sourceImageId, now,
            imageProviderConfigId, imageModel, requiresReview: true);
        job.ProductionMode = ProductionMode.TwoD;
        job.Sprites = new SpritePipelineState(settings, sourceCanvas, generationCanvas);
        return Result<PipelineJob>.Ok(job);
    }
}
