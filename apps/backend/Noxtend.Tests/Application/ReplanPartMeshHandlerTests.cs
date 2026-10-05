using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Application.Mesh;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Mesh;
using PipelineTask = Noxtend.Domain.Job.PipelineTask;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Application;

/// <summary>
/// 파츠별 "3D 전송 뷰 자유 선택 + 대칭" 유스케이스 (spec 20260917).
/// </summary>
public sealed class ReplanPartMeshHandlerTests
{
    /// <summary>
    /// 존재하지 않는 파츠와 "정면 이미지 없음"을 구분한다(merge-gate 2차 리뷰 B4).
    /// 전에는 둘 다 `MESH_INPUT_MISSING "정면 이미지가 없습니다"`로 같이 나가서
    /// 사용자가 원인을 구분할 수 없었다.
    /// </summary>
    [Fact]
    public async Task UnknownPart_FailsWithPartNotFound_NotMeshInputMissing()
    {
        var (fixture, job, _, mesh) = await ReadyAsync();

        var result = await fixture.ReplanPartMesh.HandleAsync(
            job.Id, Guid.NewGuid(), mesh, StubModelCatalog.DefaultMeshModel,
            LeftRightPlan.Skip, BackPlan.Skip, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.PartNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task LeftRightBoth_UsesBothRealImages_NoSyntheticCreated()
    {
        var (fixture, job, partId, mesh) = await ReadyAsync();

        var result = await fixture.ReplanPartMesh.HandleAsync(
            job.Id, partId, mesh, StubModelCatalog.DefaultMeshModel,
            LeftRightPlan.Both, BackPlan.Skip, default);

        Assert.True(result.IsSuccess);
        var reloaded = await fixture.Jobs.GetAsync(job.Id, default);
        var task = Assert.Single(reloaded!.Tasks, t => t.Kind == TaskKind.Reconstruct);
        Assert.NotNull(task.MeshInputs!.LeftImageId);
        Assert.NotNull(task.MeshInputs!.RightImageId);
        Assert.Null(task.MeshInputs!.BackImageId);
        Assert.DoesNotContain(reloaded.Tasks, t => t.Kind == TaskKind.Synthesize);
    }

    [Fact]
    public async Task MirrorFromLeft_CreatesSyntheticRight_IgnoringRealRight()
    {
        var (fixture, job, partId, mesh) = await ReadyAsync();
        var realRightId = job.GeneratedImages.Single(i => i.PartId == partId && i.ViewDirection == ViewDirection.Right).Id;

        var result = await fixture.ReplanPartMesh.HandleAsync(
            job.Id, partId, mesh, StubModelCatalog.DefaultMeshModel,
            LeftRightPlan.MirrorFromLeft, BackPlan.Skip, default);

        Assert.True(result.IsSuccess);
        var reloaded = await fixture.Jobs.GetAsync(job.Id, default);
        var task = Assert.Single(reloaded!.Tasks, t => t.Kind == TaskKind.Reconstruct);

        // 실제 Right 이미지가 존재해도 무시하고 합성본을 쓴다
        Assert.NotEqual(realRightId, task.MeshInputs!.RightImageId);

        var syntheticRight = reloaded.GeneratedImages.Single(i => i.Id == task.MeshInputs!.RightImageId);
        Assert.True(syntheticRight.IsSynthetic);

        // 합성 이미지는 "최신"으로 승격되지 않는다 — 일반 조회는 여전히 실제 이미지를 본다
        Assert.Equal(realRightId, reloaded.LatestCurrentImage(partId, ViewDirection.Right));
    }

    [Fact]
    public async Task MissingSourceForMirror_Fails()
    {
        var (fixture, job, partId, mesh) = await ReadyAsync(withLeftRight: false);

        var result = await fixture.ReplanPartMesh.HandleAsync(
            job.Id, partId, mesh, StubModelCatalog.DefaultMeshModel,
            LeftRightPlan.MirrorFromLeft, BackPlan.Skip, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.MeshInputMissing, result.ErrorCode);
    }

    [Fact]
    public async Task CanceledJob_FailsWithoutThrowing()
    {
        var fixture = new PipelineFixture();
        var now = fixture.Clock.Now;

        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), now, Guid.NewGuid(), "gemini-image");
        job.ApplyParts(["칼"]);

        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(now, TimeSpan.FromMinutes(2));
        decompose.Succeed(now);
        job.PlanReadyFollowUpTasks();

        var partId = job.Parts.Single().Id;

        // 정면 이미지가 있어야 ReplanMeshInputs 도메인 호출까지 도달한다 — 없으면
        // MeshInputMissing 으로 먼저 끝나 이 테스트가 잡으려는 취소 경로를 안 거친다
        var frontTask = job.Tasks.Single(t => t.Kind == TaskKind.Generate && t.ViewDirection == ViewDirection.Front);
        frontTask.Claim(now, TimeSpan.FromMinutes(2));
        using (var image = TestImages.Jpeg(64, 64))
        {
            var blobKey = await fixture.Blobs.SaveAsync(image, "image/jpeg", default);
            job.AttachGeneratedImage(partId, frontTask.Id, blobKey, "image/jpeg", image.Length, now);
        }
        frontTask.Succeed(now);

        job.Cancel(now);

        await fixture.Jobs.AddAsync(job, default);
        await fixture.Jobs.SaveChangesAsync(default);

        var mesh = await SeedMeshProviderAsync(fixture);

        var result = await fixture.ReplanPartMesh.HandleAsync(
            job.Id, partId, mesh, StubModelCatalog.DefaultMeshModel,
            LeftRightPlan.Skip, BackPlan.Skip, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.JobAlreadyTerminal, result.ErrorCode);
    }

