using Noxtend.Domain.Job;

namespace Noxtend.Domain.Scene.Projection;

/// <summary>합성이 어떤 판단을 했는지 — 운영자가 되짚을 근거 (#18 §4.7 · FR-07).</summary>
public sealed record CompositionSummary(
    int GroundedCount,
    int ElevatedCount,
    double ScaleDepthForElevated,
    double AnchorSpread,
    /// <summary>
    /// 면을 덮는 것으로 표시돼 눕거나 선 배치 수 (#20 FR-07).
    ///
    /// **배치 기준이다** — 상한에 걸려 그리지 못한 몫이 여기 포함될 수 있다.
    /// 실제로 그린 수를 알려면 <see cref="DroppedCount"/> 와 함께 봐야 한다.
    /// </summary>
    int SurfaceCount = 0,
    /// <summary>
    /// 크기 기준 물체가 표면으로 표시됐는가 — 경고다 (설계 §6).
    ///
    /// 표면의 `bounds.H` 는 높이가 아니라 누운 면의 세로 범위라, 그것으로 보정 계수를
    /// 뽑으면 장면 전체가 어긋난다. 막지는 않는다 — 운영자가 표시를 고칠 수 있게 알린다.
    /// </summary>
    bool AnchorIsSurface = false,
    /// <summary>
    /// 인스턴스 상한에 걸려 버려진 배치 수 (FR-06).
    ///
    /// 접지/고도 수는 **배치**에 대한 판정이라 상한과 무관하게 전부 센다. 그래서 인스턴스가
    /// 배치보다 적을 수 있는데, 그 차이를 설명하는 것이 이 값이다.
    /// </summary>
    int DroppedCount = 0,
    /// <summary>
    /// `ground` 로 표시됐으나 GLB 는 서 있는 형태인 파츠 수 — 부차 신호 (설계 §4.1).
    ///
    /// 표시를 뒤집지 않는다. 분해가 준 한 비트가 정본이고 기하는 그것을 못 이긴다 —
    /// 실측에서 같은 성격의 파츠 3개 중 종횡비로 걸린 것은 1개뿐이었다. 다만 어긋남이
    /// 잦으면 표시나 3D 생성 어느 한쪽을 의심할 근거가 된다.
    /// </summary>
    int SurfaceShapeMismatch = 0);

/// <summary>
/// 배치마다 어느 투영을 쓸지 고르고, 고도 미상 배치가 빌릴 대표 거리를 정한다.
///
/// Design Ref: background-placement-projection(#19) §3.4 · background-scale-calibration(#18) §4.2
///
/// **판정은 기하가 한다.** 이전에는 "깊이가 임계 0.05 이상인가" 라는 대리 지표를 썼지만,
/// 이제는 시선이 지면을 만나는가를 직접 묻는다 — 축각과 하향각의 합이 수평선 아래인가.
/// </summary>
public sealed class PlacementProjector
{
    /// <summary>접지 배치가 하나도 없을 때 빌릴 거리 — 눈높이의 배수.</summary>
    public const double NeutralGroundInEyeHeights = 10;

    private readonly double eyeHeight;
    private readonly double pitchDown;
    private readonly GroundedProjection grounded;
    private readonly ElevatedProjection elevated;

    public PlacementProjector(
        double eyeHeight, double pitchDown, IEnumerable<Bounds> allPlacements)
    {
        this.eyeHeight = eyeHeight;
        this.pitchDown = pitchDown;

        // 접지 배치들의 거리 중앙값이 고도 미상 배치의 대표 거리가 된다.
        // 평균이 아니라 중앙값인 이유는 배치 하나의 검출 오차가 장면 전체를 흔들지
        // 않게 하기 위함이다 (사이클 #1 의 기준 물체 중앙값과 같은 이유)
        var reachable = allPlacements
            .Select(bounds => PlacementGeometry.GroundDistance(bounds, eyeHeight, pitchDown))
            .Where(distance => distance is not null)
            .Select(distance => distance!.Value)
            .ToArray();

        ScaleDepthForElevated = reachable.Length == 0
            ? eyeHeight * NeutralGroundInEyeHeights
            : PartScaleResolver.Median(reachable);

        grounded = new GroundedProjection(eyeHeight, pitchDown);
        elevated = new ElevatedProjection(eyeHeight, ScaleDepthForElevated);
    }

    /// <summary>고도 미상 배치가 빌리는 지면 거리.</summary>
    public double ScaleDepthForElevated { get; }

    /// <summary>시선이 지면을 만나는가 — 그 배치가 지면에 서 있다고 볼 수 있는가.</summary>
    public bool IsGrounded(Bounds bounds)
        => PlacementGeometry.GroundDistance(bounds, eyeHeight, pitchDown) is not null;

    /// <summary>
    /// 배치에 맞는 투영. **세지 않는다** — 집계를 겸하면 호출 횟수가 결과를 바꾸고,
    /// 기준 물체를 재느라 한 번 더 부르는 것만으로 요약이 어긋난다.
    /// </summary>
    public IPlacementProjection For(Bounds bounds) => IsGrounded(bounds) ? grounded : elevated;
}
