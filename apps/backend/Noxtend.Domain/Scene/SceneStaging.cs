using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Noxtend.Domain.Job;

namespace Noxtend.Domain.Scene;

/// <summary>월드 좌표 한 점 — System.Numerics.Vector3 는 필드 기반이라 JSON 직렬화가 비어 버린다.</summary>
public sealed record ScenePoint(double X, double Y, double Z);

/// <summary>평가 렌더와 화면이 함께 쓰는 수치 카메라 (§4.2). 각도는 도 단위.</summary>
public sealed record SceneCamera(
    ScenePoint Position,
    ScenePoint Target,
    double FieldOfViewDegrees);

/// <summary>수치 조명 — 방위는 정면(+z)에서 시계 방향, 고도는 지면 기준 (§4.2).</summary>
public sealed record SceneLightRig(
    double AzimuthDegrees,
    double ElevationDegrees,
    double KeyIntensity,
    string KeyColor,
    double AmbientIntensity,
    string AmbientColor);

/// <summary>
/// mesh 조합의 서명. Design Ref: background-similarity-tuning §4.1
///
/// **개수만 세면 같은 개수의 교체를 못 본다** — 재생성으로 mesh 하나가 바뀌어도
/// 개수는 그대로라 낡은 명세가 계속 쓰인다. 정렬된 id 의 SHA-256 이 정본이다.
/// </summary>
public static class SceneMeshSignature
{
    /// <summary>
    /// 유도 규칙의 판. **규칙이 바뀌면 올린다** (background-scale-calibration #18 §4.6 · D-06).
    ///
    /// 서명이 mesh 조합만 담으면 유도 로직을 고쳐도 저장된 레이아웃이 그대로 남아 수정이
    /// 화면에 보이지 않는다 — 카메라 사이클에서 실측한 결함이다. 판을 섞으면 다음 조회에서
    /// 새 revision 이 합성된다.
    ///
    /// 2 — 사이클 #18: 배치별 접지 판정, 파츠당 단일 월드 배율
    /// 3 — 사이클 #19: 지면 역투영. 자리·크기가 한 기하에서 나온다
    /// 4 — 사이클 #20: 표면 파츠. 면을 덮는 배치가 축별 배율로 눕거나 선다
    /// </summary>
    public const int CompositionVersion = 4;

    /// <summary>
    /// 합성 입력 전체의 서명 — mesh 조합과 **분해 결과**를 함께 담는다.
    ///
    /// **mesh id 만으로는 부족하다** (#20). 재분해는 파츠를 이름으로 제자리 갱신하므로
    /// `PartId` 도 `GeneratedMeshes` 도 그대로다. 그래서 표면 표시와 배치 좌표가 통째로
    /// 바뀌어도 서명이 움직이지 않고, 저장된 낡은 레이아웃이 계속 쓰인다 — 유료 재분해를
    /// 하고도 화면이 그대로인 결함이다. 표시와 배치가 합성의 입력인 이상 서명에 들어간다.
    ///
    /// **재료는 <see cref="PipelineJob.LayoutSignatureInputs"/> 하나에서만 온다.** 저장하는
    /// 쪽과 비교하는 쪽이 조금이라도 갈리면 서명이 영원히 어긋나 유사도가 통째로 막힌다.
    /// 인자를 하나로 묶어 둔 것이 그 갈림을 타입으로 막는다.
    /// </summary>
    public static string Compute(IReadOnlyList<(Guid MeshId, AssetPart Part)> inputs)
    {
        var joined = string.Join(
            ",", inputs.Select(entry => entry.MeshId).OrderBy(id => id).Select(id => id.ToString("n")));

        // 파츠 순서가 흔들려도 같은 서명이어야 한다 — 합성 결과가 순서에 의존하지 않는다.
        // 배치 **안의** 순서는 정렬하지 않는다: 모델이 낸 순서가 곧 Ordinal 이라
        // 순서가 바뀌면 실제로 다른 합성이다 (AssetPart.ApplyDetail 계약)
        var decomposed = string.Join(";", inputs
            .Select(entry =>
                $"{entry.Part.Id:n}:{(int)entry.Part.Surface}:{entry.Part.DepthOrder ?? 0}:" +
                string.Join(",", entry.Part.Placements.Select(Coordinates)))
            .OrderBy(entry => entry, StringComparer.Ordinal));

        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"v{CompositionVersion}:{joined}|{decomposed}")));
    }

    /// <summary>
    /// 배치 좌표의 문자열 표현.
    ///
    /// **불변 문화권이어야 한다** — 프로젝트에 `InvariantGlobalization` 설정이 없어서,
    /// 기본 문화권을 쓰면 `de-DE` 컨테이너에서 `0.82` 가 `0,82` 로 나와 같은 데이터가
    /// 다른 서명을 낸다. 로케일이 다른 인스턴스 둘이 서로의 레이아웃을 끝없이 무효화한다
    /// (NFR-01 결정성).
    /// </summary>
    private static string Coordinates(Bounds bounds)
        => string.Create(
            CultureInfo.InvariantCulture, $"{bounds.X:R}/{bounds.Y:R}/{bounds.W:R}/{bounds.H:R}");
}