    [Fact]
    public async Task MirrorSucceeds_ThenBackValidationFails_NoOrphanSyntheticOrBlob()
    {
        // Left는 있어 MirrorFromLeft 자체는 유효하지만, Back 이미지가 없어 요청 전체는
        // 실패해야 한다 — 실패하기 전에 Right 합성부터 만들어 버리면 고아가 남는다
        // (merge-gate 리뷰 F3)
        var (fixture, job, partId, mesh) = await ReadyAsync(withBack: false);
        var blobCountBefore = fixture.Blobs.Count;

        var result = await fixture.ReplanPartMesh.HandleAsync(
            job.Id, partId, mesh, StubModelCatalog.DefaultMeshModel,
            LeftRightPlan.MirrorFromLeft, BackPlan.Include, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.MeshInputMissing, result.ErrorCode);

        var reloaded = await fixture.Jobs.GetAsync(job.Id, default);
        Assert.DoesNotContain(reloaded!.Tasks, t => t.Kind == TaskKind.Synthesize);
        Assert.Equal(blobCountBefore, fixture.Blobs.Count);
    }

    [Fact]
    public async Task SaveConflict_FailsWithReplanMeshConflict_AndCleansUpSyntheticBlob()
    {
        var (fixture, job, partId, mesh) = await ReadyAsync();
        var blobCountBefore = fixture.Blobs.Count;

        // 저장소가 실제 RowVersion 충돌/유니크 제약 위반을 흉내낸다 — EF InMemory 는
        // 제약을 강제하지 않으므로, 경계에서의 변환(ConcurrencyConflictException →
        // Result.Fail)만 별도로 검증한다. MirrorFromLeft 를 써서 저장 직전에 합성
        // Blob이 실제로 하나 만들어지게 한 뒤, 실패 시 그 Blob이 지워지는지 본다(F3)
        var conflicting = new ConflictingSaveJobRepository(fixture.Jobs);
        var handler = new ReplanPartMeshHandler(
            conflicting, fixture.Blobs, new SkiaImageTranscoder(), fixture.MeshSelection,
            fixture.Orchestrator, fixture.Clock, NullLogger<ReplanPartMeshHandler>.Instance);

        var result = await handler.HandleAsync(
            job.Id, partId, mesh, StubModelCatalog.DefaultMeshModel,
            LeftRightPlan.MirrorFromLeft, BackPlan.Skip, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ReplanMeshConflict, result.ErrorCode);
        Assert.Equal(blobCountBefore, fixture.Blobs.Count);
    }

