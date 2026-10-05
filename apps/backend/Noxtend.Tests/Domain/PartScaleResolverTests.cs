using Noxtend.Domain.Scene;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 파츠 하나의 배치들이 공유할 월드 배율.
///
/// Design Ref: background-scale-calibration(#18) §4.4 — D-04
///
/// **같은 에셋은 같은 실물이다.** `placements` 계약이 "같은 에셋을 이 자리들에 놓는다"
/// 이므로 월드 크기는 같아야 하고, 이미지에서의 크기 차이는 깊이가 설명할 몫이다.
/// 실측(F8053152)에서 같은 파츠 13개 배치가 0.9m~19.2m — 20.7배로 벌어졌다.
/// </summary>
public sealed class PartScaleResolverTests
{
    /// <summary>실측 식생 13개 배치 — 다섯이 깊이 하한에 걸려 폭주한 모양 그대로다.</summary>
    private static readonly double[] Vegetation =
        [8.37, 10.80, 1.49, 0.80, 9.00, 10.80, 9.60, 1.97, 1.07, 0.84, 0.96, 0.60, 0.52];

    [Fact]
    public void Resolve_LeavesNoSpreadWithinAPart()
    {
        var resolved = PartScaleResolver.Resolve(Vegetation);

        // 같은 에셋은 같은 실물이다 — 편차가 남을 자리가 없다
        Assert.Equal(resolved.Min(), resolved.Max(), precision: 9);
    }

    /// <summary>
    /// **이상치가 대표를 이기지 못한다.** 13개 중 다섯이 폭주해도 중앙값은 정상 쪽에 남는다 —
    /// 평균이었다면 폭주분이 대표를 끌어올려 통일이 무의미해진다.
    /// </summary>
    [Fact]
    public void Resolve_KeepsTheRepresentativeOnTheSaneSide()
    {
        var resolved = PartScaleResolver.Resolve(Vegetation);

        // 정상 배치들은 1 안팎이다. 폭주분(8~11)이 대표를 가져가면 안 된다
        Assert.All(resolved, scale => Assert.InRange(scale, 0.5, 2.5));
    }

    /// <summary>배치가 하나면 비교 대상이 없다 — 손대지 않는다 (SC-04 동등성의 근거).</summary>
    [Fact]
    public void Resolve_LeavesASinglePlacementAlone()
    {
        Assert.Equal([13.0], PartScaleResolver.Resolve([13.0]));
    }

    /// <summary>이미 고른 배치도 대표값 하나로 모인다 — 중앙값이 그 값이다.</summary>
    [Fact]
    public void Resolve_CollapsesCoherentScalesToTheirMedian()
    {
        Assert.Equal([2.0, 2.0, 2.0], PartScaleResolver.Resolve([2.0, 2.1, 1.9]));
    }

    /// <summary>입력 순서가 결과를 바꾸지 않는다 — 결정성(NFR-01).</summary>
    [Fact]
    public void Resolve_IsOrderIndependent()
    {
        var forward = PartScaleResolver.Resolve(Vegetation).Order().ToArray();
        var backward = PartScaleResolver.Resolve([.. Vegetation.Reverse()]).Order().ToArray();

        Assert.Equal(forward, backward);
    }

    /// <summary>짝수 개는 가운데 둘의 평균이다 — 사이클 #1 의 기준 물체 규칙과 같다.</summary>
    [Fact]
    public void Median_AveragesTheMiddlePairForEvenCounts()
    {
        Assert.Equal(1.01, PartScaleResolver.Median([1.61, 0.41]), precision: 6);
    }

    /// <summary>0·음수·비유한은 대표를 왜곡한다 — 중앙값 계산에서 뺀다.</summary>
    [Fact]
    public void Median_IgnoresNonPositiveAndNonFiniteValues()
    {
        Assert.Equal(2.0, PartScaleResolver.Median([0, -1, double.NaN, 2.0]), precision: 6);
    }

    /// <summary>쓸 수 있는 값이 없으면 판단 근거가 없다 — 원본을 그대로 돌려준다.</summary>
    [Fact]
    public void Resolve_ReturnsInputWhenNoUsableMedian()
    {
        double[] unusable = [0, -1];

        Assert.Equal(unusable, PartScaleResolver.Resolve(unusable));
    }

    /// <summary>
    /// 기준 물체 신뢰도(D-05)의 척도. 실측에서 같은 "청색 발광 단말기" 두 배치가
    /// 0.41 vs 1.61 — 3.9배였다. 보정 계수가 그 사이 임의 지점에 찍힌다는 뜻이다.
    /// </summary>
    [Fact]
    public void Spread_ReportsTheRawRatioBeforeResolving()
    {
        Assert.Equal(3.93, PartScaleResolver.Spread([1.61, 0.41]), precision: 2);
        Assert.Equal(1.0, PartScaleResolver.Spread([2.0]), precision: 6);
    }
}
