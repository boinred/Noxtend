using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Api.Controllers;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Provider;
using Noxtend.Tests.Application;

namespace Noxtend.Tests.Api;

/// <summary>
/// 파츠별 "3D 전송 뷰 자유 선택 + 대칭" endpoint 의 계약 (spec 20260917).
///
/// **접수·AddMesh 와 같은 202 여야 한다.** 비동기로 워커가 집는 동작이라 응답 모양을
/// 다르게 두면 화면이 경로마다 다른 갱신 코드를 갖게 된다.
/// </summary>
public sealed class ReplanPartMeshEndpointTests
{
    [Fact]
    public async Task Returns202WithTheAcceptedShape()
    {
        var (controller, jobId, partId, mesh) = await ReadyAsync();

        var result = Assert.IsType<ObjectResult>(
            await controller.ReplanPartMeshAsync(
                jobId,
                new ReplanPartMeshRequest(partId, mesh, StubModelCatalog.DefaultMeshModel, "Both", "Skip"),
                default));

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        var envelope = Assert.IsType<ApiResponse<JobAcceptedResponse>>(result.Value);
        Assert.Null(envelope.Error);
        Assert.Equal(jobId, envelope.Data!.Id);
    }

    /// <summary>
    /// 값이 알려진 enum 이름이 아닌 경우 — 이미지가 없어서가 아니라 요청 자체가
    /// 잘못됐다는 뜻이므로 `MeshInputMissing`과 다른 코드여야 한다(merge-gate 2차
    /// 리뷰 B4).
    /// </summary>
    [Fact]
    public async Task UnknownLeftRightValue_Returns400WithSelectionInvalidCode()
    {
        var (controller, jobId, partId, mesh) = await ReadyAsync();

        var result = Assert.IsType<ObjectResult>(
            await controller.ReplanPartMeshAsync(
                jobId,
                new ReplanPartMeshRequest(partId, mesh, StubModelCatalog.DefaultMeshModel, "Sideways", "Skip"),
                default));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        var envelope = Assert.IsType<ApiResponse<JobAcceptedResponse>>(result.Value);
        Assert.Equal(ErrorCode.ReplanMeshSelectionInvalid, envelope.Error!.Code);
    }

    // ─── 설정 ───

    private static async Task<(JobsController Controller, Guid JobId, Guid PartId, Guid MeshProvider)> ReadyAsync()
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
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToArray())
        {
            task.Claim(now, TimeSpan.FromMinutes(2));
            using var image = TestImages.Jpeg(64, 64);
            var blobKey = await fixture.Blobs.SaveAsync(image, "image/jpeg", default);
            job.AttachGeneratedImage(partId, task.Id, blobKey, "image/jpeg", image.Length, now);
            task.Succeed(now);
        }

        job.ReconcileFromTasks(now);
        await fixture.Jobs.AddAsync(job, default);
        await fixture.Jobs.SaveChangesAsync(default);

        var provider = ProviderConfig.Create("Tripo 운영", ProviderKind.Tripo, "cipher", "9a1b", now);
        await fixture.Providers.AddAsync(provider, default);

        var controller = new JobsController(
            fixture.Start, fixture.Get, fixture.List, fixture.Cancel, fixture.Delete, fixture.Retry, fixture.AddMesh,
            fixture.GetReview, fixture.AddReviewPart, fixture.FindOverlaps, fixture.RemoveReviewPart,
            fixture.MoveReviewPlacement, fixture.ApproveReview, fixture.ReviewDescriptions, fixture.GenerateViews,
            fixture.ReturnToDescriptions, fixture.ReplanPartMesh)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return (controller, job.Id, partId, provider.Id);
    }
}
