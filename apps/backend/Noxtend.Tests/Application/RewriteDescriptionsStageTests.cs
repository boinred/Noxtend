using Noxtend.Application.Stages;
using Xunit;

namespace Noxtend.Tests.Application;

public sealed class RewriteDescriptionsStageTests
{
    [Fact]
    public async Task Interpret_Resolves_Mismatched_Llm_PartName_To_TargetPart()
    {
        // Arrange: PipelineFixture를 통해 PendingReview 상태 도달
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();

        // 검수 단계에서 사용자가 서술 없는 '상의' 파츠 수동 추가 (description: "")
        var addResult = await fixture.AddReviewPart.HandleAsync(
            job.Id, "상의", "Top", new Noxtend.Domain.Job.Bounds(0.2, 0.2, 0.4, 0.4), description: "",
            occludes: null, CancellationToken.None);
        Assert.True(addResult.IsSuccess, addResult.ErrorCode);

        var approveResult = await fixture.ApproveReview.HandleAsync(job.Id, CancellationToken.None);
        Assert.True(approveResult.IsSuccess, approveResult.ErrorCode);

        var reloadedJob = await fixture.Jobs.GetAsync(job.Id, CancellationToken.None);
        Assert.NotNull(reloadedJob);
        Assert.Contains("상의", reloadedJob.DescriptionsStale);

        var stage = new RewriteDescriptionsStage();

        // LLM 응답: name 자리에 '상의' 대신 'Top'이라는 영문 카테고리명을 응답
        var rawJson = """
        {
          "parts": [
            {
              "name": "Top",
              "description": "세련된 청자켓 상의 서술",
              "category": "Top"
            }
          ]
        }
        """;

        // Act: 백엔드 Interpret 실행 (스마트 보정 레이어 동작)
        var apply = stage.Interpret(reloadedJob, rawJson);
        apply(reloadedJob);

        // Assert: LLM이 'Top'으로 보냈지만 원래 요청한 '상의' 파츠의 서술로 안전하게 보정 적용됨
        var sangiPart = Assert.Single(reloadedJob.Parts, p => p.Name == "상의");
        Assert.Equal("세련된 청자켓 상의 서술", sangiPart.Description);
    }
}
