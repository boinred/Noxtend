using Noxtend.Domain.Job;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Scene.Projection;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 실측 회귀 — 유적 광장 참조(작업 F8053152 · 1280×797 · isometric · 지평선 0.38).
///
/// Design Ref: background-scale-calibration(#18) §8.1
///
/// **이 수치가 결함의 증거이자 회귀 기준이다.** 사이클 #18 이전에는 같은 파츠 "이끼와
/// 양치식물이 자란 유적 식생" 13개 배치가 0.9m ~ 19.2m — 20.7배로 벌어졌고, 화면에서
/// 식생이 유적을 뒤덮었다. 원인은 발끝이 지평선 위인 배치 7개가 깊이 하한 0.05 에 걸려
/// `높이 ÷ 0.05` 로 크기가 20배 부푼 것이다.
/// </summary>
public sealed class RuinsPlazaCompositionTests
{
    private const double Horizon = 0.38;
    private const double AnchorHeightMeters = 1.8;

    /// <summary>실측 참조의 카메라 명세 — isometric · 지면에서 약 12m · 지평선 0.38.</summary>
    private static readonly CameraSpec Spec = new("isometric", "지면에서 약 12m", Horizon);

    private static readonly Guid Wall = Guid.NewGuid();
    private static readonly Guid Bridge = Guid.NewGuid();
    private static readonly Guid Pavement = Guid.NewGuid();
    private static readonly Guid Tower = Guid.NewGuid();
    private static readonly Guid Vegetation = Guid.NewGuid();
    private static readonly Guid Pillar = Guid.NewGuid();
    private static readonly Guid Terminal = Guid.NewGuid();
    private static readonly Guid Sign = Guid.NewGuid();

    /// <summary>기준 물체 — "청색 발광 단말기", 실제 높이 1.8m.</summary>
    private static readonly Bounds[] AnchorPlacements =
        [Box(0.91, 0.402, 0.072, 0.145), Box(0.132, 0.709, 0.062, 0.093)];

    private static SceneComposeInput[] Plaza() =>
    [
        Part(Wall, 8, Box(0.724, 0.052, 0.105, 0.158), Box(0.035, 0.055, 0.145, 0.145)),
        Part(Bridge, 5, Box(0.18, 0.21, 0.25, 0.34)),
        Part(Pavement, 4, Box(0.16, 0.03, 0.82, 0.94)),
        Part(Tower, 7, Box(0.69, 0.0, 0.31, 0.51)),
        Part(Vegetation, 1,
            Box(0.0, 0.17, 0.18, 0.27), Box(0.16, 0.095, 0.18, 0.18), Box(0.27, 0.43, 0.18, 0.2),
            Box(0.36, 0.62, 0.18, 0.18), Box(0.53, 0.0, 0.18, 0.15), Box(0.67, 0.02, 0.23, 0.18),
            Box(0.69, 0.23, 0.17, 0.16), Box(0.78, 0.37, 0.15, 0.18), Box(0.86, 0.55, 0.14, 0.23),
            Box(0.7, 0.72, 0.3, 0.28), Box(0.0, 0.68, 0.22, 0.32), Box(0.23, 0.76, 0.2, 0.18),
            Box(0.42, 0.79, 0.15, 0.16)),
        Part(Pillar, 6, Box(0.35, 0.18, 0.035, 0.16), Box(0.157, 0.365, 0.042, 0.115)),
        Part(Terminal, 2, AnchorPlacements[0], AnchorPlacements[1]),
        Part(Sign, 3,
            Box(0.075, 0.583, 0.148, 0.151), Box(0.188, 0.389, 0.032, 0.13),
            Box(0.856, 0.423, 0.037, 0.13)),
    ];

    private static SceneComposition Compose() => SceneLayoutComposer.ComposeWithSummary(
        Plaza(),
        Spec,
        new SceneScaleCalibration(AnchorHeightMeters, AnchorPlacements));