    /// <summary>
    /// 예상 함정 5(검수 단계 상호작용) 검증 — merge-gate 리뷰 F4.
    ///
    /// `ReviewPhase`는 잡 전체에 하나뿐이라, 한 파츠를 "서술 복귀"시키면(사용자가
    /// 다른 파츠의 정면을 다시 만들려고 되돌리는 경우) 잡 전체가 `Descriptions`로
    /// 바뀐다. 그 상태에서 **손대지 않은 다른 파츠**에 대칭 요청을 보내면
    /// `AttachGeneratedImage`가 방금 만든 합성 이미지를 곧바로 `MarkObsoleted()`
    /// 한다 — 스펙 초안은 이걸 "방어 코드가 필요한 위험"으로 적었다.
    ///
    /// 실제로 확인해보면 무해하다: `IsCurrentCandidate`는 이미 `IsSynthetic`
    /// 만으로 합성 이미지를 제외하므로 `IsObsoleted`가 중복으로 켜져도 조회
    /// 결과는 바뀌지 않고, 3D 제출은 "최신" 조회가 아니라 이 요청이 만든 ID를
    /// 그대로 쓰므로 정상 제출된다. 그래서 방어 코드 대신 이 사실을 굳히는
    /// 회귀 테스트를 둔다.
    /// </summary>
    [Fact]
    public async Task MirrorSucceeds_EvenWhenAnotherPartReturnedJobToDescriptionsPhase()
    {
        var fixture = new PipelineFixture();
        var now = fixture.Clock.Now;

        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), now, Guid.NewGuid(), "gemini-image");
        job.ApplyParts(["칼", "방패"]);

        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(now, TimeSpan.FromMinutes(2));
        decompose.Succeed(now);
        job.PlanReadyFollowUpTasks();

        var swordId = job.Parts.Single(p => p.Name == "칼").Id;
        var shieldId = job.Parts.Single(p => p.Name == "방패").Id;

        async Task AttachAsync(Guid partId, PipelineTask task)
        {
            task.Claim(now, TimeSpan.FromMinutes(2));
            using var image = TestImages.Jpeg(64, 64);
            var blobKey = await fixture.Blobs.SaveAsync(image, "image/jpeg", default);
            job.AttachGeneratedImage(partId, task.Id, blobKey, "image/jpeg", image.Length, now);
            task.Succeed(now);
        }

        // 둘 다 정면을 먼저 만든다
        var swordFront = job.Tasks.Single(
            t => t.Kind == TaskKind.Generate && t.PartId == swordId && t.ViewDirection == ViewDirection.Front);
        await AttachAsync(swordId, swordFront);
        var shieldFront = job.Tasks.Single(
            t => t.Kind == TaskKind.Generate && t.PartId == shieldId && t.ViewDirection == ViewDirection.Front);
        await AttachAsync(shieldId, shieldFront);

        // 칼에는 좌측도 만든다 — MirrorFromLeft 의 대칭 소스로 쓴다
        job.PlanSelectedViews([ViewDirection.Left]);
        var swordLeft = job.Tasks.Single(
            t => t.Kind == TaskKind.Generate && t.PartId == swordId && t.ViewDirection == ViewDirection.Left);
        await AttachAsync(swordId, swordLeft);

        job.ReconcileFromTasks(now);
        await fixture.Jobs.AddAsync(job, default);
        await fixture.Jobs.SaveChangesAsync(default);

        // 사용자가 "방패"만 서술 복귀시킨다 — ReviewPhase 는 잡 전체 필드라 "칼"에도 번진다
        job.ReturnToDescriptionsFromGeneration(shieldId, now);
        await fixture.Jobs.SaveChangesAsync(default);
        Assert.Equal(ReviewPhase.Descriptions, job.ReviewPhase);

        var mesh = await SeedMeshProviderAsync(fixture);

        // "칼"은 손대지 않았다 — 그런데도 대칭 요청이 이 오염된 ReviewPhase 아래서 처리된다
        var result = await fixture.ReplanPartMesh.HandleAsync(
            job.Id, swordId, mesh, StubModelCatalog.DefaultMeshModel,
            LeftRightPlan.MirrorFromLeft, BackPlan.Skip, default);

        Assert.True(result.IsSuccess);
        var reloaded = await fixture.Jobs.GetAsync(job.Id, default);
        var task = Assert.Single(
            reloaded!.Tasks, t => t.Kind == TaskKind.Reconstruct && t.PartId == swordId);

        var syntheticRight = reloaded.GeneratedImages.Single(i => i.Id == task.MeshInputs!.RightImageId);
        Assert.True(syntheticRight.IsSynthetic);

        // 실제로 obsolete 처리는 되지만(AttachGeneratedImage 의 기존 규칙), 합성
        // 이미지라 IsCurrentCandidate 는 이미 항상 false — 관측 가능한 차이가 없다
        Assert.True(syntheticRight.IsObsoleted);

        // 그래도 3D 입력에는 정상적으로 그 ID가 들어간다 — obsolete 여부와 무관하게
        // "최신 조회"가 아니라 이 요청이 만든 ID를 그대로 쓰기 때문이다
        Assert.Equal(syntheticRight.Id, task.MeshInputs!.RightImageId);
    }

    [Fact]
    public async Task NoExistingReconstruct_CreatesOne_FrontOnly()
    {
        var (fixture, job, partId, mesh) = await ReadyAsync(withLeftRight: false, withBack: false);

        var result = await fixture.ReplanPartMesh.HandleAsync(
            job.Id, partId, mesh, StubModelCatalog.DefaultMeshModel,
            LeftRightPlan.Skip, BackPlan.Skip, default);

        Assert.True(result.IsSuccess);
        var reloaded = await fixture.Jobs.GetAsync(job.Id, default);
        var task = Assert.Single(reloaded!.Tasks, t => t.Kind == TaskKind.Reconstruct);
        Assert.Null(task.MeshInputs!.LeftImageId);
        Assert.Null(task.MeshInputs!.RightImageId);
        Assert.Null(task.MeshInputs!.BackImageId);
    }

    // ─── 설정 ───

    private static async Task<(PipelineFixture Fixture, PipelineJob Job, Guid PartId, Guid MeshProvider)> ReadyAsync(
        bool withLeftRight = true, bool withBack = true)
    {
        var fixture = new PipelineFixture();
        var now = fixture.Clock.Now;

        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), now, Guid.NewGuid(), "gemini-image");
        job.ApplyParts(["칼"]);

        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(now, TimeSpan.FromMinutes(2));
        decompose.Succeed(now);
        job.PlanReadyFollowUpTasks();

        var partId = job.Parts.Single().Id;

        async Task AttachAsync(ViewDirection direction, PipelineTask task)
        {
            task.Claim(now, TimeSpan.FromMinutes(2));
            using var image = TestImages.Jpeg(64, 64);
            var blobKey = await fixture.Blobs.SaveAsync(image, "image/jpeg", default);
            job.AttachGeneratedImage(partId, task.Id, blobKey, "image/jpeg", image.Length, now);
            task.Succeed(now);
        }

        var frontTask = job.Tasks.Single(t => t.Kind == TaskKind.Generate && t.ViewDirection == ViewDirection.Front);
        await AttachAsync(ViewDirection.Front, frontTask);

        var directions = new List<ViewDirection>();
        if (withLeftRight)
        {
            directions.Add(ViewDirection.Left);
            directions.Add(ViewDirection.Right);
        }
        if (withBack)
        {
            directions.Add(ViewDirection.Back);
        }

        if (directions.Count > 0)
        {
            job.PlanSelectedViews(directions);
            foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate && t.Status == TaskStatus.Pending).ToArray())
            {
                await AttachAsync(task.ViewDirection!.Value, task);
            }
        }

        job.ReconcileFromTasks(now);

        await fixture.Jobs.AddAsync(job, default);
        await fixture.Jobs.SaveChangesAsync(default);

        var meshProvider = await SeedMeshProviderAsync(fixture);
        return (fixture, job, partId, meshProvider);
    }

    private static async Task<Guid> SeedMeshProviderAsync(PipelineFixture fixture)
    {
        var config = ProviderConfig.Create(
            "Tripo 운영", ProviderKind.Tripo,
            apiKeyCipher: "cipher", apiKeyLast4: "9a1b", fixture.Clock.Now);

        await fixture.Providers.AddAsync(config, CancellationToken.None);
        return config.Id;
    }
}

