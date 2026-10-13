using System.Text.Json;
using System.Text.Json.Nodes;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// Anthropic 구조화 출력 전송용 스키마 정리.
///
/// 공식 Python/TS SDK 는 미지원 제약을 지우고 응답을 클라이언트에서 검증하지만 C# SDK 는
/// 스키마를 그대로 보내 400 이 난다. 지운 제약(개수·길이·범위)은 각 공정 파서가 검증한다.
/// 저장된 프롬프트 스키마는 바꾸지 않고 전송 직전 사본만 정리한다.
/// </summary>
internal static class AnthropicJsonSchema
{
    private static readonly HashSet<string> Rejected =
    [
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "minLength", "maxLength", "maxItems",
    ];

    // 키가 속성·정의 이름인 맵
    private static readonly HashSet<string> NameMaps = ["properties", "$defs", "definitions"];

    // 스키마가 아닌 데이터 값
    private static readonly HashSet<string> DataKeywords = ["enum", "const", "default", "examples", "required"];

    public static Dictionary<string, JsonElement> ForStructuredOutput(string jsonSchema)
    {
        var root = JsonNode.Parse(jsonSchema);
        if (root is not JsonObject schema)
        {
            throw new JsonException("스키마 루트가 객체가 아닙니다");
        }

        CleanSchema(schema);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schema)!;
    }

    private static void CleanSchema(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject schema:
                foreach (var key in schema.Select(p => p.Key).ToList())
                {
                    if (Rejected.Contains(key) || (key == "minItems" && !IsZeroOrOne(schema[key])))
                    {
                        schema.Remove(key);
                    }
                    else if (NameMaps.Contains(key) && schema[key] is JsonObject named)
                    {
                        foreach (var child in named)
                        {
                            CleanSchema(child.Value);
                        }
                    }
                    else if (!DataKeywords.Contains(key))
                    {
                        CleanSchema(schema[key]);
                    }
                }

                break;
            case JsonArray schemas:
                foreach (var item in schemas)
                {
                    CleanSchema(item);
                }

                break;
        }
    }

    private static bool IsZeroOrOne(JsonNode? value)
        => value is JsonValue v && v.TryGetValue<double>(out var n) && n is 0 or 1;
}
