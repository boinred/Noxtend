using Noxtend.Domain.Job;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 파츠별 "3D 전송 뷰 선택" — find-or-create (spec 20260917).
///
/// **두 번째 `Reconstruct` 공정을 만들지 않는다.** 파츠당 `Reconstruct`는 DB 유니크
/// 제약으로 하나뿐이고, 자동 팬인이 이미지 2장만 모여도 그 자리를 선점한다. 있으면
/// 입력을 다시 얼려 재실행하고, 없으면(예: 정면 단독 3D처럼 자동 팬인이 아직 아무것도
/// 안 만든 경우) 새로 계획한다.
/// </summary>
public sealed class ReplanMeshInputsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly Guid MeshProvider = Guid.NewGuid();
    private const string MeshModel = "P1-20260311";

    [Fact]
    public void NoExistingReconstruct_PlansANewOne()
    {
        // 정면만 있는 파츠 — 자동 팬인은 비정면 최소 1장을 요구해 아직 아무 공정도 안 만듦
        var job = FannedOut("칼");
        var partId = job.Parts.Single().Id;
        var frontId = LatestImageId(job, partId, ViewDirection.Front);

        var task = job.ReplanMeshInputs(partId, MeshProvider, MeshModel, new MeshInputSet(frontId), Now);

        Assert.Equal(TaskKind.Reconstruct, task.Kind);
        Assert.Single(job.Tasks, t => t.Kind == TaskKind.Reconstruct);
    }

    [Fact]
    public void ExistingReconstruct_RebindsInputsAndResetsToPending_EvenIfAlreadySucceeded()
    {
        var job = FannedOut("칼");
        var partId = job.Parts.Single().Id;
        GenerateAll(job, partId);
        job.PlanReadyFollowUpTasks();

        var reconstruct = job.Tasks.Single(t => t.Kind == TaskKind.Reconstruct);
        reconstruct.Claim(Now, Lease);
        reconstruct.Succeed(Now);

        var frontId = LatestImageId(job, partId, ViewDirection.Front);
        var backId = LatestImageId(job, partId, ViewDirection.Back);
        var replanned = job.ReplanMeshInputs(
            partId, MeshProvider, MeshModel, new MeshInputSet(frontId, backImageId: backId), Now);

        // 새 공정이 아니라 같은 공정이다 — 유니크 제약을 어기지 않는다
        Assert.Equal(reconstruct.Id, replanned.Id);
        Assert.Single(job.Tasks, t => t.Kind == TaskKind.Reconstruct);
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Pending, replanned.Status);
        Assert.Equal(frontId, replanned.MeshInputs!.FrontImageId);
        Assert.Equal(backId, replanned.MeshInputs!.BackImageId);
        Assert.Null(replanned.MeshInputs!.RightImageId);
    }

    /// <summary>
    /// 애플리케이션 핸들러가 대칭 소스/개별 선택 이미지를 조회할 공개 통로 (spec 20260917).
    /// `IsCurrentCandidate` 기준이라 obsolete·합성 이미지는 자동으로 빠진다.
    /// </summary>
    [Fact]
    public void LatestCurrentImage_ReturnsNull_WhenOnlyObsoletedOrSyntheticExist()
    {
        var job = FannedOut("칼");
        var partId = job.Parts.Single().Id;

        Assert.Null(job.LatestCurrentImage(partId, ViewDirection.Right));

        var task = job.PlanSyntheticView(partId, ViewDirection.Right, Now);
        task.Claim(Now, Lease);
        job.AttachGeneratedImage(partId, task.Id, "right-synthetic.png", "image/png", 100, Now, isSynthetic: true);
        task.Succeed(Now);

        Assert.Null(job.LatestCurrentImage(partId, ViewDirection.Right));
    }

    /// <summary>
    /// 동률(같은 클럭 틱) 타이브레이크 — merge-gate 리뷰 F6.
    ///
    /// `RunGenerationTaskHandler`는 이미 같은 이유로 `.ThenByDescending(image => image.Id)`를
    /// 쓰고 있다(그 파일 주석: "독립 리뷰 지적"). `LatestCurrentImage`는 그게 빠져 있었다 —
    /// `CreatedAt`만으로는 동률일 때 어느 행이 오는지 EF/컬렉션 로드 순서에 달린다.
    /// </summary>
    [Fact]
    public void LatestCurrentImage_BreaksCreatedAtTiesDeterministically()
    {
        var job = FannedOut("칼");
        var partId = job.Parts.Single().Id;

        // 같은 시각(Now)에 두 장을 붙인다 — CreatedAt 만으로는 순서를 못 가른다.
        // Synthesize 공정을 빌려 쓰지만 이미지 자체는 실제 이미지다(isSynthetic 기본값 false) —
        // IsCurrentCandidate 조회 대상이 되려면 합성이 아니어야 하므로 이렇게 만든다
        var task1 = job.PlanSyntheticView(partId, ViewDirection.Right, Now);
        task1.Claim(Now, Lease);
        job.AttachGeneratedImage(partId, task1.Id, "right-1.png", "image/png", 100, Now);
        task1.Succeed(Now);

        var task2 = job.PlanSyntheticView(partId, ViewDirection.Right, Now);
        task2.Claim(Now, Lease);
        job.AttachGeneratedImage(partId, task2.Id, "right-2.png", "image/png", 100, Now);
        task2.Succeed(Now);

        var candidates = job.GeneratedImages
            .Where(i => i.PartId == partId && i.ViewDirection == ViewDirection.Right && i.IsCurrentCandidate)
            .ToList();
        Assert.Equal(2, candidates.Count);

        var expected = candidates
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .First()
            .Id;

        Assert.Equal(expected, job.LatestCurrentImage(partId, ViewDirection.Right));
    }

    [Fact]
    public void UnknownPart_Throws()
    {
        var job = FannedOut("칼");

        Assert.Throws<InvalidOperationException>(() => job.ReplanMeshInputs(
            Guid.NewGuid(), MeshProvider, MeshModel, new MeshInputSet(Guid.NewGuid()), Now));
    }

    [Fact]
    public void TerminalJob_Reopens()
    {
        var job = FannedOut("칼");
        var partId = job.Parts.Single().Id;
        job.ReconcileFromTasks(Now);
        Assert.True(job.IsTerminal);
        var frontId = LatestImageId(job, partId, ViewDirection.Front);

        job.ReplanMeshInputs(partId, MeshProvider, MeshModel, new MeshInputSet(frontId), Now);

        Assert.Equal(JobStatus.Running, job.Status);
    }

    // ─── 설정 ───

    private static PipelineJob FannedOut(params string[] partNames)
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini-image",
            MeshProvider, MeshModel);

        job.ApplyParts(partNames);
        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        var partId = job.Parts.Single().Id;
        var frontTask = job.Tasks.Single(
            t => t.Kind == TaskKind.Generate && t.PartId == partId && t.ViewDirection == ViewDirection.Front);
        frontTask.Claim(Now, Lease);
        job.AttachGeneratedImage(partId, frontTask.Id, "front.png", "image/png", 100, Now);
        frontTask.Succeed(Now);

        return job;
    }

    private static void GenerateAll(PipelineJob job, Guid partId)
    {
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate && t.PartId == partId && t.Status == Noxtend.Domain.Job.TaskStatus.Pending))
        {
            task.Claim(Now, Lease);
            job.AttachGeneratedImage(partId, task.Id, $"{task.ViewDirection}.png", "image/png", 100, Now);
            task.Succeed(Now);
        }
    }

    private static Guid LatestImageId(PipelineJob job, Guid partId, ViewDirection direction)
        => job.GeneratedImages
            .Where(i => i.PartId == partId && i.ViewDirection == direction)
            .OrderByDescending(i => i.CreatedAt)
            .First().Id;
}
