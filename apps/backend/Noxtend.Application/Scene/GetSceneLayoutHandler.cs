using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Scene.Projection;

namespace Noxtend.Application.Scene;

/// <summary>
/// 화면이 그릴 조립 명세 — 인스턴스에 파츠 이름·GLB id 를 붙인 형태.
/// revision 정체(id·번호·서명)와 수치 camera/light 는 유사도 캡처의 결정성 재료다
/// (background-similarity-tuning §8.1 · D-03). 문자열 힌트는 한 버전 유지 후 걷는다.
/// </summary>
public sealed record SceneLayoutView(
    Guid Id,
    int Revision,
    string SourceMeshSignature,
    IReadOnlyList<SceneInstanceView> Instances,
    CameraSpec? Camera,
    LightSpec? Light,
    SceneCamera NumericCamera,
    SceneLightRig NumericLight,
    IReadOnlyList<string> MissingPartNames,
    DateTimeOffset ComposedAt,
    CompositionSummary? Composition);

public sealed record SceneInstanceView(
    Guid PartId,
    string PartName,
    Guid MeshId,
    int Ordinal,
    double X,
    double Y,
    double Z,
    double RotationY,
    /// <summary>높이 — 정규화 기준축. 낱개 물건은 세 축이 이 값과 같다.</summary>
    double Scale,
    double ScaleX,
    double ScaleY,
    double ScaleZ);

/// <summary>
/// 조립 명세 조회 — 없거나 낡았으면 유도해 새 revision 으로 저장한다.
///
/// Design Ref: scene-assembly §3.4 · background-similarity-tuning §4.1 — **지연 생성.**
/// 낡음 판정은 mesh 서명이다: 개수만 세면 같은 개수의 교체(재생성)를 못 본다.
/// revision 은 불변이라 덮어쓰지 않는다 — 기존 활성을 Superseded 로 전이하고
/// 새 활성 revision 을 추가한다 (D-02).
/// </summary>
public sealed class GetSceneLayoutHandler(
    IJobRepository jobs,
    ISceneLayoutRepository layouts,
    IClock clock,
    IMeshExtentsReader? extents = null)
{
    public async Task<Result<SceneLayoutView>> HandleAsync(Guid jobId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<SceneLayoutView>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        // **서명과 같은 재료를 쓴다.** "파츠별 최신 GLB" 규칙을 여기서 다시 쓰면 동률에서
        // 갈린다 — 도메인은 내림차순 첫째를, 여기서 오름차순 마지막을 고르면 `CreatedAt` 이
        // 같은 재시도 두 건에서 서명은 A 를, 화면은 B 를 가리키고 스스로 낫지 않는다
        var signatureInputs = job.LayoutSignatureInputs();
        var withMesh = signatureInputs.Select(entry => entry.Part).ToList();
        var meshByPart = withMesh.ToDictionary(part => part.Id, part => job.LatestMeshFor(part.Id)!);

        // 기준 파츠는 GLB 완성 여부와 무관하게 전체 파츠에서 찾는다
        SceneScaleCalibration? calibration = null;
        if (job.Scene?.Scale.HeightMeters is { } heightMeters)
        {
            var anchor = job.Parts.FirstOrDefault(part =>
                string.Equals(part.Name, job.Scene.Scale.Object, StringComparison.Ordinal));
            if (!double.IsFinite(heightMeters) || heightMeters <= 0 ||
                anchor is null || anchor.Placements.Count == 0)
            {
                return Result<SceneLayoutView>.Fail(
                    ErrorCode.SceneIncomplete,
                    "장면의 스케일 기준 물체 또는 배치를 찾을 수 없습니다");
            }

            // 기준 물체가 표면이면 그 H 가 높이가 아니라 누운 면의 세로 범위다 —
            // 보정 계수가 어긋나므로 요약에 실어 운영자에게 알린다 (#20 §6)
            calibration = new SceneScaleCalibration(
                heightMeters, anchor.Placements, anchor.Surface != PartSurface.None);
        }

        // **빈 것은 오류가 아니라 상태다** (§4.2) — 화면이 "아직 조립할 3D 가 없다" 를 그린다
        var missing = job.Parts
            .Where(part => !meshByPart.ContainsKey(part.Id))
            .Select(part => part.Name)
            .ToList();

        // 합성 입력의 정체 — GLB 조합과 **분해 결과**를 함께 담는다 (§4.1 · #20).
        // 재료는 도메인이 한 번만 뽑는다 — 여기서 따로 만들면 비교하는 쪽과 갈린다
        var signature = SceneMeshSignature.Compute(job.LayoutSignatureInputs());

        // 형태(Extents)는 서명에 넣지 않는다 — 같은 GLB 는 같은 형태이므로 mesh id 가 대신한다
        var composeInputs = withMesh
            .Select(part => new SceneComposeInput(
                part.Id, part.DepthOrder, part.Placements, part.Surface))
            .ToList();

        var layout = await layouts.GetActiveByJobAsync(jobId, ct);

        // 낡음 판정 — 서명이 다르면 GLB 가 바뀌었거나 재분해로 표시·배치가 바뀐 것이다
        if (layout is null || layout.SourceMeshSignature != signature)
        {
            // 파츠마다 GLB 형태를 읽는다 — 바닥을 덮는 판은 크기 규칙이 다르다.
            // 합성은 캐시되므로 이 I/O 는 새 revision 을 만들 때만 일어난다
            var shapes = new Dictionary<Guid, MeshExtents?>();
            foreach (var part in withMesh)
            {
                shapes[part.Id] = extents is null
                    ? null
                    : await extents.ReadAsync(meshByPart[part.Id].Model.BlobKey, ct);
            }

            var composition = SceneLayoutComposer.ComposeWithSummary(
                [.. composeInputs.Select(part => part with { Extents = shapes[part.PartId] })],
                job.Scene?.Camera,
                calibration);
            var instances = composition.Instances;

            // 기존 활성은 이력으로 전이 — 삭제가 아니라 Superseded (D-02)
            layout?.MarkSuperseded();

            var revision = await layouts.MaxRevisionAsync(jobId, ct) + 1;
            layout = SceneLayout.ComposeActive(
                jobId, revision, instances, signature, withMesh.Count,
                SceneStaging.ComposeCamera(job.Scene?.Camera, instances),
                SceneStaging.ComposeLight(job.Scene?.Light),
                clock.Now,
                composition.Summary);
            await layouts.AddAsync(layout, ct);
        }

        var nameByPart = job.Parts.ToDictionary(part => part.Id, part => part.Name);

        return Result<SceneLayoutView>.Ok(new SceneLayoutView(
            layout.Id,
            layout.Revision,
            layout.SourceMeshSignature,
            [.. layout.Instances.Select(instance => new SceneInstanceView(
                instance.PartId,
                nameByPart.GetValueOrDefault(instance.PartId, string.Empty),
                meshByPart[instance.PartId].Id,
                instance.Ordinal,
                instance.X, instance.Y, instance.Z,
                instance.RotationY, instance.Scale,
                instance.ScaleX, instance.ScaleY, instance.ScaleZ))],
            job.Scene?.Camera,
            job.Scene?.Light,
            layout.Camera,
            layout.Light,
            missing,
            layout.ComposedAt,
            layout.Composition));
    }
}