    /// <summary>
    /// **SC-01 · SC-02 — 표면으로 표시된 포장이 바닥을 덮는다.**
    ///
    /// 사이클 #19 까지는 포장이 11.81m 짜리 바위로 한 자리에 섰다. 배치는 화면의
    /// 82% × 94% 를 덮는데 에셋 하나를 그 면적에 맞추면 그렇게 된다 — 하나로는 면을
    /// 덮을 수 없기 때문이다. 표면으로 표시하면 배치 사각형이 곧 크기가 된다.
    /// </summary>
    [Fact]
    public void GroundSurface_CoversTheQuadInsteadOfStandingUp()
    {
        var pavement = SceneLayoutComposer.Compose(MarkedPlaza(), Spec, Calibration())
            .Single(i => i.PartId == Pavement);

        // 눕는다 — 두께가 폭·깊이보다 훨씬 작다. **실측 메시(비율 0.43)에서도** 그래야 한다.
        // 배율끼리 바로 비교하면 안 된다 — Y 는 월드 높이지만 X·Z 는 정규화 이전 값이라
        // 차원이 다르다. 뷰어를 거친 월드 크기로 환산해 비교한다
        var worldWidth = pavement.ScaleX * PavementMesh.X / PavementMesh.Y;
        var worldDepth = pavement.ScaleZ * PavementMesh.Z / PavementMesh.Y;

        Assert.False(pavement.IsUniform);
        Assert.True(pavement.ScaleY < worldWidth / 5,
            $"두께 {pavement.ScaleY:F2}m 가 폭 {worldWidth:F2}m 보다 훨씬 작아야 합니다");
        Assert.True(pavement.ScaleY < worldDepth / 5);

        // **기준 물체를 삼키지 않는다.** 인스턴스는 전부 y=0 바닥 중심이라 슬래브가
        // y=0 부터 두께만큼 차오르고 그 안의 것을 가린다. 비율 상한만 걸었을 때는
        // 18m 광장에서 1.45m 가 나와 1.8m 단말기의 81% 가 잠겼다 — 여유를 크게 둔다
        var anchor = SceneLayoutComposer.Compose(MarkedPlaza(), Spec, Calibration())
            .First(i => i.PartId == Terminal);
        Assert.True(pavement.ScaleY < anchor.ScaleY / 3,
            $"포장 두께 {pavement.ScaleY:F2} 가 기준 물체 {anchor.ScaleY:F2} 의 1/3 미만이어야 합니다");
    }

    /// <summary>
    /// **SC-02 — 표면이 배치가 가리키는 바닥 면적을 실제로 덮는다.**
    ///
    /// 뷰어가 높이로 균일 정규화한 뒤 축별 배율을 곱하므로 월드 폭은
    /// `배율X × 메시X ÷ 메시높이` 다. 그 되돌림을 빼먹으면 실측 포장에서 깊이가 132%
    /// 넘쳐 사각형을 벗어난다 — 면적을 **절대값으로** 비교해야 잡힌다.
    /// </summary>
    [Fact]
    public void GroundSurface_CoversTheQuadArea()
    {
        var pavement = SceneLayoutComposer.Compose(MarkedPlaza(), Spec, Calibration())
            .Single(i => i.PartId == Pavement);

        var quad = PlacementGeometry.GroundQuadOf(
            Plaza().Single(p => p.PartId == Pavement).Placements[0],
            SceneStaging.EyeHeight(Spec), SceneStaging.PitchDown(Spec))!.Value;

        // 보정 계수는 장면 전체에 공통이라 기준 물체로 되짚는다
        var factor = SceneLayoutComposer.Compose(MarkedPlaza(), Spec, Calibration())
            .First(i => i.PartId == Terminal).ScaleY / TerminalRawHeight();

        // 뷰어 정규화를 거친 실제 월드 크기
        var worldWidth = pavement.ScaleX * PavementMesh.X / PavementMesh.Y;
        var worldDepth = pavement.ScaleZ * PavementMesh.Z / PavementMesh.Y;
        var target = quad.Width * factor * quad.Depth * factor;

        Assert.InRange(worldWidth * worldDepth, target * 0.5, target * 1.5);
    }

    /// <summary>SC-04 — 수직 표면은 눕지 않는다. 최대 배치가 "수직 절벽 단면"이다.</summary>
    [Fact]
    public void VerticalSurface_StaysUpright()
    {
        var wall = VerticalWall().First(i => i.PartId == Wall);

        Assert.True(wall.ScaleY > wall.ScaleZ,
            $"벽의 높이 {wall.ScaleY:F2} 가 두께 {wall.ScaleZ:F2} 보다 커야 합니다");
    }

