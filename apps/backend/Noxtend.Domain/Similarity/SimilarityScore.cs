namespace Noxtend.Domain.Similarity;

/// <summary>평가 축 — 정확히 여섯이며 프롬프트·화면·가중치가 이 순서를 공유한다 (§5.3).</summary>
public enum SimilarityDimensionKind
{
    Composition = 0,
    Camera = 1,
    Scale = 2,
    Shape = 3,
    Material = 4,
    Lighting = 5,
}

/// <summary>축 하나의 점수와 근거. 근거·권고는 500자 상한 (§5.4).</summary>
public sealed record SimilarityDimension(
    SimilarityDimensionKind Kind,
    int Score,
    string Evidence,
    string Recommendation);

/// <summary>
/// 유사도 점수. Design Ref: background-similarity-tuning §5.3
///
/// **overall 은 서버가 계산한다** (D-05) — 모델이 보낸 overall 을 받으면 모델별 가중치
/// 해석 차이와 임의 값이 채택 판정에 섞인다. 여섯 축만 받아 여기서 합성한다.
/// </summary>
public sealed class SimilarityScore
{
    /// <summary>축별 가중치 — 구성·카메라·크기가 형태·재질·조명보다 무겁다 (§5.3 표).</summary>
    private static readonly IReadOnlyDictionary<SimilarityDimensionKind, double> Weights =
        new Dictionary<SimilarityDimensionKind, double>
        {
            [SimilarityDimensionKind.Composition] = 0.25,
            [SimilarityDimensionKind.Camera] = 0.20,
            [SimilarityDimensionKind.Scale] = 0.20,
            [SimilarityDimensionKind.Shape] = 0.15,
            [SimilarityDimensionKind.Material] = 0.10,
            [SimilarityDimensionKind.Lighting] = 0.10,
        };

    private const int MaxTextLength = 500;

    private readonly IReadOnlyDictionary<SimilarityDimensionKind, SimilarityDimension> _byKind;

    private SimilarityScore(IReadOnlyList<SimilarityDimension> dimensions, int overall)
    {
        Dimensions = dimensions;
        Overall = overall;
        _byKind = dimensions.ToDictionary(d => d.Kind);
    }

    /// <summary>축 순서는 enum 순으로 고정 — 화면·저장·비교가 같은 순서를 본다.</summary>
    public IReadOnlyList<SimilarityDimension> Dimensions { get; }

    public int Overall { get; }

    public int ScoreOf(SimilarityDimensionKind kind) => _byKind[kind].Score;

    public static SimilarityScore Create(IReadOnlyList<SimilarityDimension> dimensions)
    {
        // 정확히 여섯 축, 중복·누락 없이 — 모델 응답의 구조 계약이다 (§5.4)
        if (dimensions.Count != 6 || dimensions.Select(d => d.Kind).Distinct().Count() != 6)
        {
            throw new ArgumentException("평가는 여섯 축을 정확히 한 번씩 담아야 합니다", nameof(dimensions));
        }

        foreach (var dimension in dimensions)
        {
            if (dimension.Score is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dimensions), dimension.Score, $"{dimension.Kind} 점수가 0..100 을 벗어났습니다");
            }

            if (dimension.Evidence.Length > MaxTextLength || dimension.Recommendation.Length > MaxTextLength)
            {
                throw new ArgumentException(
                    $"{dimension.Kind} 의 근거·권고는 {MaxTextLength}자 이하여야 합니다", nameof(dimensions));
            }
        }

        var ordered = dimensions.OrderBy(d => d.Kind).ToArray();

        // 0.5 는 올린다 — 반올림 규칙이 흔들리면 +3 채택 경계가 흔들린다
        var overall = (int)Math.Round(
            ordered.Sum(d => d.Score * Weights[d.Kind]), MidpointRounding.AwayFromZero);

        return new SimilarityScore(ordered, overall);
    }

    /// <summary>
    /// 후보 채택 판정 (§5.3) — **둘 다** 만족해야 한다:
    /// overall 이 3점 이상 오르고, 어느 축도 10점 넘게 무너지지 않는다.
    /// overall 만 보면 특정 축을 크게 희생한 "개선" 이 채택된다 (설계 검토 Acceptance).
    /// </summary>
    public static bool ShouldAdopt(SimilarityScore parent, SimilarityScore candidate)
    {
        if (candidate.Overall < parent.Overall + 3)
        {
            return false;
        }

        return Enum.GetValues<SimilarityDimensionKind>()
            .All(kind => candidate.ScoreOf(kind) - parent.ScoreOf(kind) >= -10);
    }
}