/// <summary>
/// 자유 문장 카메라·조명 힌트 → 수치. Design Ref: §4.2 · D-03
///
/// **서버가 정본이다.** 브라우저가 해석하면 평가 렌더의 결정성이 깨지고 규칙이 두 곳에
/// 생긴다. 프론트 `sceneCameraPose`·`sceneLightRig` 의 규칙을 그대로 이식했다 —
/// 모델의 힌트는 자유 문장이라("지면에서 1.7m" 실측) 숫자 → 키워드 → 기본값 순으로
/// 가늠하고 극단값은 장면 안으로 누른다.
/// </summary>
public static partial class SceneStaging
{
    private const double EyeDefault = 2.2;
    private const double FieldOfView = 50;

    // 인스턴스가 없을 때의 고정 리그 — 카메라(z=+9)에서 응시점(z=-4)까지 13 유닛.
    // GazeDistance 는 "이 높이면 얼마나 내려보는가" 를 각도로 옮기는 기준 거리이기도 하다
    private const double CameraZ = 9;
    private const double TargetZ = -4;
    private const double GazeDistance = CameraZ - TargetZ;

    // 프레임 가장자리에 딱 붙지 않도록 하는 여유 — 참조도 대개 여백을 둔다
    private const double FramingMargin = 1.05;

    // 인스턴스가 한 점으로 모여도 카메라가 겹치지 않을 만큼은 물러선다
    private const double MinFramingDistance = 1;

    // 카메라 높이 하한 — 눈높이 텍스트의 하한과 같다
    private const double MinCameraHeight = 0.8;

