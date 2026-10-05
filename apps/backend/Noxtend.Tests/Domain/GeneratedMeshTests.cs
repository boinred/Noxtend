using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 완성된 3D 결과를 작업에 붙인다.
///
/// Design Ref: §3.3 · §11.1 · D-08
///
/// **결과는 작업이 소유한다.** 실행 이력(<c>MeshRun</c>)은 외부 호출을 추적하는 별도
/// 애그리게이트지만, 사용자가 내려받는 산출물은 작업의 것이라 같은 경계 안에 있다.
/// 그래야 작업 하나를 읽는 것으로 화면에 필요한 것이 다 나온다.
///
/// **산출물이 목록이다.** 형식이 늘어도 이 타입이 안 바뀌는 것이 그 선택의 값어치다.
/// </summary>
public sealed class GeneratedMeshTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    [Fact]
    public void AttachedMesh_BelongsToItsPartAndTask()
    {
        var (job, reconstruct) = ReadyForMesh();

        job.AttachGeneratedMesh(
            reconstruct.PartId!.Value, reconstruct.Id, Guid.NewGuid(),
            MeshArtifacts.Glb("meshes/abc/model.glb", 4_821_900), credits: 50, Now);

        var mesh = Assert.Single(job.GeneratedMeshes);
        Assert.Equal(reconstruct.PartId, mesh.PartId);
        Assert.Equal(reconstruct.Id, mesh.TaskId);
        Assert.Equal(50, mesh.CreditsConsumed);
        Assert.Equal("model/gltf-binary", mesh.Model.ContentType);
    }

    /// <summary>미리보기는 없을 수 있다 — 공급자가 항상 렌더 이미지를 주지는 않는다.</summary>
    [Fact]
    public void MeshWithoutPreview_IsStillValid()
    {
        var (job, reconstruct) = ReadyForMesh();

        job.AttachGeneratedMesh(
            reconstruct.PartId!.Value, reconstruct.Id, Guid.NewGuid(),
            MeshArtifacts.Glb("meshes/abc/model.glb", 4_821_900), credits: null, Now);

        Assert.False(Assert.Single(job.GeneratedMeshes).HasPreview);
    }

    /// <summary>
    /// **Tripo 결과에는 FBX 가 없다** (Plan D-05).
    ///
    /// 화면이 이 값으로 내려받기 버튼을 감춘다 — 없는 것이 <c>false</c> 로 나오지 않으면
    /// 눌러도 404 나는 버튼이 보인다.
    /// </summary>
    [Fact]
    public void TripoShapedResult_HasNoFbx()
    {
        var (job, reconstruct) = ReadyForMesh();

        job.AttachGeneratedMesh(
            reconstruct.PartId!.Value, reconstruct.Id, Guid.NewGuid(),
            MeshArtifacts.GlbAndPreview(
                "meshes/abc/model.glb", 4_821_900, "meshes/abc/preview.png", 12_000),
            credits: null, Now);

        var mesh = Assert.Single(job.GeneratedMeshes);

        Assert.True(mesh.HasPreview);
        Assert.False(mesh.HasFbx);
        Assert.Null(mesh.Find(MeshArtifactKind.Fbx));
    }

    /// <summary>Meshy 결과는 셋을 낸다.</summary>
    [Fact]
    public void MeshyShapedResult_CarriesThreeArtifacts()
    {
        var (job, reconstruct) = ReadyForMesh();

        job.AttachGeneratedMesh(
            reconstruct.PartId!.Value, reconstruct.Id, Guid.NewGuid(),
            MeshArtifacts.All("meshes/abc", 4_821_900), credits: 30, Now);

        var mesh = Assert.Single(job.GeneratedMeshes);

        Assert.Equal(3, mesh.Artifacts.Count);
        Assert.True(mesh.HasFbx);
        Assert.Equal("meshes/abc/model.fbx", mesh.Find(MeshArtifactKind.Fbx)!.BlobKey);
    }

    /// <summary>
    /// **GLB 없이는 결과가 만들어지지 않는다** (§3.3).
    ///
    /// 여기를 지나면 공정이 성공으로 확정되는데, 그러고도 내려받을 것이 없으면 사용자가
    /// 완료를 보고 빈손이 된다.
    /// </summary>
    [Fact]
    public void ResultWithoutGlb_IsRejected()
    {
        var (job, reconstruct) = ReadyForMesh();

        Assert.Throws<InvalidOperationException>(() => job.AttachGeneratedMesh(
            reconstruct.PartId!.Value, reconstruct.Id, Guid.NewGuid(),
            [new MeshArtifactDescriptor(
                MeshArtifactKind.Preview, "meshes/abc/preview.png", "image/png", 12_000)],
            credits: null, Now));
    }

    /// <summary>
    /// 재시도가 결과를 쌓는다 — 이미지와 같은 규칙이다 (§3.3).
    ///
    /// 이전 것을 지우지 않는 이유는 비교의 재료이기 때문이고, 파츠가 가리키는 것은
    /// 언제나 가장 나중 것이다.
    /// </summary>
    [Fact]
    public void RetriedMesh_StacksInsteadOfReplacing()
    {
        var (job, reconstruct) = ReadyForMesh();

        job.AttachGeneratedMesh(
            reconstruct.PartId!.Value, reconstruct.Id, Guid.NewGuid(),
            MeshArtifacts.Glb("meshes/first/model.glb", 1_000), null, Now);
        job.AttachGeneratedMesh(
            reconstruct.PartId!.Value, reconstruct.Id, Guid.NewGuid(),
            MeshArtifacts.Glb("meshes/second/model.glb", 2_000), null, Now.AddMinutes(5));

        Assert.Equal(2, job.GeneratedMeshes.Count);
        Assert.Equal(
            "meshes/second/model.glb",
            job.LatestMeshFor(reconstruct.PartId!.Value)!.Model.BlobKey);
    }

    [Fact]
    public void PartWithoutMesh_HasNoLatest()
    {
        var (job, reconstruct) = ReadyForMesh();

        Assert.Null(job.LatestMeshFor(reconstruct.PartId!.Value));
    }

    [Fact]
    public void MeshForAnUnknownPart_IsRejected()
    {
        var (job, reconstruct) = ReadyForMesh();

        // 잘못된 파츠에 붙으면 화면이 다른 파츠의 결과를 보여준다
        Assert.Throws<InvalidOperationException>(() => job.AttachGeneratedMesh(
            Guid.NewGuid(), reconstruct.Id, Guid.NewGuid(),
            MeshArtifacts.Glb("meshes/abc/model.glb", 4_821_900), null, Now));
    }

    // ─── 설정 ───

    private static (PipelineJob Job, PipelineTask Reconstruct) ReadyForMesh()
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

        return (job, job.Tasks.Single(t => t.Kind == TaskKind.Reconstruct));
    }
}
