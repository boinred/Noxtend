using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Application.Generation;
using Noxtend.Application.Job;
using Noxtend.Application.Mesh;
using Noxtend.Application.Pipeline;
using Noxtend.Application.Stages;
using Noxtend.Application.Sprites;
using Noxtend.Application.Uploads;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Blob;
using Noxtend.Infrastructure.Image;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Infrastructure.Persistence.InMemory;
using Noxtend.Infrastructure.Queue;
using Noxtend.Infrastructure.RateLimit;

namespace Noxtend.Tests.Application;

/// <summary>
/// 인프라 없는 파이프라인 조립.
///
/// Design Ref: §2.0 · §8.1 — **L1-B 가 인프라를 요구하지 않는 것이 Option B 를 고른
/// 이유다.** DB·Redis·Blob·k8s·실제 LLM 없이 유스케이스·오케스트레이터·스위퍼가 돈다.
/// </summary>
public sealed class PipelineFixture
{
    public PipelineFixture(
        ILlmProvider? provider = null,
        JobOptions? options = null,
        IImageProvider? imageProvider = null,
        GenerationOptions? generationOptions = null)
    {
        Llm = provider ?? FakeLlmProvider.Succeeding();
        Options = options ?? new JobOptions { LeaseSeconds = 120, LeaseRenewSeconds = 15, MaxAttempts = 3 };

        Images_ = imageProvider ?? FakeImageProvider.Succeeding();
        GenerationOptions = generationOptions
            ?? new GenerationOptions { LeaseSeconds = 360, LeaseRenewSeconds = 30 };

        // 업로드 표를 넘겨야 삭제가 원본의 실제 저장소 키를 찾는다
        Jobs = new InMemoryJobRepository(Images);

        Orchestrator = new JobOrchestrator(Queue, Jobs, Clock);
        Upload = new CreateUploadHandler(Blobs, Images, Clock);
        MeshSelection = new MeshSelectionValidator(Providers, Catalog);
        Start = new StartJobHandler(
            Jobs, Images, Providers, Catalog, Prompts, Orchestrator, MeshSelection, Clock);
        // 사이클 #7: 실행 골격이 TaskExecution 으로 빠졌다. 생성 핸들러도 같은 것을 쓴다 (§2.0)
        Execution = new TaskExecution(
            Jobs, Orchestrator, Clock, Scheduler, NullLogger<TaskExecution>.Instance);

        RateLimitGate = new RateLimitGate(RateLimiter, Clock);
        Run = new RunTaskHandler(
            Images, Blobs, new StubProviderFactory(Llm), Prompts,
            new StageRegistry(
                [new AnalyzeStage(), new ExtractStage(), new DecomposeStage(),
                 new RewriteDescriptionsStage()]),
            Execution, Options, RateLimitGate);
        RunGeneration = new RunGenerationTaskHandler(
            Images, Blobs, new StubImageProviderFactory(Images_), Prompts,
            new GenerationStage(GenerationOptions),
            Execution, Clock, Options, GenerationOptions,
            RateLimitGate,
            NullLogger<RunGenerationTaskHandler>.Instance);
        StartSprites = new StartSpriteJobHandler(Jobs, Images, Blobs, Providers, Catalog, Prompts,
            new SkiaImageTranscoder(), Orchestrator, Clock, NullLogger<StartSpriteJobHandler>.Instance);
        RunSpriteAnalysis = new RunSpriteAnalysisTaskHandler(Images, Blobs, new StubProviderFactory(Llm),
            Prompts, Execution, Options, RateLimitGate);
        RunSpriteGeneration = new RunSpriteGenerationTaskHandler(Jobs, Images, Blobs,
            new StubImageProviderFactory(Images_), Prompts, new SkiaImageTranscoder(), Execution,
            Clock, Options, GenerationOptions, RateLimitGate, NullLogger<RunSpriteGenerationTaskHandler>.Instance);
        Cancel = new CancelJobHandler(Jobs, Clock);
        Retry = new RetryTaskHandler(Jobs, MeshRuns, Orchestrator);
        Get = new GetJobHandler(Jobs, MeshRuns);
        List = new ListJobsHandler(Jobs);
        Delete = new DeleteJobHandler(Jobs, Blobs, MeshArtifacts, NullLogger<DeleteJobHandler>.Instance);
        Sweep = new SweepStaleTasksHandler(Jobs, Queue, Clock, Options);
        AddMesh = new AddMeshProductionHandler(Jobs, MeshSelection, Orchestrator, Clock);
        ReplanPartMesh = new ReplanPartMeshHandler(
            Jobs, Blobs, new SkiaImageTranscoder(), MeshSelection, Orchestrator, Clock,
            NullLogger<ReplanPartMeshHandler>.Instance);

        // review-gate — 검수 게이트
        GetReview = new GetReviewHandler(Jobs);
        AddReviewPart = new AddReviewPartHandler(Jobs);
        FindOverlaps = new FindOverlapsHandler(Jobs);
        RemoveReviewPart = new RemoveReviewPartHandler(Jobs);
        MoveReviewPlacement = new MoveReviewPlacementHandler(Jobs);
        ApproveReview = new ApproveReviewHandler(Jobs, Orchestrator, Clock);
        ReviewDescriptions = new ReviewDescriptionsHandler(Jobs, Orchestrator, Clock);
        GenerateViews = new GenerateSelectedViewsHandler(Jobs, Orchestrator);
        ReturnToDescriptions = new ReturnToDescriptionsFromGenerationHandler(Jobs, Orchestrator, Clock);
    }

