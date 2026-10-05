using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;

namespace Noxtend.Tests.Application;

/// <summary>
/// prompt-category-axis §6.3 — 소비 측이 job.Category 를 프롬프트 조회로 관통시킨다.
///
/// **독립 리뷰가 지적한 유일한 미검증 구간이다.** 스텁이 카테고리를 버리면 세 호출부가
/// 상수를 넘겨도 전체 스위트가 통과한다(뮤테이션으로 확인됨). 여기서 질의된 카테고리를
/// 붙잡아 배선을 고정한다 — 이 기능의 존재 이유가 이 배선이다.
/// </summary>
public sealed class PromptCategoryWiringTests
{
    // 접수 차단 판정(StartJobHandler)이 job 카테고리로 프롬프트를 검사한다
    [Fact]
    public async Task Intake_ChecksPromptsWithJobCategory()
    {
        var fixture = new PipelineFixture();

        await fixture.StartJobAsync(category: AssetCategory.Character);

        Assert.Contains((LlmOperationKind.Analyze, AssetCategory.Character), fixture.Prompts.Queries);
        Assert.Contains((LlmOperationKind.Extract, AssetCategory.Character), fixture.Prompts.Queries);
        Assert.Contains((LlmOperationKind.Decompose, AssetCategory.Character), fixture.Prompts.Queries);
    }

    // 텍스트 공정 실행(RunTaskHandler)이 job 카테고리로 프롬프트를 조회한다
    [Fact]
    public async Task RunTask_ResolvesPromptWithJobCategory()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync(category: AssetCategory.Character);
        var analyze = job.Tasks.Single(t => t.Kind == TaskKind.Analyze);
        fixture.Prompts.Queries.Clear();   // 접수 시점 질의를 걷어내 실행 경로만 본다

        await fixture.Run.HandleAsync(analyze.Id, CancellationToken.None);

        Assert.Contains((LlmOperationKind.Analyze, AssetCategory.Character), fixture.Prompts.Queries);
    }

    // 이미지 생성 공정(RunGenerationTaskHandler)이 job 카테고리로 프롬프트를 조회한다
    [Fact]
    public async Task RunGeneration_ResolvesPromptWithJobCategory()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.FanOutAsync(category: AssetCategory.Character);
        var generate = job.Tasks.First(t => t.Kind == TaskKind.Generate);
        fixture.Prompts.Queries.Clear();

        await fixture.RunGeneration.HandleAsync(generate.Id, CancellationToken.None);

        Assert.Contains((LlmOperationKind.Generate, AssetCategory.Character), fixture.Prompts.Queries);
    }
}
