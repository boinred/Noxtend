using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Tuning.Application.Calls;

/// <summary>
/// 유사도 실행의 보수적 비용 추정 (background-similarity-tuning §11.2).
/// <paramref name="EstimatedMaximumCostUsd"/> 가 null 이면 단가 미등록 — 0 이 아니다.
/// 화면은 금액 대신 "단가 미등록" 을 보여주고 사용자의 확인을 받아야 실행한다.
/// </summary>
public sealed record SimilarityCostEstimate(
    int MaximumCalls,
    int InputTokensPerCall,
    int OutputTokensPerCall,
    decimal? EstimatedMaximumCostUsd,
    bool PriceKnown);

/// <summary>
/// Design Ref: background-similarity-tuning §11.2 — 평가 1회당 입력 6,000 · 출력 2,500
/// token 의 보수적 예산. 추정은 보증값이 아니며 실제 비용은 LlmCall usage 가 정본이다.
/// </summary>
public sealed class EstimateSimilarityCostHandler(IModelPriceRepository prices)
{
    private const int InputTokensPerCall = 6_000;
    private const int OutputTokensPerCall = 2_500;

    public async Task<SimilarityCostEstimate> HandleAsync(
        string model, int maxIterations, DateTimeOffset at, CancellationToken ct)
    {
        // 총 호출 = 기준 1 + 후보 반복 (§5.1). 반복 범위 밖은 접수가 거절하므로 여기선 자르기만
        var maximumCalls = 1 + Math.Clamp(maxIterations, 1, 3);

        var book = await prices.GetBookAsync(ct);
        var perCall = book.Estimate(model, at, InputTokensPerCall, OutputTokensPerCall);

        return new SimilarityCostEstimate(
            maximumCalls,
            InputTokensPerCall,
            OutputTokensPerCall,
            perCall is null ? null : perCall * maximumCalls,
            PriceKnown: perCall is not null);
    }
}
