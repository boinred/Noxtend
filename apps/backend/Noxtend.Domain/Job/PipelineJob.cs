using Noxtend.Domain.Mesh;

namespace Noxtend.Domain.Job;

/// <summary>
/// 작업 — 소스 이미지에서 3D 에셋까지의 전체 단위. 애그리게이트 루트.
///
/// Design Ref: §0 용어 사전 · §3.1
///
/// 사용자가 세는 단위이며, 홈의 "실행 중" / "최근 작업" 이 이것이다.
/// 하나의 작업이 여러 공정(<see cref="PipelineTask"/>)을 갖는다.
/// </summary>
public sealed partial class PipelineJob
{
    // Meshy 멀티 이미지 입력 순서와 결과 화면의 고정 방향 순서
    private static readonly ViewDirection[] GenerationViews =
        [ViewDirection.Front, ViewDirection.Right, ViewDirection.Back, ViewDirection.Left];

    private readonly List<PipelineTask> _tasks = [];
    private readonly List<AssetPart> _parts = [];
    private readonly List<GeneratedImage> _generatedImages = [];
    private readonly List<GeneratedMesh> _generatedMeshes = [];

    private PipelineJob()
    {
        // EF Core 재구성용
    }

    private PipelineJob(
        Guid id,
        AssetCategory category,
        Guid sourceImageId,
        DateTimeOffset now,
        Guid? imageProviderConfigId,
        string? imageModel,
        Guid? meshProviderConfigId,
        string? meshModel,
        Gender? gender,
        string? partHints,
        bool requiresReview)
    {
        Id = id;
        Category = category;
        SourceImageId = sourceImageId;
        Status = JobStatus.Pending;
        CreatedAt = now;
        ImageProviderConfigId = imageProviderConfigId;
        ImageModel = imageModel;
        MeshProviderConfigId = meshProviderConfigId;
        MeshModel = meshModel;
        Gender = gender;
        PartHints = partHints;
        RequiresReview = requiresReview;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// 캐릭터 · 소품 · 배경.
    ///
    /// **실행 백본은 이 값을 읽지 않는다** (§2.4). 큐·워커·오케스트레이터·스위퍼는
    /// 카테고리를 모르고, 나뉘는 것은 공정 구성뿐이다.
    /// </summary>
    public AssetCategory Category { get; private set; }

    public Guid SourceImageId { get; private set; }
    public JobStatus Status { get; private set; }

    /// <summary>
    /// 장면 공정이 채운다. 이후 단계들이 일관성 기준으로 참조한다.
    ///
    /// 사이클 #4 의 `ConsistencyPrompt`(자유 문장)를 대체한다 — 조립에 필요한
    /// 시점·광원·수평선·스케일이 산문에 담기지 않았다 (Plan D-13).
    /// </summary>
    public SceneSpec? Scene { get; private set; }

    public IReadOnlyList<PipelineTask> Tasks => _tasks;
    public IReadOnlyList<AssetPart> Parts => _parts;

    /// <summary>생성 이미지 이력. 재생성하면 쌓이고 파츠가 최신 것을 가리킨다 (§3.1).</summary>
    public IReadOnlyList<GeneratedImage> GeneratedImages => _generatedImages;

    /// <summary>3D 결과 이력. 이미지와 같은 규칙으로 재시도하면 쌓인다 (§4.5).</summary>
    public IReadOnlyList<GeneratedMesh> GeneratedMeshes => _generatedMeshes;

    /// <summary>
    /// 검수 게이트 opt-in (review-gate). 전 카테고리 공통 — 카테고리 분기가 아니라
    /// per-job 플래그로 표현해 백본의 "카테고리를 모른다" 전제를 지킨다.
    /// </summary>
    public bool RequiresReview { get; private set; }

    /// <summary><see cref="RequiresReview"/> 인 작업의 검수 단계.</summary>
    public ReviewPhase ReviewPhase { get; private set; } = ReviewPhase.Boxes;

    /// <summary>
    /// 검수 편집 횟수. 값 자체는 의미 없음 — 파츠(owned 테이블)만 바뀌는 편집도 Jobs 행을 갱신해
    /// <see cref="RowVersion"/> 충돌 검사를 태우기 위한 것 (review-gate-staged 사이클 1 독립 리뷰 #5).
    /// </summary>
    public int ReviewRevision { get; private set; }

    // 검수 게이트 판정 단일 지점 — 팬아웃 계획과 완료 판정(스위퍼 경로 포함)이 같은 조건을 본다
    private bool IsAwaitingReview => RequiresReview && ReviewPhase != ReviewPhase.Approved;

    /// <summary>
    /// 동시성 토큰 (occludedby-recompute §구현 범위 5).
    ///
    /// **검수 편집과 전체 승인이 겹치면 파츠가 조용히 미아가 된다.** 승인이 팬아웃을
    /// 계획해 저장한 뒤 옛 스냅샷을 든 추가가 저장되면, 그 파츠만 Generate 공정 없이
    /// 남는다 — <see cref="PlanGenerationFanOut"/> 이 "이미 Generate 가 있다" 로 즉시
    /// 물러나므로 이후 영원히 생기지 않는다. 실 DB 로 재현했다
    /// (<c>ReviewConcurrencyTests</c>).
    ///
    /// `Tasks`·`MeshRuns` 가 같은 이유로 이미 쓰고 있다. 그림자 속성으로 두면 안 되는
    /// 이유도 같다 — <see cref="PipelineTask.RowVersion"/> 참고.
    /// </summary>
    public byte[]? RowVersion { get; private set; }

    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// 이 작업의 파츠를 그릴 이미지 공급자·모델 (사이클 #7).
    ///
    /// **접수 시점에 정해지지만 쓰이는 것은 분해가 끝난 뒤다.** 생성 공정은 파츠 수를
    /// 알아야 계획되므로(<see cref="PlanReadyFollowUpTasks"/>) 그 사이를 작업이 기억한다.
    ///
    /// **인자로 받지 않고 여기 두는 이유** (Plan D-11): 작업 하나에 생성 모델 하나여야
    /// 파츠들의 화풍이 달라지지 않는다. 계획 시점에 인자로 받으면 호출자가 매번 다른 모델을
    /// 넘길 수 있어 그 규칙이 타입으로 보장되지 않는다.
    ///
    /// 이미지 생성 없이 접수된 옛 작업은 <c>null</c> 이고, 그런 작업은 팬아웃하지 않는다.
    /// </summary>
    public Guid? ImageProviderConfigId { get; private set; }

    public string? ImageModel { get; private set; }

    /// <summary>
    /// 이 작업의 파츠를 3D 로 만들 공급자·모델 (사이클 #10).
    ///
    /// Design Ref: §4.1
    ///
    /// **없으면 이미지까지만 한다** (NFR-06). 옛 작업과 3D 를 원하지 않는 신규 작업이
    /// 같은 길로 끝나므로 마이그레이션이 기존 행을 건드리지 않아도 된다.
    ///
    /// <see cref="ImageProviderConfigId"/> 와 같은 이유로 작업이 기억한다 — 3D 공정은
    /// 이미지가 네 장 모여야 계획되므로 접수와 계획 사이가 멀다.
    /// </summary>
    public Guid? MeshProviderConfigId { get; private set; }

    public string? MeshModel { get; private set; }

    /// <summary>3D 를 만드는 작업인가. 공급자와 모델은 둘 다 있거나 둘 다 없다 (§4.1).</summary>
    public bool ProducesMeshes =>
        MeshProviderConfigId is not null && !string.IsNullOrWhiteSpace(MeshModel);

    /// <summary>
    /// 캐릭터의 성별 — 베이스바디 구성을 가른다 (character-studio §D-01).
    ///
    /// **nullable 인 이유는 타 카테고리가 성별을 보내지 않아서다.** 캐릭터에서만 필수라는
    /// 규칙은 접수 검증(§3.2)이 지킨다. 실행 백본은 읽지 않고 프롬프트 변수로만 흐른다.
    /// </summary>
    public Gender? Gender { get; private set; }

    /// <summary>
    /// 파츠 힌트 — 직렬화된 JSON 문자열 (character-studio §D-02).
    ///
    /// **백본은 이 값을 해석하지 않는다.** 파츠 수로 조회·조인할 일이 없어 owned 엔티티나
    /// 별도 테이블 대신 한 컬럼에 굳힌다. API·프롬프트 경계에서만 역직렬화·렌더한다.
    /// </summary>
    public string? PartHints { get; private set; }

    public static PipelineJob Create(
        AssetCategory category,
        Guid sourceImageId,
        DateTimeOffset now,
        Guid? imageProviderConfigId = null,
        string? imageModel = null,
        Guid? meshProviderConfigId = null,
        string? meshModel = null,
        // 캐릭터 고유 입력 — 선택(기존 positional 호출부 하위호환). 검증은 접수(§3.2)
        Gender? gender = null,
        string? partHints = null,
        bool requiresReview = false)
    {
        // 공급자와 모델은 짝이다 (§4.1). 한쪽만 있으면 워커가 집는 순간 실패하므로
        // 접수 자리에서 막는다 — 사용자가 이유를 아는 자리다
        if (meshProviderConfigId is not null != !string.IsNullOrWhiteSpace(meshModel))
        {
            throw new ArgumentException(
                "3D 공급자와 모델은 함께 지정해야 합니다. " +
                $"공급자={(meshProviderConfigId is not null ? "있음" : "없음")}, " +
                $"모델={(string.IsNullOrWhiteSpace(meshModel) ? "없음" : "있음")}");
        }

        return new(
            Guid.NewGuid(), category, sourceImageId, now,
            imageProviderConfigId, imageModel,
            meshProviderConfigId, string.IsNullOrWhiteSpace(meshModel) ? null : meshModel.Trim(),
            gender, string.IsNullOrWhiteSpace(partHints) ? null : partHints, requiresReview);
    }

    /// <summary>
    /// 더 진행하지 않는 상태들.
    ///
    /// **값으로 노출하는 이유는 저장소가 이 목록을 질의에 써야 하기 때문이다.**
    /// 삭제는 "지금도 종료 상태인가" 를 조건으로 걸어야 하는데, 그 목록을 SQL 쪽에
    /// 따로 적으면 상태가 하나 늘 때 두 곳이 조용히 어긋난다.
    ///
    /// **`PartiallySucceeded` 가 들어 있다.** 이미지 일부만 나온 작업도 더 진행하지
    /// 않으므로 종료다 — 사용자에게는 "부분 성공" 으로 보인다.
    /// </summary>
    public static readonly JobStatus[] TerminalStatuses =
    [
        JobStatus.Succeeded,
        JobStatus.PartiallySucceeded,
        JobStatus.Failed,
        JobStatus.Canceled,
    ];

    /// <summary>
    /// 끝난 작업.
    ///
    /// **부분 성공도 끝난 것이다** (사이클 #7). 실패한 파츠를 다시 돌리는 것은 공정 단위
    /// 재시도이지 작업의 재개가 아니다 — 재시도가 성공하면 <see cref="ReconcileFromTasks"/>
    /// 가 성공으로 올린다 (V-7).
    /// </summary>
    public bool IsTerminal => TerminalStatuses.Contains(Status);

    /// <summary>
    /// 공정을 계획에 추가한다.
    ///
    /// 이번 사이클은 <see cref="TaskKind.Extract"/> 하나만 계획하지만, 분해·생성이
    /// 붙으면 여기에 행이 늘 뿐이다 — 이것이 다공정 모델을 지금 잡은 이유다 (§2.3).
    /// </summary>
    public PipelineTask PlanTask(
        TaskKind kind,
        int ordinal,
        Guid? dependsOnTaskId = null,
        Guid? providerConfigId = null,
        string? model = null)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException(
                $"종료된 작업에는 공정을 추가할 수 없습니다. 현재 상태: {Status}");
        }

