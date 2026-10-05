using System.Reflection;
using Noxtend.Api.Contracts;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;

namespace Noxtend.Tests.Api;

/// <summary>
/// 3D 가 API 응답에 어떻게 실리는가.
///
/// Design Ref: §10.3 · §13.1
///
/// **여기서 가장 중요한 것은 무엇이 실리지 않는가다.** 공급자 작업 ID·파일 참조·만료
/// 링크·Blob 키가 나가면 되돌릴 수 없다 — 한 번 나간 응답은 클라이언트 캐시와 로그에
/// 남는다.
/// </summary>
public sealed class MeshApiContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    [Fact]
    public void JobWithMeshSelection_ExposesItsMeshModel()
    {
        var job = ReadyForMesh();

        var response = JobResponse.From(new JobDetails(job, []));

        Assert.Equal("P1-20260311", response.Models.Mesh!.Model);
    }

    [Fact]
    public void JobWithoutMeshSelection_HasNoMeshModel()
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini-image");

        // 3D 를 고르지 않은 옛 작업이 여전히 조회된다 (NFR-06)
        Assert.Null(JobResponse.From(new JobDetails(job, [])).Models.Mesh);
    }

    /// <summary>
    /// 진행률은 실행 기록이 정본이다 (§8.5).
    ///
    /// 공정에 사본을 두면 둘이 어긋나고, 어느 쪽이 맞는지 화면이 알 수 없다.
    /// </summary>
    [Fact]
    public void ReconstructTask_CarriesProgressFromItsRun()
    {
        var job = ReadyForMesh();
        var task = job.Tasks.Single(t => t.Kind == TaskKind.Reconstruct);

        var run = RunFor(job, task);
        run.RecordProgress(64, Now);

        var response = JobResponse.From(new JobDetails(job, [run]));
        var reconstruct = response.Tasks.Single(t => t.Kind == "reconstruct");

        Assert.Equal(64, reconstruct.Progress);

        // 3D 는 파츠 하나를 통째로 만든다 — 방향이 없다
        Assert.Null(reconstruct.ViewDirection);
        Assert.Equal(task.PartId, reconstruct.PartId);
    }

    [Fact]
    public void TasksWithoutARun_HaveNoProgress()
    {
        var job = ReadyForMesh();

        var response = JobResponse.From(new JobDetails(job, []));

        // 이미지 공정에는 진행률 개념이 없다 — 0 으로 두면 화면이 "0% 진행 중" 을 그린다
        Assert.All(
            response.Tasks.Where(t => t.Kind != "reconstruct"),
            task => Assert.Null(task.Progress));
    }

    [Fact]
    public void PartWithAMesh_ExposesItsDownloadId()
    {
        var job = ReadyForMesh();
        var task = job.Tasks.Single(t => t.Kind == TaskKind.Reconstruct);

        job.AttachGeneratedMesh(
            task.PartId!.Value, task.Id, Guid.NewGuid(),
            MeshArtifacts.GlbAndPreview("meshes/abc/model.glb", 4_821_900, "meshes/abc/preview.png", 12_000, "image/png"),
            credits: 50, Now);

        var part = Assert.Single(JobResponse.From(new JobDetails(job, [])).Parts);

        Assert.NotNull(part.GeneratedMesh);
        Assert.True(part.GeneratedMesh.HasPreview);
        Assert.Equal(4_821_900, part.GeneratedMesh.SizeBytes);
        Assert.Equal(50, part.GeneratedMesh.CreditsConsumed);
    }

    [Fact]
    public void PartWithoutAMesh_HasNull()
    {
        var job = ReadyForMesh();

        Assert.Null(Assert.Single(JobResponse.From(new JobDetails(job, [])).Parts).GeneratedMesh);
    }

    /// <summary>
    /// **여기가 이 파일의 요지다** (§10.3 · §13.1).
    ///
    /// 공급자 작업 ID·파일 참조·만료 링크·Blob 키는 응답에 없어야 한다. 이름으로 고정하면
    /// 누군가 편의를 위해 필드를 더할 때 여기서 걸린다.
    /// </summary>
    [Fact]
    public void MeshResponse_LeaksNoProviderOrStorageDetail()
    {
        var names = typeof(GeneratedMeshResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        // **여기 늘어나는 것은 의도된 것이어야 한다.** 산출물 모델이 목록으로 바뀌었어도
        // 나가는 값은 여전히 다섯 + FBX 유무뿐이다 — Blob 키도 공급자 작업 ID 도 없다
        string[] expected =
            ["CreatedAt", "CreditsConsumed", "FbxSizeBytes", "HasFbx", "HasPreview", "Id", "SizeBytes"];

        Assert.Equal(expected, names);
    }

    // ─── 설정 ───

    private static PipelineJob ReadyForMesh()
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now,
            Guid.NewGuid(), "gemini-image", Guid.NewGuid(), "P1-20260311");

        job.ApplyParts(["가로등"]);
        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToArray())
        {
            task.Claim(Now, Lease);
            job.AttachGeneratedImage(
                task.PartId!.Value, task.Id,
                $"generated/{task.ViewDirection}.png", "image/png", 2048, Now);
            task.Succeed(Now);
        }

        job.PlanReadyFollowUpTasks();
        return job;
    }

    private static MeshRun RunFor(PipelineJob job, PipelineTask task)
        => MeshRun.Start(
            job.Id, task.Id, task.PartId!.Value, runNumber: 1,
            task.ProviderConfigId!.Value, task.Model!, task.MeshInputs!,
            modelSeed: 1, textureSeed: 2, Now);
}
