using Noxtend.Domain.Job;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Scene.Projection;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 2D 배치 → 3D 자리 유도. Design Ref: scene-assembly §3.2
///
/// **결정적이어야 하는 것이 이 계산의 존재 이유다.** 서버가 저장하고 브라우저가 그대로
/// 그리므로, 같은 입력에 다른 답이 나오면 새로고침마다 장면이 달라진다. 난수·시계·외부
/// 호출 없이 배치·깊이·지평선만으로 계산한다 (Plan D-04 · SC-01).
/// </summary>
public sealed class SceneLayoutComposerTests
{
    private const double Horizon = 0.42;

    /// <summary>지평선 0.42 · 눈높이 1.7m — 배치와 카메라가 같은 기하를 쓴다 (#19 D-01).</summary>
    private static readonly CameraSpec Spec = new("one-point", "지면에서 1.7m", Horizon);

    // SC-01 — 같은 입력, 같은 출력. 실측 장면의 결정성은 RuinsPlazaCompositionTests 가 본다
    [Fact]
    public void Compose_IsDeterministic()
    {
        var parts = new[] { Part(depthOrder: 1, Box(0.3, 0.2, 0.3, 0.5)) };

        var first = SceneLayoutComposer.Compose(parts, Spec);
        var second = SceneLayoutComposer.Compose(parts, Spec);

        Assert.Equal(first, second);
    }

    /// <summary>배치는 합성 지시다 — 세 자리면 세 번 놓는다 (SC-02 · FR-01).</summary>
    [Fact]
    public void Compose_MakesOneInstancePerPlacement()
    {
        var parts = new[]
        {
            Part(depthOrder: 1,
                Box(0.1, 0.5, 0.2, 0.3), Box(0.4, 0.5, 0.2, 0.3), Box(0.7, 0.5, 0.2, 0.3)),
        };

        var instances = SceneLayoutComposer.Compose(parts, Spec);

        Assert.Equal(3, instances.Count);
        // 배치 번호가 그대로 인스턴스 번호다 — 편집·이름표가 이 번호로 배치를 가리킨다
        Assert.Equal([0, 1, 2], instances.Select(i => i.Ordinal));
    }

    /// <summary>발끝이 이미지에서 아래일수록 카메라에 가깝다(z 가 크다).</summary>
    [Fact]
    public void Compose_PutsLowerFootCloser()
    {
        var parts = new[]
        {
            Part(depthOrder: 1, Box(0.1, 0.70, 0.2, 0.25)),   // 발끝 0.95 — 가깝다
            Part(depthOrder: 1, Box(0.6, 0.30, 0.2, 0.25)),   // 발끝 0.55 — 멀다
        };

        var instances = SceneLayoutComposer.Compose(parts, Spec);

        Assert.True(instances[0].Z > instances[1].Z, "발끝이 아래인 쪽이 더 가까워야 한다");
    }

    /// <summary>
    /// 지면에 닿지 않는 배치(하늘·벽에 붙은 것)는 접지 배치들의 대표 거리를 빌린다.
    /// 사라지지도, 무한히 멀어지지도 않는다 (#19 §3.4).
    /// </summary>
    [Fact]
    public void Compose_BorrowsARepresentativeDistanceAboveTheHorizon()
    {
        // 눈높이 1.7m — 하향각이 얕아 화면 위쪽 배치가 지면을 만나지 않는다
        var parts = new[]
        {
            Part(depthOrder: 1, Box(0.3, 0.02, 0.4, 0.06)),   // 발끝 0.08
            Part(depthOrder: 1, Box(0.1, 0.42, 0.2, 0.56)),   // 발끝 0.98 — 최근경
        };

        var composition = SceneLayoutComposer.ComposeWithSummary(parts, Spec);

        Assert.Equal(1, composition.Summary.ElevatedCount);
        Assert.Equal(1, composition.Summary.GroundedCount);

        // 빌린 거리에 선다 — 사라지지도, 무한히 멀어지지도 않는다
        var borrowed = composition.Summary.ScaleDepthForElevated;
        Assert.Equal(-borrowed - 0.05, composition.Instances[0].Z, precision: 9);
        Assert.True(double.IsFinite(borrowed) && borrowed > 0);
    }

