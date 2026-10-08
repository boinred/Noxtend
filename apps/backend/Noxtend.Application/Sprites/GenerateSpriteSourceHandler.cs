using Noxtend.Application.Common;
using Noxtend.Application.Generation;
using Noxtend.Application.Uploads;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Prompt;
using Noxtend.Domain.Sprites;
using Noxtend.Domain.Upload;

namespace Noxtend.Application.Sprites;

public sealed record GenerateSpriteSourceCommand(Guid RequestId, string Prompt, Guid ImageProviderConfigId, string ImageModel);

public sealed class GenerateSpriteSourceHandler(
    IProviderConfigRepository providers, IModelCatalog catalog, IPromptCatalog prompts,
    IImageProviderFactory providerFactory, RateLimitGate rateLimitGate, CreateUploadHandler uploads,
    TimeProvider? timeProvider = null)
{
    public const int MaxPromptLength = 1000;
    public const int MaxGenerationSeconds = 180;

    public async Task<Result<StoredImage>> HandleAsync(GenerateSpriteSourceCommand command, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(MaxGenerationSeconds), timeProvider ?? TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        var token = linked.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            if (command.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(command.Prompt)
                || command.Prompt.Trim().Length > MaxPromptLength || string.IsNullOrWhiteSpace(command.ImageModel))
                return Fail(ErrorCode.SpriteSettingsInvalid, "유효한 요청 ID, 1~1000자 장면 설명과 이미지 모델이 필요합니다");
            var description = command.Prompt.Trim();
            var modelId = command.ImageModel.Trim();
            var providerConfig = await providers.GetAsync(command.ImageProviderConfigId, token);
            token.ThrowIfCancellationRequested();
            if (providerConfig is null) return Fail(ErrorCode.JobImageProviderNotFound, "이미지 공급자가 없습니다");
            if (!providerConfig.IsEnabled) return Fail(ErrorCode.JobImageProviderDisabled, "이미지 공급자가 중지되었습니다");
            var models = await catalog.ListImageModelsAsync(providerConfig.Id, token);
            token.ThrowIfCancellationRequested();
            var model = models.FirstOrDefault(model => model.Id == modelId);
            if (model?.Sprite is not { SupportsTransparency: true } capability || capability.Sizes is null
                || capability.Sizes.Count == 0 || capability.Sizes.Any(size => size is null || size.Width <= 0 || size.Height <= 0))
                return Fail(ErrorCode.JobImageModelUnavailable, "투명 지원과 생성 크기가 확인된 이미지 모델이 필요합니다");
            var prompt = await prompts.GetActiveAsync(LlmOperationKind.GenerateSpriteSource, AssetCategory.Background, token);
            token.ThrowIfCancellationRequested();
            if (prompt is null) return Fail(ErrorCode.PromptNotActive, "2D 기준 이미지 활성 프롬프트가 없습니다");
            var provider = await providerFactory.CreateAsync(providerConfig.Id, modelId, token);
            token.ThrowIfCancellationRequested();
            var size = PickSize(capability.Sizes);
            var request = new ImageRequest(ImageCallContext.ForSourceGeneration(command.RequestId, prompt.VersionId, providerConfig.Id, modelId),
                PromptTemplate.Render($"{prompt.System}\n\n{prompt.User}", new Dictionary<string, string> { ["prompt"] = description }),
                [], $"{size.Width}x{size.Height}", ImageBackground.Opaque);
            await rateLimitGate.WaitIfNeededAsync(providerConfig.Id, token);
            token.ThrowIfCancellationRequested();
            var result = await provider.GenerateAsync(request, token);
            token.ThrowIfCancellationRequested();
            await rateLimitGate.RecordAsync(providerConfig.Id, new(result.RateLimitRemainingRequests, result.RateLimitResetAfter,
                result.RateLimitRemainingTokens, result.RateLimitResetTokensAfter,
                RateLimitHeaders.SumTokens(result.InputTokens, result.OutputTokens)), token);
            token.ThrowIfCancellationRequested();
            if (result.ImageCount != 1) return Fail(ErrorCode.ProviderBadResponse, "이미지 한 장이 필요합니다");
            using var stream = new MemoryStream(result.Bytes, writable: false);
            var upload = await uploads.HandleAsync(stream, "sprite-prompt", result.ContentType, result.Bytes.Length, token);
            token.ThrowIfCancellationRequested();
            return upload.IsSuccess ? upload : Fail(ErrorCode.ProviderBadResponse, $"생성 이미지를 저장할 수 없습니다: {upload.ErrorMessage}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return Fail(ErrorCode.ProviderCallFailed, "기준 이미지 생성 제한 시간(180초)을 초과했습니다. 다시 시도해 주세요");
        }
        catch (ProviderCallFailedException ex)
        {
            return Fail(ErrorCode.ProviderCallFailed, ex.Message);
        }
        catch (ProviderBadResponseException ex)
        {
            return Fail(ErrorCode.ProviderBadResponse, ex.Message);
        }
    }

    internal static SpriteCanvas PickSize(IReadOnlyList<SpriteCanvas> sizes)
        => sizes.OrderByDescending(size => size.Width >= size.Height).ThenByDescending(size => (long)size.Width * size.Height).First();

    private static Result<StoredImage> Fail(string code, string message) => Result<StoredImage>.Fail(code, message);
}
