namespace Noxtend.Domain.Scene;

/// <summary>allowlist 위반 — API 계층이 EvaluationInvalid 계열 400 으로 옮긴다.</summary>
public sealed class SceneAdjustmentException(string message) : Exception(message);

/// <summary>
/// 보정 명령 allowlist (§6 · D-06) — 임의 property path 대신 이 여섯 형태만 존재한다.
/// 각도 입력은 도 단위, 저장(RotationY)은 라디안.
///
/// JSON discriminator 는 평가 결과 저장(JSON 열)과 API 계약이 함께 쓴다 — 알 수 없는
/// 타입은 역직렬화에서 거부된다.
/// </summary>
[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ScaleScene), "scaleScene")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(MoveInstance), "moveInstance")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(RotateInstance), "rotateInstance")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ScaleInstance), "scaleInstance")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AdjustCamera), "adjustCamera")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AdjustLight), "adjustLight")]
public abstract record SceneAdjustmentCommand;

/// <summary>장면 전체 배율 — 위치·크기를 함께 키운다 (크기만 키우면 간격이 어긋난다).</summary>
public sealed record ScaleScene(double Factor) : SceneAdjustmentCommand;

/// <summary>인스턴스 평면 이동 — Y 이동은 허용하지 않는다 (§6).</summary>
public sealed record MoveInstance(Guid PartId, int Ordinal, double DeltaX, double DeltaZ)
    : SceneAdjustmentCommand;

public sealed record RotateInstance(Guid PartId, int Ordinal, double DeltaDegrees)
    : SceneAdjustmentCommand;

public sealed record ScaleInstance(Guid PartId, int Ordinal, double Factor)
    : SceneAdjustmentCommand;

public sealed record AdjustCamera(
    double YawDeltaDegrees,
    double PitchDeltaDegrees,
    double DistanceFactor,
    double FovDeltaDegrees) : SceneAdjustmentCommand;

public sealed record AdjustLight(
    double AzimuthDeltaDegrees,
    double ElevationDeltaDegrees,
    double IntensityFactor) : SceneAdjustmentCommand;

/// <summary>보정 적용 결과 — 후보 revision 의 재료 (CreateCandidate 로 넘어간다).</summary>
public sealed record SceneAdjustmentResult(
    IReadOnlyList<SceneInstance> Instances,
    SceneCamera Camera,
    SceneLightRig Light);

/// <summary>
/// 보정 명령 검증·적용 — 순수 함수 (§6).
///
/// Design Ref: background-similarity-tuning §6 · D-06
///
/// 1회 범위는 명령 값으로, 누적 한계는 **원점 revision(Composed/Restore 시작점) 대비
/// 결과 배율**로 검사한다 — 후보를 거듭하며 조금씩 밀면 한도를 우회할 수 있어서다.
/// 같은 이유로 같은 대상·같은 속성의 명령 중복도 거절한다.
/// </summary>
public static class SceneAdjuster
{
    // 한 후보의 명령 상한 (§13)
    private const int MaxCommands = 24;

    // 누적 배율 한계 — 원점 대비 (§6)
    private const double CumulativeScaleMin = 0.50;
    private const double CumulativeScaleMax = 2.00;