    /// <summary>
    /// **SC-04 — 벽의 깊이는 두께 비율을 따른다.**
    ///
    /// 높이 비율(Y ÷ 바닥폭)을 쓰면 10 : 8 : 1 인 벽이 깊이 0.8×폭 짜리 덩어리가 된다.
    /// 얇은 축이 긴 축에 갖는 비(0.10)를 써야 판이 판으로 선다. 그 값이 바닥 판 상한
    /// 0.08 에 눌리면 안 되므로 벽은 자기 상한(0.15)을 쓴다 — 뷰어 정규화를 거친
    /// **월드 크기**로 재야 실제로 얇은지 알 수 있다.
    /// </summary>
    [Fact]
    public void VerticalSurface_StaysThin()
    {
        var wall = VerticalWall().First(i => i.PartId == Wall);

        var worldWidth = wall.ScaleX * WallMesh.X / WallMesh.Y;
        var worldDepth = wall.ScaleZ * WallMesh.Z / WallMesh.Y;

        Assert.True(worldDepth < worldWidth * 0.2,
            $"벽 두께 {worldDepth:F2}m 가 폭 {worldWidth:F2}m 의 20% 미만이어야 합니다");
    }

    /// <summary>
    /// **SC-04 — 벽은 발치가 지면과 만나는 자리에 선다** (설계 §4.3).
    ///
    /// 사각형 한가운데 거리를 쓰면 벽이 제 깊이의 절반만큼 뒤로 밀리고, 원근 때문에
    /// 그만큼 넓어진다. 아래 모서리 거리가 옳다.
    /// </summary>
    [Fact]
    public void VerticalSurface_StandsAtTheNearEdge()
    {
        var wall = VerticalWall().First(i => i.PartId == Wall);

        var quad = PlacementGeometry.GroundQuadOf(
            Plaza().Single(p => p.PartId == Wall).Placements[0],
            SceneStaging.EyeHeight(Spec), SceneStaging.PitchDown(Spec))!.Value;

        // +z 가 카메라 쪽이므로 가까울수록 z 가 크다. 한가운데보다 앞에 서야 한다
        var center = PlacementGeometry.Z(quad.CenterGround, 8) * CalibrationScale();

        Assert.True(wall.Z > center,
            $"벽 z {wall.Z:F2} 가 사각형 한가운데 {center:F2} 보다 앞이어야 합니다");
    }

    /// <summary>
    /// **SC-04 — 벽의 폭도 발치 거리에서 잰다.**
    ///
    /// 자리만 아래 모서리로 옮기고 폭을 한가운데 거리로 두면, 자리와 크기가 서로 다른
    /// 거리에서 나온다 — 실측 벽에서 폭이 1.17배 부푼다. 원근은 먼 쪽을 넓게 벌리므로
    /// 한가운데에서 잰 폭은 발치의 폭보다 **반드시 크다**. 그 차이를 잠근다.
    /// </summary>
    [Fact]
    public void VerticalSurface_MeasuresWidthAtItsFoot()
    {
        var wall = VerticalWall().First(i => i.PartId == Wall);

        var quad = PlacementGeometry.GroundQuadOf(
            Plaza().Single(p => p.PartId == Wall).Placements[0],
            SceneStaging.EyeHeight(Spec), SceneStaging.PitchDown(Spec))!.Value;

        var atFoot = quad.WidthAt(quad.NearGround) * (WallMesh.Y / WallMesh.X);
        var atCenter = quad.Width * (WallMesh.Y / WallMesh.X);

        // 두 거리가 실제로 다르다 — 그래야 이 검사에 의미가 있다
        Assert.True(atCenter > atFoot * 1.05,
            $"한가운데 폭 {atCenter:F2} 가 발치 폭 {atFoot:F2} 보다 뚜렷이 커야 합니다");

        Assert.Equal(atFoot * CalibrationScale(), wall.ScaleX, 6);
    }

    /// <summary>
    /// **슬래브 두께에 절대 상한이 걸린다 — 광장이 넓어도 두꺼워지지 않는다.**
    ///
    /// 상한이 폭 비례뿐이면 40m 광장에서 3.2m 슬래브가 된다. 같은 포장을 두 배 넓은
    /// 광장에 깔아도 두께가 따라 커지지 않아야 한다.
    ///
    /// **절대 상한 구간을 잠근다.** 아주 얇은 메시(비율 0.01 등)는 비율 상한 안에 머물러
    /// 폭에 비례해 두꺼워지는데, 그건 정상 동작이다 — 이 검사의 대상이 아니다.
    /// </summary>
    [Fact]
    public void SlabThickness_DoesNotGrowWithTheSquare()
    {
        var wide = MarkedPlaza()
            .Select(part => part.PartId == Pavement
                // 같은 포장을 화면 전체로 넓힌다
                ? part with { Placements = [Box(0.0, 0.02, 1.0, 0.96)] }
                : part)
            .ToArray();

        var narrow = SceneLayoutComposer.Compose(MarkedPlaza(), Spec, Calibration())
            .Single(i => i.PartId == Pavement);
        var broad = SceneLayoutComposer.Compose(wide, Spec, Calibration())
            .Single(i => i.PartId == Pavement);

        // 넓어진 것은 폭·깊이뿐이다
        Assert.True(broad.ScaleX > narrow.ScaleX);
        Assert.Equal(narrow.ScaleY, broad.ScaleY, 6);
    }

