using Noxtend.Domain.Job;
using Noxtend.Domain.Scene.Projection;

namespace Noxtend.Domain.Scene;

/// <summary>유도 입력 — GLB 가 있는 파츠만 넘긴다. 거르는 것은 호출자(핸들러) 몫이다.</summary>
public sealed record SceneComposeInput(
    Guid PartId,
    int? DepthOrder,
    IReadOnlyList<Bounds> Placements,
    /// <summary>면을 덮는 파츠인가 (#20 §4.1). 기본은 낱개 물건이다.</summary>
    PartSurface Surface = PartSurface.None,
    /// <summary>
    /// GLB 의 축별 크기. 모르면 <c>null</c> — 서 있는 물체로 다룬다.
    ///
    /// 바닥을 덮는 파츠(포장·수면)는 `bounds.H` 가 높이가 아니라 **누운 지면이 화면에서
    /// 차지한 세로 범위**라, 높이로 읽으면 판이 서 버린다 (실측 포장 석재 11.81m).
    /// </summary>
    MeshExtents? Extents = null);

/// <summary>합성 결과 — 인스턴스와 그 유도가 내린 판단(§4.7).</summary>
public sealed record SceneComposition(
    IReadOnlyList<SceneInstance> Instances,
    CompositionSummary Summary);

/// <summary>
/// 2D 배치 → 3D 자리 유도.
///
/// Design Ref: scene-assembly §3.2 · background-scale-calibration(#18) §5
///
/// **결정적 계산이다** (scene-assembly D-04 · SC-01). 서버가 저장하고 브라우저가 그대로
/// 그리므로 난수·시계·외부 호출이 섞이면 새로고침마다 장면이 달라진다.
///
/// 좌표계: 바닥 y=0 · +x 오른쪽 · +z 카메라 쪽.
///
/// scene-assembly 는 "상수 넷만 조정한다 — 공식 구조는 바꾸지 않는다" 고 못박았다.
/// **사이클 #18 이 그 제약을 의도적으로 푼다.** 상수 조정으로는 닿을 수 없는 결함이
/// 실측됐기 때문이다 — 같은 파츠의 배치가 20.7배 크기로 벌어졌고, 그 원인은 상수가 아니라
/// "발끝이 곧 접지점" 이라는 전제였다. 이제 배치마다 투영을 고르고(§4.2), 같은 파츠는
/// 하나의 월드 배율을 공유한다(§4.4).
/// </summary>
public static class SceneLayoutComposer
{
    public static IReadOnlyList<SceneInstance> Compose(
        IReadOnlyList<SceneComposeInput> parts,
        CameraSpec? camera,
        SceneScaleCalibration? calibration = null)
        => ComposeWithSummary(parts, camera, calibration).Instances;

