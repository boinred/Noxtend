using Noxtend.Domain.Job;

namespace Noxtend.Domain.Scene.Projection;

/// <summary>
/// 2D 배치를 지면 위 자리로 되돌리는 기하.
///
/// Design Ref: background-placement-projection(#19) §1 · scene-assembly §3.2
///
/// **핀홀 카메라의 역투영이다.** 이전에는 세 축이 깊이를 서로 다르게 해석했다 —
/// 크기만 `1/depth`(원근이 요구하는 값)였고, x 는 선형 근사(`Spread = 1.5` 가 그
/// 보정항), z 는 아예 선형이었다. 그 불일치가 실측에서 파츠별 화면 점유율을
/// 2.0~7.5배로 갈랐다(편차 3.8배). 상수를 어떻게 조정해도 동시에 맞출 수 없는 종류다.
///
/// 이제 필요한 값은 **카메라에서 그 지점까지의 거리** 하나다. 거리를 구하면 자리도
/// 크기도 거기서 따라 나온다.
///
/// 좌표계: 바닥 y=0 · +x 오른쪽 · +z 카메라 쪽. 카메라는 원점 `(0, 눈높이, 0)` 에서
/// `pitchDown` 만큼 내려본다.
/// </summary>
public static class PlacementGeometry
{
    /// <summary>화각(도) — `SceneStaging` 의 값과 같아야 배치와 카메라가 같은 프레임을 쓴다.</summary>
    public const double FieldOfView = 50;

    /// <summary>
    /// 거리 상한 — 눈높이의 배수.
    ///
    /// `tan(축각 + 하향각)` 이 0 에 가까우면 거리가 발산한다. 그쯤이면 실질적 무한대이고
    /// 부동소수도 흔들리므로 잘라 둔다. 실측 유적 광장의 최원경이 눈높이의 약 8배다.
    /// </summary>
    public const double MaxDistanceInEyeHeights = 200;

    /// <summary>세로 반화각(라디안).</summary>
    public static double HalfFovVertical => FieldOfView / 2 * Math.PI / 180;

    /// <summary>
    /// 가로 반화각(라디안).
    ///
    /// 원본 이미지의 가로세로비를 서버가 모르므로 정사각 프레임으로 어림한다 — 가로가
    /// 좁게 잡히면 배치가 안쪽으로 모이고, 그 오차는 보정 계수가 아니라 형상에 남는다.
    /// 참조 비율을 유도에 넘기는 것은 다음 사이클의 후보다.
    /// </summary>
    public static double HalfFovHorizontal => HalfFovVertical;

    /// <summary>
    /// 배치의 발끝이 카메라 축에서 벗어난 각(라디안). 아래가 양수다.
    ///
    /// 이미지 세로 위치를 각으로 되돌린다 — 프레임 가장자리가 정확히 반화각이 되도록.
    /// </summary>
    public static double AxisAngle(Bounds bounds)
        => Math.Atan((bounds.Y + bounds.H - 0.5) * 2 * Math.Tan(HalfFovVertical));

    /// <summary>
    /// 발끝이 지면과 만나는 지점까지의 수평 거리. 지평선 위면 <c>null</c>.
    ///
    /// 축각과 하향각을 더하면 수평선 아래로 내려간 각이 되고, 눈높이를 그 tangent 로
    /// 나누면 지면까지의 수평 거리다. 이 각이 0 이하면 시선이 지면을 만나지 않는다 —
    /// 지면에 접지하지 않은 물체(벽의 간판, 유적 위 이끼)가 그 경우다.
    /// </summary>
    public static double? GroundDistance(Bounds bounds, double eyeHeight, double pitchDown)
    {
        var below = AxisAngle(bounds) + pitchDown;
        if (below <= 1e-6)
        {
            return null;
        }

        return Math.Min(eyeHeight / Math.Tan(below), eyeHeight * MaxDistanceInEyeHeights);
    }

    /// <summary>카메라에서 지면 위 그 지점까지의 실제 거리.</summary>
    public static double EyeDistance(double groundDistance, double eyeHeight)
        => Math.Sqrt((groundDistance * groundDistance) + (eyeHeight * eyeHeight));

    /// <summary>
    /// 가로 자리 — 이미지 x 오프셋이 그 거리에서 차지하는 월드 폭.
    /// 먼 것일수록 같은 오프셋이 더 넓게 벌어진다 (원근이 모은 것을 되돌린다).
    /// </summary>
    public static double X(Bounds bounds, double eyeDistance)
        => (bounds.X + (bounds.W / 2) - 0.5) * 2 * eyeDistance * Math.Tan(HalfFovHorizontal);

    /// <summary>앞뒤 자리. 같은 거리는 DepthOrder 로 가른다 — 작을수록 앞 (B-05).</summary>
    public static double Z(double groundDistance, int depthOrder)
        => -groundDistance - (depthOrder * DepthOrderStep);

    /// <summary>
    /// 실제 높이 — 화면에서 차지한 세로 비율이 그 거리에서 갖는 월드 높이.
    /// 이것이 원근의 정직한 역이다: 멀리 있는데 화면에서 같은 높이면 실제로는 크다.
    /// </summary>
    public static double RawScale(Bounds bounds, double eyeDistance)
        => bounds.H * 2 * eyeDistance * Math.Tan(HalfFovVertical);

    /// <summary>
    /// 배치 사각형이 지면에서 차지하는 면 — 표면 파츠의 크기가 곧 이것이다.
    ///
    /// Design Ref: background-surface-parts(#20) §4.3 — D-03
    ///
    /// 아래 모서리와 위 모서리를 각각 지면으로 되돌려 **깊이**를 얻는다. 가로는 거리에
    /// 따라 벌어지므로 값이 아니라 각으로 실어 보낸다 — 쓰는 쪽이 제 거리에서 되돌린다.
    /// 위 모서리가 지평선을 넘으면(시선이 지면을 만나지 않으면) 잴 수 없다 — 호출자가
    /// 고도 미상 경로로 보낸다.
    /// </summary>
    public static GroundQuad? GroundQuadOf(Bounds bounds, double eyeHeight, double pitchDown)
    {
        var near = GroundDistance(bounds, eyeHeight, pitchDown);
        var far = GroundDistance(bounds with { H = 0 }, eyeHeight, pitchDown);

        if (near is null || far is null || far.Value <= near.Value)
        {
            return null;
        }

        // 가로는 거리에 따라 달라지므로 값이 아니라 **각**으로 넘긴다 — 바닥 판은
        // 한가운데에서, 벽은 아래 모서리에서 되돌려 쓴다 (#20 §4.3)
        return new GroundQuad(
            CenterGround: (near.Value + far.Value) / 2,
            NearGround: near.Value,
            Depth: far.Value - near.Value,
            HalfWidthTangent: bounds.W * Math.Tan(HalfFovHorizontal),
            CenterTangent: (bounds.X + (bounds.W / 2) - 0.5) * 2 * Math.Tan(HalfFovHorizontal),
            EyeHeight: eyeHeight);
    }

    /// <summary>같은 거리의 앞뒤를 가르는 간격 — 겹침 판정만 흔들지 않을 만큼 작다.</summary>
    private const double DepthOrderStep = 0.05;
}