    /// <summary>같은 깊이는 DepthOrder 로 정해진다 — 작을수록 앞 (B-05).</summary>
    [Fact]
    public void Compose_BreaksDepthTiesWithDepthOrder()
    {
        var sameBox = Box(0.4, 0.5, 0.2, 0.3);
        var parts = new[] { Part(depthOrder: 0, sameBox), Part(depthOrder: 3, sameBox) };

        var instances = SceneLayoutComposer.Compose(parts, Spec);

        Assert.True(instances[0].Z > instances[1].Z, "DepthOrder 가 작은 쪽이 앞이어야 한다");
    }

    /// <summary>멀리 있는데 화면에서 같은 높이면 실제로는 크다 — 스케일이 depth 에 반비례.</summary>
    [Fact]
    public void Compose_ScalesDistantPlacementsUp()
    {
        var parts = new[]
        {
            Part(depthOrder: 1, Box(0.1, 0.65, 0.2, 0.3)),    // 가깝다
            Part(depthOrder: 1, Box(0.6, 0.25, 0.2, 0.3)),    // 멀다 — 같은 h
        };

        var instances = SceneLayoutComposer.Compose(parts, Spec);

        Assert.True(instances[1].Scale > instances[0].Scale);
    }

    /// <summary>
    /// SC-04 — **접지 배치만 있는 장면은 크기까지 사이클 #18 이전과 같다.**
    ///
    /// 같은 파츠가 배치 여럿을 갖더라도, 그 배치들이 전부 접지라면 깊이가 서로 달라도
    /// 크기는 같아야 한다 — 같은 GLB 이기 때문이다. 이전 공식은 깊이마다 다른 크기를
    /// 냈으므로 여기서 값이 갈린다. 자리(X·Z)는 그대로여야 한다.
    /// </summary>
    [Fact]
    public void Compose_OrdersGroundedPlacements_AndUnifiesTheirScale()
    {
        // 셋 다 지평선(0.42) 아래 — 전부 접지다
        var parts = new[]
        {
            Part(depthOrder: 1,
                Box(0.1, 0.50, 0.2, 0.30),    // 발끝 0.80 → 깊이 0.655
                Box(0.4, 0.60, 0.2, 0.30),    // 발끝 0.90 → 깊이 0.828
                Box(0.7, 0.70, 0.2, 0.30)),   // 발끝 1.00 → 깊이 1.0
        };

        var instances = SceneLayoutComposer.Compose(parts, Spec);

        // 자리는 배치마다 다르다 — 발끝이 아래일수록 가깝다
        Assert.True(instances[0].Z < instances[1].Z);
        Assert.True(instances[1].Z < instances[2].Z);

        // 크기는 하나로 모인다 — 가운데 배치의 값이 대표(중앙값)다 (#18 D-04)
        Assert.All(instances, instance =>
            Assert.Equal(RawScale(parts[0].Placements[1]), instance.Scale, precision: 9));
    }

    /// <summary>B-07 — 줄 것이 없으면 빈 목록이다. 오류가 아니다.</summary>
    [Fact]
    public void Compose_ReturnsEmptyForNoParts()
    {
        Assert.Empty(SceneLayoutComposer.Compose([], Spec));
    }

    /// <summary>이번 사이클의 계약 — 발은 바닥, 회전 없음 (§3.2).</summary>
    [Fact]
    public void Compose_PutsFeetOnTheGroundWithoutRotation()
    {
        var instances = SceneLayoutComposer.Compose(
            [Part(depthOrder: 1, Box(0.3, 0.4, 0.2, 0.4))], Spec);

        Assert.Equal(0, instances[0].Y);
        Assert.Equal(0, instances[0].RotationY);
    }