    public FixedClock Clock { get; } = FixedClock.Default;
    public InMemoryJobRepository Jobs { get; }

    /// <summary>백오프 재확인 예약을 실제 시간 없이 검증하는 창구.</summary>
    public RecordingDelayedActionScheduler Scheduler { get; } = new();

    /// <summary>3D 실행 기록 (사이클 #10). 진행률이 여기서 온다 (§8.5).</summary>
    public InMemoryMeshRunRepository MeshRuns { get; } = new();

    /// <summary>접수와 뒤늦은 지정이 공유하는 3D 선택 검증 (사이클 #11 D-11).</summary>
    public MeshSelectionValidator MeshSelection { get; }

    /// <summary>끝난 작업에 3D 를 붙인다 (사이클 #11).</summary>
    public AddMeshProductionHandler AddMesh { get; }

    /// <summary>파츠별 "3D 전송 뷰 선택 + 대칭" (spec 20260917).</summary>
    public ReplanPartMeshHandler ReplanPartMesh { get; }
    public InMemoryStoredImageRepository Images { get; } = new();
    public InMemoryProviderConfigRepository Providers { get; } = new();
    public InMemoryBlobStorage Blobs { get; } = new();

    /// <summary>3D 산출물 저장소. 삭제가 실제로 파일을 치우는지 보는 창구다.</summary>
    public InMemoryMeshArtifactStorage MeshArtifacts { get; } = new();
    public InMemoryTaskQueue Queue { get; } = new();
    public ILlmProvider Llm { get; }
    public JobOptions Options { get; }

    /// <summary>공유 실행 골격. 생성 공정이 붙으면 그쪽 핸들러도 이것을 쓴다.</summary>
    public TaskExecution Execution { get; }

    /// <summary>Fake 이미지 공급자 — 이름이 <c>Images</c>(업로드 저장소)와 겹쳐 뒤에 밑줄을 붙였다.</summary>
    public IImageProvider Images_ { get; }

    public GenerationOptions GenerationOptions { get; }

    /// <summary>고를 수 있는 모델 목록. 테스트가 이 목록을 바꿔 "사라진 모델" 을 재현한다.</summary>
    public StubModelCatalog Catalog { get; } = new();

    /// <summary>활성 프롬프트. 테스트가 특정 단계를 비워 PROMPT_NOT_ACTIVE 를 재현한다.</summary>
    public StubPromptCatalog Prompts { get; } = new();