    /// <summary>인스턴스와 함께 유도가 내린 판단을 돌려준다 (FR-07).</summary>
    public static SceneComposition ComposeWithSummary(
        IReadOnlyList<SceneComposeInput> parts,
        CameraSpec? camera,
        SceneScaleCalibration? calibration = null)
    {
        // **배치와 카메라가 같은 기하를 쓴다** (#19 D-01). 눈높이·하향각의 출처가
        // 하나여야 되돌려 투영했을 때 배치가 원본에서 있던 화면 자리로 온다
        var eyeHeight = SceneStaging.EyeHeight(camera);
        var pitchDown = SceneStaging.PitchDown(camera);

        var allPlacements = parts.SelectMany(part => part.Placements).ToArray();
        var projector = new PlacementProjector(eyeHeight, pitchDown, allPlacements);

        // 보정 계수를 **먼저** 구한다. 슬래브 두께 상한을 화면 비율이 아니라 미터로
        // 걸어야 하는데(<see cref="SlabCeiling"/>), 미터를 알려면 계수가 있어야 한다
        var factor = CalibrationFactor(calibration, projector);
        var slabCeiling = SlabCeiling(calibration, factor);

        // 1단계 — 배치마다 자리와 raw 배율. 파츠별로 모아 두어야 2단계에서 대표를 뽑는다
        var projected = parts
            .Select(part => part.Placements
                .Select(bounds =>
                    Project(projector, part, bounds, eyeHeight, pitchDown, slabCeiling))
                .ToArray())
            .ToArray();

        // 2단계 — 같은 파츠는 하나의 월드 배율을 공유한다 (#18 D-04).
        // **표면은 제외한다** (#20 §4.4): 배치마다 사각형이 다르고 그 사각형이 곧 크기라,
        // 통일하면 면을 못 덮는다. 같은 포장을 크기가 다른 두 구역에 까는 것은 정상이다
        var resolved = parts
            .Select((part, index) => part.Surface != PartSurface.None
                ? []
                : (IReadOnlyList<double>)PartScaleResolver.Resolve(
                    [.. projected[index].Select(placement => placement.RawScale)]))
            .ToArray();

        // 3단계 — 기준 물체 높이로 장면 전체를 균일 보정 (사이클 #1 계약 유지).
        // 계수는 위에서 이미 구했다 — 두께 상한이 그것을 먼저 필요로 했다
        var instances = new List<SceneInstance>();
        for (var partIndex = 0; partIndex < parts.Count && instances.Count < MaxInstances; partIndex++)
        {
            for (var ordinal = 0;
                ordinal < projected[partIndex].Length && instances.Count < MaxInstances;
                ordinal++)
            {
                var placement = projected[partIndex][ordinal];

                // 발은 바닥(Y=0), 회전 없음 — GLB 정면 = 원본 카메라 방향 (생성 계약).
                // 낱개는 대표 배율을 세 축에, 표면은 축별 값을 그대로 쓴다.
                // 표면은 대표 배율 자체를 뽑지 않으므로(2단계) 그 배열을 읽지 않는다
                var uniform = parts[partIndex].Surface == PartSurface.None;

                instances.Add(uniform
                    ? new SceneInstance(
                        parts[partIndex].PartId, ordinal,
                        placement.X * factor, 0, placement.Z * factor,
                        rotationY: 0,
                        resolved[partIndex][ordinal] * factor)
                    : new SceneInstance(
                        parts[partIndex].PartId, ordinal,
                        placement.X * factor, 0, placement.Z * factor,
                        rotationY: 0,
                        placement.ScaleX * factor,
                        placement.ScaleY * factor,
                        placement.ScaleZ * factor));
            }
        }

        // 요약은 배치를 다시 세서 만든다 — 투영을 몇 번 불렀는지와 무관해야 한다.
        // 접지/고도 판정은 **배치**에 대한 판단이라 상한과 무관하게 전부 센다.
        // 상한이 버린 몫은 따로 세어 "왜 인스턴스가 배치보다 적은가" 를 설명한다
        var groundedCount = allPlacements.Count(projector.IsGrounded);

        return new SceneComposition(
            instances,
            new CompositionSummary(
                groundedCount,
                allPlacements.Length - groundedCount,
                projector.ScaleDepthForElevated,
                AnchorSpread(calibration, projector),
                parts.Where(part => part.Surface != PartSurface.None)
                    .Sum(part => part.Placements.Count),
                calibration?.AnchorIsSurface ?? false,
                allPlacements.Length - instances.Count,
                ShapeMismatchCount(parts)));
    }

