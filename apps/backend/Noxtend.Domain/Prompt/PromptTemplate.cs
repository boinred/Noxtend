using System.Text.RegularExpressions;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;

namespace Noxtend.Domain.Prompt;

/// <summary>
/// 프롬프트 템플릿의 변수 치환과 검증.
///
/// Design Ref: §3.4 · §2.3-5
///
/// **Domain 에 있는 이유**: 파이프라인(`Noxtend.Application`)은 실행 시점에 렌더하고,
/// 튜닝(`Noxtend.Tuning.Application`)은 저장 시점에 검증한다. 둘은 서로를 참조하지
/// 않으므로(§2.4) 공통 조상에 있어야 한다. `Result{T}` 와 같은 사정이다.
///
/// **치환 실패를 조용히 넘기지 않는 것이 핵심 규칙이다.** `{{scen}}` 같은 오타를
/// 그대로 두면 프롬프트가 깨진 채 공급자에게 나가고, 결과가 이상해도 원인이 안 보인다.
/// </summary>
public static partial class PromptTemplate
{
    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex VariablePattern();

    /// <summary>
    /// 단계별 허용 변수.
    ///
    /// 장면 분석은 이미지만 본다 — 앞 공정이 없으므로 끼울 값이 없다.
    /// 뒤로 갈수록 앞 공정의 산출물이 쌓인다 (§3.4).
    /// </summary>
    public static IReadOnlySet<string> AllowedVariables(LlmOperationKind kind) => kind switch
    {
        LlmOperationKind.GenerateSprite => new HashSet<string> { "settings", "asset", "frame", "sourceCanvas", "outputCanvas" },
        LlmOperationKind.AnalyzeSprites => new HashSet<string> { "settings", "sourceCanvas" },
        LlmOperationKind.Analyze => new HashSet<string>(),

        // 유사도 평가 — 두 이미지가 입력의 전부다. 변수 없음 (background-similarity-tuning §15.1)
        LlmOperationKind.SimilarityEvaluate => new HashSet<string>(),

        // gender·partHints 는 캐릭터 고유 변수다 (character-studio §D-03). 종류 힌트는 추출로,
        // 개수 힌트는 분해로 흐른다. 허용하지 않으면 이후 admin 편집 저장이 거절된다 (§4.2)
        LlmOperationKind.Extract => new HashSet<string> { "scene", "gender", "partHints" },
        LlmOperationKind.Decompose => new HashSet<string> { "scene", "parts", "gender", "partHints" },

        // 서술 재작성은 대상 파츠만 받는다 (occludedby-recompute §입력→출력 3).
        // 좌표·가림 관계는 코드가 이미 정했으므로 묻지 않는다. partHints 는 armor-any-region
        // (2026-09-08)로 추가 — 사람이 새로 그린 NEW 파츠의 부위를 이름·좌표만으로 못 정할 때
        // 힌트로 교차 확인한다
        LlmOperationKind.RewriteDescriptions => new HashSet<string> { "scene", "targets", "partHints" },

        // 생성은 파츠 **하나**를 그린다 — 목록이 아니라 그 파츠의 값들이 들어온다.
        //
        // **occludedBy 를 넣지 않는다** (사이클 #7 §3.5): 가림 정보는 "이 파츠가 무엇에
        // 가려지는가" 이고, 생성의 목적은 **가림 없는 온전한 단독 이미지**다 (Plan D-1).
        // 넣으면 모델이 가려진 상태로 그릴 유인이 생긴다.
        //
        // **bounds 도 넣지 않는다** — 위치는 조립 단계의 정보다.
        // gender 는 베이스바디 그리기에 필수라 넣는다 (§D-03). partHints 는 제외 —
        // 파츠 하나만 그리는 단계에 목록을 주면 그것들까지 그릴 유인이 된다 (R-2)
        LlmOperationKind.Generate => new HashSet<string>
        {
            "scene", "partName", "partDescription", "partCategory", "viewDirection", "gender",
        },

        _ => new HashSet<string>(),
    };

    /// <summary>
    /// 템플릿에 값을 끼운다.
    ///
    /// 템플릿에 값을 안 준 변수가 있으면 예외다. 삽입한 데이터의 중괄호는 재해석하지 않는다.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        return VariablePattern().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            return values.TryGetValue(name, out var value) ? value
                : throw new InvalidOperationException($"치환되지 않은 변수가 있습니다: {{{{{name}}}}}");
        });
    }

    /// <summary>
    /// 저장 시점 검증. 통과하면 <c>null</c>, 아니면 사용자에게 보일 사유.
    ///
    /// **여기서 막는 것이 실행 중 실패보다 낫다.** 허용 목록에 없는 변수를 쓴 프롬프트를
    /// 활성화하면 그 단계의 모든 실행이 실패한다.
    /// </summary>
    public static string? FindUnknownVariable(string template, LlmOperationKind kind)
    {
        var allowed = AllowedVariables(kind);

        foreach (Match match in VariablePattern().Matches(template))
        {
            var name = match.Groups[1].Value;
            if (!allowed.Contains(name))
            {
                var hint = allowed.Count == 0
                    ? "이 단계는 변수를 쓰지 않습니다"
                    : $"쓸 수 있는 변수: {string.Join(", ", allowed.Order().Select(v => $"{{{{{v}}}}}"))}";

                return $"알 수 없는 변수 {{{{{name}}}}} — {hint}";
            }
        }

        return null;
    }
}
