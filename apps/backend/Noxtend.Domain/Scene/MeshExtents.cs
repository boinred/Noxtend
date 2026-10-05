namespace Noxtend.Domain.Scene;

/// <summary>
/// GLB 하나가 차지하는 축별 크기.
///
/// Design Ref: background-placement-projection(#19) 후속 — 납작한 파츠 대응
///
/// **왜 필요한가**: 뷰어는 메시를 "높이 1" 로 정규화하고 서버가 준 `scale` 을 곱한다
/// (scene-assembly §3.2). 그래서 `scale` 은 곧 월드 높이다. 그런데 배치의 `bounds.H` 는
/// 서 있는 물체에서는 높이지만, **바닥 판에서는 비스듬히 누운 지면이 화면에서 차지한
/// 세로 범위**다. 그걸 높이로 읽으면 판이 서 버린다 — 실측에서 포장 석재가 11.81m 가 됐다.
///
/// 축 비율을 알면 그 둘을 가를 수 있다.
/// </summary>
public sealed record MeshExtents(double X, double Y, double Z)
{
    /// <summary>
    /// 바닥에 눕는 형태로 볼 임계 — 높이가 바닥 폭의 이만큼보다 작으면 판이다.
    ///
    /// **이 임계로 표면을 가려낼 수는 없다.** 실측에서 같은 성격의 파츠 3개가
    /// 0.43 · 0.14 · 0.57 로 흩어졌고 걸린 것은 하나뿐이다 (계획 §2.2) — 3D 생성이
    /// 바닥 이미지에서 두께를 제각각 뽑기 때문이다. 그래서 표면 판정은 분해가 준
    /// 한 비트가 하고, 이 값은 **부차 신호**에만 쓴다: 표시는 `ground` 인데 형태가
    /// 서 있으면 요약이 알린다 (설계 §4.1). 실측 포장 0.98 : 0.82 : 1.90 이 0.43 으로
    /// 이 임계를 웃돌아 그 신호를 켠다 — 오작동이 아니라 의도된 동작이다.
    /// </summary>
    public const double FlatThreshold = 0.35;

    /// <summary>바닥 방향 폭 — 두 수평 축 중 큰 쪽.</summary>
    public double Footprint => Math.Max(X, Z);

    /// <summary>
    /// 지면을 덮는 형태인가. 크기를 모르면(0 이하) 서 있는 것으로 본다 —
    /// 사이클 #19 까지의 동작이 그것이고, 모를 때 바꾸지 않는 편이 안전하다.
    /// </summary>
    public bool IsGroundPlane =>
        Footprint > 0 && Y >= 0 && Y < Footprint * FlatThreshold;

    /// <summary>
    /// 바닥 폭 하나를 높이로 옮기는 비율.
    ///
    /// 정규화가 높이로 나누므로, 바닥 폭을 원하는 값으로 맞추려면
    /// `원하는 폭 × (높이 ÷ 바닥 폭)` 을 `scale` 로 줘야 한다.
    /// </summary>
    public double HeightPerFootprint => Footprint > 0 ? Y / Footprint : 1;

    /// <summary>
    /// 얇은 축이 긴 축에 대해 갖는 비 — 벽의 **두께** 비율이다.
    ///
    /// 바닥 판은 높이가 얇지만 벽은 깊이가 얇다. 높이 비율을 벽에 쓰면 덩어리가 된다.
    /// </summary>
    public double ThicknessRatio
    {
        get
        {
            var longest = Math.Max(Math.Max(X, Y), Z);
            var shortest = Math.Min(Math.Min(X, Y), Z);

            return longest > 0 ? shortest / longest : 1;
        }
    }
}
