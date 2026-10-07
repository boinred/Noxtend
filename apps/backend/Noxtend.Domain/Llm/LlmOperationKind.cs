using Noxtend.Domain.Job;

namespace Noxtend.Domain.Llm;

/// <summary>
/// LLM 호출의 종류 — pipeline `TaskKind` 에서 분리한 축 (§7.1).
///
/// Design Ref: background-similarity-tuning §7.1
///
/// 프롬프트·호출 기록이 TaskKind 에 결합돼 있으면 공정이 아닌 호출(유사도 평가)이 낄
/// 자리가 없다. **기존 숫자를 유지**해 저장된 행이 그대로 읽히고, 평가는 100 —
/// pipeline enum 이 늘어도 충돌하지 않는 자리다.
/// </summary>
public enum LlmOperationKind
{
    Analyze = 0,
    Extract = 1,
    Decompose = 2,

    /// <summary>검수 반영 재서술 (review-gate) — 분해와 생성 사이. TaskKind 순서를 따른다.</summary>
    RewriteDescriptions = 3,

    Generate = 4,
    AnalyzeSprites = 5,
    GenerateSprite = 6,
    SimilarityEvaluate = 100,
}

public static class LlmOperation
{
    /// <summary>
    /// pipeline stage → operation kind 명시적 매핑.
    /// Reconstruct 처럼 LLM 을 부르지 않는 값은 매핑하지 않는다 — 잘못된 기록의 근원이 된다.
    /// </summary>
    public static LlmOperationKind FromTask(TaskKind kind) => kind switch
    {
        TaskKind.Analyze => LlmOperationKind.Analyze,
        TaskKind.Extract => LlmOperationKind.Extract,
        TaskKind.Decompose => LlmOperationKind.Decompose,
        TaskKind.RewriteDescriptions => LlmOperationKind.RewriteDescriptions,
        TaskKind.GenerateSprite => LlmOperationKind.GenerateSprite,
        TaskKind.AnalyzeSprites => LlmOperationKind.AnalyzeSprites,
        TaskKind.Generate => LlmOperationKind.Generate,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "LLM 을 호출하지 않는 공정입니다"),
    };
}