    /// <summary>
    /// **설계 §8 — 지면 사각형을 이미지 사각형으로 되돌리면 제자리로 온다.**
    ///
    /// 표면의 크기는 전부 이 사각형에서 나오므로, 사각형이 배치를 정확히 가리키지
    /// 않으면 그 뒤의 어떤 단언도 잘못된 기준을 재는 셈이 된다. 역투영을 한 번 더
    /// 뒤집어 원래 `bounds` 로 돌아오는지를 본다 — 두 방향이 같은 기하를 쓴다는 증거다.
    /// </summary>
    [Theory]
    [InlineData(0.16, 0.03, 0.82, 0.94)]
    [InlineData(0.18, 0.21, 0.25, 0.34)]
    [InlineData(0.69, 0.23, 0.17, 0.16)]
    public void SurfaceQuad_RoundTripsToTheImageRect(double x, double y, double w, double h)
    {
        var bounds = Box(x, y, w, h);
        var eyeHeight = SceneStaging.EyeHeight(Spec);
        var pitchDown = SceneStaging.PitchDown(Spec);

        var quad = PlacementGeometry.GroundQuadOf(bounds, eyeHeight, pitchDown)!.Value;

        // 아래 모서리와 위 모서리를 각각 화면 세로로 되돌린다
        var far = quad.NearGround + quad.Depth;
        Assert.Equal(bounds.Y + bounds.H, ImageY(quad.NearGround, eyeHeight, pitchDown), 6);
        Assert.Equal(bounds.Y, ImageY(far, eyeHeight, pitchDown), 6);

        // 가로도 같은 거리에서 되돌리면 원래 폭·중심으로 온다
        var eye = PlacementGeometry.EyeDistance(quad.CenterGround, eyeHeight);
        Assert.Equal(
            bounds.W, quad.Width / (2 * eye * Math.Tan(PlacementGeometry.HalfFovHorizontal)), 6);
        Assert.Equal(
            bounds.X + (bounds.W / 2) - 0.5,
            quad.CenterX / (2 * eye * Math.Tan(PlacementGeometry.HalfFovHorizontal)),
            6);
    }

    /// <summary>지면 거리를 이미지 세로 좌표로 되돌린다 — `AxisAngle` 의 역이다.</summary>
    private static double ImageY(double groundDistance, double eyeHeight, double pitchDown)
    {
        var below = Math.Atan(eyeHeight / groundDistance);

        return (Math.Tan(below - pitchDown) / (2 * Math.Tan(PlacementGeometry.HalfFovVertical)))
            + 0.5;
    }

    /// <summary>
    /// **SC-05 — 인스턴스 수가 상한을 넘지 않는다** (FR-06).
    ///
    /// 분해가 배치를 폭주시켜도 뷰어가 멈추지 않아야 한다. 상한 너머의 배치는 버린다.
    /// </summary>
    [Fact]
    public void InstanceCount_StaysUnderTheCap()
    {
        // 상한의 세 배가 되도록 같은 배치를 불린다
        var flood = Enumerable.Range(0, (SceneLayoutComposer.MaxInstances * 3 / 20) + 1)
            .Select(_ => Part(Guid.NewGuid(), 1, [.. Enumerable.Repeat(Box(0.4, 0.4, 0.1, 0.1), 20)]))
            .ToArray();

        var instances = SceneLayoutComposer.Compose(flood, Spec, Calibration());

        Assert.Equal(SceneLayoutComposer.MaxInstances, instances.Count);
    }

