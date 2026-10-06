using Noxtend.Application.Job;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Job;

namespace Noxtend.Api.Contracts;

/// <summary>
/// Design Ref: §4.2 #6 — 폴링 대상. 상태·공정·결과.
///
/// **`tasks` 를 이번부터 내려보낸다.** 공정이 하나뿐이라 화면은 아직 쓰지 않지만,
/// 공정이 늘 때 응답 형태가 바뀌면 프론트 타입과 테스트가 전부 흔들린다.
///
/// **사이클 #5: `consistencyPrompt` → `scene`.** 자유 문장이 구조화 값으로 바뀐다.
/// 이 사이클 최대의 파장이다 — 화면·Fake·E2E 단정이 모두 따라온다 (Plan §6.2).
/// </summary>
public sealed record JobResponse(
    Guid Id,
    string Category,
    string Status,
    Guid SourceImageId,
    SceneResponse? Scene,
    JobModelsResponse Models,
    IReadOnlyList<TaskResponse> Tasks,
    IReadOnlyList<AssetPartResponse> Parts,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    /// <summary>캐릭터가 아니면 <c>null</c>(character-mesh-ui §FR-08 — "다시 시도" 이어받기 입력).</summary>
    string? Gender,
    IReadOnlyList<PartHintResponse> PartHints,
    string ProductionMode,
    SpriteResponse? Sprite)
{
    public static JobResponse From(JobDetails details)
    {
        var job = details.Job;
        var progress = ProgressByTask(details.MeshRuns);

        return FromCore(job, progress);
    }

    /// <summary>공정별 진행률 — 실행 기록이 있는 공정만 값을 갖는다 (§8.5).</summary>
    private static IReadOnlyDictionary<Guid, int> ProgressByTask(
        IReadOnlyList<Noxtend.Domain.Mesh.MeshRun> runs)
        => runs.ToDictionary(run => run.TaskId, run => run.Progress);

    private static JobResponse FromCore(PipelineJob job, IReadOnlyDictionary<Guid, int> progress)
        => new(
            job.Id,
            Wire(job.Category),
            Wire(job.Status),
            job.SourceImageId,
            job.Scene is null ? null : SceneResponse.From(job.Scene),
            JobModelsResponse.From(job),
            job.Tasks.OrderBy(t => t.Ordinal)
                .Select(task => TaskResponse.From(
                    task, progress.TryGetValue(task.Id, out var value) ? value : null))
                .ToList(),
            job.Parts.OrderBy(p => p.Ordinal)
                .Select(part => AssetPartResponse.From(part, job.GeneratedImages, job.LatestMeshFor(part.Id)))
                .ToList(),
            job.FailureReason,
            job.CreatedAt,
            job.CompletedAt,
            job.Gender is { } gender ? Wire(gender) : null,
            PartHintCodec.Deserialize(job.PartHints)
                .Select(hint => new PartHintResponse(hint.Type, hint.Count, hint.Variant))
                .ToList(),
            Wire(job.ProductionMode),
            job.Sprites is { } sprites ? SpriteResponse.From(sprites) : null);

    /// <summary>
    /// 열거형은 camelCase 로 나간다. 프론트 타입이 `'pending' | 'running' | …` 이므로 (§3.4)
    /// 여기서 맞추지 않으면 화면마다 정규화 코드가 생긴다.
    ///
    /// **사이클 #7 에서 소문자화가 camelCase 가 됐다.** 그 전에는 값이 전부 한 단어라
    /// 둘이 같았는데, `PartiallySucceeded` 가 늘면서 갈렸다 — 소문자화하면
    /// `partiallysucceeded` 가 나가고 프론트 유니온(`partiallySucceeded`)과 어긋난다.
    /// 한 단어 값들의 결과는 그대로다.
    /// </summary>
    internal static string Wire<T>(T value) where T : struct, Enum
    {
        var name = value.ToString()!;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

/// <summary>
/// 이 작업이 무엇으로 돌고 있는가 — 접수 시점에 고른 텍스트·이미지 공급자와 모델.
///
/// **저장은 사이클 #5·#7 부터 하고 있었지만 응답에 없었다.** 그래서 진행 화면이 모델을
/// 표기할 수 없었고, 실패한 작업을 다시 돌릴 때 선택이 목록 첫 항목으로 되돌아갔다 —
/// 같은 이미지로 모델만 바꿔 비교하는 튜닝 흐름에서 통제 변수가 깨진다.
///
/// **공급자 표시명은 여기 담지 않는다.** 컨트롤러가 공급자 저장소를 직접 알아야 하고
/// (이 저장소의 컨트롤러는 핸들러만 주입받는다), 이름은 화면이 이미 받아 둔 공급자
/// 목록으로 붙일 수 있다. 삭제된 공급자면 모델 id 만 남는다.
/// </summary>
public sealed record JobModelsResponse(
    ModelSelectionResponse? Text,
    ModelSelectionResponse? Image,

    /// <summary>3D 를 고르지 않은 작업은 <c>null</c> 이다 (사이클 #10 · NFR-06).</summary>
    ModelSelectionResponse? Mesh)
{
    public static JobModelsResponse From(PipelineJob job)
        => new(TextFrom(job), ImageFrom(job), MeshFrom(job));

    // 3D 없이 접수된 작업은 둘 다 null 이다 — 이미지까지만 돌고 그대로 끝난다
    private static ModelSelectionResponse? MeshFrom(PipelineJob job)
        => job.MeshProviderConfigId is { } providerConfigId && job.MeshModel is { } model
            ? new ModelSelectionResponse(providerConfigId, model)
            : null;

    /// <summary>
    /// 텍스트 모델은 작업이 아니라 **공정**이 갖는다 (`PipelineTask.Model`).
    ///
    /// 접수 때 세 텍스트 공정에 같은 값이 박히므로 가장 앞선 공정 하나를 요약으로 쓴다.
    /// 공정마다 다른 모델을 쓰게 되면 이 요약은 대표값이 되고, 그때는 공정별 표기가
    /// 따로 필요해진다 — 지금 없는 구분을 미리 만들지는 않는다.
    /// </summary>
    private static ModelSelectionResponse? TextFrom(PipelineJob job)
    {
        var task = job.Tasks
            .Where(t => t.Kind != TaskKind.Generate)
            .OrderBy(t => t.Ordinal)
            .FirstOrDefault(t => t.ProviderConfigId is not null && t.Model is not null);

        return task is null
            ? null
            : new ModelSelectionResponse(task.ProviderConfigId!.Value, task.Model!);
    }

    // 이미지 생성 없이 접수된 옛 작업은 둘 다 null 이다 (PipelineJob §ImageProviderConfigId)
    private static ModelSelectionResponse? ImageFrom(PipelineJob job)
        => job.ImageProviderConfigId is { } providerConfigId && job.ImageModel is { } model
            ? new ModelSelectionResponse(providerConfigId, model)
            : null;
}

/// <summary>공급자 설정 참조와 모델 id. 고르지 않았으면 이 객체 자체가 <c>null</c> 이다.</summary>
public sealed record ModelSelectionResponse(Guid ProviderConfigId, string Model);

/// <summary>파츠 힌트 한 줄 — 접수 요청(<c>PartHintDto</c>)과 같은 모양(character-mesh-ui §FR-08).</summary>
public sealed record PartHintResponse(string Type, int Count, string? Variant);

/// <summary>
/// Design Ref: §5.3 — 진행 표시가 읽는다.
///
/// <paramref name="AttemptCount"/>·시각은 도메인에 있었는데 응답에 없었다. 그래서
/// 화면이 "지금 몇 번째 시도인가", "이 단계가 얼마나 걸렸나" 를 알 수 없었다 —
/// 사용자가 "진행 상태를 알 수 없다" 고 한 이유의 절반이 이것이다.
/// </summary>
public sealed record TaskResponse(
    Guid Id,
    string Kind,
    int Ordinal,
    string Status,
    string? FailureReason,
    int AttemptCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    /// <summary>생성 공정만 값을 갖는다 — 어느 파츠를 그리는가 (사이클 #7 §3.6).</summary>
    Guid? PartId,
    /// <summary>생성 공정만 값을 갖는다 — 정면·우측·후면·좌측.</summary>
    string? ViewDirection,

    /// <summary>
    /// 3D 재구성 공정의 진행률 0~100. 나머지 공정은 <c>null</c> 이다 (§8.5).
    ///
    /// **0 이 아니라 null 인 이유**는 "진행률이 없다" 와 "0% 다" 가 다르기 때문이다.
    /// 0 으로 두면 화면이 모든 공정에 빈 막대를 그린다.
    /// </summary>
    int? Progress)
{
    public static TaskResponse From(PipelineTask task, int? progress = null)
        => new(
            task.Id,
            JobResponse.Wire(task.Kind),
            task.Ordinal,
            JobResponse.Wire(task.Status),
            task.FailureReason,
            task.AttemptCount,
            task.StartedAt,
            task.CompletedAt,
            task.PartId,
            task.ViewDirection is { } viewDirection
                ? JobResponse.Wire(viewDirection)
                : null,
            progress);
}

/// <summary>
/// Design Ref: §3.6 — 장면 명세.
///
/// 도메인 값 객체를 그대로 노출하지 않고 DTO 를 두는 이유는, `scaleReference` 처럼
/// 이름이 다른 필드가 있고 도메인 모양이 바뀔 때 계약이 함께 흔들리면 안 되기 때문이다.
/// </summary>
public sealed record SceneResponse(
    IReadOnlyList<PaletteEntryResponse> Palette,
    string TimeOfDay,
    string Mood,
    string RenderingStyle,
    string MaterialFeel,
    CameraResponse Camera,
    LightResponse Light,
    ScaleResponse Scale)
{
    public static SceneResponse From(SceneSpec scene)
        => new(
            [.. scene.Palette.Select(entry => new PaletteEntryResponse(entry.Name, entry.Hex))],
            scene.TimeOfDay,
            scene.Mood,
            scene.RenderingStyle,
            scene.MaterialFeel,
            new CameraResponse(scene.Camera.Type, scene.Camera.EyeLevel, scene.Camera.HorizonY),
            new LightResponse(scene.Light.Direction, scene.Light.Temperature, scene.Light.ShadowHardness),
            new ScaleResponse(
                scene.Scale.Object,
                scene.Scale.RealWorldSize,
                scene.Scale.HeightMeters));
}

/// <summary>
/// 팔레트 한 칸 (Design §7.1 · D-08).
///
/// `Hex` 가 null 인 것은 색상어를 식별할 수 없는 레거시 항목뿐이다. 숨기지 않고 그대로
/// 내보내 화면이 "색상 미확정" 으로 구분해 보여줄 수 있게 한다.
/// </summary>
public sealed record PaletteEntryResponse(string Name, string? Hex);

public sealed record CameraResponse(string Type, string EyeLevel, double HorizonY);
public sealed record LightResponse(string Direction, string Temperature, string ShadowHardness);
public sealed record ScaleResponse(string Object, string RealWorldSize, double? HeightMeters);

/// <summary>Design Ref: §3.6 — 분해가 채운 필드는 그 전까지 null 이다.</summary>
public sealed record AssetPartResponse(
    Guid Id,
    string Name,
    int Ordinal,
    string? Description,
    string? Category,
    /// <summary>
    /// 이 파츠를 놓을 자리들 (사이클 #9). 분해 전에는 빈 배열이다.
    ///
    /// `null` 을 쓰지 않는 이유는 "아직 없다" 와 "0개다" 가 여기서 같은 뜻이기 때문이다 —
    /// 화면이 null 검사를 하지 않아도 된다 (Design §7).
    /// </summary>
    IReadOnlyList<BoundsResponse> Placements,
    int? DepthOrder,
    IReadOnlyList<string> OccludedBy,
    /// <summary>
    /// 면을 덮는 파츠인가 — `none` · `ground` · `vertical` (#20 §4.1).
    ///
    /// 조립 결과만으로는 "왜 이 파츠가 누웠나" 를 되짚을 수 없어 표시 자체를 공개한다.
    /// </summary>
    string Surface,
    /// <summary>생성이 채운다. 아직 안 만들었거나 실패했으면 `null` (사이클 #7).</summary>
    Guid? GeneratedImageId,
    /// <summary>3D 재구성 입력용 방향별 최신 이미지.</summary>
    IReadOnlyList<GeneratedImageResponse> GeneratedImages,

    /// <summary>가장 나중 3D 결과. 아직 없거나 실패했으면 <c>null</c> (사이클 #10).</summary>
    GeneratedMeshResponse? GeneratedMesh)
{
    public static AssetPartResponse From(
        AssetPart part,
        IReadOnlyList<GeneratedImage> generatedImages,
        GeneratedMesh? latestMesh)
        => new(
            part.Id,
            part.Name,
            part.Ordinal,
            part.Description,
            part.Category,
            [.. part.Placements.Select(p => new BoundsResponse(p.X, p.Y, p.W, p.H))],
            part.DepthOrder,
            part.OccludedBy,
            part.Surface.ToString().ToLowerInvariant(),
            part.GeneratedImageId,
            // 재시도 이력 중 방향별 최신 결과 하나만 공개
            generatedImages
                .Where(image => image.PartId == part.Id && image.IsCurrentCandidate)
                .GroupBy(image => image.ViewDirection)
                .Select(group => group.OrderByDescending(image => image.CreatedAt).First())
                .OrderBy(image => image.ViewDirection)
                .Select(GeneratedImageResponse.From)
                .ToList(),
            latestMesh is null ? null : GeneratedMeshResponse.From(latestMesh));
}

/// <summary>
/// 내려받을 수 있는 3D 결과.
///
/// Design Ref: §10.3 · §13.1
///
/// **여기 없는 것이 중요하다.** 공급자 작업 ID, 파일 참조, 만료되는 결과 링크, Blob 키가
/// 나가면 되돌릴 수 없다 — 한 번 나간 응답은 클라이언트 캐시와 로그에 남는다.
/// 화면에 필요한 것은 내려받기 ID 와 크기, 미리보기 유무뿐이다.
/// </summary>
public sealed record GeneratedMeshResponse(
    Guid Id,
    bool HasPreview,
    bool HasFbx,
    long SizeBytes,
    long? FbxSizeBytes,
    int? CreditsConsumed,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// <c>SizeBytes</c> 는 여전히 GLB 의 크기다 (§7.1).
    ///
    /// 산출물 모델이 열에서 목록으로 바뀌었지만 **계약은 그대로다** — 내부가 뒤집혔는데
    /// 화면이 안 바뀌는 것이 이 설계가 옳다는 표시다.
    /// </summary>
    public static GeneratedMeshResponse From(GeneratedMesh mesh)
        => new(
            mesh.Id,
            mesh.HasPreview,
            mesh.HasFbx,
            mesh.Model.SizeBytes,
            // 내려받기 메뉴가 형식을 고르는 순간의 판단 재료 — 없으면(Tripo) null
            mesh.Find(MeshArtifactKind.Fbx)?.SizeBytes,
            mesh.CreditsConsumed,
            mesh.CreatedAt);
}

public sealed record GeneratedImageResponse(Guid Id, string ViewDirection)
{
    public static GeneratedImageResponse From(GeneratedImage image)
        => new(image.Id, JobResponse.Wire(image.ViewDirection));
}

public sealed record BoundsResponse(double X, double Y, double W, double H);

/// <summary>
/// Design Ref: §4.2 #7 — 목록에 장면 명세와 `tasks` 를 싣지 않는다.
/// 홈은 요약만 필요하고, 10건을 부를 때마다 명세가 따라오면 응답이 무겁다.
/// </summary>
public sealed record JobSummaryResponse(
    Guid Id,
    string Category,
    string Status,
    Guid SourceImageId,
    int PartCount,
    DateTimeOffset CreatedAt,
    string ProductionMode,
    SpriteSummaryResponse? Sprite)
{
    public static JobSummaryResponse From(PipelineJob job)
        => new(
            job.Id,
            JobResponse.Wire(job.Category),
            JobResponse.Wire(job.Status),
            job.SourceImageId,
            job.Parts.Count,
            job.CreatedAt,
            JobResponse.Wire(job.ProductionMode),
            job.Sprites is { } sprites ? SpriteSummaryResponse.From(sprites) : null);
}

/// <param name="Total">
/// 조건에 맞는 작업이 모두 몇 건인가 — <c>Items</c> 는 상한(기본 10)까지만 담는다.
/// 둘이 다를 수 있다는 것이 이 필드의 존재 이유다.
/// </param>
public sealed record JobListResponse(IReadOnlyList<JobSummaryResponse> Items, int Total);

/// <summary>Design Ref: §4.2 #5 — 접수 응답은 id 와 상태만.</summary>
public sealed record JobAcceptedResponse(Guid Id, string Status)
{
    public static JobAcceptedResponse From(PipelineJob job)
        => new(job.Id, JobResponse.Wire(job.Status));
}
