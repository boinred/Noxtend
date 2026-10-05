using System.Text.Json;
using Noxtend.Application.Stages;
using Noxtend.Domain.Ports;
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
            job.Id, "상의", "Top", new Noxtend.Domain.Job.Bounds(0.8, 0.8, 0.1, 0.1), description: "",
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

    [Fact]
    public async Task Interpret_Resolves_Only_Unique_Mismatches_For_Multiple_Targets()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();
        await fixture.AddReviewPart.HandleAsync(
            job.Id, "상의", "Top", new Noxtend.Domain.Job.Bounds(0.8, 0.8, 0.1, 0.1), description: "",
            occludes: null, CancellationToken.None);
        await fixture.AddReviewPart.HandleAsync(
            job.Id, "바지", "Bottom", new Noxtend.Domain.Job.Bounds(0.8, 0.6, 0.1, 0.1), description: "",
            occludes: null, CancellationToken.None);
        await fixture.ApproveReview.HandleAsync(job.Id, CancellationToken.None);

        var reloadedJob = await fixture.Jobs.GetAsync(job.Id, CancellationToken.None);
        Assert.NotNull(reloadedJob);
        Assert.Equal(["상의", "바지"], reloadedJob.DescriptionsStale);

        var schema = """
        {
          "type": "object",
          "properties": {
            "parts": {
              "type": "array",
              "items": { "type": "object", "properties": { "name": { "type": "string" } } }
            }
          }
        }
        """;
        using var schemaDocument = JsonDocument.Parse(
            new RewriteDescriptionsStage().BuildJsonSchema(reloadedJob, schema));
        Assert.Equal(
            ["상의", "바지"],
            schemaDocument.RootElement.GetProperty("properties").GetProperty("parts")
                .GetProperty("items").GetProperty("properties").GetProperty("name")
                .GetProperty("enum").EnumerateArray().Select(name => name.GetString()));

        var ambiguousJson = """
        {
          "parts": [
            { "name": "Top", "description": "상의 서술", "category": "Top" },
            { "name": "Bottom", "description": "바지 서술", "category": "Bottom" }
          ]
        }
        """;

        Assert.Throws<ProviderBadResponseException>(
            () => new RewriteDescriptionsStage().Interpret(reloadedJob, ambiguousJson));

        var uniquelyResolvableJson = """
        {
          "parts": [
            { "name": "바지", "description": "바지 서술", "category": "Bottom" },
            { "name": "Top", "description": "상의 서술", "category": "Top" }
          ]
        }
        """;
        var apply = new RewriteDescriptionsStage().Interpret(reloadedJob, uniquelyResolvableJson);
        apply(reloadedJob);

        Assert.Equal("상의 서술", reloadedJob.Parts.Single(p => p.Name == "상의").Description);
        Assert.Equal("바지 서술", reloadedJob.Parts.Single(p => p.Name == "바지").Description);
    }
}
