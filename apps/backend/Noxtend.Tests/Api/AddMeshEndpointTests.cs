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
/// 3D 를 뒤늦게 붙이는 endpoint 의 계약.
///
/// Design Ref: §6 · D-10
///
/// **접수와 같은 202 여야 한다.** 응답 모양이 달라지면 화면이 경로마다 다른 갱신 코드를
/// 갖게 되고, 그 차이는 한쪽만 고칠 때 조용히 어긋난다.
/// </summary>
public sealed class AddMeshEndpointTests
{
    [Fact]
    public async Task AddingMesh_Returns202WithTheAcceptedShape()
    {
        var (controller, jobId, mesh) = await ReadyAsync();

        var result = Assert.IsType<ObjectResult>(
            await controller.AddMeshAsync(
                jobId, new AddMeshRequest(mesh, StubModelCatalog.DefaultMeshModel), default));

        // 접수(`POST /api/jobs`)와 같은 202 다
        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);

        var envelope = Assert.IsType<ApiResponse<JobAcceptedResponse>>(result.Value);

        Assert.Null(envelope.Error);
        Assert.Equal(jobId, envelope.Data!.Id);

        // 작업이 다시 열려 3D 를 만든다
        Assert.Equal("running", envelope.Data.Status);
    }

    /// <summary>
    /// 요청이 틀린 게 아니라 작업 상태가 맞지 않는다 — 조금 전이었다면 통과했을 수 있다.
    /// </summary>
    [Fact]
    public async Task SecondRequest_Returns409()
    {
        var (controller, jobId, mesh) = await ReadyAsync();
        var request = new AddMeshRequest(mesh, StubModelCatalog.DefaultMeshModel);

        await controller.AddMeshAsync(jobId, request, default);

        var result = Assert.IsType<ObjectResult>(
            await controller.AddMeshAsync(jobId, request, default));

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public async Task UnknownJob_Returns404()
    {
        var (controller, _, mesh) = await ReadyAsync();

        var result = Assert.IsType<ObjectResult>(
            await controller.AddMeshAsync(
                Guid.NewGuid(), new AddMeshRequest(mesh, StubModelCatalog.DefaultMeshModel), default));

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
    }

    /// <summary>모든 3D 오류 코드가 상태에 매핑돼 있어야 한다 — 빠지면 500 으로 나간다.</summary>
    [Theory]
    [InlineData(ErrorCode.JobMeshNotApplicable, StatusCodes.Status409Conflict)]
    [InlineData(ErrorCode.JobMeshProviderNotFound, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorCode.JobMeshProviderDisabled, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorCode.JobMeshModelUnavailable, StatusCodes.Status400BadRequest)]
    public void MeshErrorCodesAreMapped(string code, int expected)
    {
        var result = Assert.IsType<ObjectResult>(
            ApiResults.From(Result<PipelineJob>.Fail(code, "메시지"), JobAcceptedResponse.From));

        Assert.Equal(expected, result.StatusCode);
    }

    // ─── 설정 ───

    private static async Task<(JobsController Controller, Guid JobId, Guid MeshProvider)> ReadyAsync()
    {
        var fixture = new PipelineFixture();
        var now = fixture.Clock.Now;

        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), now, Guid.NewGuid(), "gemini-image");

        job.ApplyParts(["가로등"]);

        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(now, TimeSpan.FromMinutes(2));
        decompose.Succeed(now);
        job.PlanReadyFollowUpTasks();
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToArray())
        {
            task.Claim(now, TimeSpan.FromMinutes(2));
            job.AttachGeneratedImage(
                task.PartId!.Value, task.Id,
                $"generated/{task.ViewDirection}.png", "image/png", 2048, now);
            task.Succeed(now);
        }

        job.ReconcileFromTasks(now);
        await fixture.Jobs.AddAsync(job, default);
        await fixture.Jobs.SaveChangesAsync(default);

        var provider = ProviderConfig.Create(
            "Tripo 운영", ProviderKind.Tripo, "cipher", "9a1b", now);
        await fixture.Providers.AddAsync(provider, default);

        var controller = new JobsController(
            fixture.Start, fixture.Get, fixture.List, fixture.Cancel, fixture.Delete, fixture.Retry, fixture.AddMesh,
            fixture.GetReview, fixture.AddReviewPart, fixture.FindOverlaps, fixture.RemoveReviewPart, fixture.MoveReviewPlacement, fixture.ApproveReview, fixture.ReviewDescriptions, fixture.GenerateViews, fixture.ReturnToDescriptions, fixture.ReplanPartMesh)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return (controller, job.Id, provider.Id);
    }
}