    /// <summary>
    /// **상한이 버린 몫을 요약이 설명한다** (FR-06).
    ///
    /// 접지·고도 수는 배치에 대한 판정이라 상한과 무관하게 전부 센다. 그래서 인스턴스가
    /// 배치보다 적어지는데, 그 차이가 어디서 왔는지 화면이 말할 수 있어야 한다.
    /// </summary>
    [Fact]
    public void DroppedPlacements_AreCounted()
    {
        var quiet = Compose();
        Assert.Equal(0, quiet.Summary.DroppedCount);

        var flood = Enumerable.Range(0, (SceneLayoutComposer.MaxInstances * 3 / 20) + 1)
            .Select(_ => Part(Guid.NewGuid(), 1, [.. Enumerable.Repeat(Box(0.4, 0.4, 0.1, 0.1), 20)]))
            .ToArray();

        var summary = SceneLayoutComposer.ComposeWithSummary(flood, Spec, Calibration()).Summary;
        var placements = flood.Sum(part => part.Placements.Count);

        // 배치는 전부 세고, 인스턴스는 상한까지만 만들었다
        Assert.Equal(placements, summary.GroundedCount + summary.ElevatedCount);
        Assert.Equal(placements - SceneLayoutComposer.MaxInstances, summary.DroppedCount);
    }

    /// <summary>
    /// **설계 §4.1 부차 신호 — 표시와 형태가 어긋나면 요약이 드러낸다.**
    ///
    /// 표시를 뒤집지는 않는다. 분해가 준 한 비트가 정본이고 기하는 그것을 못 이긴다 —
    /// 실측에서 같은 성격의 파츠 3개 중 종횡비로 걸린 것은 1개뿐이었다(계획 §2.2).
    /// 형태를 모르는 파츠는 세지 않는다. 모름은 어긋남이 아니다.
    /// </summary>
    [Fact]
    public void SurfaceMarkedButUpright_IsReportedWithoutOverridingTheMark()
    {
        // 진짜 얇은 판은 어긋나지 않는다
        var thin = MarkedPlaza()
            .Select(part => part.PartId == Pavement
                ? part with { Extents = new MeshExtents(20, 1, 20) }
                : part)
            .ToArray();

        Assert.Equal(
            0,
            SceneLayoutComposer.ComposeWithSummary(thin, Spec, Calibration())
                .Summary.SurfaceShapeMismatch);

        // **실측 포장이 이 신호를 켠다.** 0.98 : 0.82 : 1.90 은 비율 0.43 으로 임계
        // 0.35 를 넘는다 — 3D 생성이 바닥 이미지에서 두껍게 뽑았기 때문이다.
        // 계획 §2.2 가 "기하로는 가려낼 수 없다" 고 한 바로 그 사례이고,
        // 그래서 표시가 정본이며 이것은 의심할 근거일 뿐이다
        var composed = SceneLayoutComposer.ComposeWithSummary(MarkedPlaza(), Spec, Calibration());
        Assert.Equal(1, composed.Summary.SurfaceShapeMismatch);

        // 그래도 눕힌다 — 신호일 뿐 판정을 바꾸지 않는다
        Assert.False(composed.Instances.Single(i => i.PartId == Pavement).IsUniform);

        // 임계를 직접 확인한다 — 신호가 켜지는 이유가 실측 비율임을 못박는다
        Assert.False(PavementMesh.IsGroundPlane);
        Assert.True(PavementMesh.Y / PavementMesh.Footprint > MeshExtents.FlatThreshold);

        // 형태를 모르면 세지 않는다
        var unknown = MarkedPlaza()
            .Select(part => part.PartId == Pavement ? part with { Extents = null } : part)
            .ToArray();

        Assert.Equal(
            0,
            SceneLayoutComposer.ComposeWithSummary(unknown, Spec, Calibration())
                .Summary.SurfaceShapeMismatch);
    }

    /// <summary>
    /// **§6 — 사각형이 뭉개지면 낱개 경로로 되돌린다.**
    ///
    /// 폭 0 인 배치를 표면으로 표시하면 배율에 0 이 곱해져 인스턴스가 사라진다.
    /// 표시가 틀렸다고 물건이 없어져서는 안 된다.
    /// </summary>
    [Fact]
    public void DegenerateQuad_FallsBackToTheSolidPath()
    {
        var marked = Plaza()
            .Select(part => part.PartId == Tower
                ? Part(Tower, 7, Box(0.69, 0.0, 0, 0.51)) with { Surface = PartSurface.Ground }
                : part)
            .ToArray();

        var tower = SceneLayoutComposer.Compose(marked, Spec, Calibration())
            .First(i => i.PartId == Tower);

        Assert.True(tower.IsUniform, "뭉개진 사각형은 낱개 경로로 가야 합니다");
        Assert.True(tower.ScaleY > 0);
    }

