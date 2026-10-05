using Noxtend.Domain.Job;

namespace Noxtend.Domain.Scene.Projection;

/// <summary>
/// 발끝이 지면에 닿는 배치 — 지면 역투영으로 거리를 구하고 거기서 자리·크기를 얻는다.
///
/// Design Ref: background-placement-projection(#19) §1
/// </summary>
public sealed class GroundedProjection(double eyeHeight, double pitchDown) : IPlacementProjection
{
    public ProjectedPlacement Project(Bounds bounds, int depthOrder)
    {
        // 접지 배치만 여기 오므로 거리가 반드시 나온다 — 분류는 PlacementProjector 몫이다
        var ground = PlacementGeometry.GroundDistance(bounds, eyeHeight, pitchDown)
            ?? throw new InvalidOperationException("접지 배치가 아닙니다");
        var eye = PlacementGeometry.EyeDistance(ground, eyeHeight);

        return new ProjectedPlacement(
            PlacementGeometry.X(bounds, eye),
            PlacementGeometry.Z(ground, depthOrder),
            PlacementGeometry.RawScale(bounds, eye));
    }
}
