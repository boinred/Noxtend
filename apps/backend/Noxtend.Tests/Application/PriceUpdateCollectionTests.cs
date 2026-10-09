using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Persistence.InMemory;
using Noxtend.Infrastructure.Prices;
using Noxtend.Tests.Api;
using Noxtend.Tuning.Application.Prices;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Tests.Application;

public sealed class PriceUpdateCollectionTests
{
    [Fact]
    public async Task CredentialFailure_PreservesSiblingResultAndDoesNotChangePrices()
    {
        var configs = new InMemoryProviderConfigRepository();
        var clock = new AcceptanceClock();
        var first = ProviderConfig.Create("first", ProviderKind.OpenAI, "cipher", "test", clock.GetUtcNow());
        var second = ProviderConfig.Create("second", ProviderKind.Anthropic, "cipher", "test", clock.GetUtcNow());
        await configs.AddAsync(first, default);
        await configs.AddAsync(second, default);
        var prices = new InMemoryModelPriceRepository();
        var store = new RecordingStore();
        var handler = new CollectPriceUpdateHandler(configs, prices, new CredentialFailureSource(first.Id, clock), store, clock, new ExistingPriceUpdateExecutionSupport());
        var result = await handler.HandleAsync([first.Id, second.Id], default);
        Assert.True(result.IsSuccess);
        Assert.Equal("failed", result.Value!.Providers[0].Status);
        Assert.Equal("공급자 설정 접근 실패", result.Value.Providers[0].ModelError);
        Assert.Equal("success", result.Value.Providers[1].Status);
        Assert.Same(result.Value, store.Preview);
        Assert.Empty(await prices.ListAsync(default));
    }

    [Fact]
    public async Task CallerCancellation_PropagatesWithoutPersistingPreview()
    {
        var configs = new InMemoryProviderConfigRepository();
        var clock = new AcceptanceClock();
        var config = ProviderConfig.Create("cancel", ProviderKind.OpenAI, "cipher", "test", clock.GetUtcNow());
        await configs.AddAsync(config, default);
        var store = new RecordingStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new CollectPriceUpdateHandler(configs, new InMemoryModelPriceRepository(),
            new CredentialFailureSource(Guid.Empty, clock), store, clock, new ExistingPriceUpdateExecutionSupport());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync([config.Id], cancellation.Token));
        Assert.Null(store.Preview);
    }

    [Fact]
    public void ExecutionSupport_UsesExactExistingModelAndAreaWithoutNewFamilyInference()
    {
        var support = new ExistingPriceUpdateExecutionSupport().SupportedModels;
        Assert.Contains(("tripo", "P1-20260311", "mesh"), support);
        Assert.Contains(("google", "gemini-2.5-flash-image", "image"), support);
        Assert.DoesNotContain(("tripo", "P1-20260311", "text"), support);
        Assert.DoesNotContain(("tripo", "P1-20990101", "mesh"), support);
        Assert.DoesNotContain(("meshy", "meshy-6", "mesh"), support);
        Assert.DoesNotContain(("google", "gemini-new-family-image", "image"), support);
    }

    private sealed class CredentialFailureSource(Guid deniedId, TimeProvider clock) : IOfficialModelPriceSource
    {
        public Task<OfficialPriceCollection> CollectAsync(Guid providerConfigId, CancellationToken ct)
            => providerConfigId == deniedId
                ? throw new ProviderCallFailedException("Sensitive synthetic credential failure")
                : Task.FromResult(new OfficialPriceCollection(providerConfigId, "anthropic", "success", "complete", clock.GetUtcNow(), [], [], null, null));
    }

    private sealed class RecordingStore : IPriceUpdateStore
    {
        public PriceUpdatePreview? Preview { get; private set; }
        public Task SavePreviewAsync(PriceUpdatePreview preview, CancellationToken ct)
        {
            Preview = preview;
            return Task.CompletedTask;
        }
        public Task<Result<PriceUpdateReceipt>> ApplyAsync(Guid previewId, PriceUpdateApplyInput input, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
