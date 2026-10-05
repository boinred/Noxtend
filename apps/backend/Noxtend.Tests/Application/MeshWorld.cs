using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Application.Common;
using Noxtend.Application.Job;
using Noxtend.Application.Mesh;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Blob;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Infrastructure.Persistence.InMemory;
using Noxtend.Infrastructure.Queue;

namespace Noxtend.Tests.Application;

/// <summary>
/// 3D 제작 공정 하나가 도는 데 필요한 최소한.
///
/// **인프라 없이 돈다** (§2.0). 실행 상태 저장소·Blob·공급자가 모두 메모리라, 재기동
/// 시나리오를 컨테이너 없이 초 단위로 돌릴 수 있다 — 그 속도가 아니면 크래시 지점을
/// 여덟 군데나 짚어 보지 못한다.
/// </summary>
public sealed class MeshWorld
{
    public static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private MeshWorld(PipelineJob job, PipelineTask task, RecordingMeshProvider provider)
    {
        Job = job;
        Task = task;
        Provider = provider;

        Orchestrator = new JobOrchestrator(new InMemoryTaskQueue(), Jobs, Clock);
        Execution = new TaskExecution(
            Jobs, Orchestrator, Clock, new RecordingDelayedActionScheduler(),
            NullLogger<TaskExecution>.Instance);

        Handler = new RunMeshTaskHandler(
            Runs,
            new StubMeshProviderFactory(Provider),
            Blobs,
            Artifacts,
            new MeshInputNormalizer(new SkiaImageTranscoder(), Options),
            Jobs,
            Execution,
            Clock,
            Options,
            NullLogger<RunMeshTaskHandler>.Instance);

        RetryHandler = new RetryTaskHandler(Jobs, Runs, Orchestrator);
    }

    public PipelineJob Job { get; }
    public PipelineTask Task { get; private set; }

    public FixedClock Clock { get; } = FixedClock.Default;
    public InMemoryJobRepository Jobs { get; } = new();
    public InMemoryMeshRunRepository Runs { get; } = new();
    public InMemoryBlobStorage Blobs { get; } = new();
    public InMemoryMeshArtifactStorage Artifacts { get; } = new();
    public RecordingMeshProvider Provider { get; }
    public RunMeshTaskHandler Handler { get; }
    public TaskExecution Execution { get; }
    public JobOrchestrator Orchestrator { get; }

    /// <summary>사용자가 누르는 다시 시도 — 3D 도 대상이다 (§4.7).</summary>
    public RetryTaskHandler RetryHandler { get; }

    /// <summary>조회 간격을 0 으로 둔다 — 여기서 보는 것은 분기이지 시간이 아니다.</summary>
    public static MeshGenerationOptions Options { get; } = new()
    {
        PollIntervalSeconds = 0,
        RunTimeoutSeconds = 600,
    };

