using Noxtend.Domain.Job;

namespace Noxtend.Domain.Scene.Projection;

/// <summary>
/// 배치 하나가 옮겨 간 월드 자리와 보정 이전 배율.
///
/// 축별 배율은 표면 파츠에서만 갈린다 (#20 §4.2) — 낱개 물건은 세 값이 `RawScale` 로 같다.
/// </summary>
public readonly record struct ProjectedPlacement(double X, double Z, double RawScale)
{
    public double ScaleX { get; init; } = RawScale;

    public double ScaleY { get; init; } = RawScale;

    public double ScaleZ { get; init; } = RawScale;
}

/// <summary>
/// 배치 하나를 월드로 옮기는 규칙.
///
/// Design Ref: background-scale-calibration(#18) §4.1 — D-01
///
/// **구현이 갈리는 축은 "발끝이 지면에 닿는가" 하나다.** 초안은 장면 단위로 원근·정사영을
/// 가르려 했으나 전수 조사가 그 근거를 없앴다 — 발끝이 지평선 위인 비율이 가장 높은 것은
/// 부감(isometric 80m, 21.4%)이 아니라 눈높이 one-point 1.6m 장면(36.4%)이었다.
/// 그 조건은 카메라 종류가 아니라 **공중에 있는 물체**(벽의 간판, 유적 위의 이끼)를 뜻하며
/// 모든 장면에 섞여 있다.
///
/// x·z 공식은 두 구현이 공유한다(<see cref="PlacementGeometry"/>). 다른 것은 크기를 정할
/// 때 쓰는 깊이뿐이다 — 이 좁음이 의도다. z 를 바꾸지 않는다는 비목표가 타입으로 강제된다.
/// </summary>
public interface IPlacementProjection
{
    ProjectedPlacement Project(Bounds bounds, int depthOrder);
}
