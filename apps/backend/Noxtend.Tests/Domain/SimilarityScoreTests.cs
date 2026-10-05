using Noxtend.Domain.Similarity;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 유사도 점수 계약. Design Ref: background-similarity-tuning §5.3~5.4
///
/// **overall 은 서버가 계산한다** (D-05). 모델이 보낸 overall 을 믿으면 모델별 가중치
/// 해석 차이와 임의 값이 채택 판정에 섞인다 — 축 여섯 개만 받고 합성은 여기서 한다.
/// </summary>
public sealed class SimilarityScoreTests
{
    // ─── 구조 검증 — 정확히 여섯 축 ───

    [Fact]
    public void Create_RequiresExactlySixDimensions()
    {
        var five = Dimensions().Take(5).ToList();

        var ex = Assert.Throws<ArgumentException>(() => SimilarityScore.Create(five));
        Assert.Contains("여섯", ex.Message);
    }

    [Fact]
    public void Create_RejectsDuplicateDimensionKinds()
    {
        var dims = Dimensions().ToList();
        dims[5] = dims[0] with { Kind = dims[1].Kind };   // lighting 누락 + camera 중복

        Assert.Throws<ArgumentException>(() => SimilarityScore.Create(dims));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Create_RejectsScoresOutsideRange(int score)
    {
        var dims = Dimensions().ToList();
        dims[0] = dims[0] with { Score = score };

        Assert.Throws<ArgumentOutOfRangeException>(() => SimilarityScore.Create(dims));
    }

    /// <summary>근거·권고는 500자 상한 — 프롬프트가 계약을 어겨도 저장이 부풀지 않는다 (§5.4).</summary>
    [Fact]
    public void Create_RejectsOverlongEvidence()
    {
        var dims = Dimensions().ToList();
        dims[0] = dims[0] with { Evidence = new string('가', 501) };

        Assert.Throws<ArgumentException>(() => SimilarityScore.Create(dims));
    }

    // ─── overall 합성 — 가중치 25/20/20/15/10/10 ───

    [Fact]
    public void Overall_IsTheWeightedSum()
    {
        // 100*0.25 + 80*0.20 + 60*0.20 + 40*0.15 + 20*0.10 + 0*0.10 = 61
        var score = Score(composition: 100, camera: 80, scale: 60, shape: 40, material: 20, lighting: 0);

        Assert.Equal(61, score.Overall);
    }

    /// <summary>0.5 는 올린다 — 반올림 규칙이 흔들리면 +3 경계 판정이 흔들린다.</summary>
    [Fact]
    public void Overall_RoundsHalfAwayFromZero()
    {
        // 80*0.25 + 70*0.20 + 60*0.20 + 50*0.15 + 40*0.10 + 30*0.10 = 60.5 → 61
        var score = Score(80, 70, 60, 50, 40, 30);

        Assert.Equal(61, score.Overall);
    }

    // ─── 채택 판정 — +3 overall, 축별 -10 floor (§5.3) ───

    [Fact]
    public void ShouldAdopt_AcceptsExactlyPlusThree()
    {
        var parent = Uniform(60);
        var candidate = Uniform(63);

        Assert.True(SimilarityScore.ShouldAdopt(parent, candidate));
    }

    [Fact]
    public void ShouldAdopt_RejectsPlusTwo()
    {
        Assert.False(SimilarityScore.ShouldAdopt(Uniform(60), Uniform(62)));
    }

    /// <summary>
    /// **overall 만 오르면 안 된다** — 특정 축이 크게 무너지는 개선은 개선이 아니다.
    /// 축 하락 10점까지는 허용, 11점부터 거부 (설계 검토 Acceptance 항목).
    /// </summary>
    [Fact]
    public void ShouldAdopt_AcceptsADimensionDropOfExactlyTen()
    {
        var parent = Score(60, 60, 60, 60, 60, 60);
        // composition 만 -10, 나머지를 크게 올려 overall +3 이상 확보
        var candidate = Score(50, 70, 70, 70, 70, 70);

        Assert.True(SimilarityScore.ShouldAdopt(parent, candidate));
    }

    [Fact]
    public void ShouldAdopt_RejectsADimensionDropOfEleven()
    {
        var parent = Score(60, 60, 60, 60, 60, 60);
        var candidate = Score(49, 75, 75, 75, 75, 75);   // overall 은 크게 올라도

        Assert.False(SimilarityScore.ShouldAdopt(parent, candidate));
    }

    // ─── 설정 ───

    private static IEnumerable<SimilarityDimension> Dimensions()
        => Enum.GetValues<SimilarityDimensionKind>()
            .Select(kind => new SimilarityDimension(kind, 50, "관찰", "권고"));

    private static SimilarityScore Uniform(int score)
        => Score(score, score, score, score, score, score);

    private static SimilarityScore Score(
        int composition, int camera, int scale, int shape, int material, int lighting)
        => SimilarityScore.Create(
        [
            new(SimilarityDimensionKind.Composition, composition, "관찰", "권고"),
            new(SimilarityDimensionKind.Camera, camera, "관찰", "권고"),
            new(SimilarityDimensionKind.Scale, scale, "관찰", "권고"),
            new(SimilarityDimensionKind.Shape, shape, "관찰", "권고"),
            new(SimilarityDimensionKind.Material, material, "관찰", "권고"),
            new(SimilarityDimensionKind.Lighting, lighting, "관찰", "권고"),
        ]);
}
