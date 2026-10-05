using Noxtend.Domain.Job;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 파츠의 네 방향 이미지가 모이면 3D 제작 공정 하나가 생긴다.
///
/// Design Ref: §4.2~4.4 · Plan D-02 · FR-01~04
///
/// **이것이 이 기능의 유일한 팬인이다.** 앞의 모든 계획은 하나가 여럿이 되는 팬아웃이었고,
/// 여기서 처음 넷이 하나로 모인다. 단일 `DependsOnTaskId` 로는 부모 넷을 가리킬 수 없어
/// **의존을 간선이 아니라 입력으로 표현한다** — 네 이미지 ID 를 공정에 얼려 두면
/// 그 ID 들이 존재한다는 사실 자체가 의존이 충족됐다는 증거다.
///
/// 파츠 하나에 에셋 하나다 (Plan D-01). 배치가 여덟이어도 가로등은 한 번만 만든다 —
/// 배치 수만큼 만들면 같은 가로등을 여덟 번 과금한다.
/// </summary>
public sealed class MeshFanInTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly Guid MeshProvider = Guid.NewGuid();
    private const string MeshModel = "P1-20260311";

    [Fact]
    public void OnlyFrontDirection_PlanNoReconstruct()
    {
        var job = FannedOut("가로등");

        // 정면만 생성된 상태 — 비정면이 없다
        Generate(job, "가로등", ViewDirection.Front);
        job.PlanReadyFollowUpTasks();

        // 비정면이 0개이면 3D 팬인을 스케줄링하지 않는다
        Assert.Empty(Reconstructs(job));
    }

    [Fact]
    public void TwoDirections_PlanReconstruct()
    {
        var job = FannedOut("가로등");

        // 정면 + 후면 (총 2면) — 선택적 생성 3D 요구조건 충족
        Generate(job, "가로등", ViewDirection.Front, ViewDirection.Back);
        job.PlanReadyFollowUpTasks();

        var task = Assert.Single(Reconstructs(job));
        Assert.Equal(Part(job, "가로등").Id, task.PartId);
    }

    [Fact]
    public void FourDirections_PlanExactlyOneReconstruct()
    {
        var job = FannedOut("가로등");

        GenerateAll(job, "가로등");
        job.PlanReadyFollowUpTasks();

        var task = Assert.Single(Reconstructs(job));
        Assert.Equal(Part(job, "가로등").Id, task.PartId);

        // 방향은 파츠 이미지 공정의 것이다. 3D 는 파츠 하나를 통째로 만든다
        Assert.Null(task.ViewDirection);
    }

    /// <summary>
    /// **멱등해야 한다.** 이미지 워커가 여럿이라 마지막 두 장이 거의 동시에 끝나면
    /// 계획이 두 번 불린다. 그때 공정이 둘이 되면 같은 파츠를 두 번 과금한다.
    /// </summary>
    [Fact]
    public void PlanningRepeatedly_DoesNotAddMoreReconstructs()
    {
        var job = FannedOut("가로등");
        GenerateAll(job, "가로등");

        job.PlanReadyFollowUpTasks();
        job.PlanReadyFollowUpTasks();
        job.PlanReadyFollowUpTasks();

        Assert.Single(Reconstructs(job));
    }

    /// <summary>
    /// 넷이 먼저 모인 파츠는 다른 파츠를 기다리지 않는다 (Plan D-02).
    ///
    /// 전부 끝나기를 기다리면 파츠가 스물일 때 마지막 한 장이 늦어지는 만큼 열아홉 개의
    /// 3D 제작이 통째로 밀린다.
    /// </summary>
    [Fact]
    public void OnlyTheCompletedPart_GetsItsReconstruct()
    {
        var job = FannedOut("가로등", "벤치");

        GenerateAll(job, "가로등");
        Generate(job, "벤치", ViewDirection.Front);
        job.PlanReadyFollowUpTasks();

        Assert.Equal(Part(job, "가로등").Id, Assert.Single(Reconstructs(job)).PartId);
    }

    /// <summary>
    /// 실제로 쓴 이미지 ID 네 개를 얼린다 (FR-04).
    ///
    /// 재생성이 이미지 행을 쌓으므로 "그때 최신" 이 나중의 최신과 다르다. 얼려 두지 않으면
    /// 같은 공정을 재시도할 때 다른 입력으로 다른 결과가 나와 재현이 안 된다.
    /// </summary>
    [Fact]
    public void ReconstructFreezes_TheImageIdsItActuallyUsed()
    {
        var job = FannedOut("가로등");
        GenerateAll(job, "가로등");
        job.PlanReadyFollowUpTasks();

        var inputs = Assert.Single(Reconstructs(job)).MeshInputs;

        Assert.NotNull(inputs);
        Assert.Equal(LatestImageId(job, "가로등", ViewDirection.Front), inputs.FrontImageId);
        Assert.Equal(LatestImageId(job, "가로등", ViewDirection.Right), inputs.RightImageId);
        Assert.Equal(LatestImageId(job, "가로등", ViewDirection.Back), inputs.BackImageId);
        Assert.Equal(LatestImageId(job, "가로등", ViewDirection.Left), inputs.LeftImageId);
    }

    /// <summary>
    /// 방향은 위치가 아니라 이름이다 (FR-02 · Plan D-05).
    ///
    /// Tripo 의 위치 기반 순서는 `front, left, back, right` 로 우리 내부 순서와 좌우가
    /// 다르다. 네 ID 를 배열로 두면 언젠가 그 순서로 넘겨 좌우가 뒤집힌 mesh 가 나온다.
    /// 이름 붙은 필드로 두면 그 실수를 할 자리가 없다.
    /// </summary>
    [Fact]
    public void FrozenInputs_AreNamedNotOrdered()
    {
        var job = FannedOut("가로등");
        GenerateAll(job, "가로등");
        job.PlanReadyFollowUpTasks();

        var inputs = Assert.Single(Reconstructs(job)).MeshInputs!;

        // 네 값이 서로 다르다 — 같으면 위 검사가 우연히 통과했을 수 있다
        Assert.Equal(4, new HashSet<Guid?>(
            [inputs.FrontImageId, inputs.RightImageId, inputs.BackImageId, inputs.LeftImageId]).Count);
    }

    [Fact]
    public void JobWithoutMeshSelection_PlansNoReconstruct()
    {
        var job = FannedOut(meshSelected: false, "가로등");

        GenerateAll(job, "가로등");
        job.PlanReadyFollowUpTasks();

        // 3D 를 고르지 않은 작업은 예전처럼 이미지까지만 한다 (NFR-06)
        Assert.Empty(Reconstructs(job));
    }

    // ─── 설정 ───

    private static PipelineJob FannedOut(params string[] partNames)
        => FannedOut(meshSelected: true, partNames);

    /// <summary>분해까지 성공해 생성 공정이 깔린 작업.</summary>
    private static PipelineJob FannedOut(bool meshSelected, params string[] partNames)
    {
        var job = PipelineJob.Create(
            AssetCategory.Background,
            Guid.NewGuid(),
            Now,
            Guid.NewGuid(),
            "gemini-image",
            meshSelected ? MeshProvider : null,
            meshSelected ? MeshModel : null);

        job.ApplyParts(partNames);
        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        return job;
    }

    /// <summary>지정한 방향의 생성 공정을 성공시키고 이미지를 붙인다.</summary>
    private static void Generate(PipelineJob job, string partName, params ViewDirection[] directions)
    {
        job.PlanSelectedViews(directions);
        var part = Part(job, partName);

        foreach (var direction in directions)
        {
            var task = job.Tasks.Single(
                t => t.Kind == TaskKind.Generate
                     && t.PartId == part.Id
                     && t.ViewDirection == direction);

            task.Claim(Now, Lease);
            job.AttachGeneratedImage(
                part.Id, task.Id, $"generated/{partName}-{direction}.png", "image/png", 2048, Now);
            task.Succeed(Now);
        }
    }

    private static void GenerateAll(PipelineJob job, string partName)
        => Generate(job, partName,
            ViewDirection.Front, ViewDirection.Right, ViewDirection.Back, ViewDirection.Left);

    private static AssetPart Part(PipelineJob job, string name)
        => job.Parts.Single(part => part.Name == name);

    private static Guid LatestImageId(PipelineJob job, string partName, ViewDirection direction)
        => job.GeneratedImages
            .Where(image => image.PartId == Part(job, partName).Id
                            && image.ViewDirection == direction)
            .OrderByDescending(image => image.CreatedAt)
            .Select(image => image.Id)
            .First();

    private static PipelineTask[] Reconstructs(PipelineJob job)
        => [.. job.Tasks.Where(task => task.Kind == TaskKind.Reconstruct)];
}