    /// <summary>
    /// 배치 하나를 옮긴다. **바닥을 덮는 파츠는 크기 규칙이 다르다.**
    ///
    /// 서 있는 물체는 `bounds.H` 가 곧 높이라 역투영 거리에서 바로 실제 높이가 나온다.
    /// 바닥 판은 그 H 가 누운 지면의 세로 범위이므로, 위·아래 모서리를 각각 지면으로
    /// 되돌려 그 사이 거리(바닥 깊이)를 재고, 정규화가 높이로 나누는 것을 되돌린다.
    /// </summary>
    private static ProjectedPlacement Project(
        PlacementProjector projector,
        SceneComposeInput part,
        Bounds bounds,
        double eyeHeight,
        double pitchDown,
        double slabCeiling)
    {
        var placed = projector.For(bounds).Project(bounds, part.DepthOrder ?? 0);

        // 낱개 물건은 사이클 #19 경로 그대로다 (SC-03)
        if (part.Surface == PartSurface.None)
        {
            return placed;
        }

        var quad = PlacementGeometry.GroundQuadOf(bounds, eyeHeight, pitchDown);

        // 위 모서리가 지평선을 넘었거나(면을 잴 수 없다) 사각형이 뭉개졌으면(폭·깊이 0)
        // 낱개 경로에 맡긴다 — 0 을 배율에 곱하면 인스턴스가 사라진다 (설계 §6)
        if (quad is null || quad.Value.Width <= DegenerateQuad ||
            quad.Value.Depth <= DegenerateQuad)
        {
            return placed;
        }

        return part.Surface == PartSurface.Ground
            ? Lay(placed, quad.Value, part.Extents, part.DepthOrder ?? 0, slabCeiling)
            : Stand(placed, quad.Value, part.Extents, part.DepthOrder ?? 0);
    }

    /// <summary>
    /// 지면 표면을 눕힌다 — 배치 사각형이 곧 바닥 면이다 (#20 §4.3).
    ///
    /// 두께는 메시의 자연 비율을 따른다. 형태를 모르면 얇은 판으로 가정한다 (설계 §6).
    /// </summary>
    private static ProjectedPlacement Lay(
        ProjectedPlacement placed, GroundQuad quad, MeshExtents? extents,
        int depthOrder, double slabCeiling)
    {
        // **두께에 상한을 둔다.** 실측 포장 GLB 는 0.98 : 0.82 : 1.90 이라 비율이 0.43 이고,
        // 18m 폭에 그대로 곱하면 7.88m 짜리 슬래브가 선다. 3D 생성이 바닥 이미지에서
        // 두껍게 뽑은 결과라 비율을 그대로 믿을 수 없다
        var ratio = Math.Min(
            extents?.HeightPerFootprint ?? DefaultSlabThickness, MaxSlabThickness);

        // **비율만으로는 부족하다.** 폭에 비례하므로 광장이 넓을수록 슬래브가 두꺼워진다 —
        // 실측 18m 광장에서 1.45m 가 나와 1.8m 기준 물체의 81% 를 삼켰다. 절대 두께를
        // 함께 걸어야 "포장이 바닥을 덮고 구조물이 그 위에 선다" 가 성립한다
        var thickness = Math.Min(quad.Width * ratio, slabCeiling);

        return placed with
        {
            X = quad.CenterX,
            Z = PlacementGeometry.Z(quad.CenterGround, depthOrder),
            // 뷰어가 **높이로** 균일 정규화한 뒤 축별 배율을 곱하므로(scene-assembly §3.2),
            // 원하는 월드 폭을 얻으려면 그 정규화를 되돌려야 한다 — 안 그러면 실측
            // 포장에서 깊이가 132% 넘쳐 배치 사각형을 벗어난다
            ScaleX = quad.Width * NormalizationInverse(extents?.X, extents?.Y),
            ScaleY = thickness,
            ScaleZ = quad.Depth * NormalizationInverse(extents?.Z, extents?.Y),
        };
    }

    /// <summary>
    /// 뷰어의 높이 정규화를 되돌리는 계수.
    ///
    /// 뷰어는 `1 / 메시높이` 로 **균일** 정규화한 뒤 축별 배율을 곱한다. 그래서 월드 폭은
    /// `배율X × 메시X ÷ 메시높이` 가 된다 — 원하는 폭을 그대로 넣으려면 그 비를 나눠 둬야 한다.
    /// 형태를 모르면 1 (보정 없음).
    /// </summary>
    private static double NormalizationInverse(double? axis, double? height)
        => axis is > 0 && height is > 0 ? height.Value / axis.Value : 1;

