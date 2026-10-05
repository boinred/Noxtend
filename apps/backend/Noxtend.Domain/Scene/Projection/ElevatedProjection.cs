using Noxtend.Domain.Job;

namespace Noxtend.Domain.Scene.Projection;

/// <summary>
/// 발끝이 지면에 닿지 않는 배치 — 벽의 간판, 유적 위의 이끼, 매달린 조명.
///
/// Design Ref: background-placement-projection(#19) §3.4 · background-scale-calibration(#18) §4.3
///
/// **거리를 모른다는 것이 사실이다.** 시선이 지면을 만나지 않으므로 역투영이 답을 주지
/// 않는다. 사이클 #18 이 세운 규칙을 새 기하 위에서 그대로 쓴다 — 접지 배치들의 대표
/// 거리를 빌린다. 이전에는 깊이 하한 0.05 로 나눠 크기가 20배로 부풀던 자리다.
/// </summary>
public sealed class ElevatedProjection(double eyeHeight, double representativeGround)
    : IPlacementProjection
{
    public ProjectedPlacement Project(Bounds bounds, int depthOrder)
    {
        var eye = PlacementGeometry.EyeDistance(representativeGround, eyeHeight);

        return new ProjectedPlacement(
            PlacementGeometry.X(bounds, eye),
            PlacementGeometry.Z(representativeGround, depthOrder),
            PlacementGeometry.RawScale(bounds, eye));
    }
}
