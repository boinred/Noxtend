using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Tuning.Application.Prices;

public sealed record PriceUpdatePreview(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    IReadOnlyList<PriceUpdateProviderResult> Providers, IReadOnlyList<PriceUpdateCandidate> Candidates);
public sealed record PriceUpdateApplyInput(Guid RequestId, IReadOnlyList<Guid>? CandidateIds, DateTimeOffset? EffectiveFrom);
public sealed record PriceUpdateReceiptItem(Guid CandidateId, Guid PriceId, DateTimeOffset EffectiveFrom);
public sealed record PriceUpdateReceipt(Guid RequestId, Guid PreviewId, DateTimeOffset AppliedAt, IReadOnlyList<PriceUpdateReceiptItem> Items);

public interface IPriceUpdateExecutionSupport
{
    IReadOnlyCollection<(string Provider, string Model, string Area)> SupportedModels { get; }
}

public interface IPriceUpdateStore
{
    Task SavePreviewAsync(PriceUpdatePreview preview, CancellationToken ct);
    Task<Result<PriceUpdateReceipt>> ApplyAsync(Guid previewId, PriceUpdateApplyInput input, CancellationToken ct);
}

public sealed class CollectPriceUpdateHandler(IProviderConfigRepository configs, IModelPriceRepository prices,
    IOfficialModelPriceSource source, IPriceUpdateStore store, TimeProvider clock, IPriceUpdateExecutionSupport execution)
{
    public async Task<Result<PriceUpdatePreview>> HandleAsync(IReadOnlyList<Guid>? configIds, CancellationToken ct)
    {
        var ids = configIds?.Distinct().ToArray() ?? [];
        if (ids.Length is < 1 or > 25 || ids.Contains(Guid.Empty))
            return Result<PriceUpdatePreview>.Fail(ErrorCode.PriceUpdateInvalid, "수집 설정은 1–25개여야 합니다");
        var available = await configs.ListAsync(ct);
        var selected = ids.Select(id => available.FirstOrDefault(c => c.Id == id)).ToArray();
        if (selected.Any(c => c is null || !c.IsEnabled || c.Kind is not (ProviderKind.OpenAI or ProviderKind.Anthropic or ProviderKind.Google or ProviderKind.Tripo or ProviderKind.Meshy)))
            return Result<PriceUpdatePreview>.Fail(ErrorCode.PriceUpdateInvalid, "유효한 활성 공급자 설정이 필요합니다");

        // 모든 설정에 공유되는 수집 시간 상한
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(120));
        var collections = new List<OfficialPriceCollection>();
        foreach (var config in selected)
        {
            try
            {
                budget.Token.ThrowIfCancellationRequested();
                collections.Add(await source.CollectAsync(config!.Id, budget.Token));
            }
            catch (ProviderCallFailedException)
            {
                collections.Add(new(config!.Id, config.Kind.ToString().ToLowerInvariant(), "failed", "failed",
                    clock.GetUtcNow(), [], [], "공급자 설정 접근 실패", "공급자 설정 접근 실패"));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                collections.Add(new(config!.Id, config.Kind.ToString().ToLowerInvariant(), "failed", "failed",
                    clock.GetUtcNow(), [], [], "전체 수집 시간 초과", "전체 수집 시간 초과"));
            }
        }
        ct.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow();
        var comparison = PriceCandidateComparison.Compare(collections, await prices.ListAsync(ct), now, execution.SupportedModels);
        var preview = new PriceUpdatePreview(Guid.NewGuid(), now, now.AddMinutes(30), comparison.Providers, comparison.Candidates);
        await store.SavePreviewAsync(preview, ct);
        return Result<PriceUpdatePreview>.Ok(preview);
    }
}

public sealed class ApplyPriceUpdateHandler(IPriceUpdateStore store)
{
    public Task<Result<PriceUpdateReceipt>> HandleAsync(Guid previewId, PriceUpdateApplyInput input, CancellationToken ct)
        => store.ApplyAsync(previewId, input, ct);
}