    public JobOrchestrator Orchestrator { get; }
    public CreateUploadHandler Upload { get; }
    public StartJobHandler Start { get; }
    public StartSpriteJobHandler StartSprites { get; }
    public RunSpriteAnalysisTaskHandler RunSpriteAnalysis { get; }
    public RunSpriteGenerationTaskHandler RunSpriteGeneration { get; }
    public RunTaskHandler Run { get; }

    /// <summary>이미지 경로의 핸들러 (사이클 #7). 같은 <see cref="TaskExecution"/> 위에 선다.</summary>
    public RunGenerationTaskHandler RunGeneration { get; }

    /// <summary>속도 제한 상태 저장소 (generation-rate-limiting §3①). Redis 없이 인메모리.</summary>
    public InMemoryRateLimiter RateLimiter { get; } = new();

    /// <summary>호출 전 확인·대기. 테스트가 미리 상태를 심어 대기를 재현할 수 있다.</summary>
    public RateLimitGate RateLimitGate { get; }

    public CancelJobHandler Cancel { get; }

    /// <summary>실패한 파츠 하나만 다시 돌린다 (사이클 #7 FR-08).</summary>
    public RetryTaskHandler Retry { get; }
    public GetJobHandler Get { get; }
    public ListJobsHandler List { get; }
    public DeleteJobHandler Delete { get; }
    public SweepStaleTasksHandler Sweep { get; }

    /// <summary>검수 대기 상태 조회 (review-gate).</summary>
    public GetReviewHandler GetReview { get; }

    /// <summary>검수 화면에서 파츠 추가.</summary>
    public AddReviewPartHandler AddReviewPart { get; }

    public FindOverlapsHandler FindOverlaps { get; }

    /// <summary>검수 화면에서 파츠 삭제.</summary>
    public RemoveReviewPartHandler RemoveReviewPart { get; }

    public MoveReviewPlacementHandler MoveReviewPlacement { get; }

    /// <summary>전체 승인 — 보류됐던 팬아웃을 계획하고 큐에 적재한다.</summary>
    public ApproveReviewHandler ApproveReview { get; }
    public ReviewDescriptionsHandler ReviewDescriptions { get; }
    public GenerateSelectedViewsHandler GenerateViews { get; }
    public ReturnToDescriptionsFromGenerationHandler ReturnToDescriptions { get; }

    /// <summary>
    /// 업로드 + 공급자를 갖춘 상태에서 작업을 접수한다 — 대부분의 테스트가 여기서 시작한다.
    ///
    /// 사이클 #5 부터 공정이 셋이다. 의존이 없는 첫 공정(Analyze)만 큐에 들어간다.
    /// </summary>
    /// <param name="reuseUploadId">
    /// 같은 원본으로 두 번 접수하는 상황. 원본 공유 검사가 이것을 쓴다.
    /// </param>
    public async Task<PipelineJob> StartJobAsync(
        Guid? reuseUploadId = null, AssetCategory category = AssetCategory.Background)
    {
        var uploadId = reuseUploadId ?? await SeedUploadAsync();
        var providerId = await SeedProviderAsync();

        var result = await Start.HandleAsync(
            category, uploadId, providerId, StubModelCatalog.DefaultModel, CancellationToken.None,
            // 캐릭터는 성별이 필수(character-studio §3.2) — 테스트 기본값으로 채운다
            gender: DefaultGenderFor(category));
        Assert.True(result.IsSuccess, result.ErrorCode);

        return result.Value!;
    }

    // 캐릭터면 성별을 채우고, 타 카테고리는 null(안 보냄)
    private static Gender? DefaultGenderFor(AssetCategory category)
        => category == AssetCategory.Character ? Gender.Female : null;

    public async Task<Guid> SeedUploadAsync()
    {
        using var content = new MemoryStream([0x89, 0x50, 0x4E, 0x47]);
        var result = await Upload.HandleAsync(content, "harbor.png", "image/png", 4, CancellationToken.None);
        Assert.True(result.IsSuccess);

        return result.Value!.Id;
    }