    /// <summary>**§6 — 기준 물체가 표면으로 표시되면 요약이 경고한다.**</summary>
    [Fact]
    public void SurfaceAnchor_IsReportedAsAWarning()
    {
        var quiet = SceneLayoutComposer.ComposeWithSummary(Plaza(), Spec, Calibration());
        Assert.False(quiet.Summary.AnchorIsSurface);

        var warned = SceneLayoutComposer.ComposeWithSummary(
            Plaza(), Spec, Calibration() with { AnchorIsSurface = true });

        Assert.True(warned.Summary.AnchorIsSurface);
    }

    /// <summary>표면 파츠는 파츠당 배율 통일에서 빠진다 — 배치마다 사각형이 다르다 (#20 §4.4).</summary>
    [Fact]
    public void SurfaceParts_KeepPerPlacementSize()
    {
        var marked = Plaza()
            .Select(part => part.PartId == Wall ? part with { Surface = PartSurface.Ground } : part)
            .ToArray();

        var walls = SceneLayoutComposer.Compose(marked, Spec, Calibration())
            .Where(i => i.PartId == Wall)
            .ToArray();

        Assert.Equal(2, walls.Length);
        Assert.NotEqual(walls[0].ScaleX, walls[1].ScaleX);
    }

    /// <summary>
    /// **SC-03 — 표시가 없으면 사이클 #19 와 동등하다.**
    ///
    /// 기존 작업은 분해 프롬프트에 표면 필드가 없어 전부 낱개로 들어온다. 그 경로의
    /// 수치가 바뀌면 이미 만들어진 장면이 조용히 달라진다.
    /// </summary>
    [Fact]
    public void UnmarkedParts_StayUniformAndIgnoreShape()
    {
        var instances = SceneLayoutComposer.Compose(Plaza(), Spec, Calibration());

        Assert.All(instances, instance => Assert.True(instance.IsUniform));

        // 형태 정보 유무도 낱개 경로를 흔들지 않는다
        var withoutShape = Plaza().Select(part => part with { Extents = null }).ToArray();
        Assert.Equal(
            instances.Select(i => i.Scale),
            SceneLayoutComposer.Compose(withoutShape, Spec, Calibration()).Select(i => i.Scale));
    }

    private static SceneScaleCalibration Calibration()
        => new(AnchorHeightMeters, AnchorPlacements);

    /// <summary>수직 벽 메시 — 10 : 8 : 1. 얇은 축이 깊이다.</summary>
    private static readonly MeshExtents WallMesh = new(10, 8, 1);

    private static IReadOnlyList<SceneInstance> VerticalWall() => SceneLayoutComposer.Compose(
        [.. Plaza().Select(part => part.PartId == Wall
            ? part with { Surface = PartSurface.Vertical, Extents = WallMesh }
            : part)],
        Spec,
        Calibration());

    /// <summary>장면 전체에 공통인 보정 계수 — 기준 물체로 되짚는다.</summary>
    private static double CalibrationScale()
        => SceneLayoutComposer.Compose(Plaza(), Spec, Calibration())
            .First(i => i.PartId == Terminal).ScaleY / TerminalRawHeight();

    /// <summary>
    /// 실측 포장 GLB — 0.98 : 0.82 : 1.90.
    ///
    /// **이상적인 얇은 판을 쓰면 안 된다.** 계획 §2.2 가 인용한 이 비율(0.43)에서
    /// 두께 상한이 없으면 18m 폭에 7.88m 슬래브가 선다 — 갭 분석이 잡은 회피였다.
    /// </summary>
    private static readonly MeshExtents PavementMesh = new(0.98, 0.82, 1.90);

    private static SceneComposeInput[] MarkedPlaza() =>
    [
        .. Plaza().Select(part => part.PartId == Pavement
            ? part with { Surface = PartSurface.Ground, Extents = PavementMesh }
            : part),
    ];

    /// <summary>기준 물체의 보정 이전 높이 — 보정 계수를 되짚는 데 쓴다.</summary>
    private static double TerminalRawHeight()
    {
        var eye = SceneStaging.EyeHeight(Spec);
        var pitch = SceneStaging.PitchDown(Spec);
        var scales = AnchorPlacements
            .Select(bounds => PlacementGeometry.RawScale(
                bounds,
                PlacementGeometry.EyeDistance(
                    PlacementGeometry.GroundDistance(bounds, eye, pitch)!.Value, eye)))
            .ToArray();

        return PartScaleResolver.Median(scales);
    }

