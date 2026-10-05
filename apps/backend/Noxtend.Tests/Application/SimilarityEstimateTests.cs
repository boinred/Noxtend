using Noxtend.Infrastructure.Persistence.InMemory;
using Noxtend.Tuning.Application.Calls;
using Noxtend.Tuning.Domain.Call;

namespace Noxtend.Tests.Application;

/// <summary>
/// 비용 추정 (background-similarity-tuning §11.2).
///
/// **단가 미등록은 0원이 아니다** — null 로 내려 화면이 "단가 미등록" 을 보여주고
/// 사용자의 확인을 받게 한다. 추정은 보증값이 아니며 실제 비용은 LlmCall usage 가 정본.
/// </summary>
public sealed class SimilarityEstimateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Estimate_MultipliesThePerCallBudgetByMaximumCalls()
    {
        var prices = new InMemoryModelPriceRepository();
        // 입력 $10/M · 출력 $20/M → 1회 = 6000×10/1M + 2500×20/1M = 0.06 + 0.05 = $0.11
        await prices.AddAsync(
            ModelPrice.Create("gpt-test", 10, 20, null, null, null, Now.AddDays(-1), "테스트"),
            CancellationToken.None);

        var estimate = await new EstimateSimilarityCostHandler(prices)
            .HandleAsync("gpt-test", maxIterations: 2, Now, CancellationToken.None);

        Assert.Equal(3, estimate.MaximumCalls);   // 기준 1 + 후보 2
        Assert.True(estimate.PriceKnown);
        Assert.Equal(0.33m, estimate.EstimatedMaximumCostUsd);
    }

    [Fact]
    public async Task Estimate_ReturnsNullForAnUnknownModel()
    {
        var estimate = await new EstimateSimilarityCostHandler(new InMemoryModelPriceRepository())
            .HandleAsync("unknown-model", 1, Now, CancellationToken.None);

        Assert.False(estimate.PriceKnown);
        Assert.Null(estimate.EstimatedMaximumCostUsd);
        Assert.Equal(2, estimate.MaximumCalls);
    }
}