    /// <summary>
    /// 이미지 넷이 성공해 3D 공정까지 계획된 작업.
    /// </summary>
    /// <param name="durableHandles">
    /// 공급자가 다시 쓸 수 있는 입력 참조를 주는가 (D-10). Tripo 는 <c>true</c>,
    /// Meshy 는 base64 라 <c>false</c> 다.
    /// </param>
    /// <param name="producesFbx">Meshy 만 FBX 를 낸다 (Plan D-05).</param>
    public static async Task<MeshWorld> ReadyAsync(
        bool durableHandles = true, bool producesFbx = false)
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now,
            Guid.NewGuid(), "gemini-image", Guid.NewGuid(), "P1-20260311");

        job.ApplyParts(["가로등"]);
        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        var world = new MeshWorld(job, null!, new RecordingMeshProvider(durableHandles, producesFbx));

        // 3D 재구성 테스트 환경용 4방향 선택 계획
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        // 방향마다 실제 JPEG 을 Blob 에 넣는다 — 정규화가 디코딩을 하므로 헤더만으로는 안 된다
        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToArray())
        {
            task.Claim(Now, Lease);

            using var image = TestImages.Jpeg(512, 512);
            var blobKey = await world.Blobs.SaveAsync(image, "image/jpeg", default);

            job.AttachGeneratedImage(
                task.PartId!.Value, task.Id, blobKey, "image/jpeg", image.Length, Now);
            task.Succeed(Now);
        }

        job.PlanReadyFollowUpTasks();
        await world.Jobs.AddAsync(job, default);
        await world.Jobs.SaveChangesAsync(default);

        world.Task = job.Tasks.Single(t => t.Kind == TaskKind.Reconstruct);
        return world;
    }

    public Task<RunTaskOutcome> RunAsync(CancellationToken ct = default)
        => Handler.HandleAsync(Task.Id, ct);

    /// <summary>공정을 실패로 확정한다 — 다시 시도를 누를 수 있는 상태다.</summary>
    public void FailTask()
    {
        Task.Claim(Now, Lease);
        Task.Fail("MESH_TASK_FAILED", Now);
        Job.ReconcileFromTasks(Now);
    }

    /// <summary>공정을 실패시켜 두고 수동 재시도를 누른 상태로 만든다.</summary>
    public void RetryTask()
    {
        FailTask();
        Job.RetryOutputTask(Task.Id);
    }

    /// <summary>아무것도 올리지 않은 실행.</summary>
    public async Task<MeshRun> StartRunAsync()
    {
        var run = MeshRun.Start(
            Job.Id, Task.Id, Task.PartId!.Value, runNumber: 1,
            Task.ProviderConfigId!.Value, Task.Model!, Task.MeshInputs!,
            modelSeed: 1, textureSeed: 2, Now);

        await Runs.AddAsync(run, default);
        return run;
    }

    /// <summary>네 방향이 모두 올라간 실행.</summary>
    public async Task<MeshRun> UploadedRunAsync()
    {
        var run = await StartRunAsync();

        foreach (var input in run.Inputs.ToArray())
        {
            run.RecordPrepared(input.ViewDirection, $"file_{input.ViewDirection}", "image/jpeg", Now);
        }

        await Runs.SaveAsync(run, default);
        return run;
    }

    private sealed class StubMeshProviderFactory(IMeshProvider provider) : IMeshProviderFactory
    {
        public Task<IMeshProvider> CreateAsync(Guid providerConfigId, string model, CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult(provider);
    }
}

/// <summary>
/// 무엇을 불렀는지 세는 공급자.
///
/// **횟수가 곧 과금이다.** 재기동 시나리오에서 확인하려는 것이 "제출을 몇 번 했는가" 라
/// 이 숫자가 검사의 대상이다.
/// </summary>
public sealed class RecordingMeshProvider(bool durableHandles = true, bool producesFbx = false)
    : IMeshProvider
{
    private readonly FakeMeshProvider inner = new(producesFbx);

    public List<ViewDirection> Uploaded { get; } = [];
    public int Submits { get; private set; }
    public List<string> Polled { get; } = [];

    /// <summary>마지막 제출이 실제로 들고 간 핸들 — 비내구적 경로가 빈손인지 보는 창구다.</summary>
    public IReadOnlyDictionary<ViewDirection, MeshInputHandle> LastSubmitted { get; private set; }
        = new Dictionary<ViewDirection, MeshInputHandle>();

    public async Task<MeshInputHandle> UploadInputAsync(MeshInputUpload input, CancellationToken ct)
    {
        Uploaded.Add(input.Direction);

        var handle = await inner.UploadInputAsync(input, ct);

        return handle with { IsDurable = durableHandles };
    }

    public async Task<MeshSubmission> SubmitAsync(MultiviewMeshRequest request, CancellationToken ct)
    {
        Submits++;
        LastSubmitted = request.Inputs;
        return await inner.SubmitAsync(request, ct);
    }

    public async Task<MeshTaskSnapshot> GetTaskAsync(string providerTaskId, CancellationToken ct)
    {
        Polled.Add(providerTaskId);
        return await inner.GetTaskAsync(providerTaskId, ct);
    }

    public Task<IMeshResultDownload> OpenResultAsync(string providerTaskId, CancellationToken ct)
        => inner.OpenResultAsync(providerTaskId, ct);
}