    /// <summary>기준 파츠의 중앙 raw scale이 실제 높이가 되도록 장면 전체를 균일 보정한다.</summary>
    [Fact]
    public void Compose_CalibratesTheAnchorMedianAndEverySpatialValue()
    {
        var anchorPlacements = new[]
        {
            Box(0.1, 0.3, 0.2, 0.2),
            Box(0.1, 0.4, 0.2, 0.3),
            Box(0.1, 0.5, 0.2, 0.4),
        };
        var parts = new[] { Part(depthOrder: 1, Box(0.65, 0.45, 0.2, 0.35)) };

        var raw = Assert.Single(SceneLayoutComposer.Compose(parts, Spec));
        var calibrated = Assert.Single(SceneLayoutComposer.Compose(
            parts,
            Spec,
            new SceneScaleCalibration(3, anchorPlacements)));

        var factor = calibrated.Scale / raw.Scale;
        Assert.Equal(raw.X * factor, calibrated.X, precision: 10);
        Assert.Equal(raw.Z * factor, calibrated.Z, precision: 10);

        // 기준 배치가 출력 파츠에 없어도 같은 raw 공식으로 중앙값을 계산할 수 있어야 한다
        var calibratedAnchorScales = anchorPlacements
            .Select(bounds => RawScale(bounds) * factor)
            .OrderBy(scale => scale)
            .ToArray();
        Assert.Equal(3, calibratedAnchorScales[1], precision: 10);
    }

    /// <summary>짝수 개 placement의 중앙값은 가운데 두 값의 평균이다.</summary>
    [Fact]
    public void Compose_UsesTheAverageMiddleScaleForAnEvenAnchorCount()
    {
        var anchors = new[] { Box(0.1, 0.4, 0.2, 0.2), Box(0.1, 0.5, 0.2, 0.4) };
        var part = Part(depthOrder: 0, anchors[0]);

        var raw = Assert.Single(SceneLayoutComposer.Compose([part], Spec));
        var calibrated = Assert.Single(SceneLayoutComposer.Compose(
            [part], Spec, new SceneScaleCalibration(4, anchors)));

        var expectedMedian = anchors.Select(RawScale).Average();
        Assert.Equal(4 / expectedMedian, calibrated.Scale / raw.Scale, precision: 10);
    }

    /// <summary>
    /// **균일 생성자는 세 축을 같게 만든다** (#20 §4.2 · SC-03).
    ///
    /// 사이클 #20 이전의 모든 호출이 이 생성자를 쓴다. 세 축이 갈리면 기존 장면의
    /// 모양이 조용히 바뀐다 — 동등성이 여기서 구조적으로 성립해야 한다.
    /// </summary>
    [Fact]
    public void UniformConstructor_SetsEveryAxisAlike()
    {
        var instance = new SceneInstance(Guid.NewGuid(), 0, 1, 0, -2, 0, scale: 2.5);

        Assert.Equal(2.5, instance.ScaleX);
        Assert.Equal(2.5, instance.ScaleY);
        Assert.Equal(2.5, instance.ScaleZ);
        Assert.Equal(2.5, instance.Scale);
        Assert.True(instance.IsUniform);
    }

    /// <summary>합성기가 내는 인스턴스는 아직 전부 균일하다 — 표면 분기 이전 상태.</summary>
    [Fact]
    public void Compose_StillProducesUniformInstances()
    {
        var instances = SceneLayoutComposer.Compose(
            [Part(depthOrder: 1, Box(0.3, 0.5, 0.2, 0.3))], Spec);

        Assert.All(instances, instance => Assert.True(instance.IsUniform));
    }

    // ─── 설정 ───

    private static Bounds Box(double x, double y, double w, double h) => new(x, y, w, h);

    private static SceneComposeInput Part(int? depthOrder, params Bounds[] placements)
        => new(Guid.NewGuid(), depthOrder, placements);

    /// <summary>지면 역투영으로 얻는 보정 이전 배율 — 합성기와 같은 기하다 (#19 §1).</summary>
    private static double RawScale(Bounds bounds)
    {
        var eye = SceneStaging.EyeHeight(Spec);
        var ground = PlacementGeometry.GroundDistance(bounds, eye, SceneStaging.PitchDown(Spec))
            ?? throw new InvalidOperationException("접지 배치가 아닙니다");

        return PlacementGeometry.RawScale(bounds, PlacementGeometry.EyeDistance(ground, eye));
    }
}
