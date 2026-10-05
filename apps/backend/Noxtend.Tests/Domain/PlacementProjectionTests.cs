using Noxtend.Domain.Job;
using Noxtend.Domain.Scene.Projection;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 투영 규칙 하나하나. Design Ref: background-placement-projection(#19) §7.2
///
/// **왕복이 이 설계의 핵심 단언이다** — 역투영이 옳다면, 거리로 옮긴 배치를 같은
/// 카메라로 되돌려 투영했을 때 원래 이미지 자리로 와야 한다. 이전 공식은 세 축이
/// 깊이를 다르게 해석해 이 성질이 성립하지 않았고, 실측 화면 점유율이 파츠마다
/// 2.0~7.5배로 갈렸다.
/// </summary>
public sealed class PlacementProjectionTests
{
    private const double EyeHeight = 12;
    private static readonly double Pitch = 43 * Math.PI / 180;

    // ─── 지면 역투영 (§1 · FR-01) ───

    /// <summary>
    /// 화면 정중앙에 발을 딛는 배치는 카메라 축이 지면과 만나는 곳에 선다 —
    /// 그 거리는 `눈높이 / tan(하향각)` 이다.
    /// </summary>
    [Fact]
    public void Ground_InvertsTheImageFootToADistance()
    {
        // 발끝이 정확히 y=0.5 — 축각 0
        var bounds = new Bounds(0.4, 0.45, 0.2, 0.05);

        var ground = PlacementGeometry.GroundDistance(bounds, EyeHeight, Pitch);

        Assert.Equal(EyeHeight / Math.Tan(Pitch), ground!.Value, precision: 9);
    }

    /// <summary>
    /// **왕복** — 거리로 옮긴 뒤 같은 카메라로 되돌려 투영하면 원래 발끝 y 로 온다.
    /// 이 성질이 곧 "렌더가 원본과 같은 자리에 그린다" 는 뜻이다.
    /// </summary>
    [Theory]
    [InlineData(0.55)]
    [InlineData(0.70)]
    [InlineData(0.95)]
    [InlineData(1.00)]
    public void Ground_ProjectsBackToTheSameImagePosition(double foot)
    {
        var bounds = new Bounds(0.3, foot - 0.1, 0.2, 0.1);

        var ground = PlacementGeometry.GroundDistance(bounds, EyeHeight, Pitch)!.Value;

        // 카메라 (0, 눈높이, 0) 에서 하향각만큼 기울여 그 지점을 본다
        var belowHorizon = Math.Atan(EyeHeight / ground);
        var axisAngle = belowHorizon - Pitch;
        var imageY = 0.5 + (Math.Tan(axisAngle) / (2 * Math.Tan(PlacementGeometry.HalfFovVertical)));

        Assert.Equal(foot, imageY, precision: 9);
    }

    /// <summary>가로도 왕복한다 — 이미지 x 오프셋이 그 거리에서 갖는 폭이 되돌아온다.</summary>
    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void X_ProjectsBackToTheSameImageColumn(double centerX)
    {
        var bounds = new Bounds(centerX - 0.05, 0.6, 0.1, 0.1);
        var ground = PlacementGeometry.GroundDistance(bounds, EyeHeight, Pitch)!.Value;
        var eye = PlacementGeometry.EyeDistance(ground, EyeHeight);

        var world = PlacementGeometry.X(bounds, eye);
        var imageX = 0.5 + (world / (2 * eye * Math.Tan(PlacementGeometry.HalfFovHorizontal)));

        Assert.Equal(centerX, imageX, precision: 9);
    }

    /// <summary>크기도 왕복한다 — 그 거리에 세우면 화면에서 원래 세로 비율을 차지한다.</summary>
    [Fact]
    public void RawScale_ProjectsBackToTheSameImageHeight()
    {
        var bounds = new Bounds(0.3, 0.6, 0.2, 0.18);
        var ground = PlacementGeometry.GroundDistance(bounds, EyeHeight, Pitch)!.Value;
        var eye = PlacementGeometry.EyeDistance(ground, EyeHeight);

        var scale = PlacementGeometry.RawScale(bounds, eye);
        var fraction = scale / (2 * eye * Math.Tan(PlacementGeometry.HalfFovVertical));

        Assert.Equal(bounds.H, fraction, precision: 9);
    }

    /// <summary>시선이 지면을 만나지 않으면 거리가 없다 — 지면에 접지하지 않은 물체다.</summary>
    [Fact]
    public void Ground_ReturnsNothingAboveTheHorizon()
    {
        // 하향각 43° 보다 더 위를 보는 배치 — 축각이 −43° 보다 작다
        var bounds = new Bounds(0.3, 0.0, 0.2, 0.02);

        Assert.Null(PlacementGeometry.GroundDistance(bounds, EyeHeight, 0.01));
    }

    /// <summary>
    /// 수평선에 거의 붙은 배치는 거리가 발산한다 — 실질적 무한대이고 부동소수도
    /// 흔들리므로 눈높이의 배수로 잘라 둔다.
    /// </summary>
    [Fact]
    public void Ground_CapsRunawayDistance()
    {
        var bounds = new Bounds(0.3, 0.4, 0.2, 0.1);

        var ground = PlacementGeometry.GroundDistance(bounds, EyeHeight, 1e-5);

        Assert.Equal(EyeHeight * PlacementGeometry.MaxDistanceInEyeHeights, ground!.Value, 6);
    }

    /// <summary>발끝이 아래일수록 가깝다 — 원근의 기본 성질 (SC-05).</summary>
    [Fact]
    public void Ground_PutsLowerFeetCloser()
    {
        var near = PlacementGeometry.GroundDistance(new Bounds(0, 0.9, 0.1, 0.1), EyeHeight, Pitch);
        var far = PlacementGeometry.GroundDistance(new Bounds(0, 0.5, 0.1, 0.1), EyeHeight, Pitch);

        Assert.True(near!.Value < far!.Value, "발끝이 아래인 쪽이 더 가까워야 합니다");
    }

    // ─── 분류와 대표 거리 (§3.4 · FR-06) ───

    [Fact]
    public void Projector_TreatsPlacementsThatMeetTheGroundAsGrounded()
    {
        var projector = Projector(new Bounds(0, 0.7, 0.2, 0.2));

        Assert.True(projector.IsGrounded(new Bounds(0, 0.7, 0.2, 0.2)));
    }

    /// <summary>시선이 지면을 벗어나면 고도 미상이다 — 카메라 종류와 무관하다.</summary>
    [Fact]
    public void Projector_FallsBackForPlacementsAboveTheHorizon()
    {
        var shallow = new PlacementProjector(EyeHeight, 0.01, [new Bounds(0, 0.7, 0.2, 0.2)]);

        Assert.False(shallow.IsGrounded(new Bounds(0, 0.0, 0.2, 0.02)));
    }

    /// <summary>고도 미상 배치는 접지 배치들의 거리 중앙값을 빌린다.</summary>
    [Fact]
    public void Projector_TakesTheMedianGroundDistance()
    {
        var boxes = new[]
        {
            new Bounds(0, 0.85, 0.1, 0.05),
            new Bounds(0, 0.75, 0.1, 0.05),
            new Bounds(0, 0.65, 0.1, 0.05),
        };
        var middle = PlacementGeometry.GroundDistance(boxes[1], EyeHeight, Pitch)!.Value;

        Assert.Equal(middle, Projector(boxes).ScaleDepthForElevated, precision: 9);
    }

    /// <summary>접지 배치가 하나도 없으면 빌릴 곳이 없다 — 눈높이 기준 중립 거리.</summary>
    [Fact]
    public void Projector_UsesTheNeutralDistanceWithoutAnyGroundedPlacement()
    {
        var projector = new PlacementProjector(EyeHeight, 0.01, [new Bounds(0, 0, 0.1, 0.02)]);

        Assert.Equal(
            EyeHeight * PlacementProjector.NeutralGroundInEyeHeights,
            projector.ScaleDepthForElevated,
            precision: 9);
    }

    /// <summary>판정은 집계를 겸하지 않는다 — 같은 배치를 여러 번 물어도 답이 같다.</summary>
    [Fact]
    public void Projector_IsFreeOfCallCountSideEffects()
    {
        var bounds = new Bounds(0, 0.7, 0.2, 0.2);
        var projector = Projector(bounds);

        var first = projector.For(bounds).Project(bounds, 0);
        var second = projector.For(bounds).Project(bounds, 0);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// **눈높이는 형상을 바꾸지 않는다** (§3.2 · SC-06). 눈높이를 k 배 하면 거리도
    /// 자리도 크기도 k 배가 되므로, 보정 계수가 그 k 를 정확히 상쇄한다.
    /// </summary>
    [Fact]
    public void EyeHeight_ScalesEverythingUniformly()
    {
        var bounds = new Bounds(0.3, 0.6, 0.2, 0.18);

        var one = new GroundedProjection(EyeHeight, Pitch).Project(bounds, 0);
        var twice = new GroundedProjection(EyeHeight * 2, Pitch).Project(bounds, 0);

        Assert.Equal(2, twice.X / one.X, precision: 9);
        Assert.Equal(2, twice.Z / one.Z, precision: 9);
        Assert.Equal(2, twice.RawScale / one.RawScale, precision: 9);
    }

    // ─── 면 역투영 (#20 §4.3) ───

    /// <summary>
    /// **왕복** — 지면 면을 되돌려 투영하면 원래 배치 사각형의 세로 범위로 온다.
    /// 이 성질이 곧 "표면이 배치가 가리킨 자리를 정확히 덮는다" 는 뜻이다.
    /// </summary>
    [Theory]
    [InlineData(0.30, 0.60)]
    [InlineData(0.10, 0.90)]
    [InlineData(0.55, 0.99)]
    public void GroundQuad_RoundTripsToTheImageRect(double top, double bottom)
    {
        var bounds = new Bounds(0.2, top, 0.5, bottom - top);

        var quad = PlacementGeometry.GroundQuadOf(bounds, EyeHeight, Pitch)!.Value;

        // 면의 앞·뒤 끝을 이미지 y 로 되돌린다
        var near = quad.CenterGround - (quad.Depth / 2);
        var far = quad.CenterGround + (quad.Depth / 2);

        Assert.Equal(bottom, ImageY(near), precision: 9);
        Assert.Equal(top, ImageY(far), precision: 9);
    }

    /// <summary>가로도 왕복한다 — 면의 폭이 배치 사각형의 가로 범위로 되돌아온다.</summary>
    [Fact]
    public void GroundQuad_WidthRoundTripsToTheImageColumns()
    {
        var bounds = new Bounds(0.2, 0.4, 0.5, 0.4);

        var quad = PlacementGeometry.GroundQuadOf(bounds, EyeHeight, Pitch)!.Value;
        var eye = PlacementGeometry.EyeDistance(quad.CenterGround, EyeHeight);
        var span = 2 * eye * Math.Tan(PlacementGeometry.HalfFovHorizontal);

        Assert.Equal(bounds.W, quad.Width / span, precision: 9);
        Assert.Equal(bounds.X + (bounds.W / 2), 0.5 + (quad.CenterX / span), precision: 9);
    }

    /// <summary>먼 배치일수록 같은 화면 높이가 더 깊은 면을 뜻한다 — 원근의 역이다.</summary>
    [Fact]
    public void GroundQuad_IsDeeperForFartherPlacements()
    {
        var near = PlacementGeometry.GroundQuadOf(
            new Bounds(0.2, 0.8, 0.5, 0.15), EyeHeight, Pitch)!.Value;
        var far = PlacementGeometry.GroundQuadOf(
            new Bounds(0.2, 0.45, 0.5, 0.15), EyeHeight, Pitch)!.Value;

        Assert.True(far.Depth > near.Depth, "먼 배치의 면이 더 깊어야 합니다");
    }

    /// <summary>위 모서리가 지평선을 넘으면 면을 잴 수 없다 — 고도 미상 경로로 간다.</summary>
    [Fact]
    public void GroundQuad_ReturnsNothingWhenTheTopEdgeIsAboveTheHorizon()
        => Assert.Null(PlacementGeometry.GroundQuadOf(
            new Bounds(0.2, 0.0, 0.5, 0.9), EyeHeight, 0.01));

    /// <summary>이미지 y 로 되돌리는 역투영 — 왕복 검증의 반대 방향.</summary>
    private static double ImageY(double ground)
        => 0.5 + (Math.Tan(Math.Atan(EyeHeight / ground) - Pitch)
            / (2 * Math.Tan(PlacementGeometry.HalfFovVertical)));

    private static PlacementProjector Projector(params Bounds[] placements)
        => new(EyeHeight, Pitch, placements);
}
