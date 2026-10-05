using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Api.Controllers;
using Noxtend.Domain.Common;
using Noxtend.Tests.Application;

namespace Noxtend.Tests.Api;

/// <summary>
/// asset-category-contract — 카테고리는 이름만 받는다.
///
/// 숫자 문자열("99"·"0")은 <c>Enum.TryParse</c> 를 그냥 통과해 정의되지 않은 값이 백본으로
/// 새어 든다. PromptsController 는 이미 막았고(72c4c5c), JobsController 도 같은 어휘라
/// 같은 가드를 둔다 — 접수·목록 두 입구 모두.
/// </summary>
public sealed class JobCategoryGuardTests
{
    private static JobsController Build()
    {
        var fixture = new PipelineFixture();
        return new JobsController(
            fixture.Start, fixture.Get, fixture.List, fixture.Cancel, fixture.Delete, fixture.Retry, fixture.AddMesh,
            fixture.GetReview, fixture.AddReviewPart, fixture.FindOverlaps, fixture.RemoveReviewPart, fixture.MoveReviewPlacement, fixture.ApproveReview, fixture.ReviewDescriptions, fixture.GenerateViews, fixture.ReturnToDescriptions, fixture.ReplanPartMesh);
    }

    // 접수(POST): 숫자 문자열 카테고리는 거부된다
    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    public async Task StartAsync_RejectsNumericCategory(string category)
    {
        var request = new StartJobRequest(category, Guid.NewGuid(), Guid.NewGuid(), "model");

        var result = await Build().StartAsync(request, CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        var response = Assert.IsType<ApiResponse<JobAcceptedResponse>>(obj.Value);
        Assert.Equal(ErrorCode.JobCategoryInvalid, response.Error!.Code);
    }

    // 목록 필터(GET ?category=): 숫자 문자열 카테고리는 거부된다
    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    public async Task ListAsync_RejectsNumericCategory(string category)
    {
        var result = await Build().ListAsync("active", category, 10, CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        var response = Assert.IsType<ApiResponse<JobListResponse>>(obj.Value);
        Assert.Equal(ErrorCode.JobCategoryInvalid, response.Error!.Code);
    }

    // 정상 이름은 통과한다 — 가드가 유효 값까지 막지 않는지 확인
    [Fact]
    public async Task ListAsync_AcceptsNamedCategory()
    {
        var result = await Build().ListAsync("active", "character", 10, CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        var response = Assert.IsType<ApiResponse<JobListResponse>>(obj.Value);
        Assert.Null(response.Error);
    }
}