    /// <summary>
    /// 수직 표면을 세운다 — 배치 사각형이 곧 그 벽이다.
    ///
    /// 폭은 면의 가로를, 높이는 배치가 화면에서 차지한 세로를 그 거리에서 되돌린 값을 쓴다.
    /// 깊이는 벽의 두께이므로 메시 비율을 따른다.
    /// </summary>
    private static ProjectedPlacement Stand(
        ProjectedPlacement placed, GroundQuad quad, MeshExtents? extents, int depthOrder)
    {
        // **아래 모서리 거리에서 전부 잰다** (설계 §4.3). 벽은 발치가 지면과 만나는
        // 자리에 서므로 자리도 크기도 그 거리의 것이다 — 자리만 옮기고 폭을 한가운데
        // 거리로 두면 실측 벽에서 폭이 1.17배 부풀어 자리와 크기가 어긋난다
        var foot = quad.NearGround;

        // 깊이는 벽의 **두께 비율**이다 — 높이 비율(Y/바닥폭)을 쓰면 벽이 덩어리가 된다.
        // 상한은 바닥 판과 따로 둔다: 바닥의 0.08 은 "기준 물체가 잠기지 않는 선" 이라는
        // 바닥 전용 근거에서 나온 값이고, 서 있는 면에는 그 근거가 없다
        var thickness = Math.Min(
            extents?.ThicknessRatio ?? DefaultSlabThickness, MaxWallThicknessRatio);
        var width = quad.WidthAt(foot);

        return placed with
        {
            X = quad.CenterXAt(foot),
            Z = PlacementGeometry.Z(foot, depthOrder),
            ScaleX = width * NormalizationInverse(extents?.X, extents?.Y),
            ScaleY = placed.RawScale,
            ScaleZ = width * thickness * NormalizationInverse(extents?.Z, extents?.Y),
        };
    }

    /// <summary>
    /// 한 장면이 낼 수 있는 인스턴스 상한 (FR-06).
    ///
    /// **방어선이다.** 표면은 배치 하나가 인스턴스 하나이므로 정상 경로에서는 닿지 않는다 —
    /// 실측 최대가 84개다. 분해가 배치를 폭주시켰을 때 뷰어가 멈추는 것을 막는다.
    /// </summary>
    public const int MaxInstances = 400;

    /// <summary>
    /// 표시와 형태가 어긋난 파츠 수 — 설계 §4.1 의 **부차 신호**.
    ///
    /// 분해가 준 한 비트가 정본이고 기하는 그것을 뒤집지 않는다. 다만 `ground` 라 했는데
    /// GLB 가 명백히 서 있는 형태면 운영자가 의심할 근거는 된다 — 막지 않고 알린다.
    /// 형태를 모르는(GLB 를 못 읽은) 파츠는 세지 않는다. 모름은 어긋남이 아니다.
    ///
    /// 역방향(`vertical` 인데 납작함)은 세지 않는다 — 설계 §4.1 문언이 `ground` 만 든다.
    /// </summary>
    private static int ShapeMismatchCount(IReadOnlyList<SceneComposeInput> parts)
        => parts.Count(part =>
            part.Surface == PartSurface.Ground &&
            part.Extents is { } extents &&
            !extents.IsGroundPlane);

    /// <summary>사각형이 뭉개진 것으로 볼 하한 — 이보다 작으면 면이 아니다 (미터).</summary>
    private const double DegenerateQuad = 1e-3;

    /// <summary>형태를 모를 때 가정하는 두께 비율 — 얇은 판 (설계 §6).</summary>
    private const double DefaultSlabThickness = 0.05;

    /// <summary>
    /// 바닥 판 두께의 상한 — 바닥 폭 대비.
    ///
    /// **이것만으로는 부족하다.** 폭에 비례하므로 광장이 넓어질수록 슬래브가 두꺼워진다 —
    /// 18m 광장에서 1.45m, 40m 면 3.2m 다. <see cref="SlabCeiling"/> 이 절대 두께를
    /// 함께 걸어야 그 위에 선 구조물이 잠기지 않는다.
    /// </summary>
    private const double MaxSlabThickness = 0.08;