    /// <summary>
    /// 눈높이 문장 → 미터. Design Ref: background-placement-projection(#19) §3.2
    ///
    /// **이 값은 형상에 영향을 주지 않는다.** 지면 역투영에서 거리·자리·크기가 모두
    /// 눈높이에 비례하므로, 보정 계수(기준 물체 높이 ÷ 기준 배율)가 그 비례를 정확히
    /// 상쇄한다. 분석 모델이 얼마나 틀리게 잡든 장면의 모양은 같다.
    /// </summary>
    public static double EyeHeight(CameraSpec? spec)
    {
        var height = EyeDefault;

        if (spec is not null)
        {
            var text = $"{spec.Type} {spec.EyeLevel}".ToLowerInvariant();

            // "1.7m" 류의 미터 표기가 가장 믿을 만한 신호다
            var meters = MetersPattern().Match(text);
            if (meters.Success)
            {
                height = double.Parse(meters.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            else if (AerialPattern().IsMatch(text))
            {
                // 부감·isometric — 미터 표기가 없어도 높은 시점이다
                height = 10;
            }
            else if (LowPattern().IsMatch(text))
            {
                height = 1.2;
            }
            else if (HighPattern().IsMatch(text))
            {
                height = 4.5;
            }

            // 상한 15 — isometric 참조가 10~12m 를 정당하게 쓴다 (실측 "지면에서 약 12m").
            // 이전 상한 6 은 그 높이를 깎아 부감 구도를 재현하지 못했다
            height = Math.Clamp(height, 0.8, 15);
        }

        return height;
    }

    /// <summary>
    /// 카메라가 지면을 내려보는 각(라디안). Design Ref: #19 §3.1 — D-01
    ///
    /// **배치 유도와 카메라가 이 값을 함께 쓴다.** 이전에는 배치가 `horizonY` 만
    /// 선형 근사로 보고 카메라는 `horizonY + 눈높이` 를 봐서, 같은 장면에 두 개의 각이
    /// 있었다 — 두 계산이 서로 다른 카메라를 상정하던 결함의 뿌리다.
    ///
    /// 지평선이 화면 가운데에서 벗어난 만큼 기울이고, **높은 시점은 그만큼 더 내려본다**
    /// (실측): 12m 상공에서 horizonY 만으로는 6° 라 348m 짜리 장면이 나온다.
    /// </summary>
    public static double PitchDown(CameraSpec? spec)
    {
        var horizonY = spec?.HorizonY ?? 0.5;
        var horizonPitch = (0.5 - horizonY) * FieldOfView * Math.PI / 180;
        var heightPitch = Math.Atan2(Math.Max(0, EyeHeight(spec) - EyeDefault), GazeDistance);

        return Math.Min(horizonPitch + heightPitch, 75 * Math.PI / 180);
    }

    public static SceneCamera ComposeCamera(
        CameraSpec? spec,
        IReadOnlyList<SceneInstance> instances)
    {
        var height = EyeHeight(spec);
        var pitchDown = PitchDown(spec);

        // 합성 결과가 없으면 담을 것도 없다 — 눈높이 텍스트만으로 세운 고정 리그
        var target = ContentCenter(instances);
        if (target is null)
        {
            return new SceneCamera(
                new ScenePoint(0, height, CameraZ),
                new ScenePoint(0, height - (Math.Tan(pitchDown) * GazeDistance), TargetZ),
                FieldOfView);
        }

        // **각도는 텍스트가, 거리는 내용이 정한다** (실측 결함). 리그가 z=+9 → z=-4 로
        // 박혀 있어 34m 폭 장면에서 먼 줄이 통째로 프레임 위로 잘렸다. 눈높이 문장은
        // 배치가 얼마나 퍼졌는지 모르므로 기울기 힌트로만 쓰고, 서 있을 자리는 유도한다
        var distance = FramingDistance(instances, target, pitchDown);

        // 올려보는 구도(지평선이 화면 아래)에서 카메라가 지면 밑으로 내려가는 것을 막는다.
        // 기울기를 눕히면 담아야 할 깊이가 달라지므로 거리를 다시 잰다
        if (target.Y + (distance * Math.Sin(pitchDown)) < MinCameraHeight)
        {
            pitchDown = Math.Asin(Math.Clamp((MinCameraHeight - target.Y) / distance, -1, 1));
            distance = FramingDistance(instances, target, pitchDown);
        }

        return new SceneCamera(
            new ScenePoint(
                target.X,
                Math.Max(target.Y + (distance * Math.Sin(pitchDown)), MinCameraHeight),
                target.Z + (distance * Math.Cos(pitchDown))),
            target,
            FieldOfView);
    }

    /// <summary>
    /// 내용을 전부 담는 최소 응시 거리.
    ///
    /// 카메라 공간에서 한 점이 프레임 안에 있으려면 축에서 벗어난 거리가
    /// `(전방 성분 + 응시 거리) × tan(반화각)` 이하여야 한다. 이를 거리에 대해 풀면
    /// 점마다 필요한 거리가 나오고, 그 최댓값이 장면 전체를 담는 거리다.
    /// **세로 반화각을 반지름 방향에 그대로 쓴다** — 가로가 더 넓은 가로 프레임에서
    /// 안전한 쪽이다. 세로가 더 긴 참조는 가로가 좁아지므로 이 근사가 깨진다(미대응).
    /// </summary>
    private static double FramingDistance(
        IReadOnlyList<SceneInstance> instances,
        ScenePoint target,
        double pitchDown)
    {
        var tangent = Math.Tan(FieldOfView / 2 * Math.PI / 180);

        // 카메라 기저 — 전방은 응시점을 향해 내려가고, 위쪽은 그에 직교한다
        var (forwardY, forwardZ) = (-Math.Sin(pitchDown), -Math.Cos(pitchDown));
        var (upY, upZ) = (Math.Cos(pitchDown), -Math.Sin(pitchDown));

        var required = MinFramingDistance;

        foreach (var instance in instances)
        {
            foreach (var (x, y, z) in Corners(instance))
            {
                var (dx, dy, dz) = (x - target.X, y - target.Y, z - target.Z);

                var forward = (dy * forwardY) + (dz * forwardZ);
                var vertical = (dy * upY) + (dz * upZ);

                // 축에서 벗어난 반지름 — 가로(dx)와 세로를 함께 본다
                var radius = Math.Sqrt((dx * dx) + (vertical * vertical)) * FramingMargin;

                required = Math.Max(required, (radius / tangent) - forward);
            }
        }

        return required;
    }

    /// <summary>합성된 내용의 중심 — 인스턴스가 없으면 null.</summary>
    private static ScenePoint? ContentCenter(IReadOnlyList<SceneInstance> instances)
    {
        if (instances.Count == 0)
        {
            return null;
        }

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        foreach (var instance in instances)
        {
            foreach (var (x, y, z) in Corners(instance))
            {
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
            }
        }

        return new ScenePoint((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
    }

    /// <summary>
    /// 인스턴스가 차지하는 상자의 여덟 꼭짓점.
    ///
    /// 배율은 "높이 1 · 바닥 중심 원점" 으로 정규화한 모델에 곱하는 값이다
    /// (scene-assembly §3.2 계약). **축별로 읽어야 한다** — 표면은 두께가 얇고 가로가
    /// 넓어서, 높이 하나로 어림하면 30m 짜리 포장이 1.4m 로 잡혀 프레임 밖으로 나간다.
    /// </summary>
    private static IEnumerable<(double X, double Y, double Z)> Corners(SceneInstance instance)
    {
        var halfX = instance.ScaleX / 2;
        var halfZ = instance.ScaleZ / 2;

        foreach (var dx in new[] { -halfX, halfX })
        {
            foreach (var dz in new[] { -halfZ, halfZ })
            {
                yield return (instance.X + dx, instance.Y, instance.Z + dz);
                yield return (instance.X + dx, instance.Y + instance.ScaleY, instance.Z + dz);
            }
        }
    }

    public static SceneLightRig ComposeLight(LightSpec? spec)
    {
        if (spec is null)
        {
            // 회화 관습 — 좌후방 상단
            return new SceneLightRig(225, 40, 1.3, KeyColors["neutral"], 0.8, AmbientColors["neutral"]);
        }

        var direction = spec.Direction.ToLowerInvariant();

        var azimuth = Azimuths.FirstOrDefault(entry => entry.Pattern.IsMatch(direction)).Degrees;
        if (azimuth == 0 && !FrontPattern().IsMatch(direction))
        {
            azimuth = 225;
        }

        var elevationMatch = ElevationPattern().Match(direction);
        var elevation = elevationMatch.Success
            ? double.Parse(elevationMatch.Groups[1].Value, CultureInfo.InvariantCulture)
            : UpperPattern().IsMatch(direction) ? 50 : 40;
        elevation = Math.Clamp(elevation, 15, 75);

        // 주광과 환경광이 다르게 서술된다 — "환경광" 절을 비탐욕으로 떼서 따로 읽는다
        var temperature = spec.Temperature.ToLowerInvariant();
        var ambientPart = AmbientClausePattern().Match(temperature).Value;
        // 환경광 절이 없으면 빈 문자열 — Replace 가 빈 oldValue 를 거부하므로 건너뛴다
        var keyPart = ambientPart.Length == 0 ? temperature : temperature.Replace(ambientPart, "");

        var keyTone = ToneOf(keyPart) ?? ToneOf(temperature) ?? "neutral";
        var ambientTone = ToneOf(ambientPart) ?? "neutral";

        return new SceneLightRig(
            azimuth, elevation, 1.3, KeyColors[keyTone], 0.8, AmbientColors[ambientTone]);
    }

    private static readonly IReadOnlyDictionary<string, string> KeyColors =
        new Dictionary<string, string>
        {
            ["warm"] = "#ffd9a8", ["neutral"] = "#ffffff", ["cool"] = "#cfe0ff",
        };

    private static readonly IReadOnlyDictionary<string, string> AmbientColors =
        new Dictionary<string, string>
        {
            ["warm"] = "#fff3e0", ["neutral"] = "#ffffff", ["cool"] = "#e4edff",
        };

    // 방위 사분면 — 키워드 조합의 평균. 순서가 우선순위다 (사분면 조합이 단일 키워드보다 먼저)
    private static readonly (Regex Pattern, double Degrees)[] Azimuths =
    [
        (new Regex("(좌|left).*(후방|back)|(후방|back).*(좌|left)"), 225),
        (new Regex("(우|right).*(후방|back)|(후방|back).*(우|right)"), 135),
        (new Regex("(좌|left).*(전방|front)|(전방|front).*(좌|left)"), 315),
        (new Regex("(우|right).*(전방|front)|(전방|front).*(우|right)"), 45),
        (new Regex("좌|left"), 270),
        (new Regex("우|right"), 90),
        (new Regex("후방|back"), 180),
        (new Regex("전방|front"), 0),
    ];

    private static string? ToneOf(string text)
    {
        if (WarmPattern().IsMatch(text)) return "warm";
        if (CoolPattern().IsMatch(text)) return "cool";
        return null;
    }

    [GeneratedRegex(@"부감|조감|항공|쿼터\s*뷰|isometric|aerial|bird|top\s*-?\s*down|overhead")]
    private static partial Regex AerialPattern();

    [GeneratedRegex(@"([0-9]+(?:\.[0-9]+)?)\s*m")]
    private static partial Regex MetersPattern();

    [GeneratedRegex("low|낮|로우")]
    private static partial Regex LowPattern();

    [GeneratedRegex("high|높|부감|조감|bird")]
    private static partial Regex HighPattern();

    [GeneratedRegex(@"([0-9]+(?:\.[0-9]+)?)\s*°?\s*(?:고도|elevation)|(?:고도|elevation)\s*([0-9]+(?:\.[0-9]+)?)")]
    private static partial Regex ElevationPattern();

    [GeneratedRegex("upper|상단|높")]
    private static partial Regex UpperPattern();

    [GeneratedRegex("전방|front")]
    private static partial Regex FrontPattern();

    [GeneratedRegex("[^,.]*?(?:환경광|ambient)")]
    private static partial Regex AmbientClausePattern();

    [GeneratedRegex("따뜻|warm|황갈|골드|golden|주황")]
    private static partial Regex WarmPattern();

    [GeneratedRegex("차가|cool|회청|푸른|blue")]
    private static partial Regex CoolPattern();
}