    public async Task<Guid> SeedProviderAsync(bool enabled = true)
    {
        // 암호문을 직접 넣는다. 평문 키는 Infrastructure 밖으로 나가지 않으므로
        // Application 테스트가 다룰 대상이 아니다 (§2.2)
        var config = ProviderConfig.Create(
            "Claude 운영", ProviderKind.Anthropic,
            apiKeyCipher: "cipher", apiKeyLast4: "4f2c", Clock.Now);

        if (!enabled)
        {
            config.Update("Claude 운영", ProviderKind.Anthropic,
                null, null, isEnabled: false, Clock.Now);
        }

        await Providers.AddAsync(config, CancellationToken.None);
        return config.Id;
    }

    /// <summary>
    /// 공정 셋을 순서대로 끝까지 돌린다.
    ///
    /// 한 공정만 돌리는 테스트가 많지만, "작업이 성공했는가" 를 보는 테스트는 셋을 다
    /// 돌려야 한다 — 마지막 공정이 끝나야 작업이 성공한다.
    /// </summary>
    public async Task RunAllStagesAsync(PipelineJob job)
    {
        foreach (var kind in new[] { TaskKind.Analyze, TaskKind.Extract, TaskKind.Decompose })
        {
            var current = await Jobs.GetAsync(job.Id, CancellationToken.None);
            var task = current!.Tasks.First(t => t.Kind == kind);

            // 이미 성공한 공정은 건너뛴다 — 스위퍼 시나리오처럼 일부만 돌려둔 뒤
            // 나머지를 이어 붙이는 테스트가 있다
            if (task.Status == Noxtend.Domain.Job.TaskStatus.Succeeded)
            {
                continue;
            }

            if (task.Status != Noxtend.Domain.Job.TaskStatus.Pending)
            {
                break;   // 실패·취소된 공정 뒤로는 진행하지 않는다
            }

            await Run.HandleAsync(task.Id, CancellationToken.None);
        }
    }

    /// <summary>첫 공정(Analyze)만 돌린다 — 단일 공정 동작을 보는 테스트용.</summary>
    public async Task<RunTaskOutcome> RunFirstStageAsync(PipelineJob job)
        => await Run.HandleAsync(job.Tasks.First(t => t.Ordinal == 0).Id, CancellationToken.None);

    /// <summary>
    /// 한 공정을 **종료 상태가 될 때까지** 돌린다.
    ///
    /// 계약 위반은 이제 한도까지 재시도되므로 (§2.2), "한 번 돌려서 실패" 를 단정하던
    /// 테스트가 전부 이것으로 바뀐다. 확정 실패를 보려면 한도를 소진시켜야 한다.
    /// </summary>
    public async Task<RunTaskOutcome> RunUntilTerminalAsync(PipelineJob job, TaskKind kind)
    {
        var task = job.Tasks.First(t => t.Kind == kind);
        var outcome = RunTaskOutcome.Skipped;

        // 한도 + 여유 1회. 무한 루프로 테스트가 멈추는 것을 막는다
        for (var i = 0; i <= Options.MaxAttempts; i++)
        {
            if (task.IsTerminal)
            {
                break;
            }

            outcome = await Run.HandleAsync(task.Id, CancellationToken.None);
        }

        return outcome;
    }

    /// <summary>공급자 종류를 모르는 Factory. 실제 구현은 여기서 키를 복호화한다 (§2.2).</summary>
    private sealed class StubProviderFactory(ILlmProvider provider) : ILlmProviderFactory
    {
        public Task<ILlmProvider> CreateAsync(Guid providerConfigId, string model, CancellationToken ct)
            => Task.FromResult(provider);
    }

    internal sealed class StubImageProviderFactory(IImageProvider provider) : IImageProviderFactory
    {
        public Task<IImageProvider> CreateAsync(Guid providerConfigId, string model, CancellationToken ct)
            => Task.FromResult(provider);
    }

