using System.Text.Json;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 시드 스키마의 strict 모드 준수 — **실측 400 의 회귀 방지**.
///
/// OpenAI strict(json_schema) 는 모든 객체에 additionalProperties:false 와 전 속성
/// required 를 요구한다. 유사도 평가 v1 이 이 규칙을 어겨 요청 자체가 400 으로
/// 거절됐고, run 이 PROVIDER_CALL_FAILED 로 닫혔다 — 스키마 오류는 실행이 아니라
/// 여기서 죽어야 한다.
/// </summary>
public sealed class StrictSchemaComplianceTests
{
    public static TheoryData<string, string> ActiveSeedSchemas => new()
    {
        { "analyze", SeedPrompts.BackgroundAnalyze().Schema },
        { "extract", SeedPrompts.BackgroundExtract().Schema },
        { "decompose", SeedPrompts.DecomposeV3().Schema },
        { "generate", SeedPrompts.BackgroundGenerateV3().Schema },
        { "similarityEvaluateV3", SeedPrompts.SimilarityEvaluateBackgroundV3().Schema },
    };

    [Theory]
    [MemberData(nameof(ActiveSeedSchemas))]
    public void EveryObjectListsAllPropertiesAsRequired(string name, string schema)
    {
        using var document = JsonDocument.Parse(schema);
        var violations = new List<string>();
        Walk(document.RootElement, $"{name}$", violations);

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    // properties 를 가진 객체마다: required = 전 속성, additionalProperties = false
    private static void Walk(JsonElement element, string path, List<string> violations)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("properties", out var properties)
                && properties.ValueKind == JsonValueKind.Object)
            {
                var names = properties.EnumerateObject().Select(p => p.Name).ToHashSet();

                var required = element.TryGetProperty("required", out var req)
                    ? req.EnumerateArray().Select(v => v.GetString()!).ToHashSet()
                    : [];
                foreach (var missing in names.Except(required))
                {
                    violations.Add($"{path}: '{missing}' 가 required 에 없다");
                }

                if (!element.TryGetProperty("additionalProperties", out var extra)
                    || extra.ValueKind != JsonValueKind.False)
                {
                    violations.Add($"{path}: additionalProperties:false 가 없다");
                }
            }

            foreach (var child in element.EnumerateObject())
            {
                Walk(child.Value, $"{path}.{child.Name}", violations);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                Walk(item, $"{path}[{index++}]", violations);
            }
        }
    }
}
