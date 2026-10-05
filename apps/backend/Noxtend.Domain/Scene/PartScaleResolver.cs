namespace Noxtend.Domain.Scene;

/// <summary>
/// 파츠 하나의 배치들이 공유할 월드 배율.
///
/// Design Ref: background-scale-calibration(#18) §4.4 — D-04
///
/// **같은 에셋은 같은 실물이다.** `placements` 계약이 "같은 에셋을 이 자리들에 놓는다"
/// (part-placements §목표)이므로 월드 크기는 같아야 하고, 이미지에서의 크기 차이는
/// 깊이가 설명할 몫이지 크기가 흡수할 몫이 아니다.
///
/// 실측 전수 조사(배경 18작업·배치 354개)에서 같은 파츠의 배치 간 크기 편차가
/// isometric 20.8배 · orthographic 20.4배 · two-point 11.5배였다. 카메라 종류를 가리지
/// 않는 결함이라 판정도 카메라와 무관하게 한다.
/// </summary>
public static class PartScaleResolver
{
    /// <summary>
    /// 파츠의 모든 배치가 하나의 배율을 쓴다 — 대표는 **중앙값**이다.
    ///
    /// 중앙값인 이유는 이상치가 대표를 이기지 못하게 하기 위함이다. 실측 식생 13개 중
    /// 다섯이 깊이 하한에 걸려 폭주했지만 중앙값은 정상 쪽(약 1)에 남았다. 평균이었다면
    /// 폭주분이 대표를 끌어올렸을 것이다.
    ///
    /// **범위로 누르지 않고 하나로 모으는 이유** (설계 D-04 의 실측 수정): 허용 배수를
    /// 1.5 로 두자 기준 물체의 두 배치가 1.11m 와 2.49m 가 됐다 — 1.8m 로 보정한 물체가
    /// 정작 어느 쪽도 1.8m 가 아니다. `placements` 계약이 "같은 에셋"인 이상 크기가
    /// 갈릴 자리가 없고, 갈리면 보정 계수의 의미가 무너진다.
    /// </summary>
    public static IReadOnlyList<double> Resolve(IReadOnlyList<double> rawScales)
    {
        var median = Median(rawScales);

        // 쓸 수 있는 값이 없으면 판단 근거가 없다 — 원본을 그대로 둔다
        if (median <= 0)
        {
            return rawScales;
        }

        return [.. rawScales.Select(_ => median)];
    }

    /// <summary>
    /// 유한한 양수만 골라 낸 중앙값. 짝수 개는 가운데 둘의 평균이다
    /// (사이클 #1 의 기준 물체 규칙과 같은 형태). 쓸 값이 없으면 0.
    /// </summary>
    public static double Median(IReadOnlyList<double> values)
    {
        var usable = values.Where(value => double.IsFinite(value) && value > 0).Order().ToArray();
        if (usable.Length == 0)
        {
            return 0;
        }

        var middle = usable.Length / 2;

        return usable.Length % 2 == 0
            ? (usable[middle - 1] + usable[middle]) / 2
            : usable[middle];
    }

    /// <summary>
    /// 최댓값 ÷ 최솟값 — 기준 물체 신뢰도(D-05)의 척도다.
    /// **통일 이전 raw 로 재야 한다** — 통일 뒤에는 편차가 사라져 신뢰도를 못 읽는다.
    /// 쓸 값이 하나 이하면 비교 대상이 없으므로 1.
    /// </summary>
    public static double Spread(IReadOnlyList<double> values)
    {
        var usable = values.Where(value => double.IsFinite(value) && value > 0).ToArray();

        return usable.Length < 2 ? 1 : usable.Max() / usable.Min();
    }
}
