namespace Noxtend.Domain.Scene.Projection;

/// <summary>
/// 배치 사각형이 지면에 드리운 면.
///
/// Design Ref: background-surface-parts(#20) §4.3
///
/// 표면 파츠에서는 **이 사각형이 곧 크기다.** 낱개 물건처럼 "얼마나 큰 물체인가" 를
/// 따로 묻지 않는다 — 배치가 가리키는 면을 그대로 덮으면 된다.
///
/// 가로는 **거리에 따라 달라진다.** 같은 화면 폭이라도 먼 쪽이 더 넓게 벌어지므로 값
/// 하나로 굳히지 않고 각을 들고 다니다 필요한 거리에서 되돌린다. 바닥 판은 깊이
/// 한가운데에서, 벽은 발치가 지면과 만나는 아래 모서리에서 재야 자리와 크기가 같은
/// 거리에서 나온다 — 실측 벽에서 이 둘이 어긋나면 폭이 1.17배 부푼다.
/// </summary>
public readonly record struct GroundQuad(
    double CenterGround,
    double NearGround,
    double Depth,
    /// <summary>화면 반폭의 탄젠트 — 눈 거리를 곱하면 월드 반폭이 된다.</summary>
    double HalfWidthTangent,
    /// <summary>화면 중심에서 벗어난 각의 탄젠트 — 눈 거리를 곱하면 가로 오프셋이 된다.</summary>
    double CenterTangent,
    /// <summary>이 사각형을 만든 카메라의 눈높이 — 다른 거리로 환산할 때 필요하다.</summary>
    double EyeHeight)
{
    /// <summary>깊이 한가운데에서 잰 폭 — 바닥 판이 쓰는 대표값.</summary>
    public double Width => WidthAt(CenterGround);

    /// <summary>깊이 한가운데에서 잰 가로 자리.</summary>
    public double CenterX => CenterXAt(CenterGround);

    /// <summary>그 지면 거리에서의 월드 폭.</summary>
    public double WidthAt(double groundDistance)
        => 2 * PlacementGeometry.EyeDistance(groundDistance, EyeHeight) * HalfWidthTangent;

    /// <summary>그 지면 거리에서의 월드 가로 자리.</summary>
    public double CenterXAt(double groundDistance)
        => PlacementGeometry.EyeDistance(groundDistance, EyeHeight) * CenterTangent;
}