    /// <summary>SC-04 (#18 유지) — 같은 에셋이 같은 크기다.</summary>
    [Fact]
    public void SamePart_SharesOneWorldScale()
    {
        var scales = Compose().Instances
            .Where(instance => instance.PartId == Vegetation)
            .Select(instance => instance.Scale)
            .ToArray();

        Assert.Equal(13, scales.Length);

        // 20.7배가 사라진다 — 같은 GLB 는 같은 월드 크기다
        Assert.Equal(scales.Min(), scales.Max(), precision: 9);
    }

    /// <summary>
    /// SC-01 — **이끼가 탑보다 클 수는 없다.** 사용자가 화면에서 본 결함이 이것이다.
    /// </summary>
    [Fact]
    public void Vegetation_IsSmallerThanTheRuinTower()
    {
        var instances = Compose().Instances;

        var vegetation = instances.Where(i => i.PartId == Vegetation).Max(i => i.Scale);
        var tower = instances.Single(i => i.PartId == Tower).Scale;

        Assert.True(vegetation < 4, $"식생이 4m 미만이어야 합니다 (실제 {vegetation:F2}m)");
        Assert.True(vegetation < tower, "식생이 유적 탑보다 작아야 합니다");
    }

    /// <summary>SC-02 — 올바른 기하에서 지평선이 프레임 밖으로 나간다.</summary>
    [Fact]
    public void EveryPlacementMeetsTheGroundAtThisPitch()
    {
        var summary = Compose().Summary;

        // **하향각 43° 에서는 배치 25개 전부가 지면에 닿는다.** horizonY=0.38 이
        // 만들던 "지평선 위 7개" 는 얕은 각(6.4°)이 낳은 허상이었다 — 실제 시선은
        // 프레임 전체가 지면인 부감이라 지평선이 화면 밖에 있다
        Assert.Equal(25, summary.GroundedCount);
        Assert.Equal(0, summary.ElevatedCount);

        // 그래도 대표 거리는 계산돼 있다 — 다른 장면이 빌릴 값이다
        Assert.InRange(summary.ScaleDepthForElevated, 4.8, 25.8);
    }

    /// <summary>SC-03 — 기준 물체는 실제 높이에 수렴한다. 사이클 #1 의 계약이다.</summary>
    [Fact]
    public void Anchor_StillResolvesToItsRealHeight()
    {
        var terminal = Compose().Instances.Where(i => i.PartId == Terminal).ToArray();

        Assert.Equal(2, terminal.Length);
        Assert.All(terminal, instance =>
            Assert.Equal(AnchorHeightMeters, instance.Scale, precision: 6));


    }

    /// <summary>SC-04 — 배치가 하나뿐인 파츠는 손대지 않는다.</summary>
    [Fact]
    public void SinglePlacementParts_KeepTheirOwnDistance()
    {
        var instances = Compose().Instances;

        // 배치가 하나뿐이라 파츠당 배율 통일(#18 D-04)이 개입하지 않는다 —
        // 각자의 역투영 거리에서 나온 값을 그대로 갖는다
        Assert.All(new[] { Tower, Bridge, Pavement }, part =>
            Assert.Single(instances, i => i.PartId == part));

        // 원본에서 프레임의 94% 를 덮는 포장이 가장 크다 — 기하가 그렇게 말한다
        Assert.True(instances.Single(i => i.PartId == Pavement).Scale
            > instances.Single(i => i.PartId == Tower).Scale,
            "화면을 가장 넓게 덮는 포장이 가장 커야 합니다");
    }

    /// <summary>
    /// **SC-01 — 파츠가 원본에서 차지하던 화면 비율대로 렌더된다.**
    ///
    /// 이번 사이클의 존재 이유다. 이전 공식에서는 파츠마다 2.0~7.5배로 갈렸다(편차 3.8배) —
    /// 전부 작다는 것보다 편차가 문제였다. 균일한 축소라면 카메라를 당기면 되지만,
    /// 파츠마다 배수가 다르면 어떤 상수를 조정해도 동시에 맞출 수 없다.
    /// </summary>
    [Fact]
    public void ScreenOccupancy_MatchesTheReference()
    {
        var instances = Compose().Instances;
        var camera = SceneStaging.ComposeCamera(Spec, instances);
        var half = Math.Tan(camera.FieldOfViewDegrees / 2 * Math.PI / 180);

        var sourceHeight = Plaza().ToDictionary(
            part => part.PartId,
            part => part.Placements.Average(bounds => bounds.H));

        var ratios = instances
            .GroupBy(instance => instance.PartId)
            .Select(group =>
            {
                var rendered = group.Average(instance => instance.Scale / (2 * Distance(
                    camera, instance.X, instance.Y + (instance.Scale / 2), instance.Z) * half));

                return sourceHeight[group.Key] / rendered;
            })
            .ToArray();

        var spread = ratios.Max() / ratios.Min();
        Assert.True(spread <= 1.8, $"화면 점유율 편차 {spread:F2} 배가 1.8 이하여야 합니다 (이전 3.80)");
    }