    /// <summary>
    /// 벽 두께의 상한 — 면 폭 대비.
    ///
    /// 바닥 판과 따로 둔다. 0.08 은 바닥 전용 근거에서 나온 값이라 서 있는 면에 그대로
    /// 쓰면 실측 벽 10 : 8 : 1 의 실제 두께비 0.10 이 근거 없이 눌린다.
    /// </summary>
    private const double MaxWallThicknessRatio = 0.15;

    /// <summary>
    /// 바닥 판 두께의 절대 상한 — 기준 물체 높이 대비.
    ///
    /// 0.2 는 1.8m 기준에서 0.36m 다. 계단 한 단이나 단이 있는 포장의 실제 두께를 담으면서
    /// 그 위에 선 구조물을 삼키지 않는다. 비율 상한만 걸었을 때 실측 광장에서 1.45m 가
    /// 나와 1.8m 기준 물체의 81% 가 잠겼다 — 그 결함을 막는 값이다.
    /// </summary>
    private const double SlabThicknessInAnchorHeights = 0.2;

    /// <summary>
    /// 보정 이전 단위로 환산한 슬래브 두께 상한.
    ///
    /// 기준 물체가 없으면 장면 전체가 임의 단위라 절대 두께를 말할 근거가 없다 —
    /// 그 경우 비율 상한만 남긴다.
    ///
    /// **표면이 있으면서 기준이 없는 조합은 나오지 않는다.** 분석 스키마가
    /// `scaleReference.heightMeters` 를 required 로 두고(`SeedPrompts` 분석 스키마),
    /// 핸들러가 기준 물체를 못 찾으면 `SceneIncomplete` 로 막는다. 그래도 이 분기를
    /// 남기는 것은 보정 없는 단위 테스트와 옛 작업 때문이다.
    /// </summary>
    private static double SlabCeiling(SceneScaleCalibration? calibration, double factor)
        => calibration is null || factor <= 0
            ? double.PositiveInfinity
            : calibration.HeightMeters * SlabThicknessInAnchorHeights / factor;

    /// <summary>
    /// 기준 물체 배치의 raw 배율 편차 — 보정 계수를 얼마나 믿을 수 있는지 (D-05).
    ///
    /// **통일 이전 raw 로 잰다.** 통일 뒤에는 편차가 사라져 신뢰도를 읽을 수 없다.
    /// 실측에서 같은 "청색 발광 단말기" 두 배치가 0.41 vs 1.61 — 3.9배였다.
    /// </summary>
    private static double AnchorSpread(
        SceneScaleCalibration? calibration, PlacementProjector projector)
        => calibration is null
            ? 1
            : PartScaleResolver.Spread(AnchorScales(calibration, projector));

    /// <summary>기준 물체 대표 배율을 실제 높이에 맞추는 균일 계수.</summary>
    private static double CalibrationFactor(
        SceneScaleCalibration? calibration, PlacementProjector projector)
    {
        if (calibration is null)
        {
            return 1;
        }

        if (!double.IsFinite(calibration.HeightMeters) || calibration.HeightMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(calibration), "기준 높이는 유한한 양수여야 합니다");
        }

        if (calibration.AnchorPlacements.Count == 0)
        {
            throw new ArgumentException("기준 물체 배치가 없습니다", nameof(calibration));
        }

        // 기준 물체도 파츠다 — 다른 파츠와 같은 규칙(D-04)을 거친 배율로 계수를 잡는다
        var median = PartScaleResolver.Median(
            PartScaleResolver.Resolve(AnchorScales(calibration, projector)));

        if (median <= 0)
        {
            throw new ArgumentException("기준 물체 scale을 계산할 수 없습니다", nameof(calibration));
        }

        return calibration.HeightMeters / median;
    }

    /// <summary>
    /// 기준 물체의 배율도 같은 접지 판정을 거친다 — 기준만 옛 규칙을 쓰면 계수와 본체가
    /// 서로 다른 척도 위에 서게 된다.
    /// </summary>
    private static IReadOnlyList<double> AnchorScales(
        SceneScaleCalibration calibration, PlacementProjector projector)
        => [.. calibration.AnchorPlacements.Select(bounds =>
            projector.For(bounds).Project(bounds, 0).RawScale)];
}