    /// <summary>
    /// 이미지 공급자까지 지정해 작업을 접수한다 (사이클 #7).
    ///
    /// 텍스트 공급자와 같은 설정을 쓴다 — 이 계층에서 검증하는 것은 공급자 종류가 아니라
    /// **접수가 이미지 모델을 따로 확인하는가** 다.
    /// </summary>
    public async Task<PipelineJob> StartJobWithGenerationAsync(
        AssetCategory category = AssetCategory.Background, bool requiresReview = false)
    {
        var uploadId = await SeedUploadAsync();
        var providerId = await SeedProviderAsync();

        var result = await Start.HandleAsync(
            category, uploadId, providerId, StubModelCatalog.DefaultModel, CancellationToken.None,
            imageProviderConfigId: providerId,
            imageModel: StubModelCatalog.DefaultImageModel,
            gender: DefaultGenderFor(category),
            requiresReview: requiresReview);

        Assert.True(result.IsSuccess, result.ErrorCode);
        return result.Value!;
    }

    /// <summary>
    /// 앞 세 단계를 끝내 팬아웃까지 마친 작업.
    ///
    /// 생성 공정은 분해가 끝나야 계획되므로(§2.3 A-2) 여기까지 와야 존재한다.
    /// </summary>
    public async Task<PipelineJob> FanOutAsync(AssetCategory category = AssetCategory.Background)
    {
        var job = await StartJobWithGenerationAsync(category);
        await RunAllStagesAsync(job);

        return (await Jobs.GetAsync(job.Id, CancellationToken.None))!;
    }

    /// <summary>
    /// 분해까지 끝나 검수 대기(<see cref="JobStatus.PendingReview"/>)로 멈춘 작업 (review-gate).
    ///
    /// <see cref="FanOutAsync"/> 와 같은 단계를 돌리지만 <c>requiresReview: true</c> 라
    /// Generate 팬아웃은 계획되지 않는다 — <see cref="ApproveReview"/> 를 불러야 이어진다.
    /// </summary>
    public async Task<PipelineJob> ReachPendingReviewAsync(AssetCategory category = AssetCategory.Character)
    {
        var job = await StartJobWithGenerationAsync(category, requiresReview: true);
        await RunAllStagesAsync(job);

        return (await Jobs.GetAsync(job.Id, CancellationToken.None))!;
    }

    /// <summary>생성 공정 하나를 종료 상태가 될 때까지 돌린다.</summary>
    public async Task<RunTaskOutcome> RunGenerationUntilTerminalAsync(PipelineTask task)
    {
        var outcome = RunTaskOutcome.Skipped;

        // 한도 + 여유 1회. 무한 루프로 테스트가 멈추는 것을 막는다
        for (var i = 0; i <= Options.MaxAttempts && !task.IsTerminal; i++)
        {
            outcome = await RunGeneration.HandleAsync(task.Id, CancellationToken.None);
        }

        return outcome;
    }
}

/// <summary>
/// 실제 시간 없이 백오프 재확인 예약을 검증하는 대역.
///
/// 리뷰 지적(백오프가 스위퍼 주기에 종속되던 문제)을 고치면서 추가됐다 — 예약이
/// "됐는지"는 <see cref="Scheduled"/> 로, "그 시각이 되면 실행되는지"는
/// <see cref="FireAllAsync"/> 로 실제 대기 없이 검증한다.
/// </summary>
public sealed class RecordingDelayedActionScheduler : Noxtend.Domain.Ports.IDelayedActionScheduler
{
    private readonly List<(TimeSpan Delay, Func<CancellationToken, Task> Action)> _scheduled = [];

    public IReadOnlyList<(TimeSpan Delay, Func<CancellationToken, Task> Action)> Scheduled => _scheduled;

    public void Schedule(TimeSpan delay, Func<CancellationToken, Task> action)
        => _scheduled.Add((delay, action));