    public static SceneAdjustmentResult Apply(
        SceneLayout baseline,
        SceneLayout origin,
        IReadOnlyList<SceneAdjustmentCommand> commands)
    {
        if (commands.Count == 0)
        {
            throw new SceneAdjustmentException("적용할 보정 명령이 없습니다");
        }

        if (commands.Count > MaxCommands)
        {
            throw new SceneAdjustmentException($"보정 명령은 한 번에 {MaxCommands}개까지입니다");
        }

        RejectDuplicates(commands);

        // 원점 배율 조회 — 누적 한계의 분모
        var originScaleByKey = origin.Instances.ToDictionary(
            i => (i.PartId, i.Ordinal), i => i.Scale);

        // **축별 배율을 보존한다** (background-surface-parts #20 §4.2). 균일 생성자로
        // 다시 만들면 표면 인스턴스가 (18.17, 7.88, 29.91) → (7.88, 7.88, 7.88) 로
        // 무너져, 보정을 한 번만 돌아도 포장이 다시 정육면체 바위가 된다
        var instances = baseline.Instances.ToDictionary(
            i => (i.PartId, i.Ordinal), i => Copy(i));
        var camera = baseline.Camera;
        var light = baseline.Light;

        foreach (var command in commands)
        {
            switch (command)
            {
                case ScaleScene scene:
                    RequireFinite(scene.Factor);
                    RequireRange(scene.Factor, 0.80, 1.25, "장면 배율");
                    foreach (var key in instances.Keys.ToList())
                    {
                        var i = instances[key];
                        var scaled = i.Scale * scene.Factor;
                        RequireCumulative(scaled, originScaleByKey.GetValueOrDefault(key, i.Scale));
                        instances[key] = Scaled(i, scene.Factor, i.X * scene.Factor, i.Z * scene.Factor);
                    }

                    break;

                case MoveInstance move:
                {
                    RequireFinite(move.DeltaX, move.DeltaZ);
                    var i = Require(instances, move.PartId, move.Ordinal);
                    // 이동 한도는 크기 비례 — 큰 물체는 0.5m 로는 티도 안 난다.
                    // **바닥 크기로 잰다** (#20): 높이로 재면 30m 짜리 포장이 두께 0.36m
                    // 때문에 하한 0.5m 에 묶여 사실상 못 움직인다. 낱개는 세 축이 같아
                    // 이전과 수치가 동등하다
                    var limit = Math.Max(0.5, Math.Max(i.ScaleX, i.ScaleZ) * 0.25);
                    RequireRange(move.DeltaX, -limit, limit, "X 이동");
                    RequireRange(move.DeltaZ, -limit, limit, "Z 이동");
                    instances[(move.PartId, move.Ordinal)] =
                        Copy(i, x: i.X + move.DeltaX, z: i.Z + move.DeltaZ);
                    break;
                }

                case RotateInstance rotate:
                {
                    RequireFinite(rotate.DeltaDegrees);
                    RequireRange(rotate.DeltaDegrees, -15, 15, "회전");
                    var i = Require(instances, rotate.PartId, rotate.Ordinal);
                    instances[(rotate.PartId, rotate.Ordinal)] = Copy(
                        i, rotationY: i.RotationY + (rotate.DeltaDegrees * Math.PI / 180));
                    break;
                }

                case ScaleInstance scale:
                {
                    RequireFinite(scale.Factor);
                    RequireRange(scale.Factor, 0.80, 1.25, "인스턴스 배율");
                    var i = Require(instances, scale.PartId, scale.Ordinal);
                    var scaled = i.Scale * scale.Factor;
                    RequireCumulative(
                        scaled,
                        originScaleByKey.GetValueOrDefault((scale.PartId, scale.Ordinal), i.Scale));
                    instances[(scale.PartId, scale.Ordinal)] = Scaled(i, scale.Factor, i.X, i.Z);
                    break;
                }

                case AdjustCamera cam:
                    RequireFinite(cam.YawDeltaDegrees, cam.PitchDeltaDegrees, cam.DistanceFactor, cam.FovDeltaDegrees);
                    RequireRange(cam.YawDeltaDegrees, -10, 10, "카메라 yaw");
                    RequireRange(cam.PitchDeltaDegrees, -8, 8, "카메라 pitch");
                    RequireRange(cam.DistanceFactor, 0.90, 1.10, "카메라 거리 배율");
                    RequireRange(cam.FovDeltaDegrees, -5, 5, "화각");
                    camera = ApplyCamera(camera, cam);
                    break;

                case AdjustLight lit:
                    RequireFinite(lit.AzimuthDeltaDegrees, lit.ElevationDeltaDegrees, lit.IntensityFactor);
                    RequireRange(lit.AzimuthDeltaDegrees, -15, 15, "광원 방위");
                    RequireRange(lit.ElevationDeltaDegrees, -10, 10, "광원 고도");
                    RequireRange(lit.IntensityFactor, 0.80, 1.20, "광원 세기 배율");
                    light = light with
                    {
                        // 방위는 0..360 순환, 고도는 물리적으로 지평선 아래·천정 위가 없다
                        AzimuthDegrees = Wrap360(light.AzimuthDegrees + lit.AzimuthDeltaDegrees),
                        ElevationDegrees = Math.Clamp(light.ElevationDegrees + lit.ElevationDeltaDegrees, 0, 90),
                        KeyIntensity = light.KeyIntensity * lit.IntensityFactor,
                    };
                    break;

                default:
                    throw new SceneAdjustmentException($"알 수 없는 보정 명령: {command.GetType().Name}");
            }
        }

        return new SceneAdjustmentResult(
            [.. instances.Values.OrderBy(i => i.PartId).ThenBy(i => i.Ordinal)], camera, light);
    }

