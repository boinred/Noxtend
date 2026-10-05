using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Provider;

namespace Noxtend.Tests.Application;

/// <summary>
/// 끝난 작업에 3D 를 붙이는 유스케이스.
///
/// Design Ref: §5.2 · Plan NFR-01
///
/// **가장 중요한 검사는 "이미지가 큐에 안 들어간다" 이다.** 이 기능의 값어치가 그것뿐이고,
/// 그것이 깨지면 아끼려던 $1.34 가 그대로 나가면서도 결과는 정상으로 보인다.
/// </summary>
public sealed class AddMeshProductionHandlerTests
{
    [Fact]
    public async Task AddingMesh_QueuesOnlyReconstructTasks()
    {
        var (fixture, job, mesh) = await ReadyAsync();

        // 접수 때 들어간 것들은 세지 않는다 — 이 호출이 무엇을 넣는지가 관심사다
        var before = fixture.Queue.Enqueued.Count;

        var result = await fixture.AddMesh.HandleAsync(
            job.Id, mesh, StubModelCatalog.DefaultMeshModel, default);

        Assert.True(result.IsSuccess);

        var added = fixture.Queue.Enqueued.Skip(before).ToArray();

        // **이미지 공정이 하나라도 들어가면 그만큼 다시 그린다**
        Assert.NotEmpty(added);
        Assert.All(added, queued => Assert.Equal(TaskKind.Reconstruct, queued.Kind));
    }

    [Fact]
    public async Task AddingMesh_PlansOneTaskPerReadyPart()
    {
        var (fixture, job, mesh) = await ReadyAsync(partCount: 3);

        await fixture.AddMesh.HandleAsync(job.Id, mesh, StubModelCatalog.DefaultMeshModel, default);

        var reloaded = await fixture.Jobs.GetAsync(job.Id, default);

        Assert.Equal(3, reloaded!.Tasks.Count(task => task.Kind == TaskKind.Reconstruct));
    }

    [Fact]
    public async Task UnknownJob_IsNotFound()
    {
        var (fixture, _, mesh) = await ReadyAsync();

        var result = await fixture.AddMesh.HandleAsync(
            Guid.NewGuid(), mesh, StubModelCatalog.DefaultMeshModel, default);

        Assert.Equal(ErrorCode.JobNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task DisabledProvider_IsRejected()
    {
        var (fixture, job, _) = await ReadyAsync();
        var disabled = await SeedMeshProviderAsync(fixture, enabled: false);

        var result = await fixture.AddMesh.HandleAsync(
            job.Id, disabled, StubModelCatalog.DefaultMeshModel, default);

        Assert.Equal(ErrorCode.JobMeshProviderDisabled, result.ErrorCode);
    }

    [Fact]
    public async Task UnknownModel_IsRejected()
    {
        var (fixture, job, mesh) = await ReadyAsync();

        var result = await fixture.AddMesh.HandleAsync(job.Id, mesh, "P9-없는-모델", default);

        Assert.Equal(ErrorCode.JobMeshModelUnavailable, result.ErrorCode);
    }

    /// <summary>
    /// 두 번째 요청은 도메인이 막는다 (D-06).
    ///
    /// 화면이 애초에 버튼을 안 그리므로 이 경로는 경합에서만 온다.
    /// </summary>
    [Fact]
    public async Task SecondRequest_IsRejected()
    {
        var (fixture, job, mesh) = await ReadyAsync();
        await fixture.AddMesh.HandleAsync(job.Id, mesh, StubModelCatalog.DefaultMeshModel, default);

        var result = await fixture.AddMesh.HandleAsync(
            job.Id, mesh, StubModelCatalog.DefaultMeshModel, default);

        Assert.Equal(ErrorCode.JobMeshNotApplicable, result.ErrorCode);
    }

    [Fact]
    public async Task JobWithoutReadyParts_IsRejected()
    {
        var (fixture, job, mesh) = await ReadyAsync(withImages: false);

        var result = await fixture.AddMesh.HandleAsync(
            job.Id, mesh, StubModelCatalog.DefaultMeshModel, default);

        Assert.Equal(ErrorCode.JobMeshNotApplicable, result.ErrorCode);
    }

    // ─── 설정 ───

    /// <summary>
    /// 이미지가 끝나 3D 를 붙일 수 있는 작업과, 등록된 3D 공급자.
    ///
    /// 작업을 도메인으로 직접 만든다 — 접수 핸들러를 거치면 이미지 공급자를 함께 넣어야
    /// 하고, 여기서 보려는 것은 접수가 아니라 **끝난 뒤에 무엇이 되는가** 다.
    /// </summary>
    private static async Task<(PipelineFixture Fixture, PipelineJob Job, Guid MeshProvider)> ReadyAsync(
        int partCount = 1, bool withImages = true)
    {
        var fixture = new PipelineFixture();
        var now = fixture.Clock.Now;

        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), now, Guid.NewGuid(), "gemini-image");

        job.ApplyParts([.. Enumerable.Range(0, partCount).Select(i => $"파츠{i}")]);

        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(now, TimeSpan.FromMinutes(2));
        decompose.Succeed(now);
        job.PlanReadyFollowUpTasks();
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToArray())
        {
            task.Claim(now, TimeSpan.FromMinutes(2));

            if (withImages)
            {
                job.AttachGeneratedImage(
                    task.PartId!.Value, task.Id,
                    $"generated/{task.PartId}-{task.ViewDirection}.png", "image/png", 2048, now);
                task.Succeed(now);
            }
            else
            {
                task.Fail("GENERATION_EMPTY_RESPONSE", now);
            }
        }

        job.ReconcileFromTasks(now);

        await fixture.Jobs.AddAsync(job, default);
        await fixture.Jobs.SaveChangesAsync(default);

        return (fixture, job, await SeedMeshProviderAsync(fixture));
    }

    private static async Task<Guid> SeedMeshProviderAsync(PipelineFixture fixture, bool enabled = true)
    {
        var config = ProviderConfig.Create(
            "Tripo 운영", ProviderKind.Tripo,
            apiKeyCipher: "cipher", apiKeyLast4: "9a1b", fixture.Clock.Now);

        if (!enabled)
        {
            config.Update("Tripo 운영", ProviderKind.Tripo, null, null, isEnabled: false, fixture.Clock.Now);
        }

        await fixture.Providers.AddAsync(config, CancellationToken.None);
        return config.Id;
    }
}
