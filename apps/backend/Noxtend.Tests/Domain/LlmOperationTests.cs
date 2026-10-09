using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;

namespace Noxtend.Tests.Domain;

/// <summary>
/// LLM operation kind 와 호출 상관관계. Design Ref: background-similarity-tuning §7.1·§7.3
///
/// **프롬프트·호출 기록의 축이 pipeline TaskKind 에 결합돼 있으면** 공정이 아닌 호출
/// (유사도 평가)이 낄 자리가 없다. 분리하되 기존 숫자는 유지해 저장된 행이 그대로 읽힌다.
/// 상관관계는 Task 또는 SimilarityEvaluation **정확히 하나** — factory 만 열어 둔다.
/// </summary>
public sealed class LlmOperationTests
{
    // ─── TaskKind → LlmOperationKind 매핑 ───

    [Theory]
    [InlineData(TaskKind.Analyze, LlmOperationKind.Analyze)]
    [InlineData(TaskKind.Extract, LlmOperationKind.Extract)]
    [InlineData(TaskKind.Decompose, LlmOperationKind.Decompose)]
    [InlineData(TaskKind.Generate, LlmOperationKind.Generate)]
    public void FromTask_MapsLlmStagesKeepingTheirNumbers(TaskKind task, LlmOperationKind expected)
    {
        Assert.Equal(expected, LlmOperation.FromTask(task));
        // 기존 행의 int 값이 그대로 읽혀야 한다 (§7.1)
        Assert.Equal((int)task, (int)expected);
    }

    /// <summary>Reconstruct 는 LLM 을 부르지 않는다 — 매핑하면 잘못된 기록이 생긴다.</summary>
    [Theory]
    [InlineData(TaskKind.Reconstruct)]
    [InlineData(TaskKind.Synthesize)]
    [InlineData(TaskKind.PackSprites)]
    public void FromTask_RefusesANonLlmStage(TaskKind kind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LlmOperation.FromTask(kind));
    }

    /// <summary>평가 값 100 — pipeline enum 이 늘어도 충돌하지 않는 자리 (§7.1).</summary>
    [Fact]
    public void SimilarityEvaluate_LivesAtOneHundred()
    {
        Assert.Equal(100, (int)LlmOperationKind.SimilarityEvaluate);
    }

    [Fact]
    public void SpriteAnalysis_HasExplicitIndependentNumbers()
    {
        Assert.Equal(7, (int)TaskKind.AnalyzeSprites);
        Assert.Equal(5, (int)LlmOperationKind.AnalyzeSprites);
        Assert.Equal(LlmOperationKind.AnalyzeSprites, LlmOperation.FromTask(TaskKind.AnalyzeSprites));
    }

    // ─── 호출 상관관계 — Task 또는 SimilarityEvaluation 정확히 하나 (§7.3) ───

    [Fact]
    public void ForTask_CarriesTheTaskAndNoEvaluation()
    {
        var taskId = Guid.NewGuid();

        var context = LlmCallContext.ForTask(
            Guid.NewGuid(), taskId, TaskKind.Analyze, Guid.NewGuid(), Guid.NewGuid(), "m");

        Assert.Equal(taskId, context.TaskId);
        Assert.Null(context.SimilarityEvaluationId);
        Assert.Equal(LlmOperationKind.Analyze, context.Kind);
    }

    [Fact]
    public void ForSourceGeneration_CarriesOnlySourceGenerationCorrelation()
    {
        var sourceGenerationId = Guid.NewGuid();
        var promptVersionId = Guid.NewGuid();
        var providerConfigId = Guid.NewGuid();
        var context = LlmCallContext.ForSourceGeneration(sourceGenerationId, promptVersionId, providerConfigId, "m");

        Assert.Equal(LlmOperationKind.GenerateSpriteSource, context.Kind);
        Assert.Null(context.JobId);
        Assert.Null(context.TaskId);
        Assert.Null(context.SimilarityEvaluationId);
        Assert.Equal(sourceGenerationId, context.SourceGenerationId);
        Assert.Equal(promptVersionId, context.PromptVersionId);
        Assert.Equal(providerConfigId, context.ProviderConfigId);
        Assert.Equal("m", context.Model);
    }

    [Fact]
    public void ForSimilarityEvaluation_CarriesTheEvaluationAndNoTask()
    {
        var evaluationId = Guid.NewGuid();

        var context = LlmCallContext.ForSimilarityEvaluation(
            Guid.NewGuid(), evaluationId, Guid.NewGuid(), Guid.NewGuid(), "m");

        Assert.Null(context.TaskId);
        Assert.Equal(evaluationId, context.SimilarityEvaluationId);
        Assert.Equal(LlmOperationKind.SimilarityEvaluate, context.Kind);
    }
}
