using Noxtend.Domain.Scene;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 보정 명령 allowlist 와 적용 규칙. Design Ref: background-similarity-tuning §6 · D-06
///
/// **임의 property path 를 받지 않는다** — 모델이 제안한 보정은 discriminated command 로만
/// 표현되고, 1회 범위·누적 한계·존재하는 대상·중복 금지를 모두 통과해야 적용된다.
/// 적용은 순수 함수다: 입력 revision 은 바뀌지 않고 새 값 집합이 나온다.
/// </summary>
public sealed class SceneAdjusterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    // ─── 장면 배율 ───

    /// <summary>장면 배율은 위치와 크기를 함께 키운다 — 크기만 키우면 간격이 어긋난다.</summary>
    [Fact]
    public void ScaleScene_ScalesPositionsAndSizesTogether()
    {
        var baseline = Layout(Instance(x: 2, z: -4, scale: 1.5));

        var result = SceneAdjuster.Apply(baseline, baseline, [new ScaleScene(1.2)]);

        var instance = result.Instances.Single();
        Assert.Equal(2.4, instance.X, precision: 6);
        Assert.Equal(-4.8, instance.Z, precision: 6);
        Assert.Equal(1.8, instance.Scale, precision: 6);
    }

    [Theory]
    [InlineData(0.79)]
    [InlineData(1.26)]
    public void ScaleScene_RejectsAFactorOutsideTheSingleShotRange(double factor)
    {
        var baseline = Layout(Instance());

        Assert.Throws<SceneAdjustmentException>(() =>
            SceneAdjuster.Apply(baseline, baseline, [new ScaleScene(factor)]));
    }

    /// <summary>누적 한계 — 원점 대비 2배를 넘는 결과는 1회 범위 안이라도 거절한다 (§6).</summary>
    [Fact]
    public void ScaleScene_RejectsWhenTheCumulativeLimitAgainstTheOriginIsExceeded()
    {
        var partId = Guid.NewGuid();
        var origin = Layout(new SceneInstance(partId, 0, 0, 0, -3, 0, scale: 1.0));
        // 이전 후보들로 이미 1.7배까지 커진 baseline — 인스턴스 정체는 revision 을 넘어 유지된다
        var baseline = Layout(new SceneInstance(partId, 0, 0, 0, -3, 0, scale: 1.7));

        Assert.Throws<SceneAdjustmentException>(() =>
            SceneAdjuster.Apply(baseline, origin, [new ScaleScene(1.25)]));   // 2.125 > 2.0

        var ok = SceneAdjuster.Apply(baseline, origin, [new ScaleScene(1.15)]);   // 1.955 ≤ 2.0
        Assert.Equal(1.955, ok.Instances.Single().Scale, precision: 6);
    }

    // ─── 인스턴스 이동 ───

    [Fact]
    public void MoveInstance_ShiftsOnlyTheTarget()
    {
        var target = Instance(x: 1, z: -3);
        var other = Instance(x: 5, z: -5);
        var baseline = Layout(target, other);

        var result = SceneAdjuster.Apply(baseline, baseline,
            [new MoveInstance(target.PartId, target.Ordinal, 0.4, -0.3)]);

        var moved = result.Instances.Single(i => i.PartId == target.PartId);
        Assert.Equal(1.4, moved.X, precision: 6);
        Assert.Equal(-3.3, moved.Z, precision: 6);
        Assert.Equal(5, result.Instances.Single(i => i.PartId == other.PartId).X);
    }

    /// <summary>이동 한도는 크기에 비례한다 — 큰 물체는 0.5m 로는 티도 안 난다 (§6).</summary>
    [Fact]
    public void MoveInstance_AllowsLargerDeltasForLargerInstances()
    {
        var big = Instance(scale: 4.0);   // 한도 max(0.5, 1.0) = 1.0
        var baseline = Layout(big);

        var ok = SceneAdjuster.Apply(baseline, baseline,
            [new MoveInstance(big.PartId, big.Ordinal, 1.0, 0)]);
        Assert.Equal(1.0, ok.Instances.Single().X - big.X, precision: 6);

        Assert.Throws<SceneAdjustmentException>(() => SceneAdjuster.Apply(baseline, baseline,
            [new MoveInstance(big.PartId, big.Ordinal, 1.01, 0)]));
    }

    [Fact]
    public void MoveInstance_RejectsAnUnknownTarget()
    {
        var baseline = Layout(Instance());

        Assert.Throws<SceneAdjustmentException>(() => SceneAdjuster.Apply(baseline, baseline,
            [new MoveInstance(Guid.NewGuid(), 0, 0.1, 0)]));
    }

    // ─── 인스턴스 회전·배율 ───

    /// <summary>도 단위 명령 → 라디안 저장 — R3F rotation 이 라디안이라 저장도 라디안이다.</summary>
    [Fact]
    public void RotateInstance_ConvertsDegreesToRadians()
    {
        var target = Instance();
        var baseline = Layout(target);

        var result = SceneAdjuster.Apply(baseline, baseline,
            [new RotateInstance(target.PartId, target.Ordinal, 15)]);

        Assert.Equal(Math.PI / 12, result.Instances.Single().RotationY, precision: 6);

        Assert.Throws<SceneAdjustmentException>(() => SceneAdjuster.Apply(baseline, baseline,
            [new RotateInstance(target.PartId, target.Ordinal, 15.1)]));
    }

    [Fact]
    public void ScaleInstance_EnforcesTheCumulativeLimitPerInstance()
    {
        var partId = Guid.NewGuid();
        var origin = Layout(new SceneInstance(partId, 0, 0, 0, -3, 0, scale: 1.0));

        // 이전 후보들로 이미 0.55배까지 줄어든 인스턴스 — 0.85배 더 줄이면 0.4675 < 0.5
        var target = new SceneInstance(partId, 0, 0, 0, -3, 0, scale: 0.55);
        var baseline = Layout(target);

        Assert.Throws<SceneAdjustmentException>(() => SceneAdjuster.Apply(baseline, origin,
            [new ScaleInstance(target.PartId, target.Ordinal, 0.85)]));
    }

    // ─── 카메라·조명 ───

    /// <summary>yaw 는 응시점을 도는 회전 — 거리와 높이는 그대로여야 한다.</summary>
    [Fact]
    public void AdjustCamera_YawOrbitsAroundTheTargetKeepingDistance()
    {
        var baseline = Layout(Instance());
        var before = baseline.Camera;
        var beforeDistance = Distance(before);

        var result = SceneAdjuster.Apply(baseline, baseline,
            [new AdjustCamera(YawDeltaDegrees: 10, PitchDeltaDegrees: 0, DistanceFactor: 1.0, FovDeltaDegrees: 0)]);

        Assert.Equal(beforeDistance, Distance(result.Camera), precision: 6);
        Assert.Equal(before.Position.Y, result.Camera.Position.Y, precision: 6);
        Assert.NotEqual(before.Position.X, result.Camera.Position.X);
    }

    [Fact]
    public void AdjustCamera_ScalesDistanceAndShiftsFov()
    {
        var baseline = Layout(Instance());
        var beforeDistance = Distance(baseline.Camera);

        var result = SceneAdjuster.Apply(baseline, baseline,
            [new AdjustCamera(0, 0, DistanceFactor: 0.9, FovDeltaDegrees: 5)]);

        Assert.Equal(beforeDistance * 0.9, Distance(result.Camera), precision: 6);
        Assert.Equal(baseline.Camera.FieldOfViewDegrees + 5, result.Camera.FieldOfViewDegrees);

        Assert.Throws<SceneAdjustmentException>(() => SceneAdjuster.Apply(baseline, baseline,
            [new AdjustCamera(11, 0, 1.0, 0)]));
        Assert.Throws<SceneAdjustmentException>(() => SceneAdjuster.Apply(baseline, baseline,
            [new AdjustCamera(0, 0, 0.89, 0)]));
    }

    [Fact]
    public void AdjustLight_ShiftsAnglesAndScalesIntensity()
    {
        var baseline = Layout(Instance());   // 기본 조명 az 225 · el 40 · key 1.3

        var result = SceneAdjuster.Apply(baseline, baseline,
            [new AdjustLight(AzimuthDeltaDegrees: 15, ElevationDeltaDegrees: -10, IntensityFactor: 1.2)]);

        Assert.Equal(240, result.Light.AzimuthDegrees, precision: 6);
        Assert.Equal(30, result.Light.ElevationDegrees, precision: 6);
        Assert.Equal(1.3 * 1.2, result.Light.KeyIntensity, precision: 6);

        Assert.Throws<SceneAdjustmentException>(() => SceneAdjuster.Apply(baseline, baseline,
            [new AdjustLight(16, 0, 1.0)]));
    }

    // ─── 공통 규칙 ───

    /// <summary>같은 대상·같은 속성 두 번 금지 — 순서에 따라 한도를 우회할 수 있다 (§6).</summary>
    [Fact]
    public void Apply_RejectsDuplicateCommandsOnTheSameTargetProperty()
    {
        var target = Instance();
        var baseline = Layout(target);

        Assert.Throws<SceneAdjustmentException>(() => SceneAdjuster.Apply(baseline, baseline,
        [
            new MoveInstance(target.PartId, target.Ordinal, 0.3, 0),
            new MoveInstance(target.PartId, target.Ordinal, 0.3, 0),
        ]));

        Assert.Throws<SceneAdjustmentException>(() => SceneAdjuster.Apply(baseline, baseline,
            [new ScaleScene(1.1), new ScaleScene(1.1)]));
    }

    [Fact]
    public void Apply_RejectsEmptyOversizedOrNonFiniteCommandSets()
    {
        var target = Instance();
        var baseline = Layout(target);

        Assert.Throws<SceneAdjustmentException>(() =>
            SceneAdjuster.Apply(baseline, baseline, []));

        // 한 후보의 명령 상한 24 (§13)
        var tooMany = Enumerable.Range(0, 25)
            .Select(SceneAdjustmentCommand (i) => new RotateInstance(Guid.NewGuid(), i, 1))
            .ToList();
        Assert.Throws<SceneAdjustmentException>(() =>
            SceneAdjuster.Apply(baseline, baseline, tooMany));

        Assert.Throws<SceneAdjustmentException>(() => SceneAdjuster.Apply(baseline, baseline,
            [new MoveInstance(target.PartId, target.Ordinal, double.NaN, 0)]));
    }

    /// <summary>적용은 순수 함수 — 입력 revision 의 값은 그대로다 (§6).</summary>
    [Fact]
    public void Apply_LeavesTheBaselineUntouched()
    {
        var target = Instance(x: 1, scale: 1.5);
        var baseline = Layout(target);

        SceneAdjuster.Apply(baseline, baseline,
            [new ScaleScene(1.2), new MoveInstance(target.PartId, target.Ordinal, 0.4, 0)]);

        var after = baseline.Instances.Single();
        Assert.Equal(1, after.X);
        Assert.Equal(1.5, after.Scale);
    }

    // ─── 설정 ───

    private static double Distance(SceneCamera camera)
    {
        var dx = camera.Position.X - camera.Target.X;
        var dy = camera.Position.Y - camera.Target.Y;
        var dz = camera.Position.Z - camera.Target.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static SceneLayout Layout(params SceneInstance[] instances)
        => SceneLayout.ComposeActive(
            Guid.NewGuid(), revision: 1, instances,
            // 이 검사들은 서명을 비교하지 않는다 — 임의 값이면 충분하다
            $"SIGNATURE-{Guid.NewGuid():n}",
            sourceMeshCount: instances.Length,
            SceneStaging.ComposeCamera(null, []),
            SceneStaging.ComposeLight(null),
            Now);

    private static SceneInstance Instance(double x = 0, double z = -3, double scale = 1.0)
        => new(Guid.NewGuid(), 0, x, y: 0, z, rotationY: 0, scale);

    /// <summary>
    /// **보정이 표면을 무너뜨리면 안 된다** (background-surface-parts #20 §4.2).
    ///
    /// 균일 생성자로 인스턴스를 재구성하면 `Scale => ScaleY` 라 표면
    /// (18.17, 7.88, 29.91) 이 (7.88, 7.88, 7.88) 이 된다 — 보정을 한 번만 돌아도
    /// 포장이 다시 정육면체 바위가 된다. 실측 갭 분석이 잡은 회귀다.
    /// </summary>
    [Fact]
    public void Adjust_PreservesPerAxisScale()
    {
        var partId = Guid.NewGuid();
        var surface = new SceneInstance(partId, 0, 1, 0, -4, 0,
            scaleX: 18.17, scaleY: 7.88, scaleZ: 29.91);
        var layout = Layout([surface]);

        var moved = SceneAdjuster.Apply(layout, layout,
            [new MoveInstance(partId, 0, 1.0, 0.5)]).Instances.Single();

        Assert.Equal(18.17, moved.ScaleX, precision: 9);
        Assert.Equal(7.88, moved.ScaleY, precision: 9);
        Assert.Equal(29.91, moved.ScaleZ, precision: 9);
        Assert.False(moved.IsUniform);
    }

    /// <summary>배율 보정은 세 축에 같은 배수를 곱한다 — 표면의 비율은 사각형이 정한 것이다.</summary>
    [Fact]
    public void Scale_MultipliesEveryAxisAlike()
    {
        var partId = Guid.NewGuid();
        var surface = new SceneInstance(partId, 0, 1, 0, -4, 0,
            scaleX: 10, scaleY: 1, scaleZ: 20);
        var layout = Layout([surface]);

        var scaled = SceneAdjuster.Apply(layout, layout,
            [new ScaleInstance(partId, 0, 1.2)]).Instances.Single();

        Assert.Equal(12, scaled.ScaleX, precision: 9);
        Assert.Equal(1.2, scaled.ScaleY, precision: 9);
        Assert.Equal(24, scaled.ScaleZ, precision: 9);
    }

    private static SceneLayout Layout(IReadOnlyList<SceneInstance> instances)
        => SceneLayout.ComposeActive(
            Guid.NewGuid(), 1, instances, "sig", instances.Count,
            SceneStaging.ComposeCamera(null, []), SceneStaging.ComposeLight(null),
            new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero));
}