    /// <summary>yaw 는 응시점을 도는 수평 회전, pitch 는 고도 회전, distance 는 시선 축 배율.</summary>
    private static SceneCamera ApplyCamera(SceneCamera camera, AdjustCamera cam)
    {
        var dx = camera.Position.X - camera.Target.X;
        var dy = camera.Position.Y - camera.Target.Y;
        var dz = camera.Position.Z - camera.Target.Z;

        // 수평 회전 — 높이는 그대로
        var yaw = cam.YawDeltaDegrees * Math.PI / 180;
        var (rx, rz) = (
            (dx * Math.Cos(yaw)) - (dz * Math.Sin(yaw)),
            (dx * Math.Sin(yaw)) + (dz * Math.Cos(yaw)));

        // 고도 회전 — 수평 거리와 높이를 잇는 평면에서 돈다
        var pitch = cam.PitchDeltaDegrees * Math.PI / 180;
        var horizontal = Math.Sqrt((rx * rx) + (rz * rz));
        var elevation = Math.Atan2(dy, horizontal) + pitch;
        var distance = Math.Sqrt((rx * rx) + (rz * rz) + (dy * dy)) * cam.DistanceFactor;

        var newHorizontal = distance * Math.Cos(elevation);
        var ratio = horizontal == 0 ? 0 : newHorizontal / horizontal;

        return camera with
        {
            Position = new ScenePoint(
                camera.Target.X + (rx * ratio),
                camera.Target.Y + (distance * Math.Sin(elevation)),
                camera.Target.Z + (rz * ratio)),
            FieldOfViewDegrees = camera.FieldOfViewDegrees + cam.FovDeltaDegrees,
        };
    }

    /// <summary>같은 대상·같은 속성 두 번 금지 — 나눠 보내면 1회 한도를 우회할 수 있다.</summary>
    private static void RejectDuplicates(IReadOnlyList<SceneAdjustmentCommand> commands)
    {
        var seen = new HashSet<string>();
        foreach (var command in commands)
        {
            var key = command switch
            {
                ScaleScene => "scene-scale",
                MoveInstance m => $"move:{m.PartId:n}:{m.Ordinal}",
                RotateInstance r => $"rotate:{r.PartId:n}:{r.Ordinal}",
                ScaleInstance s => $"scale:{s.PartId:n}:{s.Ordinal}",
                AdjustCamera => "camera",
                AdjustLight => "light",
                _ => command.GetType().Name,
            };
            if (!seen.Add(key))
            {
                throw new SceneAdjustmentException($"같은 대상에 같은 보정을 두 번 적용할 수 없습니다: {key}");
            }
        }
    }

    private static SceneInstance Require(
        Dictionary<(Guid, int), SceneInstance> instances, Guid partId, int ordinal)
        => instances.GetValueOrDefault((partId, ordinal))
            ?? throw new SceneAdjustmentException($"기준 revision 에 없는 인스턴스입니다: {partId:n}/{ordinal}");

    /// <summary>세 축을 그대로 옮긴다 — 자리·회전만 바꿀 때 쓴다.</summary>
    private static SceneInstance Copy(
        SceneInstance i, double? x = null, double? z = null, double? rotationY = null)
        => new(i.PartId, i.Ordinal, x ?? i.X, i.Y, z ?? i.Z, rotationY ?? i.RotationY,
            i.ScaleX, i.ScaleY, i.ScaleZ);

    /// <summary>
    /// 세 축에 같은 배수를 곱한다 — 보정은 크기의 **비율**을 바꾸지 않는다.
    ///
    /// 표면의 폭·두께·깊이 관계는 배치 사각형이 정한 것이라 보정이 흔들 대상이 아니다.
    /// 균일 물체에서는 이전과 완전히 같은 결과다.
    /// </summary>
    private static SceneInstance Scaled(SceneInstance i, double factor, double x, double z)
        => new(i.PartId, i.Ordinal, x, i.Y, z, i.RotationY,
            i.ScaleX * factor, i.ScaleY * factor, i.ScaleZ * factor);

    private static void RequireCumulative(double resultScale, double originScale)
    {
        var cumulative = resultScale / originScale;
        if (cumulative < CumulativeScaleMin || cumulative > CumulativeScaleMax)
        {
            throw new SceneAdjustmentException(
                $"누적 배율이 한계를 벗어났습니다: {cumulative:0.###} (허용 {CumulativeScaleMin}..{CumulativeScaleMax})");
        }
    }

    // 방위 순환 — 0..360 밖으로 나가면 되감는다
    private static double Wrap360(double degrees)
        => ((degrees % 360) + 360) % 360;

    private static void RequireRange(double value, double min, double max, string name)
    {
        if (value < min || value > max)
        {
            throw new SceneAdjustmentException($"{name} 값이 허용 범위를 벗어났습니다: {value} (허용 {min}..{max})");
        }
    }

    private static void RequireFinite(params double[] values)
    {
        if (values.Any(v => !double.IsFinite(v)))
        {
            throw new SceneAdjustmentException("보정 값은 유한한 수여야 합니다");
        }
    }
}
