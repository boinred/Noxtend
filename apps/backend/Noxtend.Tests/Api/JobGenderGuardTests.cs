using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Api.Controllers;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Tests.Application;

namespace Noxtend.Tests.Api;

/// <summary>
/// character-studio slice 5 §5 — 성별 파싱은 카테고리 가드와 같은 형태다.
///
/// 설계 §5.1 은 "기존 Enum.TryParse 패턴" 이라 적었지만 그 패턴은 숫자 문자열("0"·"99")을
/// 통과시키는 버그였다(asset-category-contract 로 고침). 성별도 같은 가드(숫자 거부 + IsDefined)를
/// 써서 정의되지 않은 값이 백본으로 새지 않게 한다. 성별은 선택 필드라 안 보내는 것은 유효하다.
/// </summary>
public sealed class JobGenderGuardTests
{
    private static (JobsController Controller, PipelineFixture Fixture) Build()
    {
        var fixture = new PipelineFixture();
        var controller = new JobsController(
            fixture.Start, fixture.Get, fixture.List, fixture.Cancel, fixture.Delete, fixture.Retry, fixture.AddMesh,
            fixture.GetReview, fixture.AddReviewPart, fixture.FindOverlaps, fixture.RemoveReviewPart, fixture.MoveReviewPlacement, fixture.ApproveReview, fixture.ReviewDescriptions, fixture.GenerateViews, fixture.ReturnToDescriptions, fixture.ReplanPartMesh);
        return (controller, fixture);
    }

    // 숫자 문자열 성별은 거부된다 — Enum.TryParse 만으로는 통과하는 값
    [Theory]
    [InlineData("0")]
    [InlineData("99")]
    public async Task StartAsync_RejectsNumericGender(string gender)
    {
        var (controller, _) = Build();
        var request = new StartJobRequest(
            "character", Guid.NewGuid(), Guid.NewGuid(), "model", Gender: gender);

        var result = await controller.StartAsync(request, CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        var response = Assert.IsType<ApiResponse<JobAcceptedResponse>>(obj.Value);
        Assert.Equal(ErrorCode.JobGenderInvalid, response.Error!.Code);
    }

    // 정의되지 않은 이름도 거부된다
    [Fact]
    public async Task StartAsync_RejectsUnknownGenderName()
    {
        var (controller, _) = Build();
        var request = new StartJobRequest(
            "character", Guid.NewGuid(), Guid.NewGuid(), "model", Gender: "nonbinary");

        var result = await controller.StartAsync(request, CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        var response = Assert.IsType<ApiResponse<JobAcceptedResponse>>(obj.Value);
        Assert.Equal(ErrorCode.JobGenderInvalid, response.Error!.Code);
    }

    // 유효한 성별·힌트는 접수돼 도메인에 보존된다 (DTO→PartHint 매핑까지 배선됨)
    [Fact]
    public async Task StartAsync_AcceptsGenderAndHints_AndPersists()
    {
        var (controller, fixture) = Build();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var request = new StartJobRequest(
            "character", uploadId, providerId, StubModelCatalog.DefaultModel,
            Gender: "female",
            PartHints: [new PartHintDto("팔찌", 3, null), new PartHintDto("장갑", 2, "손목형")]);

        var result = await controller.StartAsync(request, CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, obj.StatusCode);
        var response = Assert.IsType<ApiResponse<JobAcceptedResponse>>(obj.Value);
        Assert.Null(response.Error);

        var job = await fixture.Jobs.GetAsync(response.Data!.Id, CancellationToken.None);
        Assert.Equal(Gender.Female, job!.Gender);
        Assert.Contains("팔찌", job.PartHints);
    }

    // 콤마 목록은 거부된다 — Enum.TryParse 는 "Male,Male" 를 0 으로 통과시킨다 (리뷰 #7)
    [Fact]
    public async Task StartAsync_RejectsCommaSeparatedGender()
    {
        var (controller, _) = Build();
        var request = new StartJobRequest(
            "character", Guid.NewGuid(), Guid.NewGuid(), "model", Gender: "Male,Male");

        var result = await controller.StartAsync(request, CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        var response = Assert.IsType<ApiResponse<JobAcceptedResponse>>(obj.Value);
        Assert.Equal(ErrorCode.JobGenderInvalid, response.Error!.Code);
    }

    // partHints 배열의 null 원소는 500 이 아니라 JobPartHintInvalid 로 거절된다 (리뷰 #1)
    [Fact]
    public async Task StartAsync_RejectsNullHintElement()
    {
        var (controller, fixture) = Build();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var request = new StartJobRequest(
            "character", uploadId, providerId, StubModelCatalog.DefaultModel,
            Gender: "female", PartHints: [null!]);

        var result = await controller.StartAsync(request, CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        var response = Assert.IsType<ApiResponse<JobAcceptedResponse>>(obj.Value);
        Assert.Equal(ErrorCode.JobPartHintInvalid, response.Error!.Code);
    }

    // 힌트 개수가 범위 밖이면 접수에서 거부된다 — DTO 가 핸들러 검증까지 도달함을 확인
    [Fact]
    public async Task StartAsync_RejectsOutOfRangeHintCount()
    {
        var (controller, fixture) = Build();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var request = new StartJobRequest(
            "character", uploadId, providerId, StubModelCatalog.DefaultModel,
            Gender: "female",
            PartHints: [new PartHintDto("팔찌", 21, null)]);

        var result = await controller.StartAsync(request, CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        var response = Assert.IsType<ApiResponse<JobAcceptedResponse>>(obj.Value);
        Assert.Equal(ErrorCode.JobPartHintInvalid, response.Error!.Code);
    }
}
