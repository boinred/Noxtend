using Noxtend.Domain.Job;
using Noxtend.Domain.Scene;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 장면 서명과 수치 카메라·조명. Design Ref: background-similarity-tuning §4.1~4.2
///
/// **개수만 세면 같은 개수의 교체를 못 본다** — mesh 하나를 재생성해도 개수는 그대로라
/// 낡은 명세가 계속 쓰인다. 정렬된 asset id 의 SHA-256 이 캐시 유효성의 정본이다.
///
/// **카메라·조명 수치는 서버가 정본이다** (D-03) — 브라우저가 자유 문장을 해석하면
/// 평가 렌더의 결정성이 깨진다. 프론트 `sceneCameraPose`·`sceneLightRig` 를 이식한다.
/// </summary>
public sealed class SceneStagingTests
{
    // ─── mesh 서명 ───

    [Fact]
    public void Signature_IsOrderIndependent()
    {
        var a = (Guid.NewGuid(), SignaturePart());
        var b = (Guid.NewGuid(), SignaturePart());

        Assert.Equal(
            SceneMeshSignature.Compute([a, b]),
            SceneMeshSignature.Compute([b, a]));
    }

    /// <summary>
    /// 같은 개수, 다른 asset — 개수 기반 캐시가 놓치던 바로 그 경우.
    ///
    /// **파츠를 고정하고 mesh 만 바꾼다.** 파츠까지 새로 만들면 PartId 차이로 통과해
    /// 정작 mesh 교체를 보는지 알 수 없다.
    /// </summary>
    [Fact]
    public void Signature_ChangesWhenAnAssetIsSwapped()
    {
        var part = SignaturePart();
        var other = SignaturePart();

        Assert.NotEqual(
            SceneMeshSignature.Compute([(Guid.NewGuid(), part), (Guid.NewGuid(), other)]),
            SceneMeshSignature.Compute([(Guid.NewGuid(), part), (Guid.NewGuid(), other)]));
    }

    /// <summary>
    /// **배치 좌표가 서명에 들어간다** (#20).
    ///
    /// 재분해는 파츠를 이름으로 제자리 갱신하므로 mesh id 가 그대로다. 좌표가 서명 밖이면
    /// 재분해 결과가 화면에 영영 반영되지 않는다.
    /// </summary>
    [Fact]
    public void Signature_ChangesWhenPlacementsMove()
    {
        var part = SignaturePart();
        var moved = SignaturePart(y: 0.31);

        Assert.NotEqual(
            SceneMeshSignature.Compute([(SharedMesh, part)]),
            SceneMeshSignature.Compute([(SharedMesh, moved)]));
    }

    /// <summary>표면 표시도 합성 입력이다 — 같은 mesh·같은 좌표라도 서명이 달라야 한다.</summary>
    [Fact]
    public void Signature_ChangesWhenTheSurfaceMarkChanges()
    {
        var plain = SignaturePart();
        var marked = SignaturePart(surface: PartSurface.Ground);

        Assert.NotEqual(
            SceneMeshSignature.Compute([(SharedMesh, plain)]),
            SceneMeshSignature.Compute([(SharedMesh, marked)]));
    }

