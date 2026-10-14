using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;

namespace Noxtend.Application.Sprites;

public static class SpriteGenerationSizes
{
    public static async Task<Result<IReadOnlyList<SpriteCanvas>>> ResolveAsync(PipelineJob job,
        IProviderConfigRepository providers, IModelCatalog catalog, CancellationToken ct)
    {
        var provider = job.ImageProviderConfigId is { } id ? await providers.GetAsync(id, ct) : null;
        if (provider is null || !provider.IsEnabled)
            return Result<IReadOnlyList<SpriteCanvas>>.Fail(ErrorCode.JobImageProviderDisabled, "이미지 공급자를 사용할 수 없습니다");
        IReadOnlyList<ProviderModel> models;
        try { models = await catalog.ListImageModelsAsync(provider.Id, ct); }
        catch (ProviderCallFailedException)
        {
            return Result<IReadOnlyList<SpriteCanvas>>.Fail(ErrorCode.JobImageModelUnavailable,
                "이미지 모델의 생성 크기를 확인할 수 없습니다");
        }
        var model = models.FirstOrDefault(m => m.Id == job.ImageModel);
        if (model?.Sprite is not { SupportsTransparency: true } capability || capability.Sizes is not { Count: > 0 }
            || capability.Sizes.Any(s => s.Width <= 0 || s.Height <= 0))
            return Result<IReadOnlyList<SpriteCanvas>>.Fail(ErrorCode.JobImageModelUnavailable,
                "투명 지원과 생성 크기가 확인된 이미지 모델이 필요합니다");
        return Result<IReadOnlyList<SpriteCanvas>>.Ok(capability.Sizes);
    }
}
