using Noxtend.Api.Contracts;
using Noxtend.Domain.Job;
using Noxtend.Tests.Application;

namespace Noxtend.Tests.Api;

/// <summary>review-gate-staged 사이클 1 T10 — 검수 응답의 단계·서술 출처·팔레트.</summary>
public sealed class ReviewStateResponseTests
{
    // 상자 단계 → "boxes", 서술 단계 → "descriptions", 파츠 출처·팔레트 포함
    [Fact]
    public async Task From_ExposesPhaseSourceAndPalette()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();

        Assert.Equal("boxes", ReviewStateResponse.From(job).ReviewPhase);

        await fixture.ApproveReview.HandleAsync(job.Id, CancellationToken.None);
        var response = ReviewStateResponse.From(job);

        Assert.Equal("descriptions", response.ReviewPhase);
        Assert.All(response.Parts, p => Assert.Equal("model", p.DescriptionSource));
        Assert.Equal(job.Scene!.Palette.Count, response.Palette.Count);
    }

    // 게이트 없는 작업은 단계 null
    [Fact]
    public async Task From_NonGatedJob_HasNullPhase()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();

        Assert.Null(ReviewStateResponse.From(job).ReviewPhase);
    }
}