        var task = PipelineTask.Plan(Id, kind, ordinal, dependsOnTaskId, providerConfigId, model);
        _tasks.Add(task);
        return task;
    }

    /// <summary>
    /// 앞 공정의 결과로 계획할 수 있게 된 공정을 계획한다.
    ///
    /// Design Ref: §2.3 A-2 · §4.4 · Plan FR-01
    ///
    /// **접수 시점에 계획할 수 없는 이유**는 파츠 수를 분해가 끝나야 알기 때문이다.
    /// 그래서 계획 시점만 뒤로 밀리고, 계획 규칙 자체는 도메인이 소유한다 — 분해 단계의
    /// `Interpret` 안에 두면 계획이 단계 지식에 섞인다.
    ///
    /// 규칙 둘을 순서대로 본다 (§4.4).
    /// ① 분해 성공 → 파츠×방향 생성 팬아웃
    /// ② 파츠의 네 방향 이미지가 모임 → 그 파츠의 3D 재구성 팬인
    ///
    /// **둘 다 멱등하다.** 큐 메시지 중복 배달, 분해 재시도, 그리고 마지막 두 이미지가
    /// 거의 동시에 끝나는 경합으로 두 번 불릴 수 있는데, 그때 공정이 두 배가 되면
    /// 비용도 두 배다.
    /// </summary>
    public void PlanReadyFollowUpTasks()
    {
        if (IsTerminal)
        {
            return;
        }

        PlanGenerationFanOut();
        PlanMeshFanIn();
    }

    private void PlanGenerationFanOut()
    {
        if (_tasks.Any(t => t.Kind == TaskKind.Generate))
        {
            return;   // 이미 계획됐다
        }

        // 이미지 공급자 없이 접수된 작업은 그리지 않는다 — 앞 세 단계로 끝난다
        if (ImageProviderConfigId is not { } imageProviderConfigId
            || string.IsNullOrWhiteSpace(ImageModel))
        {
            return;
        }

        // 파츠 명세가 생성의 입력이므로 분해가 성공했을 때만 계획한다 (V-6)
        var decompose = _tasks.FirstOrDefault(t => t.Kind == TaskKind.Decompose);
        if (decompose is not { Status: TaskStatus.Succeeded } || _parts.Count == 0)
        {
            return;
        }

        // occludedby-recompute §함정1 — 조건이 "미완료" 가 아니라 "성공하지 않음" 이다.
        // 실패한 재작성에 가드를 열어주면 갱신 안 된 옛 서술로 이미지가 통째로 나간다.
        // 게이트 가드보다 앞 — 재작성 도중 검수 대기로 떨어지면 빈 서술 단계가 뜬다 (staged §함정1)
        var rewrite = LatestRewriteTask();
        if (rewrite is not null && rewrite.Status != TaskStatus.Succeeded)
        {
            return;
        }

        // review-gate §함정1 — 승인 전까지는 팬아웃을 미루고 검수 대기로 멈춘다.
        // 여기서 막지 않으면 ReconcileFromTasks 가 "Generate 이 하나도 없음"을 공집합
        // 전원 성공으로 오판해 검수 대기 작업을 조용히 Succeeded 로 확정해버린다.
        if (IsAwaitingReview)
        {
            Status = JobStatus.PendingReview;
            return;
        }

        var ordinal = _tasks.Count;

        // 파츠 순서 안에서 방향 순서를 고정해 API·화면·후속 3D 입력의 정렬을 일치시킨다.
        // 정면이 비정면 3면의 유일한 참조다 — 먼저 계획해 의존을 건다 (workstream B §5.3).
        // 파츠별로 정면(Front) 이미지를 먼저 계획함 (selective-view-generation §2)
        foreach (var part in _parts.OrderBy(p => p.Ordinal))
        {
            var frontTask = PlanTask(
                TaskKind.Generate, ordinal++, dependsOnTaskId: decompose.Id,
                providerConfigId: ImageProviderConfigId, model: ImageModel);
            frontTask.BindToPart(part.Id, ViewDirection.Front);
        }
    }

    /// <summary>
    /// 정면 생성 완료 후 사용자가 선택한 비정면(좌/후/우) 이미지 생성을 계획한다 (selective-view-generation §2).
    /// </summary>
    public IReadOnlyList<PipelineTask> PlanSelectedViews(IReadOnlyList<ViewDirection> directions)
    {
        if (directions == null || directions.Count == 0)
        {
            return [];
        }

        if (Status is JobStatus.Failed or JobStatus.Canceled)
        {
            throw new InvalidOperationException("성공 또는 부분 성공한 작업에 대해서만 비정면 뷰를 생성할 수 있습니다.");
        }

        var candidates = new List<(AssetPart Part, PipelineTask FrontTask, ViewDirection Direction)>();

        foreach (var part in _parts.OrderBy(p => p.Ordinal))
        {
            var frontTask = _tasks.FirstOrDefault(
                t => t.Kind == TaskKind.Generate && t.PartId == part.Id && t.ViewDirection == ViewDirection.Front);

            // 정면 생성 공정이 없거나 이미 실패한 파츠는 비정면 뷰를 계획할 수 없음
            if (frontTask == null || frontTask.Status == TaskStatus.Failed)
            {
                continue;
            }

            foreach (var viewDirection in directions)
            {
                if (viewDirection == ViewDirection.Front)
                {
                    continue;
                }

                var existingTask = _tasks.FirstOrDefault(
                    t => t.Kind == TaskKind.Generate && t.PartId == part.Id && t.ViewDirection == viewDirection);

                if (existingTask != null)
                {
                    continue;
                }

                candidates.Add((part, frontTask, viewDirection));
            }
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        // 계획할 새 공정이 있을 때만 완료 상태였던 작업을 Running으로 재개방 (PlanTask 가드를 통과하도록 사전 변경)
        if (Status is JobStatus.Succeeded or JobStatus.PartiallySucceeded)
        {
            Status = JobStatus.Running;
            CompletedAt = null;
            FailureReason = null;
        }

        var plannedTasks = new List<PipelineTask>();
        var ordinal = _tasks.Count;

        foreach (var (part, frontTask, viewDirection) in candidates)
        {
            var task = PlanTask(
                TaskKind.Generate,
                ordinal++,
                dependsOnTaskId: frontTask.Id,
                providerConfigId: ImageProviderConfigId,
                model: ImageModel);

            task.BindToPart(part.Id, viewDirection);
            plannedTasks.Add(task);
        }

        return plannedTasks;
    }

    // 최신 서술 재작성 공정 — 되돌리기·재승인으로 여러 개일 때 옛 것의 결과를 보지 않도록
    private PipelineTask? LatestRewriteTask()
        => _tasks.Where(t => t.Kind == TaskKind.RewriteDescriptions).MaxBy(t => t.Ordinal);

    /// <summary>
    /// 검수 게이트 전체 승인 (review-gate §목표). 미뤄뒀던 Generate 팬아웃을 그제야 계획한다.
    ///
    /// **여기서 팬아웃하지 않고 <see cref="PlanReadyFollowUpTasks"/> 를 다시 부르는 이유**는
    /// 팬아웃 로직을 두 곳에 중복 정의하지 않기 위해서다 — 검수 없이 접수된 작업과 같은
    /// 경로(<see cref="PlanGenerationFanOut"/>)를 타되, 이번엔 <see cref="ReviewPhase"/>
    /// 가 Approved 라 guard 를 통과한다.
    /// </summary>
    public void ApproveReview(DateTimeOffset now)
    {
        if (!RequiresReview)
        {
            throw new InvalidOperationException("검수 게이트가 적용되지 않은 작업입니다.");
        }

        if (Status != JobStatus.PendingReview)
        {
            throw new InvalidOperationException(
                $"검수 대기 상태에서만 승인할 수 있습니다. 현재 상태: {Status}");
        }

        // 서술 단계에서 중복 클릭·옛 화면이 다시 통과하면 재작성·팬아웃이 두 번 나감
        EnsureReviewPhase(ReviewPhase.Boxes);

        // 상자 확정 — 서술 단계로. 재작성이 있으면 끝난 뒤 PlanGenerationFanOut 이 검수 대기로 세움
        ReviewPhase = ReviewPhase.Descriptions;
        Status = JobStatus.Running;

        // 가려지게 된 파츠가 있으면 서술을 먼저 다시 쓴다. 편집을 몇 개 했든 공정 하나다 —
        // 팬아웃은 이 공정이 성공한 뒤에야 PlanGenerationFanOut 의 가드를 통과한다
        //
        // **공급자·모델을 반드시 붙인다.** 작업은 이미지·3D 공급자만 기억하고 텍스트
        // 공급자는 접수 시점에 공정으로만 흘러간다. 안 붙이면 RunTaskHandler 가 "공급자가
        // 지정되지 않았습니다" 로 즉시 실패하고(재시도 없음), 그 실패가 비-Generate 라
        // 작업 전체를 죽인다. 같은 텍스트 단계인 Decompose 가 쓴 값을 그대로 물려받는다
        if (_descriptionsStale.Count > 0)
        {
            var decomposeTask = _tasks.First(t => t.Kind == TaskKind.Decompose);
            PlanTask(
                TaskKind.RewriteDescriptions,
                _tasks.Count,
                providerConfigId: decomposeTask.ProviderConfigId,
                model: decomposeTask.Model);
        }

        PlanReadyFollowUpTasks();
    }

    /// <summary>
    /// 서술 확정 (review-gate-staged 사이클 1). 미뤄뒀던 Generate 팬아웃을 그제야 계획한다.
    ///
    /// <see cref="ApproveReview"/> 와 메서드를 나눈다 — 한 메서드에 단계 분기를 넣으면 API 하나가
    /// 두 뜻을 갖고, 잘못된 단계 호출이 오류로 드러나지 않는다.
    /// </summary>
    /// <exception cref="PartValidationException">서술이 빈 파츠가 있다</exception>
    public void ConfirmDescriptions(DateTimeOffset now)
    {
        EnsurePendingReviewPhase(ReviewPhase.Descriptions);

        // 재작성 응답이 stale 일부를 빠뜨리면 빈 서술이 남는다 — 빈 서술로 유료 생성 방지
        var empty = _parts
            .Where(p => string.IsNullOrWhiteSpace(p.Description))
            .OrderBy(p => p.Ordinal)
            .Select(p => p.Name)
            .ToArray();
        if (empty.Length > 0)
        {
            throw new PartValidationException(
                PartValidationError.DescriptionEmpty,
                $"서술이 빈 파츠가 있습니다: {string.Join(", ", empty)}");
        }

        ReviewPhase = ReviewPhase.Approved;
        Status = JobStatus.Running;
        PlanReadyFollowUpTasks();
    }

    /// <summary>서술 단계에서 상자 단계로 되돌린다. 서술·stale 은 유지.</summary>
    public void ReturnToBoxes()
    {
        EnsurePendingReviewPhase(ReviewPhase.Descriptions);
        ReviewPhase = ReviewPhase.Boxes;
    }

    /// <summary>
    /// 정면 생성 완료 후 서술 수정 복귀 시 기존 이미지 만료, 공정 취소 및 검수 서술 단계 원자적 전환 (selective-view-generation §1.1).
    /// </summary>
    public void ReturnToDescriptionsFromGeneration(Guid partId, DateTimeOffset? now = null)
    {
        if (Status is JobStatus.Failed or JobStatus.Canceled)
        {
            throw new InvalidOperationException("종료된 작업은 서술 단계로 되돌릴 수 없습니다.");
        }

        var part = _parts.FirstOrDefault(p => p.Id == partId)
            ?? throw new KeyNotFoundException($"파츠를 찾을 수 없습니다: {partId}");

        var time = now ?? DateTimeOffset.UtcNow;

        // 진행 중이거나 대기 중인 이미지/3D 공정 취소
        foreach (var task in _tasks.Where(t => t.PartId == partId && (t.Kind == TaskKind.Generate || t.Kind == TaskKind.Reconstruct)))
        {
            task.Cancel(time);
        }

        // 해당 파츠의 기존 이미지 만료 처리
        foreach (var image in _generatedImages.Where(i => i.PartId == partId))
        {
            image.MarkObsoleted();
        }

        if (!_descriptionsStale.Contains(part.Name))
        {
            _descriptionsStale.Add(part.Name);
        }

        ReviewPhase = ReviewPhase.Descriptions;
        Status = JobStatus.PendingReview;
        ReviewRevision++;
    }

    /// <summary>서술 단계에서 사람이 파츠 서술을 고친다. 출처 Human, 재작성 대상에서 제외.</summary>
    /// <exception cref="KeyNotFoundException">그 id 의 파츠가 없다</exception>
    /// <exception cref="PartValidationException">서술이 비었다</exception>
    public void EditReviewDescription(Guid partId, string description)
    {
        EnsurePendingReviewPhase(ReviewPhase.Descriptions);

        var part = _parts.FirstOrDefault(p => p.Id == partId)
            ?? throw new KeyNotFoundException($"파츠를 찾을 수 없습니다: {partId}");

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new PartValidationException(
                PartValidationError.DescriptionEmpty, $"'{part.Name}' 의 서술이 비었습니다.");
        }

        part.EditDescription(description.Trim());
        _descriptionsStale.Remove(part.Name);
        // 서술 확정과 겹친 옛 스냅샷 편집을 충돌로 막음
        ReviewRevision++;
    }

    // 팔레트 개수 — 추출 검증과 같은 범위
    private const int MinPaletteEntries = 3;

    private const int MaxPaletteEntries = 8;

    /// <summary>
    /// 서술 단계에서 사람이 장면 팔레트를 통째로 교체한다 (Plan §10 결정 A).
    ///
    /// 생성·재작성 단계가 실행 시점에 <see cref="Scene"/> 을 읽으므로 값만 바꾸면 이후 공정에 반영된다.
    /// 서술 stale 은 등록하지 않는다 — 서술은 사람이 같은 화면에서 직접 고친다.
    /// </summary>
    /// <exception cref="PartValidationException">개수·이름·Hex 위반</exception>
    public void EditReviewPalette(IReadOnlyList<PaletteEntry> entries)
    {
        EnsurePendingReviewPhase(ReviewPhase.Descriptions);

        // 상태는 검수 대기라 InvalidOperationException(→ REVIEW_NOT_PENDING)이면 원인을 오해함
        var scene = Scene
            ?? throw new PartValidationException(
                PartValidationError.PaletteInvalid, "장면이 없는 작업은 팔레트를 고칠 수 없습니다.");

        if (entries.Count is < MinPaletteEntries or > MaxPaletteEntries)
        {
            throw new PartValidationException(
                PartValidationError.PaletteInvalid,
                $"팔레트는 {MinPaletteEntries}~{MaxPaletteEntries}칸이어야 합니다 (받은 값 {entries.Count}칸)");
        }

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)
                || entry.Hex is null
                || !System.Text.RegularExpressions.Regex.IsMatch(entry.Hex, "^#[0-9A-F]{6}$"))
            {
                throw new PartValidationException(
                    PartValidationError.PaletteInvalid,
                    $"팔레트 칸 '{entry.Name}' 은 이름과 대문자 #RRGGBB 색이 필요합니다 (받은 값 {entry.Hex})");
            }
        }

        Scene = scene with { Palette = [.. entries] };
    }

    // 검수 대기 + 단계 검사 — 상태 위반은 InvalidOperationException, 단계 위반은 전용 예외
    private void EnsurePendingReviewPhase(ReviewPhase expected)
    {
        if (Status != JobStatus.PendingReview)
        {
            throw new InvalidOperationException(
                $"검수 대기 상태에서만 할 수 있습니다. 현재 상태: {Status}");
        }

        EnsureReviewPhase(expected);
    }

    private void EnsureReviewPhase(ReviewPhase expected)
    {
        if (ReviewPhase != expected)
        {
            throw new ReviewPhaseMismatchException(expected, ReviewPhase);
        }
    }

    /// <summary>
    /// 네 방향 이미지가 모인 파츠마다 3D 재구성 공정 하나 (§4.4 · Plan D-02).
    ///
    /// **파츠마다 독립이다.** 전부 끝나기를 기다리면 파츠가 스물일 때 마지막 한 장이
    /// 늦어지는 만큼 열아홉 개의 3D 제작이 통째로 밀린다.
    ///
    /// 부모를 억지로 지정하지 않는다 — 네 입력이 실제 존재하는 순간 만들기 때문에
    /// `DependsOnTaskId` 가 없어도 즉시 실행 가능하다.
    /// </summary>
    private void PlanMeshFanIn()
    {
        if (!ProducesMeshes || _parts.Count == 0)
        {
            return;
        }

        var ordinal = _tasks.Count;

        foreach (var part in _parts.OrderBy(p => p.Ordinal))
        {
            // 멱등 1층 — 같은 파츠의 3D 공정이 이미 있으면 만들지 않는다.
            // 2층은 DB 의 filtered unique index 다 (§4.4)
            if (_tasks.Any(t => t.Kind == TaskKind.Reconstruct && t.PartId == part.Id))
            {
                continue;
            }

            // 요청된 이미지 생성 공정이 완료되지 않았거나(Pending/Running) 실패(Failed)한 경우 3D 팬인을 스케줄링하지 않음
            if (_tasks.Any(t => t.Kind == TaskKind.Generate && t.PartId == part.Id && t.Status != TaskStatus.Succeeded))
            {
                continue;
            }

            if (LatestImagesFor(part.Id) is not { } inputs)
            {
                continue;   // 아직 정면 필수 + 최소 1개 이상 비정면 이미지가 모이지 않았다
            }

            var task = PlanTask(
                TaskKind.Reconstruct,
                ordinal++,
                providerConfigId: MeshProviderConfigId,
                model: MeshModel);

            task.BindToMeshInputs(part.Id, inputs);
        }
    }

    /// <summary>
    /// 이 파츠·방향의 현재 이미지(합성·obsolete 제외) — 애플리케이션 계층이 파츠별
    /// "3D 전송 뷰 선택"에서 개별 선택/대칭 소스를 조회하는 공개 통로다 (spec 20260917).
    /// </summary>
    public Guid? LatestCurrentImage(Guid partId, ViewDirection direction)
        => _generatedImages
            .Where(image => image.PartId == partId && image.ViewDirection == direction && image.IsCurrentCandidate)
            .OrderByDescending(image => image.CreatedAt)
            // 같은 클럭 틱에 몰리면 CreatedAt 만으로는 어느 행이 오는지 EF/컬렉션 로드
            // 순서에 달린다 — Id 로 결정적 타이브레이크한다(RunGenerationTaskHandler와
            // 같은 이유, merge-gate 리뷰 F6)
            .ThenByDescending(image => image.Id)
            .Select(image => (Guid?)image.Id)
            .FirstOrDefault();

    /// <summary>
    /// 이 파츠의 방향별 **최신** 이미지 (만료된 이미지 제외).
    /// 정면(Front) 필수 + 최소 1개 이상의 비정면 이미지가 모여야 (총 2면 이상) 반환함.
    /// </summary>
    private MeshInputSet? LatestImagesFor(Guid partId)
    {
        Guid? Latest(ViewDirection direction) => LatestCurrentImage(partId, direction);

        var front = Latest(ViewDirection.Front);
        if (front is not { } frontId)
        {
            return null;
        }

        var right = Latest(ViewDirection.Right);
        var back = Latest(ViewDirection.Back);
        var left = Latest(ViewDirection.Left);

        var nonFrontCount = (right.HasValue ? 1 : 0) + (back.HasValue ? 1 : 0) + (left.HasValue ? 1 : 0);
        if (nonFrontCount == 0)
        {
            return null;
        }

        return new MeshInputSet(frontId, right, back, left);
    }

    /// <summary>
    /// 실패한 생성 공정 하나를 다시 대기로 되돌린다 (사이클 #7 FR-08 · §4.2 #3).
    ///
    /// **생성 공정만 가능하다.** 앞 세 단계를 다시 돌리면 그 뒤의 결과가 전부 무의미해진다 —
    /// 장면이 바뀌면 파츠 명세가 어긋나고 이미 그린 이미지들이 다른 장면의 것이 된다.
    ///
    /// **시도 횟수를 0 으로 되돌린다.** 자동 재시도 한도는 "공급자가 흔들리는가" 를 재는
    /// 값이고, 이것은 사용자의 명시적 행위라 같은 예산을 쓰지 않는다.
    ///
    /// **작업이 다시 열린다.** 부분 성공은 종료 상태이므로, 되돌리지 않으면 오케스트레이터가
    /// 종료 판정에서 먼저 빠져나가 이 공정을 큐에 넣지 않는다.
    /// </summary>
    /// <returns>되돌렸으면 <c>true</c>. 대상이 아니면 <c>false</c> — 호출자가 오류를 만든다.</returns>
    public bool RetryOutputTask(Guid taskId)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);

        // 결과물을 내는 두 단계만 다시 돌릴 수 있다 (§4.7). 3D 재구성이 여기 들어오는
        // 이유는 이미지와 같은 성질이기 때문이다 — 파츠마다 독립이고, 하나를 다시
        // 돌려도 다른 파츠의 결과가 무의미해지지 않는다.
        //
        // 서술 재작성도 들어온다 (occludedby-recompute §구현 범위 3). 여기 없으면 재작성이
        // 실패했을 때 복구 경로가 아예 없어, 검수자가 그린 좌표가 통째로 사라진다
        if (task is not { Status: TaskStatus.Failed }
            || task.Kind is not (TaskKind.Generate or TaskKind.Reconstruct
                or TaskKind.RewriteDescriptions))
        {
            return false;
        }

        // 캐스케이드로 스킵된 형제는 단독으로 재시도할 수 없다 (독립 리뷰 지적, §6.1-③).
        // 부모 정면이 아직 Failed 인 채로 형제만 Pending 으로 돌리면 IsReadyToRun 이
        // 계속 거짓이라 큐에 영원히 안 실리는데, 작업은 Running 으로 거짓 열려버린다 —
        // "재시도 접수됨"으로 보이지만 실제로는 아무 일도 안 일어난다. 정면을 먼저
        // 재시도해야 형제까지 함께 되살아난다
        if (task.Kind == TaskKind.Generate
            && task.DependsOnTaskId is { } dependsOnTaskId
            && _tasks.FirstOrDefault(t => t.Id == dependsOnTaskId) is
                { Kind: TaskKind.Generate, Status: TaskStatus.Failed })
        {
            return false;
        }

        task.ResetForManualRetry();

        // 캐스케이드로 스킵된 형제도 함께 되살린다 (workstream B §6.1-③). 정면이
        // Pending 으로 돌아가고 형제도 Pending 이 되면, 정면이 다시 성공했을 때
        // 기존 EnqueueReadyTasksAsync 가 준비된 형제를 알아서 적재한다 — guard 우회도
        // 새 계획 규칙도 필요 없다
        if (task.Kind == TaskKind.Generate)
        {
            foreach (var sibling in _tasks.Where(t =>
                t.Kind == TaskKind.Generate && t.PartId == task.PartId
                && t.DependsOnTaskId == task.Id && t.Status == TaskStatus.Failed
                && t.FailureReason == CascadeSkipReason))
            {
                sibling.ResetForManualRetry();
            }
        }

        Status = JobStatus.Running;
        CompletedAt = null;
        FailureReason = null;

        return true;
    }

    /// <summary>
    /// 파츠별 "3D 전송 뷰 선택" — find-or-create (spec 20260917).
    ///
    /// **두 번째 `Reconstruct` 공정을 만들지 않는다.** 파츠당 `Reconstruct`는 DB
    /// 유니크 제약으로 하나뿐이고, 자동 팬인(`PlanMeshFanIn`)이 이미지 2장만 모여도
    /// 그 자리를 먼저 선점한다. 있으면 입력을 다시 얼려 재실행하고, 없으면(예: 정면
    /// 단독 3D처럼 자동 팬인이 아직 아무것도 안 만든 경우) 새로 계획한다.
    /// </summary>
    public PipelineTask ReplanMeshInputs(
        Guid partId, Guid providerConfigId, string model, MeshInputSet inputs, DateTimeOffset now)
    {
        if (Status == JobStatus.Canceled)
        {
            throw new InvalidOperationException("취소된 작업은 3D를 다시 만들 수 없습니다.");
        }

        if (_parts.All(p => p.Id != partId))
        {
            throw new InvalidOperationException($"작업에 없는 파츠입니다: {partId}");
        }

        // 종료된 잡에도 붙일 수 있어야 한다 — PlanTask 가 종료 작업을 거절하므로 먼저 연다
        if (IsTerminal)
        {
            Status = JobStatus.Running;
            CompletedAt = null;
            FailureReason = null;
        }

        var existing = _tasks.FirstOrDefault(t => t.Kind == TaskKind.Reconstruct && t.PartId == partId);
        if (existing is not null)
        {
            existing.BindToMeshInputs(partId, inputs);
            // 이미 성공했어도 되돌린다 — 입력이 달라졌으므로 다시 돌아야 한다
            // (RunMeshTaskHandler.ResumeOrStartAsync 가 입력이 같은지 비교해 새 실행을 만든다)
            existing.ResetForManualRetry();
            return existing;
        }

        var task = PlanTask(TaskKind.Reconstruct, _tasks.Count, providerConfigId: providerConfigId, model: model);
        task.BindToMeshInputs(partId, inputs);
        return task;
    }

    /// <summary>
    /// 대칭(합성) 이미지용 공정을 계획한다 (spec 20260917).
    ///
    /// **바이트를 다루지 않는다.** 반전·Blob 저장(I/O)은 도메인이 할 수 없으므로
    /// 애플리케이션 계층이 하고, 끝나면 <see cref="AttachGeneratedImage"/>(
    /// <c>isSynthetic: true</c>)로 이 공정에 이미지를 붙인다.
    /// </summary>
    public PipelineTask PlanSyntheticView(Guid partId, ViewDirection targetDirection, DateTimeOffset now)
    {
        if (Status == JobStatus.Canceled)
        {
            throw new InvalidOperationException("취소된 작업에는 합성 이미지를 만들 수 없습니다.");
        }

        if (_parts.All(p => p.Id != partId))
        {
            throw new InvalidOperationException($"작업에 없는 파츠입니다: {partId}");
        }

        // 종료된 잡에도 붙일 수 있어야 한다 — PlanTask 가 종료 작업을 거절하므로 먼저 연다
        if (IsTerminal)
        {
            Status = JobStatus.Running;
            CompletedAt = null;
            FailureReason = null;
        }

        var task = PlanTask(TaskKind.Synthesize, _tasks.Count);
        task.BindToPart(partId, targetDirection);
        return task;
    }

    /// <summary>
    /// 생성 성공 반영 — Blob 저장은 이미 끝났고 여기서는 잇기만 한다.
    ///
    /// Design Ref: §3.1 · Plan FR-03
    /// </summary>
    public void AttachGeneratedImage(
        Guid partId, Guid taskId, string blobKey, string contentType, long sizeBytes, DateTimeOffset now,
        bool isSynthetic = false)
    {
        EnsureNotTerminal();

        var part = _parts.FirstOrDefault(p => p.Id == partId)
            ?? throw new InvalidOperationException($"작업에 없는 파츠입니다: {partId}");

        // 이전 배포에서 계획된 방향 없는 생성 공정의 정면 호환
        var viewDirection = _tasks.FirstOrDefault(task => task.Id == taskId)?.ViewDirection
            ?? ViewDirection.Front;
        var image = GeneratedImage.Create(
            Id, partId, taskId, viewDirection, blobKey, contentType, sizeBytes, now, isSynthetic);
        if (ReviewPhase == ReviewPhase.Descriptions)
        {
            image.MarkObsoleted();
        }
        _generatedImages.Add(image);
        part.AttachImage(image.Id, viewDirection);
    }

    /// <summary>
    /// 끝난 작업에 3D 를 뒤늦게 붙인다 (사이클 #11).
    ///
    /// Design Ref: §4.1 · Plan D-01
    ///
    /// **이미지를 다시 만들지 않는 것이 이 메서드의 전부다.** 전체 재실행 비용의 99.5% 가
    /// 이미 갖고 있는 이미지이므로, 3D 공정만 계획하고 앞 단계는 읽기만 한다.
    ///
    /// **셋을 한 번에 하는 이유**는 순서가 규칙이기 때문이다. 나눠 두면 호출자가 재개방을
    /// 계획보다 먼저 해야 한다는 것을 알아야 하고, 틀리면 종료 가드에 걸려 **조용히
    /// 아무 일도 일어나지 않는다.**
    /// </summary>
    /// <returns>붙였으면 <c>true</c>. 대상이 아니면 <c>false</c> — 호출자가 오류를 만든다.</returns>
    public bool AddMeshProduction(Guid providerConfigId, string model, DateTimeOffset now)
    {
        // ① 거절 조건 — 여기서 걸리면 아무것도 바꾸지 않는다.
        //    반쪽 상태를 남기면 다음 시도가 꼬인다
        if (ProducesMeshes || string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        // 취소는 "끝났다" 가 아니라 "하지 말라" 다 (§4.1 D-08). 사용자가 멈춘 작업을
        // 되살리면 그 뜻이 사라진다. 실패·성공·부분 성공은 되살릴 수 있다
        if (Status == JobStatus.Canceled)
        {
            return false;
        }

        // 만들 수 있는 파츠가 하나도 없으면 붙일 이유가 없다
        if (!_parts.Any(part => LatestImagesFor(part.Id) is not null))
        {
            return false;
        }

        // ② 선택 저장 — 이 값이 곧 재진입 차단 기준이 된다 (§4.2)
        MeshProviderConfigId = providerConfigId;
        MeshModel = model.Trim();

        // ③ 재개방. **④ 보다 먼저다** — `PlanTask` 가 종료 작업을 거절한다
        Status = JobStatus.Running;
        CompletedAt = null;
        FailureReason = null;

        // ④ 기존 팬인을 그대로 부른다. 새 계획 규칙을 만들지 않는다
        PlanMeshFanIn();

        return true;
    }

    /// <summary>
    /// 3D 결과 반영 — Blob 저장은 이미 끝났고 여기서는 잇기만 한다.
    ///
    /// Design Ref: §4.5 · §8.1
    ///
    /// **취소 확인 뒤에 부른다.** 공정 실행이 이 직전에 취소 토큰을 다시 보는데, 취소된
    /// 작업에 늦게 도착한 mesh 를 붙이면 사용자가 취소한 것이 결과에 나타난다 (FR-12).
    /// </summary>
    public void AttachGeneratedMesh(
        Guid partId,
        Guid taskId,
        Guid meshRunId,
        IReadOnlyList<MeshArtifactDescriptor> artifacts,
        int? credits,
        DateTimeOffset now)
    {
        EnsureNotTerminal();

        if (_parts.All(part => part.Id != partId))
        {
            throw new InvalidOperationException($"작업에 없는 파츠입니다: {partId}");
        }

        _generatedMeshes.Add(
            GeneratedMesh.Create(Id, partId, taskId, meshRunId, artifacts, credits, now));
    }

    /// <summary>
    /// 이 파츠의 가장 나중 3D 결과. 아직 없으면 <c>null</c>.
    ///
    /// 재시도가 행을 쌓으므로 화면과 API 가 보는 것은 언제나 마지막 것이다.
    /// </summary>
    public GeneratedMesh? LatestMeshFor(Guid partId)
        => _generatedMeshes
            .Where(mesh => mesh.PartId == partId)
            .OrderByDescending(mesh => mesh.CreatedAt)
            .FirstOrDefault();

    /// <summary>
    /// 레이아웃 서명의 재료 — GLB 가 있는 파츠와 그 최신 mesh id.
    ///
    /// Design Ref: background-surface-parts(#20)
    ///
    /// **한 곳에서만 뽑는다.** 서명은 조회·유사도 시작·상태 조회·revision 복원 네 곳에서
    /// 계산되는데, 재료를 각자 만들던 시절에는 파츠 집합 기준마저 달랐다(`Parts` 전체 vs
    /// GLB 있는 것만). 저장하는 쪽과 비교하는 쪽이 조금이라도 갈리면 서명이 영원히
    /// 어긋나 유사도가 통째로 막힌다 — 재료를 여기서 한 번만 만드는 이유다.
    /// </summary>
    public IReadOnlyList<(Guid MeshId, AssetPart Part)> LayoutSignatureInputs()
        => [.. _parts
            .Select(part => (Mesh: LatestMeshFor(part.Id), Part: part))
            .Where(entry => entry.Mesh is not null)
            .Select(entry => (entry.Mesh!.Id, entry.Part))];

    /// <summary>
    /// 이 공정을 지금 실행해도 되는가 — 의존이 없거나, 의존한 공정이 **성공**했는가.
    ///
    /// Design Ref: §2.2
    ///
    /// **엔티티에 있는 이유** (사이클 #5): 오케스트레이터와 스위퍼가 같은 판정을 해야 한다.
    /// 오케스트레이터에만 있던 시절에는 공정이 하나뿐이라 스위퍼가 무조건 재적재해도
    /// 무해했지만, 공정이 셋이 되자 스위퍼가 **장면이 끝나기 전에 추출·분해를 큐에
    /// 넣는** 결함이 됐다. 판정이 두 곳에 흩어지면 언제든 다시 어긋난다.
    ///
    /// 팬인(다중 부모)은 간선 테이블이 필요하므로 지금은 단일 부모만 본다 (§2.3).
    /// 그때의 변경은 이 메서드 하나를 간선 조회로 바꾸는 추가형이다.
    /// </summary>
    public bool IsReadyToRun(PipelineTask task, DateTimeOffset now)
    {
        if (task.Status != TaskStatus.Pending)
        {
            return false;
        }

        // 백오프 대기 중이다 (generation-rate-limiting §3②) — 시각이 지나기 전엔
        // 준비된 것으로 보지 않는다. 스위퍼가 되돌린 공정은 NotBefore 가 null 이라
        // 여기서 항상 통과한다
        if (task.NotBefore is { } notBefore && notBefore > now)
        {
            return false;
        }

        if (task.DependsOnTaskId is not { } dependencyId)
        {
            return true;
        }

        return _tasks.FirstOrDefault(t => t.Id == dependencyId)
            is { Status: TaskStatus.Succeeded };
    }

    /// <summary>첫 공정이 시작되면 작업도 실행 중이 된다.</summary>
    public void MarkRunning()
    {
        if (Status == JobStatus.Pending)
        {
            Status = JobStatus.Running;
        }
    }

    /// <summary>장면 분석 결과 반영 (공정 0).</summary>
    public void ApplyScene(SceneSpec scene)
    {
        EnsureNotTerminal();
        Scene = scene;
    }

    /// <summary>
    /// 파츠 이름 반영 (공정 1).
    ///
    /// 파츠 순서는 LLM 이 낸 순서를 그대로 보존한다 — 사용자가 본 목록과 일치해야 한다.
    /// </summary>
    public void ApplyParts(IEnumerable<string> partNames)
    {
        EnsureNotTerminal();

        _parts.Clear();
        var ordinal = 0;
        foreach (var name in partNames)
        {
            _parts.Add(AssetPart.Create(Id, name, ordinal++));
        }
    }

    /// <summary>
    /// 분해 결과 반영 (공정 2).
    ///
    /// **유효성 위반은 예외다** (FR-05). 여기 있는 검사는 전부 품질이 아니라 오류다
    /// (Plan D-9) — 좌표가 화면 밖이거나, 없는 파츠를 가리키거나, 추출과 이름이
    /// 어긋나는 것은 취향의 문제가 아니다.
    ///
    /// 검사가 엔티티 안에 있는 이유는 규칙이 흩어지지 않게 하기 위해서다. 유스케이스에
    /// 두면 다음 호출 경로가 생길 때 같은 검사를 다시 써야 한다.
    /// </summary>
    /// <exception cref="PartValidationException">유효성 위반</exception>
    public void ApplyPartDetails(IReadOnlyList<PartDetail> details)
    {
        EnsureNotTerminal();
        ValidatePartDetails(details);

        var byName = details.ToDictionary(d => d.Name);
        foreach (var part in _parts)
        {
            part.ApplyDetail(byName[part.Name]);
        }
    }

    /// <summary>
    /// 겹침 판정 임계값 (review-gate 결정로그 T-03). 겹친 넓이 ÷ 작은 쪽 넓이가 이 값
    /// 이상이면 막는다. 실측 전 잠정치 — 실제 Decompose 좌표 분포로 구현 단계에서 조정한다.
    /// </summary>
    private const double OverlapThreshold = 0.15;

    /// <summary>
    /// 겹치는 파츠 이름 (occludedby-recompute §입력→출력 1).
    ///
    /// **순수 조회다** — 화면이 "무엇을 가리는가" 체크박스를 그리려면 추가하기 전에 겹침을
    /// 알아야 한다. 상태를 바꾸지 않는다.
    /// </summary>
    public IReadOnlyList<string> FindOverlappingPartNames(Bounds bounds)
        => [.. _parts
            .Where(p => p.Placements.Any(
                placement => bounds.OverlapCoefficient(placement) >= OverlapThreshold))
            .Select(p => p.Name)];

    /// <summary>
    /// 검수 화면에서 사람이 사각형으로 파츠 하나를 추가한다
    /// (occludedby-recompute §입력→출력 1).
    ///
    /// **겹쳐도 추가된다.** 겹침으로 생기는 가림 관계는 여기서 <c>occludedBy</c> 에 바로
    /// 반영하고, 가려지게 된 파츠의 서술만 승인 시점에 다시 쓴다(§목표). 서술 재작성 대상은
    /// <see cref="DescriptionsStale"/> 에 쌓인다.
    ///
    /// <paramref name="occludes"/> 는 **생략과 빈 목록의 뜻이 다르다**(§입력→출력 1 V-1).
    /// <c>null</c> 이면 기본 추정 — 겹치는 파츠 전부를 이 파츠가 가린다. 빈 목록이면
    /// 검수자가 전부 해제했다는 뜻이라 아무 관계도 기록하지 않는다.
    /// </summary>
    /// <exception cref="InvalidOperationException">검수 대기 상태가 아니다</exception>
    /// <exception cref="PartValidationException">
    /// 좌표가 프레임 밖이거나, 이름이 중복이거나, 겹치지 않는 파츠를 가린다고 지목했다
    /// </exception>
    public AssetPart AddReviewPart(
        string name,
        string? category,
        Bounds bounds,
        string? description,
        IReadOnlyList<string>? occludes = null)
    {
        if (Status != JobStatus.PendingReview)
        {
            throw new InvalidOperationException(
                $"검수 대기 상태에서만 파츠를 추가할 수 있습니다. 현재 상태: {Status}");
        }

        // 상자 편집은 상자 단계에서만 — 서술 단계 화면에 안 보이는 상자 변경 방지
        EnsureReviewPhase(ReviewPhase.Boxes);

        // 이름은 occludedBy·descriptionsStale·재작성 응답이 전부 쓰는 키다. 앞뒤 공백만
        // 다른 이름이 따로 들어오면 그 키가 어긋난다 (§함정3)
        var normalized = name.Trim();

        if (!bounds.IsWithinFrame())
        {
            throw new PartValidationException(
                PartValidationError.BoundsOutOfRange, $"'{normalized}' 좌표가 화면을 벗어났습니다.");
        }

        if (_parts.Any(p => p.Name == normalized))
        {
            throw new PartValidationException(
                PartValidationError.Duplicate, $"'{normalized}' 은 이미 있는 파츠 이름입니다.");
        }

        var overlapping = FindOverlappingPartNames(bounds);

        // 화면이 본 겹침 목록 밖을 지목하면 그 파츠 서술이 근거 없이 깎인다 (§함정9)
        var occluded = occludes ?? overlapping;
        var unrelated = occluded.FirstOrDefault(n => !overlapping.Contains(n));
        if (unrelated is not null)
        {
            throw new PartValidationException(
                PartValidationError.NotOverlapping,
                $"'{normalized}' 은 '{unrelated}' 과 겹치지 않아 가릴 수 없습니다.");
        }

        var nextOrdinal = _parts.Count == 0 ? 0 : _parts.Max(p => p.Ordinal) + 1;
        var nextDepthOrder = _parts
            .Where(p => p.DepthOrder.HasValue)
            .Select(p => p.DepthOrder!.Value)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var part = AssetPart.Create(Id, normalized, nextOrdinal);
        part.InitializeManual(category, description, bounds, nextDepthOrder);
        part.MarkManuallyAdded();
        _parts.Add(part);

        // 서술이 비면 승인 시 재작성 공정이 원본 이미지를 보고 채운다. 좌표 기반 대체 문구를
        // 넣던 것을 걷어냈다 — 좌표는 생성 프롬프트에 안 들어가므로 그 문장은 그림을 그릴 수
        // 있는 지시가 아니었고, 결과가 원본과 무관한 파츠로 나온다
        if (string.IsNullOrWhiteSpace(description))
        {
            MarkDescriptionStale(normalized);
        }

        // 가려지게 된 파츠는 서술에서 그 영역을 빼야 한다 — 승인 시 한 번에 다시 쓴다
        foreach (var target in _parts.Where(p => occluded.Contains(p.Name)))
        {
            target.AddOccluder(normalized);
            MarkDescriptionStale(target.Name);
        }

        return part;
    }

    /// <summary>
    /// 검수 화면에서 사람이 상자를 끌어 옮기거나 크기를 바꾼다.
    ///
    /// **지우고 다시 그리는 것과 다르다.** 삭제·추가로 고치면 이름·카테고리·서술을 다시
    /// 넣어야 해서 처음부터 그리는 노동과 같아진다. 여기서는 좌표만 바뀐다.
    ///
    /// **가림 관계를 다시 계산한다.** 상자가 움직이면 겹치던 파츠에서 벗어나고 새 파츠와
    /// 겹친다 — 관계를 그대로 두면 승인 시 엉뚱한 파츠의 서술을 깎는다. 추정 규칙은 추가
    /// 경로와 같다(겹치면 이 파츠가 위). 달라진 쪽만 서술 재작성 대상으로 표시한다.
    /// </summary>
    /// <exception cref="InvalidOperationException">검수 대기 상태가 아니다</exception>
    /// <exception cref="KeyNotFoundException">그 파츠나 배치 번호가 없다</exception>
    /// <exception cref="PartValidationException">좌표가 프레임 밖이다</exception>
    public void MoveReviewPlacement(Guid partId, int ordinal, Bounds bounds)
    {
        if (Status != JobStatus.PendingReview)
        {
            throw new InvalidOperationException(
                $"검수 대기 상태에서만 좌표를 고칠 수 있습니다. 현재 상태: {Status}");
        }

        // 상자 편집은 상자 단계에서만 — 서술 단계 화면에 안 보이는 상자 변경 방지
        EnsureReviewPhase(ReviewPhase.Boxes);

        var part = _parts.FirstOrDefault(p => p.Id == partId)
            ?? throw new KeyNotFoundException($"파츠를 찾을 수 없습니다: {partId}");

        if (!bounds.IsWithinFrame())
        {
            throw new PartValidationException(
                PartValidationError.BoundsOutOfRange, $"'{part.Name}' 좌표가 화면을 벗어났습니다.");
        }

        part.ReplacePlacement(ordinal, bounds);
        // 상자 확정과 겹친 옛 스냅샷 이동을 충돌로 막음 — 겹침 변화가 없으면 Jobs 행이 안 바뀜
        ReviewRevision++;

        // 자기 자신은 뺀다 — 파츠가 이미 목록에 있어 추가 경로와 달리 스스로와 겹친다
        var overlapping = FindOverlappingPartNames(bounds)
            .Where(name => name != part.Name)
            .ToHashSet();

        foreach (var other in _parts.Where(p => p.Id != part.Id))
        {
            var wasOccluded = other.OccludedBy.Contains(part.Name);
            var isOccluded = overlapping.Contains(other.Name);
            if (wasOccluded == isOccluded)
            {
                continue;
            }

            // 가리게 됐든 벗어났든 그 파츠의 서술은 지금 상태와 어긋난다
            if (isOccluded)
            {
                other.AddOccluder(part.Name);
            }
            else
            {
                other.RemoveOccluder(part.Name);
            }

            MarkDescriptionStale(other.Name);
        }
    }

    /// <summary>
    /// 검수 화면에서 사람이 파츠 하나를 제외한다 (occludedby-recompute §입력→출력 2).
    ///
    /// **참조 정리가 삭제의 일부다.** 지운 이름이 남의 <c>occludedBy</c> 에 유령으로 남으면
    /// API 응답과 재작성 입력이 오염되고, 지운 이름이 <see cref="DescriptionsStale"/> 에
    /// 남으면 승인 시 "없는 파츠를 다시 써라" 가 되어 재작성이 실패하고 그 실패가 작업
    /// 전체를 죽인다.
    /// </summary>
    /// <exception cref="InvalidOperationException">검수 대기 상태가 아니다</exception>
    /// <exception cref="KeyNotFoundException">그 id 의 파츠가 없다</exception>
    public void RemoveReviewPart(Guid partId)
    {
        if (Status != JobStatus.PendingReview)
        {
            throw new InvalidOperationException(
                $"검수 대기 상태에서만 파츠를 삭제할 수 있습니다. 현재 상태: {Status}");
        }

        // 상자 편집은 상자 단계에서만 — 서술 단계 화면에 안 보이는 상자 변경 방지
        EnsureReviewPhase(ReviewPhase.Boxes);

        var part = _parts.FirstOrDefault(p => p.Id == partId)
            ?? throw new KeyNotFoundException($"파츠를 찾을 수 없습니다: {partId}");
        _parts.Remove(part);

        // 이 파츠에 가려져 있던 파츠들은 서술이 아직 "가린 채"라 다시 써야 한다
        foreach (var other in _parts.Where(p => p.OccludedBy.Contains(part.Name)))
        {
            other.RemoveOccluder(part.Name);
            MarkDescriptionStale(other.Name);
        }

        _descriptionsStale.Remove(part.Name);
    }

    /// <summary>
    /// 서술 재작성 결과 반영 (occludedby-recompute §입력→출력 3).
    ///
    /// **<see cref="ValidatePartDetails"/> 를 쓰지 않는다**(§함정6). 그 검사는 파츠 전체
    /// 목록을 1:1 로 받는 전제인데 여기 오는 것은 부분집합이다. 대신 대상 이름이 전부
    /// 있는지와 빈 서술이 아닌지만 본다.
    ///
    /// 좌표·가림 관계는 건드리지 않는다 — 모델에게 묻지도 않았다.
    /// </summary>
    /// <exception cref="PartValidationException">없는 파츠를 가리키거나 서술이 비었다</exception>
    public void ApplyDescriptionRewrites(
        IReadOnlyList<(string Name, string Description, string? Category)> rewrites)
    {
        EnsureNotTerminal();
        ValidateDescriptionRewrites(rewrites);

        var byName = _parts.ToDictionary(p => p.Name);
        // 재작성 대상만 반영 — 프롬프트가 가리는 파츠 서술도 넘기므로 모델이 그 파츠까지
        // 응답할 수 있고, 반영하면 사람 서술이 덮임 (staged 함정 4)
        foreach (var (name, description, category) in rewrites.Where(r => _descriptionsStale.Contains(r.Name)))
        {
            byName[name].ReviseDescription(description, category);
        }

        // 다시 쓴 파츠는 대상에서 뺀다 — 남겨두면 다음 승인이 또 재작성을 계획한다
        _descriptionsStale.RemoveAll(name => rewrites.Any(r => r.Name == name));
    }

    /// <summary>
    /// 반영 없이 검사만 한다 — 공정을 성공으로 확정하기 전에 부른다
    /// (<see cref="ValidatePartDetails"/> 와 같은 이유).
    /// </summary>
    /// <exception cref="PartValidationException">없는 파츠를 가리키거나 서술이 비었다</exception>
    public void ValidateDescriptionRewrites(
        IReadOnlyList<(string Name, string Description, string? Category)> rewrites)
    {
        var known = _parts.Select(p => p.Name).ToHashSet();

        foreach (var (name, description, _) in rewrites)
        {
            if (!known.Contains(name))
            {
                throw new PartValidationException(
                    PartValidationError.UnknownReference,
                    $"'{name}' 은 이 작업에 없는 파츠입니다.");
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                throw new PartValidationException(
                    PartValidationError.NameMismatch, $"'{name}' 의 서술이 비었습니다.");
            }
        }
    }

    /// <summary>
    /// 서술을 다시 써야 하는 파츠 이름 (occludedby-recompute §입력→출력 3).
    ///
    /// 승인 시 이 목록이 비어 있으면 곧바로 팬아웃하고, 비어 있지 않으면 이 파츠들만 묶어
    /// 서술 재작성 공정 하나를 계획한다 — 편집을 몇 개 했든 승인당 1회다.
    /// </summary>
    public IReadOnlyList<string> DescriptionsStale => _descriptionsStale;

    private readonly List<string> _descriptionsStale = [];

    // 같은 파츠를 두 번 담지 않는다 — 재작성 응답 매칭이 이름 유일성을 전제한다
    // 사람이 쓴 서술은 담지 않는다 — 되돌리기 후 재승인이 사람 서술을 덮지 않게 (staged 함정 4)
    private void MarkDescriptionStale(string partName)
    {
        if (_parts.Any(p => p.Name == partName && p.DescriptionSource == DescriptionSource.Human))
        {
            return;
        }

        if (!_descriptionsStale.Contains(partName))
        {
            _descriptionsStale.Add(partName);
        }
    }

    /// <summary>
    /// 반영 없이 검사만 한다.
    ///
    /// **공정을 성공으로 확정하기 전에 불러야 한다.** 확정 뒤에 검사가 실패하면
    /// 그 공정은 이미 종료 상태라 실패로 바꿀 수 없고, 잘못된 결과가 성공으로 남는다.
    /// </summary>
    /// <exception cref="PartValidationException">유효성 위반</exception>
    /// <summary>파츠당 배치 수. 스무 개를 넘으면 개체 목록이 아니라 면으로 다루는 편이 맞다 (D-04).</summary>
    private const int MinPlacements = 1;

    private const int MaxPlacements = 20;

    public void ValidatePartDetails(IReadOnlyList<PartDetail> details)
    {
        var known = _parts.Select(p => p.Name).ToHashSet();

        // 중복을 먼저 본다. 이름은 전부 맞는데 하나가 두 번 오는 일이 실제로 있었고,
        // 그때 "이름 불일치" 로 안내하면 사용자가 엉뚱한 곳을 고친다
        var duplicate = details
            .GroupBy(d => d.Name)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new PartValidationException(
                PartValidationError.Duplicate,
                $"'{duplicate.Key}' 가 {duplicate.Count()}번 나왔습니다 — 파츠마다 한 번씩이어야 합니다");
        }

        // 추출이 낸 이름과 정확히 같아야 한다. 분해가 파츠를 더하거나 이름을 고치면
        // 두 단계의 결과가 어긋나고, 그 뒤 단계는 어느 쪽을 믿을지 알 수 없다
        if (details.Count != _parts.Count || !details.All(d => known.Contains(d.Name)))
        {
            throw new PartValidationException(
                PartValidationError.NameMismatch,
                $"파츠 이름이 추출 결과와 다릅니다 (추출 {_parts.Count}개, 분해 {details.Count}개)");
        }

        foreach (var detail in details)
        {
            // 놓을 자리가 없는 파츠는 이름만 있고 위치가 없다 — 고치기 전 상태와 같다 (FR-05)
            if (detail.Placements.Count is < MinPlacements or > MaxPlacements)
            {
                throw new PartValidationException(
                    PartValidationError.BoundsOutOfRange,
                    $"'{detail.Name}' 의 배치는 {MinPlacements}~{MaxPlacements}개여야 합니다 " +
                    $"(받은 값 {detail.Placements.Count}개)");
            }

            // **몇 번째인지 밝힌다.** 배치가 스무 개까지 올 수 있어 "좌표가 벗어납니다" 만으로는
            // 어느 것을 고칠지 알 수 없다. 하나라도 벗어나면 파츠 전체가 실패한다 (D-06) —
            // 일부만 버리면 화면이 조용히 불완전해진다
            for (var index = 0; index < detail.Placements.Count; index++)
            {
                if (!detail.Placements[index].IsWithinFrame())
                {
                    throw new PartValidationException(
                        PartValidationError.BoundsOutOfRange,
                        $"'{detail.Name}' 의 배치[{index}] 좌표가 화면을 벗어납니다");
                }
            }

            var unknown = detail.OccludedBy.FirstOrDefault(name => !known.Contains(name));
            if (unknown is not null)
            {
                throw new PartValidationException(
                    PartValidationError.UnknownReference,
                    $"'{detail.Name}' 이 존재하지 않는 파츠 '{unknown}' 에 가려진다고 합니다");
            }
        }

        // 깊이가 겹치면 앞뒤 순서를 정의하지 못한다 — 조립 단계가 쌓을 수 없다.
        //
        // **캐릭터는 예외다** (2026-08-31 결정,
        // docs/specs/2026-08-31-character-depth-duplicate-allowed.md). DepthOrder 의
        // 유일한 소비처(SceneLayoutComposer 의 z 좌표)는 배경 전용이고, 캐릭터에는 3D
        // 조립이 없다 — 최종 산출물은 파츠 세트다. 겹치지 않는 파츠(어깨 갑주·귀걸이 등)
        // 사이에까지 전순서를 강제해 재시도를 소진시키던 것을 여기서 걷어낸다.
        if (Category != AssetCategory.Character)
        {
            var sameDepth = details
                .GroupBy(d => d.DepthOrder)
                .FirstOrDefault(g => g.Count() > 1);

            if (sameDepth is not null)
            {
                throw new PartValidationException(
                    PartValidationError.DepthDuplicate,
                    $"깊이 순서 {sameDepth.Key} 가 중복됩니다: {string.Join(", ", sameDepth.Select(d => d.Name))}");
            }
        }
    }

    private void EnsureNotTerminal()
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException(
                $"종료된 작업에는 결과를 반영할 수 없습니다. 현재 상태: {Status}");
        }
    }

    public void Succeed(DateTimeOffset now)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException(
                $"이미 종료된 작업입니다. 현재 상태: {Status}");
        }

        Status = JobStatus.Succeeded;
        CompletedAt = now;
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException(
                $"이미 종료된 작업입니다. 현재 상태: {Status}");
        }

        Status = JobStatus.Failed;
        FailureReason = reason;
        CompletedAt = now;
    }

    /// <summary>
    /// 취소. 아직 끝나지 않은 공정도 함께 취소한다.
    ///
    /// 실행 중이던 공정은 워커가 다음 리스 갱신(최대 15초)에 알아채고 스스로 끊는다 —
    /// 브로커는 워커 프로세스 안을 모르므로 중단은 협조적이어야 한다 (§2.2).
    /// </summary>
    public void Cancel(DateTimeOffset now)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException(
                $"이미 종료된 작업입니다. 현재 상태: {Status}");
        }

        foreach (var task in _tasks)
        {
            task.Cancel(now);
        }

        Status = JobStatus.Canceled;
        CompletedAt = now;
    }

    /// <summary>
    /// 공정 결과를 작업 상태에 반영한다.
    ///
    /// **사이클 #7 에서 분기가 하나 늘었다.** 이전에는 "하나라도 실패하면 작업 실패" 였는데,
    /// 생성 공정은 그렇지 않다 (Plan D-3) — 9장 중 7장을 버리는 비용이 상태 하나 늘리는
    /// 비용보다 크다.
    ///
    /// **순서가 곧 규칙이다**:
    /// ① 생성이 **아닌** 공정이 실패 → 작업 실패 (뒤가 못 돈다, FR-07)
    /// ② 취소된 공정이 있으면 취소
    /// ③ 아직 안 끝난 공정이 있으면 아무것도 하지 않는다 — 남은 것이 성공할 수도 있다
    /// ④ 전부 성공 → 성공
    /// ⑤ 생성이 하나라도 성공했고 일부 실패 → **부분 성공**
    /// ⑥ 생성이 전부 실패 → 실패 (건질 것이 없다, V-3)
    /// </summary>
    public void ReconcileFromTasks(DateTimeOffset now)
    {
        if (IsTerminal || _tasks.Count == 0)
        {
            return;
        }

        // 정면이 실패하면 그에 의존하는 비정면 3개는 IsReadyToRun(부모가 Succeeded 여야
        // 함)을 영원히 통과 못 해 Pending 에 갇힌다 (workstream B §6). 종료 판정 직전에
        // Failed 로 확정해 행업을 막는다 — 호출자가 워커 완료 경로든 스위퍼든 이 메서드를
        // 거치므로 두 경로가 여기 한 곳으로 커버된다 (§6.1-②)
        CascadeFrontFailureToSiblings(now);

        // ① 앞 세 단계의 실패는 뒤 단계의 입력을 없앤다 — 부분 성공이 성립하지 않는다.
        //
        // **결과물을 내는 두 단계는 여기 들지 않는다** (§4.6). 3D 가 전부 실패해도 네 방향
        // 이미지는 그대로 남아 있고, 그것만으로도 사용자가 쓸 것이 있다
        var blockingFailure = _tasks.FirstOrDefault(
            t => t.Status == TaskStatus.Failed
                 && t.Kind is not (TaskKind.Generate or TaskKind.Reconstruct));

        if (blockingFailure is not null)
        {
            Fail(blockingFailure.FailureReason ?? "공정이 실패했습니다", now);
            return;
        }

        if (_tasks.Any(t => t.Status == TaskStatus.Canceled))
        {
            Status = JobStatus.Canceled;
            CompletedAt = now;
            return;
        }

        // review-gate §함정1 — 검수 대기 중에는 여기서 더 내려가지 않는다. Generate 를
        // 아직 계획 안 한 상태라 아래 outputs 판정이 빈 배열을 "전원 성공"으로 오판한다.
        // 실패·취소 판정 뒤 — 앞이면 게이트 작업의 분해·재작성 실패가 Running 에 갇힌다 (staged §함정2)
        if (IsAwaitingReview)
        {
            return;
        }

        // ③ 미완 공정이 남아 있으면 판정을 미룬다. 실패한 생성 공정이 이미 있어도 마찬가지다 —
        // 남은 형제가 끝나기 전에 부분 성공으로 확정하면 그 뒤의 성공을 반영할 자리가 없다
        if (_tasks.Any(t => !t.IsTerminal))
        {
            return;
        }

        // 결과물을 내는 두 단계를 함께 센다 — 이미지든 mesh 든 하나라도 남으면 건진 것이 있다
        var outputs = _tasks
            .Where(t => t.Kind is TaskKind.Generate or TaskKind.Reconstruct)
            .ToArray();

        if (outputs.All(t => t.Status == TaskStatus.Succeeded))
        {
            Succeed(now);
            return;
        }

        // ⑤/⑥ — 하나라도 건졌는가가 갈림길이다.
        //
        // **3D 가 전부 실패해도 이미지가 남으면 부분 성공이다** (§4.6). 실패로 확정하면
        // 사용자는 아무것도 못 받았다고 읽는데, 실제로는 가장 비싼 중간 결과가 그대로 있다
        if (outputs.Any(t => t.Status == TaskStatus.Succeeded))
        {
            Status = JobStatus.PartiallySucceeded;
            CompletedAt = now;
            return;
        }

        Fail(
            _tasks.First(t => t.Status == TaskStatus.Failed).FailureReason
                ?? "생성 공정이 모두 실패했습니다",
            now);
    }

    /// <summary>
    /// 캐스케이드로 스킵됐다는 표식. <see cref="RetryOutputTask"/> 가 형제를 되살릴 때
    /// 이 사유로 실패한 것만 골라야 한다 — 독립적으로 실패한 형제(§4.7 개별 재시도
    /// 대상)까지 정면 재시도에 묻어 되살리면 사용자가 안 시킨 재생성이 과금된다.
    /// </summary>
    private const string CascadeSkipReason = "정면 실패로 스킵";

    /// <summary>
    /// 정면이 실패한 파츠의 비정면 3개를 <see cref="TaskStatus.Failed"/> 로 내린다
    /// (workstream B §6.1-①).
    ///
    /// **신규 <c>Skipped</c> 상태를 만들지 않고 <c>Failed</c> 를 재사용한다.** 새 상태를
    /// 넣으면 <see cref="TaskStatus"/> enum·<see cref="PipelineTask.IsTerminal"/>·EF 매핑까지
    /// 전부 함께 고쳐야 하고, 하나라도 빠뜨리면 형제가 비종료로 남아 행업이 재발한다.
    /// <c>Failed</c> 는 이미 terminal 인 데다, Generate 는 <see cref="ReconcileFromTasks"/> 의
    /// blockingFailure(비생성만)에서 제외되므로 다른 파츠의 부분 성공을 막지 않는다.
    /// </summary>
    private void CascadeFrontFailureToSiblings(DateTimeOffset now)
    {
        var failedFronts = _tasks.Where(t =>
            t.Kind == TaskKind.Generate && t.Status == TaskStatus.Failed
            && t.ViewDirection == ViewDirection.Front);

        foreach (var front in failedFronts)
        {
            foreach (var sibling in _tasks.Where(t =>
                t.Kind == TaskKind.Generate && t.Status == TaskStatus.Pending
                && t.DependsOnTaskId == front.Id))
            {
                sibling.Fail(CascadeSkipReason, now);
            }
        }
    }
}