    /// <summary>예약된 것을 전부 지금 실행하고 목록을 비운다 — "그 시각이 됐다"를 흉내낸다.</summary>
    public async Task FireAllAsync(CancellationToken ct)
    {
        var pending = _scheduled.ToList();
        _scheduled.Clear();

        foreach (var (_, action) in pending)
        {
            await action(ct);
        }
    }
}

/// <summary>
/// 활성 프롬프트 대역.
///
/// 실제 구현은 DB 를 읽는다. 여기서 검증하는 것은 프롬프트 내용이 아니라
/// **없을 때 접수가 막히는가** 와 **버전 id 가 내역까지 전달되는가** 다.
/// </summary>
public sealed class StubPromptCatalog : IPromptCatalog
{
    private const string RewriteSchema = """
        { "type": "object", "properties": { "parts": { "type": "array", "items": {
          "type": "object", "properties": { "name": { "type": "string" } }
        } } } }
        """;

    private readonly Dictionary<LlmOperationKind, PromptSnapshot> _active = new()
    {
        [LlmOperationKind.GenerateSprite] = Snapshot(LlmOperationKind.GenerateSprite, "{{settings}}", "{{asset}}\n{{frame}}\n{{sourceCanvas}}\n{{outputCanvas}}"),
        [LlmOperationKind.AnalyzeSprites] = Snapshot(LlmOperationKind.AnalyzeSprites, "2D 분석", "{{settings}} {{sourceCanvas}}"),
        [LlmOperationKind.Analyze] = Snapshot(LlmOperationKind.Analyze, "장면을 분석하라", string.Empty),
        [LlmOperationKind.Extract] = Snapshot(LlmOperationKind.Extract, "파츠를 세라", "{{scene}}"),
        [LlmOperationKind.Decompose] = Snapshot(LlmOperationKind.Decompose, "파츠를 서술하라", "{{scene}} {{parts}}"),

        // 서술 재작성 (occludedby-recompute). 자리표시자를 실제 시드와 같게 둔다 —
        // 비워두면 targets 가 조용히 버려지는 결함을 테스트가 못 잡는다
        [LlmOperationKind.RewriteDescriptions] = Snapshot(
            LlmOperationKind.RewriteDescriptions, "가린 부분을 뺀 서술로 다시 써라", "{{scene}} {{targets}}"),

        // 생성은 파츠 하나를 그린다 — 목록이 아니라 그 파츠의 값들이 들어온다 (사이클 #7 §3.5)
        [LlmOperationKind.Generate] = Snapshot(
            LlmOperationKind.Generate,
            "단독 파츠를 그려라",
            "{{scene}} {{partName}} {{partDescription}} {{partCategory}} " +
            "요청 방향: {{viewDirection}}"),
    };

    // 카테고리 전용 프롬프트 — 실제 TuningPromptCatalog 의 폴백을 흉내낸다
    private readonly Dictionary<(LlmOperationKind, AssetCategory), PromptSnapshot> _dedicated = new();

    /// <summary>
    /// 소비 측이 조회한 (단계, 카테고리) 이력.
    ///
    /// **이것이 job.Category 관통을 고정하는 관측 지점이다.** 세 호출부가 카테고리를
    /// 안 넘기고 상수를 넘겨도 스냅숏 자체는 나오므로, 질의된 카테고리를 여기서 붙잡지
    /// 않으면 배선이 검증되지 않는다(독립 리뷰 §6.3 지적).
    /// </summary>
    public List<(LlmOperationKind Kind, AssetCategory Category)> Queries { get; } = [];

    /// <summary>카테고리 전용 활성 프롬프트를 심는다 — 폴백 우선 대상.</summary>
    public void SetForCategory(LlmOperationKind kind, AssetCategory category, string system)
        => _dedicated[(kind, category)] = Snapshot(kind, system, string.Empty);

