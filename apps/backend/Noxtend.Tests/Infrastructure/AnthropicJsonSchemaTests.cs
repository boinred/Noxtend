using System.Text.Json;
using System.Text.Json.Nodes;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// Anthropic 구조화 출력이 거절하는 제약 키워드를 전송 전에 지우는지 확인한다.
/// 지운 제약은 각 공정 파서가 검증한다.
/// </summary>
public sealed class AnthropicJsonSchemaTests
{
    private static readonly string[] Stripped =
    [
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "minLength", "maxLength", "maxItems",
    ];

    [Fact]
    public void SeedSpriteAnalyzeSchema_RemovesRejectedKeywordsAndKeepsStructure()
    {
        var result = Convert(SeedPrompts.AnalyzeSprites().Schema);

        foreach (var keyword in Stripped)
        {
            Assert.DoesNotContain($"\"{keyword}\"", result.ToJsonString());
        }

        var assets = result["properties"]!["assets"]!;
        Assert.Equal(1, assets["minItems"]!.GetValue<int>());
        Assert.Equal("array", assets["type"]!.GetValue<string>());
        Assert.False(result["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(["view", "outputKind", "assets"], result["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(["sideView", "topDown", "isometric"], result["properties"]!["view"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal([4, 8], assets["items"]!["properties"]!["frameCount"]!["enum"]!.AsArray().Select(n => n!.GetValue<int>()));
    }

    [Fact]
    public void PropertyNamesLikeKeywords_AreKept_AndTheirSubschemasAreStripped()
    {
        var result = Convert("""
            {
              "type":"object",
              "properties":{
                "minimum":{"type":"number","maximum":5},
                "maxLength":{"type":"string","maxLength":9,"description":"keep"}
              },
              "required":["minimum","maxLength"]
            }
            """);

        var properties = result["properties"]!.AsObject();
        Assert.Equal(["minimum", "maxLength"], properties.Select(p => p.Key));
        Assert.Equal("number", properties["minimum"]!["type"]!.GetValue<string>());
        Assert.Null(properties["minimum"]!["maximum"]);
        Assert.Null(properties["maxLength"]!["maxLength"]);
        Assert.Equal("keep", properties["maxLength"]!["description"]!.GetValue<string>());
        Assert.Equal(["minimum", "maxLength"], result["required"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void NestedKeywords_AreStrippedInItemsAnyOfAndDefs()
    {
        var result = Convert("""
            {
              "type":"object",
              "properties":{
                "list":{"type":"array","minItems":0,"items":{"type":"array","minItems":3,"items":{"type":"number","minimum":0}}},
                "choice":{"anyOf":[{"type":"number","exclusiveMinimum":0},{"type":"string","minLength":1}]},
                "ref":{"$ref":"#/$defs/point"}
              },
              "$defs":{
                "point":{"type":"number","exclusiveMinimum":0,"multipleOf":2},
                "minimum":{"type":"number","exclusiveMaximum":1}
              }
            }
            """);

        var list = result["properties"]!["list"]!;
        Assert.Equal(0, list["minItems"]!.GetValue<int>());
        Assert.Null(list["items"]!["minItems"]);
        Assert.Null(list["items"]!["items"]!["minimum"]);

        var choice = result["properties"]!["choice"]!["anyOf"]!.AsArray();
        Assert.Null(choice[0]!["exclusiveMinimum"]);
        Assert.Null(choice[1]!["minLength"]);

        var defs = result["$defs"]!.AsObject();
        Assert.Null(defs["point"]!["exclusiveMinimum"]);
        Assert.Null(defs["point"]!["multipleOf"]);
        Assert.Equal("number", defs["minimum"]!["type"]!.GetValue<string>());
        Assert.Null(defs["minimum"]!["exclusiveMaximum"]);
        Assert.Equal("#/$defs/point", result["properties"]!["ref"]!["$ref"]!.GetValue<string>());
    }

    [Fact]
    public void SchemaWithoutRejectedKeywords_IsUnchanged()
    {
        const string schema = """
            {"type":"object","additionalProperties":false,"required":["a"],"properties":{"a":{"type":"array","minItems":1,"items":{"type":"string","enum":["x","y"],"format":"date"}}}}
            """;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(schema), Convert(schema)));
    }

    [Fact]
    public void DataValuesWithKeywordNamedKeys_AreLeftUnchanged()
    {
        const string schema = """
            {"type":"object","properties":{
              "a":{"type":"object","default":{"minimum":1,"maxItems":2}},
              "b":{"type":"object","enum":[{"maximum":3}]},
              "c":{"type":"object","const":{"minLength":4}}
            }}
            """;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(schema), Convert(schema)));
    }

    private static JsonNode Convert(string schema)
        => JsonSerializer.SerializeToNode(AnthropicJsonSchema.ForStructuredOutput(schema))!;
}