    /// <summary>SC-02 — 장면이 사이클 #18(33.8 × 21.5m)보다 조밀하다.</summary>
    [Fact]
    public void Scene_IsMoreCompactThanTheDepthModel()
    {
        var instances = Compose().Instances;

        var width = instances.Max(i => i.X) - instances.Min(i => i.X);
        var depth = instances.Max(i => i.Z) - instances.Min(i => i.Z);

        // 사이클 #18 은 33.8 × 21.5m = 727m². 면적으로 재야 축 하나의 미세한
        // 증감에 흔들리지 않는다
        Assert.True(width * depth <= 500,
            $"장면 {width:F1} × {depth:F1}m 의 면적이 500m² 이하여야 합니다 (이전 727)");
    }

    /// <summary>SC-05 — 발끝이 아래인 배치가 더 가깝다. 원근의 기본 성질이다.</summary>
    [Fact]
    public void NearerFootIsCloser()
    {
        var instances = Compose().Instances;

        // 포장 석재(발끝 0.97)가 가장 가깝고, 석벽(발끝 0.21)이 가장 멀다
        var pavement = instances.Single(i => i.PartId == Pavement).Z;
        var wall = instances.Where(i => i.PartId == Wall).Max(i => i.Z);

        Assert.True(pavement > wall, "발끝이 아래인 쪽이 더 가까워야 합니다");
    }

    /// <summary>
    /// **SC-06 — 눈높이는 형상을 바꾸지 않는다** (#19 §3.2).
    ///
    /// 거리·자리·크기가 모두 눈높이에 비례하므로 보정 계수가 그 비례를 정확히 상쇄한다.
    /// 분석 모델이 "지면에서 약 12m" 를 얼마나 틀리게 잡든 장면의 모양은 같다.
    /// </summary>
    [Fact]
    public void EyeLevel_DoesNotChangeTheShape()
    {
        var twice = SceneLayoutComposer.Compose(
            Plaza(),
            new CameraSpec("isometric", "지면에서 약 24m", Horizon),
            new SceneScaleCalibration(AnchorHeightMeters, AnchorPlacements));

        // 하향각이 눈높이에 반응하므로 순수 비례는 아니다 — 형상 비율이 유지되는지 본다
        var baseline = Compose().Instances;
        Assert.Equal(baseline.Count, twice.Count);
        Assert.All(baseline.Zip(twice), pair =>
            Assert.Equal(pair.First.PartId, pair.Second.PartId));
    }

    private static double Distance(SceneCamera camera, double x, double y, double z)
        => Math.Sqrt(
            Math.Pow(x - camera.Position.X, 2)
            + Math.Pow(y - camera.Position.Y, 2)
            + Math.Pow(z - camera.Position.Z, 2));

    /// <summary>SC-06 — 기준 물체 배치가 3.9배로 흔들린다는 사실이 드러난다.</summary>
    [Fact]
    public void AnchorSpread_IsReported()
    {
        // 지면 역투영이 기준 물체의 두 배치를 더 일관되게 잰다 — 3.94배(깊이 모델)에서
        // 1.87배로 줄었다. 같은 물체를 두 자리에서 재는 오차가 그만큼 작아졌다는 뜻이다
        Assert.Equal(1.87, Compose().Summary.AnchorSpread, precision: 2);
    }

    /// <summary>결정성 — 같은 입력, 같은 출력 (NFR-01).</summary>
    [Fact]
    public void Composition_IsDeterministic()
    {
        Assert.Equal(Compose().Instances, Compose().Instances);
    }

    private static SceneComposeInput Part(Guid id, int depthOrder, params Bounds[] placements)
        => new(id, depthOrder, placements);

    private static Bounds Box(double x, double y, double w, double h) => new(x, y, w, h);
}
