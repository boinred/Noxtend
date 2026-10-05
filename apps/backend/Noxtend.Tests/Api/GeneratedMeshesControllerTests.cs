using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Controllers;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Blob;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Infrastructure.Persistence.InMemory;
using Noxtend.Tests;

namespace Noxtend.Tests.Api;

/// <summary>
/// 3D 결과 내려받기.
///
/// Design Ref: §10.4 · §13.2 · Plan NFR-07
///
/// **공급자 링크를 대신하는 자리다.** Tripo 가 주는 URL 은 5분이면 만료되므로, 화면이
/// 그것을 들고 있으면 어제 만든 에셋을 못 받는다. 여기서는 우리 Blob 키만 쓴다.
/// </summary>
public sealed class GeneratedMeshesControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Glb_IsServedWithItsContentType()
    {
        var (controller, mesh) = await ReadyAsync();

        var result = Assert.IsType<FileStreamResult>(await controller.GetAsync(mesh.Id, default));

        Assert.Equal("model/gltf-binary", result.ContentType);
    }

    /// <summary>
    /// **파일명에 id 만 쓴다** (§13.2).
    ///
    /// 파츠 이름을 쓰면 사용자 문자열이 헤더로 나가고, 그 안의 따옴표·줄바꿈이 헤더를
    /// 쪼갠다. 이름은 사용자가 정하는 값이라 무엇이든 들어올 수 있다.
    /// </summary>
    [Fact]
    public async Task DownloadName_IsTheIdAlone()
    {
        var (controller, mesh) = await ReadyAsync(partName: "가로등\"; drop\n");

        var result = Assert.IsType<FileStreamResult>(await controller.GetAsync(mesh.Id, default));

        Assert.Equal($"{mesh.Id}.glb", result.FileDownloadName);
    }

    /// <summary>
    /// 결과는 바뀌지 않는다 — 다시 만들면 새 id 가 나오므로 이 id 의 내용은 불변이다.
    ///
    /// `private` 인 이유는 인증이 붙었을 때 공유 캐시에 남아 있으면 안 되기 때문이다.
    /// </summary>
    [Fact]
    public async Task Glb_IsCachedImmutablyButPrivately()
    {
        var (controller, mesh) = await ReadyAsync();

        await controller.GetAsync(mesh.Id, default);

        var cacheControl = controller.Response.Headers.CacheControl.ToString();

        Assert.Contains("private", cacheControl);
        Assert.Contains("immutable", cacheControl);
    }

    [Fact]
    public async Task UnknownMesh_IsNotFound()
    {
        var (controller, _) = await ReadyAsync();

        Assert.IsType<NotFoundResult>(await controller.GetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Preview_IsServedWhenPresent()
    {
        var (controller, mesh) = await ReadyAsync(withPreview: true);

        var result = Assert.IsType<FileStreamResult>(
            await controller.GetPreviewAsync(mesh.Id, default));

        Assert.Equal("image/png", result.ContentType);
    }

    /// <summary>공급자가 렌더 이미지를 주지 않는 경우가 있다 — 그때는 404 다.</summary>
    [Fact]
    public async Task MissingPreview_IsNotFound()
    {
        var (controller, mesh) = await ReadyAsync(withPreview: false);

        Assert.IsType<NotFoundResult>(await controller.GetPreviewAsync(mesh.Id, default));
    }

    // ─── 설정 ───

    private static async Task<(GeneratedMeshesController Controller, GeneratedMesh Mesh)> ReadyAsync(
        bool withPreview = false, bool withFbx = false, string partName = "가로등")
    {
        var jobs = new InMemoryJobRepository();
        var artifacts = new InMemoryMeshArtifactStorage();

        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now,
            Guid.NewGuid(), "gemini-image", Guid.NewGuid(), "P1-20260311");

        job.ApplyParts([partName]);
        var part = job.Parts.Single();
        var task = job.PlanTask(TaskKind.Reconstruct, ordinal: 0);
        task.Claim(Now, Lease);

        var runId = Guid.NewGuid();

        // 실제 저장소를 거쳐야 키와 바이트가 맞는다 — 손으로 키를 지어내면 열 때 어긋난다
        using var glb = new MemoryStream(FakeGlb.Bytes);
        var model = await artifacts.SaveAsync(
            runId, MeshArtifactKind.Glb, glb, null, 128 * 1024 * 1024, default);

        List<MeshArtifactDescriptor> descriptors =
        [
            new(MeshArtifactKind.Glb, model.BlobKey, model.ContentType, model.SizeBytes),
        ];

        if (withPreview)
        {
            using var png = new MemoryStream(Png);
            var stored = await artifacts.SaveAsync(
                runId, MeshArtifactKind.Preview, png, "image/png", 16 * 1024 * 1024, default);

            descriptors.Add(new MeshArtifactDescriptor(
                MeshArtifactKind.Preview, stored.BlobKey, stored.ContentType, stored.SizeBytes));
        }

        if (withFbx)
        {
            using var fbx = new MemoryStream(FakeMeshProvider.MinimalFbx());
            var stored = await artifacts.SaveAsync(
                runId, MeshArtifactKind.Fbx, fbx, null, 128 * 1024 * 1024, default);

            descriptors.Add(new MeshArtifactDescriptor(
                MeshArtifactKind.Fbx, stored.BlobKey, stored.ContentType, stored.SizeBytes));
        }

        job.AttachGeneratedMesh(part.Id, task.Id, runId, descriptors, credits: 50, Now);

        await jobs.AddAsync(job, default);
        await jobs.SaveChangesAsync(default);

        var controller = new GeneratedMeshesController(jobs, artifacts)
        {
            // 헤더를 쓰려면 실제 HttpContext 가 있어야 한다
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return (controller, job.GeneratedMeshes.Single());
    }

    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    ];
}