    /// <summary>
    /// **유도 규칙이 바뀌면 저장된 레이아웃도 낡는다** (#18 §4.6 · SC-07).
    ///
    /// 서명이 mesh 조합만 담으면 공식을 고쳐도 캐시가 그대로 남아 수정이 화면에 보이지
    /// 않는다 — 카메라 사이클에서 실측한 결함이다. 판이 섞여 있어야 재합성이 일어난다.
    /// </summary>
    [Fact]
    public void Signature_DependsOnTheCompositionVersion()
    {
        var inputs = new (Guid MeshId, AssetPart Part)[]
        {
            (Guid.NewGuid(), SignaturePart()),
            (Guid.NewGuid(), SignaturePart()),
        };
        var joined = string.Join(
            ",", inputs.Select(i => i.MeshId).OrderBy(id => id).Select(id => id.ToString("n")));

        // 판을 뺀 문자열의 해시와 같으면 규칙 변경이 캐시에 반영되지 않는다
        var withoutVersion = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(joined)));

        Assert.NotEqual(withoutVersion, SceneMeshSignature.Compute(inputs));

        // 판이 다르면 서명도 달라야 한다 — 이 관계가 성립해야 재합성이 일어난다.
        // 상수 자기 자신을 확인하는 것은 규칙 변경을 강제하지 못하므로 관계를 잠근다
        var previousVersion = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    $"v{SceneMeshSignature.CompositionVersion - 1}:{joined}|")));

        Assert.NotEqual(previousVersion, SceneMeshSignature.Compute(inputs));
    }

    private static readonly Guid SharedMesh = Guid.NewGuid();

    /// <summary>
    /// 배치를 가진 파츠 — 좌표·표시가 서명에 미치는 영향을 재는 재료.
    ///
    /// 파츠는 작업을 거쳐 만든다. 생성자가 internal 이기도 하고, 실제 경로가 붙이는
    /// 값들(Ordinal·JobId)이 서명에 섞이는지도 함께 보게 된다.
    /// </summary>
    private static AssetPart SignaturePart(
        double y = 0.3, PartSurface surface = PartSurface.None, Bounds[]? placements = null)
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero), Guid.NewGuid(), "gemini");
        job.ApplyParts(["포장"]);
        job.ApplyPartDetails([
            new PartDetail(
                "포장", "지형", "포장 석재",
                placements ?? [new Bounds(0.1, y, 0.5, 0.4)], 0, [], surface)]);

        return job.Parts[0];
    }

    // ─── 카메라 수치화 — 프론트 sceneCameraPose 와 같은 규칙 ───

    [Fact]
    public void Camera_ReadsMetersFromFreeText()
    {
        var camera = SceneStaging.ComposeCamera(new CameraSpec("light", "지면에서 1.7m", 0.5), []);

        Assert.Equal(1.7, camera.Position.Y, precision: 3);
        Assert.Equal(50, camera.FieldOfViewDegrees);
    }

    [Fact]
    public void Camera_ClampsExtremeHeights()
    {
        // 상한 15 — isometric 참조가 10~12m 를 정당하게 쓴다 (실측: "지면에서 약 12m").
        // 이전 상한 6 은 그 높이를 깎아 부감 구도를 재현하지 못했다
        Assert.Equal(15, SceneStaging.ComposeCamera(new CameraSpec("l", "120m 상공", 0.5), []).Position.Y, 3);
        Assert.Equal(0.8, SceneStaging.ComposeCamera(new CameraSpec("l", "0.1m", 0.5), []).Position.Y, 3);
    }

    /// <summary>
    /// **높은 카메라는 장면을 내려봐야 한다** (실측 결함). isometric 12m 참조에서
    /// horizonY(0.38)만으로는 6° — 12m 상공에서 거의 수평을 보는 카메라가 나와
    /// 평가 렌더가 원본과 전혀 다른 구도가 됐다. 눈높이를 넘는 높이만큼
    /// 응시점이 지면 쪽으로 당겨져야 한다.
    /// </summary>
    [Fact]
    public void Camera_HighViewpointLooksDownAtTheScene()
    {
        var camera = SceneStaging.ComposeCamera(new CameraSpec("isometric", "지면에서 약 12m", 0.38), []);

        Assert.Equal(12, camera.Position.Y, precision: 3);

        // 응시점이 지면 부근(장면 중심)으로 내려온다 — 내려보는 각 ≈ 40° 이상
        var pitch = Math.Atan2(camera.Position.Y - camera.Target.Y, 13) * 180 / Math.PI;
        Assert.InRange(pitch, 35, 60);
        Assert.InRange(camera.Target.Y, -1, 3);
    }

    /// <summary>부감 키워드 — 미터 표기가 없어도 높은 기본값을 잡는다.</summary>
    [Fact]
    public void Camera_AerialKeywordsRaiseTheDefaultHeight()
    {
        Assert.True(SceneStaging.ComposeCamera(new CameraSpec("부감", "부감 시점", 0.3), []).Position.Y >= 8);
        Assert.True(SceneStaging.ComposeCamera(new CameraSpec("isometric", "쿼터뷰", 0.3), []).Position.Y >= 8);
    }

    [Fact]
    public void Camera_DefaultsWhenUnparsable()
    {
        Assert.Equal(2.2, SceneStaging.ComposeCamera(new CameraSpec("l", "???", 0.5), []).Position.Y, 3);
        Assert.Equal(2.2, SceneStaging.ComposeCamera(null, []).Position.Y, 3);
    }

    /// <summary>지평선이 가운데보다 위(0.5 미만)면 내려본다 — 응시점이 눈높이보다 낮다.</summary>
    [Fact]
    public void Camera_PitchFollowsTheHorizon()
    {
        var level = SceneStaging.ComposeCamera(new CameraSpec("l", "eye", 0.5), []);
        var down = SceneStaging.ComposeCamera(new CameraSpec("l", "eye", 0.4), []);

        Assert.Equal(level.Position.Y, level.Target.Y, precision: 3);
        Assert.True(down.Target.Y < down.Position.Y);
    }

    // ─── 프레이밍 — 카메라는 합성된 내용을 담아야 한다 ───

    /// <summary>
    /// 실측 장면 — 유적 광장 참조(1280×797)의 합성 결과에서 뽑은 대표 배치.
    /// 먼 줄의 큰 유적(z≈-20, scale 15~19)과 코앞의 포장 석재(z≈-1.7)가 함께 있다.
    /// </summary>
    private static IReadOnlyList<SceneInstance> RuinsPlaza() =>
    [
        Instance(-10.78, -20.45, 19.20),
        Instance(12.29, -20.45, 19.20),
        Instance(-5.71, -21.33, 17.07),
        Instance(11.92, -21.69, 16.85),
        Instance(-16.92, -21.69, 15.47),
        Instance(13.41, -18.11, 12.97),
        Instance(-7.24, -16.37, 6.61),
        Instance(1.33, -1.74, 5.27),
    ];

    private static SceneInstance Instance(double x, double z, double scale)
        => new(Guid.NewGuid(), 0, x, 0, z, rotationY: 0, scale);

    /// <summary>
    /// **카메라가 장면을 담아야 한다** (실측 결함).
    ///
    /// 카메라 리그가 z=+9 → z=-4 고정이라 합성 결과를 보지 못했다. 유적 광장 실측에서
    /// 내용은 z -21.7~-0.2 에 퍼지는데 카메라는 z=-4 를 봐서, 프레임 아래 3분의 1이 빈
    /// 바닥이고 먼 줄의 큰 유적 11/25 는 위로 잘려 나갔다 — 원본과 전혀 다른 구도다.
    /// </summary>
    [Fact]
    public void Camera_FramesEveryInstance()
    {
        var instances = RuinsPlaza();

        var camera = SceneStaging.ComposeCamera(
            new CameraSpec("isometric", "지면에서 약 12m", 0.38), instances);

        // 세로 화각의 절반이 이루는 원뿔 — 가로는 더 넓으므로 이 안이면 프레임 안이다
        var half = camera.FieldOfViewDegrees / 2;

        foreach (var instance in instances)
        {
            foreach (var corner in Corners(instance))
            {
                Assert.InRange(AngleFromAxis(camera, corner), 0, half);
            }
        }
    }

    /// <summary>배치가 넓어지면 카메라도 그만큼 물러선다 — 고정 거리면 잘린다.</summary>
    [Fact]
    public void Camera_StandsBackFartherForWiderScenes()
    {
        var spec = new CameraSpec("isometric", "지면에서 약 12m", 0.38);

        var near = Distance(SceneStaging.ComposeCamera(spec, [Instance(0, -4, 2)]));
        var far = Distance(SceneStaging.ComposeCamera(spec, RuinsPlaza()));

        Assert.True(far > near * 2, $"넓은 장면 {far:F1} 이 좁은 장면 {near:F1} 보다 멀어야 합니다");
    }

    /// <summary>인스턴스가 없으면 유도할 내용이 없다 — 기본 리그로 돌아가고 NaN 을 내지 않는다.</summary>
    [Fact]
    public void Camera_FallsBackWhenThereIsNoContent()
    {
        var camera = SceneStaging.ComposeCamera(new CameraSpec("l", "지면에서 1.7m", 0.5), []);

        Assert.True(double.IsFinite(camera.Position.Y));
        Assert.True(double.IsFinite(camera.Target.Y));
        Assert.True(Distance(camera) > 0);
    }

    /// <summary>인스턴스 하나가 지면 아래로 카메라를 밀어넣지 않는다 (올려보는 지평선).</summary>
    [Fact]
    public void Camera_StaysAboveGround()
    {
        // horizonY 0.7 — 지평선이 화면 아래쪽, 즉 올려보는 구도다
        var camera = SceneStaging.ComposeCamera(
            new CameraSpec("l", "지면에서 1.6m", 0.7), RuinsPlaza());

        Assert.True(camera.Position.Y > 0, $"카메라가 지면 위에 있어야 합니다 (Y={camera.Position.Y:F2})");
    }

    /// <summary>인스턴스 박스의 여덟 꼭짓점 — scale 은 정규화된 모델의 높이다 (§3.2 계약).</summary>
    private static IEnumerable<ScenePoint> Corners(SceneInstance instance)
    {
        var half = instance.Scale / 2;

        foreach (var dx in new[] { -half, half })
        {
            foreach (var dz in new[] { -half, half })
            {
                yield return new ScenePoint(instance.X + dx, instance.Y, instance.Z + dz);
                yield return new ScenePoint(instance.X + dx, instance.Y + instance.Scale, instance.Z + dz);
            }
        }
    }

    /// <summary>카메라 축에서 벗어난 각(도).</summary>
    private static double AngleFromAxis(SceneCamera camera, ScenePoint point)
    {
        var (ax, ay, az) = Delta(camera.Position, camera.Target);
        var (bx, by, bz) = Delta(camera.Position, point);

        var dot = (ax * bx) + (ay * by) + (az * bz);
        var lengths = Math.Sqrt((ax * ax) + (ay * ay) + (az * az))
            * Math.Sqrt((bx * bx) + (by * by) + (bz * bz));

        return Math.Acos(Math.Clamp(dot / lengths, -1, 1)) * 180 / Math.PI;
    }

    private static double Distance(SceneCamera camera)
    {
        var (dx, dy, dz) = Delta(camera.Position, camera.Target);

        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static (double X, double Y, double Z) Delta(ScenePoint from, ScenePoint to)
        => (to.X - from.X, to.Y - from.Y, to.Z - from.Z);

    // ─── 조명 수치화 — 프론트 sceneLightRig 와 같은 규칙 ───

    [Fact]
    public void Light_ReadsQuadrantAndElevationFromKoreanFreeText()
    {
        var rig = SceneStaging.ComposeLight(new LightSpec(
            "좌측 후방 20° 방위, 28° 고도",
            "차가운 회청색 환경광과 좌측 후방의 약한 따뜻한 황갈색 광원",
            "soft"));

        Assert.Equal(225, rig.AzimuthDegrees);
        Assert.Equal(28, rig.ElevationDegrees, precision: 3);
        Assert.Equal("#ffd9a8", rig.KeyColor);      // 주광은 따뜻하고
        Assert.Equal("#e4edff", rig.AmbientColor);  // 환경광은 차갑다
    }

    [Fact]
    public void Light_DefaultsToThePaintingConvention()
    {
        var rig = SceneStaging.ComposeLight(null);

        Assert.Equal(225, rig.AzimuthDegrees);
        Assert.Equal(40, rig.ElevationDegrees, precision: 3);
        Assert.Equal("#ffffff", rig.KeyColor);
    }

    [Fact]
    public void Light_ClampsElevation()
    {
        Assert.Equal(75, SceneStaging.ComposeLight(new LightSpec("89° 고도", "", "s")).ElevationDegrees, 3);
        Assert.Equal(15, SceneStaging.ComposeLight(new LightSpec("2° 고도", "", "s")).ElevationDegrees, 3);
    }
}