/// <summary>
/// `SaveChangesAsync` 한 번만 <see cref="ConcurrencyConflictException"/>을 던지는
/// 저장소 — 실제 EF 충돌(RowVersion/유니크 제약)이 경계에서 올바르게 번역되는지가
/// 아니라, 그 예외를 애플리케이션 핸들러가 잡아 <c>Result.Fail</c>로 바꾸는지만
/// 검증한다. 번역 자체는 <c>EfJobRepository.SaveChangesAsync</c>가 한다.
/// </summary>
file sealed class ConflictingSaveJobRepository(IJobRepository inner) : IJobRepository
{
    public Task AddAsync(PipelineJob job, CancellationToken ct) => inner.AddAsync(job, ct);
    public Task<PipelineJob?> GetAsync(Guid jobId, CancellationToken ct) => inner.GetAsync(jobId, ct);
    public Task<PipelineJob?> ReloadAsync(Guid jobId, CancellationToken ct) => inner.ReloadAsync(jobId, ct);
    public Task<PipelineJob?> GetByTaskAsync(Guid taskId, CancellationToken ct) => inner.GetByTaskAsync(taskId, ct);

    public Task<int> CountAsync(JobListFilter filter, AssetCategory? category, CancellationToken ct) =>
        inner.CountAsync(filter, category, ct);

    public Task<IReadOnlyList<PipelineJob>> ListAsync(
        JobListFilter filter, AssetCategory? category, int limit, CancellationToken ct) =>
        inner.ListAsync(filter, category, limit, ct);

    public Task<IReadOnlyList<PipelineJob>> ListBySourceImageAsync(Guid sourceImageId, CancellationToken ct) =>
        inner.ListBySourceImageAsync(sourceImageId, ct);

    public Task<IReadOnlyList<PipelineJob>> ListSweepCandidatesAsync(DateTimeOffset now, CancellationToken ct) =>
        inner.ListSweepCandidatesAsync(now, ct);

    public Task<GeneratedImage?> GetGeneratedImageAsync(Guid imageId, CancellationToken ct) =>
        inner.GetGeneratedImageAsync(imageId, ct);

    public Task<GeneratedMesh?> GetGeneratedMeshAsync(Guid meshId, CancellationToken ct) =>
        inner.GetGeneratedMeshAsync(meshId, ct);

    public Task<DeletedJobBlobs?> DeleteIfTerminalAsync(Guid jobId, CancellationToken ct) =>
        inner.DeleteIfTerminalAsync(jobId, ct);

    public Task SaveChangesAsync(CancellationToken ct) =>
        throw new ConcurrencyConflictException(
            "다른 요청과 동시에 처리되어 충돌했습니다", new InvalidOperationException("simulated conflict"));
}
