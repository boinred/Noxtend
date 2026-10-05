using Noxtend.Application.Scene;

namespace Noxtend.Api.Contracts;

/// <summary>Design Ref: scene-assembly §4.2 — 빈 instances 는 오류가 아니라 상태다.</summary>
public sealed record SceneLayoutResponse(
    Guid Id,
    int Revision,
    string SourceMeshSignature,
    IReadOnlyList<SceneInstanceResponse> Instances,
    SceneCameraResponse? Camera,
    SceneLightResponse? Light,
    NumericCameraResponse NumericCamera,
    NumericLightResponse NumericLight,
    IReadOnlyList<string> MissingPartNames,
    DateTimeOffset ComposedAt,
    SceneCompositionResponse? Composition)
{
    public static SceneLayoutResponse From(SceneLayoutView view)
        => new(
            view.Id,
            view.Revision,
            view.SourceMeshSignature,
            [.. view.Instances.Select(SceneInstanceResponse.From)],
            view.Camera is null ? null : new SceneCameraResponse(view.Camera.EyeLevel, view.Camera.HorizonY),
            view.Light is null ? null : new SceneLightResponse(view.Light.Direction, view.Light.Temperature),
            NumericCameraResponse.From(view.NumericCamera),
            NumericLightResponse.From(view.NumericLight),
            view.MissingPartNames,
            view.ComposedAt,
            view.Composition is null ? null : new SceneCompositionResponse(
                view.Composition.GroundedCount,
                view.Composition.ElevatedCount,
                view.Composition.ScaleDepthForElevated,
                view.Composition.AnchorSpread,
                view.Composition.SurfaceCount,
                view.Composition.AnchorIsSurface,
                view.Composition.DroppedCount,
                view.Composition.SurfaceShapeMismatch));
}

/// <summary>
/// 유도가 내린 판단 (background-scale-calibration #18 §4.7).
///
/// 사이클 #18 이전에 합성된 레이아웃에는 없다 — nullable 이 그 사실이다.
/// `anchorSpread` 는 기준 물체 배치들의 크기 편차로, 1 에서 멀수록 보정 계수를 덜 믿을 만하다.
/// </summary>
public sealed record SceneCompositionResponse(
    int GroundedCount,
    int ElevatedCount,
    double ScaleDepthForElevated,
    double AnchorSpread,
    int SurfaceCount,
    /// <summary>크기 기준 물체가 표면으로 표시됐다 — 보정 계수를 못 믿을 신호 (#20 §6).</summary>
    bool AnchorIsSurface,
    /// <summary>인스턴스 상한에 걸려 버려진 배치 수 (#20 FR-06).</summary>
    int DroppedCount,
    /// <summary>`ground` 로 표시됐으나 GLB 는 서 있는 형태인 파츠 수 — 부차 신호 (#20 §4.1).</summary>
    int SurfaceShapeMismatch);

/// <summary>
/// 수치 카메라 — 서버가 정본 (background-similarity-tuning D-03). 평가 렌더와 화면이
/// 같은 값을 써야 캡처가 결정적이다.
/// </summary>
public sealed record NumericCameraResponse(
    SceneVectorResponse Position, SceneVectorResponse Target, double FieldOfViewDegrees)
{
    public static NumericCameraResponse From(Noxtend.Domain.Scene.SceneCamera camera)
        => new(
            new SceneVectorResponse(camera.Position.X, camera.Position.Y, camera.Position.Z),
            new SceneVectorResponse(camera.Target.X, camera.Target.Y, camera.Target.Z),
            camera.FieldOfViewDegrees);
}

public sealed record NumericLightResponse(
    double AzimuthDegrees,
    double ElevationDegrees,
    double KeyIntensity,
    string KeyColor,
    double AmbientIntensity,
    string AmbientColor)
{
    public static NumericLightResponse From(Noxtend.Domain.Scene.SceneLightRig light)
        => new(
            light.AzimuthDegrees, light.ElevationDegrees, light.KeyIntensity,
            light.KeyColor, light.AmbientIntensity, light.AmbientColor);
}

public sealed record SceneInstanceResponse(
    Guid PartId,
    string PartName,
    Guid MeshId,
    int Ordinal,
    SceneVectorResponse Position,
    double RotationY,
    /// <summary>높이 — 정규화 기준축. 낱개 물건은 세 축이 이 값과 같다.</summary>
    double Scale,
    /// <summary>축별 배율 (#20 §4.2) — 표면 파츠에서만 갈린다.</summary>
    SceneVectorResponse ScaleVector)
{
    public static SceneInstanceResponse From(SceneInstanceView view)
        => new(view.PartId, view.PartName, view.MeshId, view.Ordinal,
            new SceneVectorResponse(view.X, view.Y, view.Z), view.RotationY, view.Scale,
            new SceneVectorResponse(view.ScaleX, view.ScaleY, view.ScaleZ));
}

public sealed record SceneVectorResponse(double X, double Y, double Z);

/// <summary>초기 카메라 힌트 — 첫인상이 원본과 닮아야 한다 (§5.2).</summary>
public sealed record SceneCameraResponse(string EyeLevel, double HorizonY);

public sealed record SceneLightResponse(string Direction, string Temperature);