    private static PromptSnapshot Snapshot(LlmOperationKind kind, string system, string user)
        => new(
            // 단계마다 고정 id — 내역 단정이 값을 알 수 있어야 한다
            Guid.Parse($"b0000000-0000-4000-8000-00000000000{(int)kind + 1}"),
            Version: 1,
            system,
            user,
            JsonSchema: kind == LlmOperationKind.RewriteDescriptions ? RewriteSchema : "{}");

    /// <summary>해당 단계의 활성 프롬프트를 없앤다 — PROMPT_NOT_ACTIVE 재현.</summary>
    public void Remove(LlmOperationKind kind) => _active.Remove(kind);

    public Guid VersionIdFor(LlmOperationKind kind) => _active[kind].VersionId;

    // 질의된 카테고리를 기록하고, 전용 → 없으면 기본으로 폴백한다(실제 어댑터와 같은 규칙)
    public Task<PromptSnapshot?> GetActiveAsync(LlmOperationKind kind, AssetCategory category, CancellationToken ct)
    {
        Queries.Add((kind, category));

        var snapshot = _dedicated.GetValueOrDefault((kind, category))
                       ?? _active.GetValueOrDefault(kind);

        return Task.FromResult(snapshot);
    }
}

/// <summary>
/// 목록을 테스트가 조작할 수 있는 카탈로그.
///
/// 실제 구현은 공급자 API 를 부르고 capability 로 거른다 (§3.2). 여기서 검증하는 것은
/// 그 필터가 아니라 **접수가 목록에 없는 모델을 거절하는가** 다.
/// </summary>
public sealed class StubModelCatalog : IModelCatalog
{
    public const string DefaultModel = "claude-opus-5";

    public List<ProviderModel> Models { get; } =
    [
        new(DefaultModel, "Claude Opus 5"),
        new("claude-sonnet-5", "Claude Sonnet 5"),
    ];

    /// <summary>설정하면 <see cref="ListAsync"/> 가 이 예외를 던진다 — 공급자 장애 재현.</summary>
    public ProviderCallFailedException? Failure { get; set; }

    /// <summary>
    /// 이미지 모델 목록 (사이클 #7). 텍스트 목록과 분리돼 있어야 접수가 둘을 따로 검증한다.
    /// </summary>
    public List<ProviderModel> ImageModels { get; } =
    [
        new(DefaultImageModel, "GPT Image 1"),
    ];

    public const string DefaultImageModel = "gpt-image-1";

    public Task<IReadOnlyList<ProviderModel>> ListAsync(Guid providerConfigId, CancellationToken ct)
        => Failure is not null
            ? Task.FromException<IReadOnlyList<ProviderModel>>(Failure)
            : Task.FromResult<IReadOnlyList<ProviderModel>>(Models);

    public Task<IReadOnlyList<ProviderModel>> ListImageModelsAsync(
        Guid providerConfigId, CancellationToken ct)
        => Failure is not null
            ? Task.FromException<IReadOnlyList<ProviderModel>>(Failure)
            : Task.FromResult<IReadOnlyList<ProviderModel>>(ImageModels);

    /// <summary>3D 모델 목록 (사이클 #10). 이미지와 같은 이유로 따로 있다.</summary>
    public List<ProviderModel> MeshModels { get; } =
    [
        new(DefaultMeshModel, "Tripo P1"),
    ];

    public const string DefaultMeshModel = "P1-20260311";

    public Task<IReadOnlyList<ProviderModel>> ListMeshModelsAsync(
        Guid providerConfigId, CancellationToken ct)
        => Failure is not null
            ? Task.FromException<IReadOnlyList<ProviderModel>>(Failure)
            : Task.FromResult<IReadOnlyList<ProviderModel>>(MeshModels);

    /// <summary>스텁은 잔액을 모른다 — 0 이 아니라 null 이다.</summary>
    public int? MeshCreditBalance { get; set; }

    public Task<int?> GetMeshCreditBalanceAsync(Guid providerConfigId, CancellationToken ct)
        => Task.FromResult(MeshCreditBalance);
}
