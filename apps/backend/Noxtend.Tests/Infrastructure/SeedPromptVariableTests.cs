using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Prompt;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 시드 프롬프트의 자리표시자와 단계가 만드는 변수가 맞물리는지 (occludedby-recompute).
///
/// **이 검사가 없어서 결함이 조용히 남았다.** 서술 재작성 시드에 <c>{{targets}}</c> 가
/// 없었는데, <see cref="PromptTemplate.Render"/> 는 끼울 자리가 없는 변수를 그냥 버린다.
/// 모델은 어느 파츠를 다시 써야 하는지 모른 채 답했고, 테스트는 전부 통과했다 —
/// FakeLlmProvider 가 "아는 파츠 전부" 를 돌려주기 때문이다.
/// </summary>
public sealed class SeedPromptVariableTests
{
    private const string IsometricTiles =
        "isometric with tiles: each repeating cell is a 2:1 diamond (width = 2 x height) and neighbouring cells";

    private static readonly Regex Placeholder = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    [Fact]
    public void SpriteGenerateV2UsesExistingVariablesAndRoleRules()
    {
        var prompt = SeedPrompts.GenerateSpriteV2();
        var used = Placeholder.Matches(prompt.System + prompt.User).Select(m => m.Groups[1].Value);
        Assert.Equal(PromptTemplate.AllowedVariables(LlmOperationKind.GenerateSprite).Order(), used.Order());
        Assert.Equal(SeedPrompts.GenerateSprite().User, prompt.User);
        foreach (var role in new[] { "tile", "backdrop", "object", "layer" })
            Assert.Contains($"For role {role}:", prompt.System);
        Assert.Contains("without clipping", prompt.System);
        Assert.Contains("opaque distant background", prompt.System);
        Assert.True(prompt.Note.Length <= 500);
    }

    [Fact]
    public void SpriteAnalyzeV4PreservesV3ContractWithSupportedUnion()
    {
        var v3 = SeedPrompts.AnalyzeSpritesV3();
        var v4 = SeedPrompts.AnalyzeSpritesV4();
        Assert.Equal(v3.System, v4.System);
        Assert.Equal(v3.User, v4.User);
        Assert.Equal(v3.Schema.Replace("\"oneOf\"", "\"anyOf\""), v4.Schema);
        Assert.True(v4.Note.Length <= 500);
        using var document = JsonDocument.Parse(v4.Schema);
        foreach (var (_, node) in ObjectsIn(document.RootElement, "$"))
        {
            Assert.False(node.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(node.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(),
                node.GetProperty("required").EnumerateArray().Select(p => p.GetString()).Order());
        }
        Assert.DoesNotContain("\"oneOf\"", v4.Schema);
        Assert.Equal(2, document.RootElement.GetProperty("properties").GetProperty("grid").GetProperty("anyOf").GetArrayLength());
    }

    [Fact]
    public void SpriteAnalyzeV5KeepsV4VariablesAndAddsNullablePlacements()
    {
        var v4 = SeedPrompts.AnalyzeSpritesV4();
        var v5 = SeedPrompts.AnalyzeSpritesV5();
        var used = Placeholder.Matches(v5.System + v5.User).Select(m => m.Groups[1].Value);
        Assert.Equal(PromptTemplate.AllowedVariables(LlmOperationKind.AnalyzeSprites).Order(), used.Order());
        Assert.Equal(v4.User, v5.User);
        Assert.Equal("이미지 기반 2D 배경 역할·격자·자리 분석 v5", v5.Note);

        using var document = JsonDocument.Parse(v5.Schema);
        foreach (var (_, node) in ObjectsIn(document.RootElement, "$"))
        {
            Assert.False(node.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(node.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(),
                node.GetProperty("required").EnumerateArray().Select(p => p.GetString()).Order());
        }
        Assert.DoesNotContain("\"oneOf\"", v5.Schema);
        var asset = document.RootElement.GetProperty("properties").GetProperty("assets").GetProperty("items");
        Assert.Contains("placements", asset.GetProperty("required").EnumerateArray().Select(x => x.GetString()));

        // 자리 외 V4 계약은 그대로
        var withoutPlacements = JsonNode.Parse(v5.Schema)!;
        var assetNode = withoutPlacements["properties"]!["assets"]!["items"]!;
        assetNode["properties"]!.AsObject().Remove("placements");
        var required = assetNode["required"]!.AsArray();
        required.Remove(required.Single(x => x!.GetValue<string>() == "placements"));
        Assert.Equal(v4.Schema, withoutPlacements.ToJsonString());

        var union = asset.GetProperty("properties").GetProperty("placements").GetProperty("anyOf");
        Assert.Equal(2, union.GetArrayLength());
        Assert.Equal("null", union[0].GetProperty("type").GetString());
        var list = union[1];
        Assert.Equal("array", list.GetProperty("type").GetString());
        Assert.Equal(1, list.GetProperty("minItems").GetInt32());
        Assert.Equal(20, list.GetProperty("maxItems").GetInt32());
        Assert.Equal(asset.GetProperty("properties").GetProperty("sourceBounds").GetRawText(), list.GetProperty("items").GetRawText());
    }

    [Fact]
    public void SpriteAnalyzeV5MovesWallsToObjectsAndAsksForEveryPlacement()
    {
        var v4 = SeedPrompts.AnalyzeSpritesV4().System;
        var v5 = SeedPrompts.AnalyzeSpritesV5().System;
        Assert.Contains("Every asset requires role.", v5);
        Assert.Contains("cellWidth", v5);
        Assert.Contains("A roof is not a tile", v5);
        Assert.DoesNotContain("walls, water, roads", v5);
        Assert.DoesNotContain("Use one representative instance", v5);
        Assert.Contains("(ground, water, roads, sidewalks)", v5);
        Assert.Contains("standing wall, fence, building facade, roof or awning is an object", v5);
        Assert.Contains("every position where the object appears in placements (at most 20)", v5);
        Assert.Contains("sourceBounds is the clearest one", v5);
        Assert.Contains("1024px", v5);
        Assert.Contains("one repeating segment as the object and list the segments in placements", v5);
        Assert.NotEqual(v4, v5);
    }

    [Fact]
    public void SpriteAnalyzeV3UsesAllowedVariablesAndRequiresRoleAndGrid()
    {
        var prompt = SeedPrompts.AnalyzeSpritesV3();
        var used = Placeholder.Matches(prompt.System + prompt.User).Select(m => m.Groups[1].Value);
        Assert.Equal(PromptTemplate.AllowedVariables(LlmOperationKind.AnalyzeSprites).Order(), used.Order());
        using var document = JsonDocument.Parse(prompt.Schema);
        Assert.Contains("grid", document.RootElement.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("role", document.RootElement.GetProperty("properties").GetProperty("assets").GetProperty("items")
            .GetProperty("required").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void SpriteAnalyzeV2UsesTheSameVariablesAsV1AndTeachesNormalizedBounds()
    {
        var v1 = SeedPrompts.AnalyzeSprites();
        var v2 = SeedPrompts.AnalyzeSpritesV2();
        var used = Placeholder.Matches(v2.System + v2.User).Select(m => m.Groups[1].Value).ToHashSet();

        Assert.Equal(PromptTemplate.AllowedVariables(LlmOperationKind.AnalyzeSprites).OrderBy(v => v), used.OrderBy(v => v));
        Assert.Equal(v1.User, v2.User);
        Assert.Contains("0~1", v2.System);
        Assert.Contains("{x:0.5,y:0.5,w:0.5,h:0.5}", v2.System);

        Assert.True(v2.Note.Length <= 500);

        using var document = JsonDocument.Parse(v2.Schema);
        foreach (var (_, node) in ObjectsIn(document.RootElement, "$"))
        {
            Assert.Equal(
                node.GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(n => n),
                node.GetProperty("required").EnumerateArray().Select(v => v.GetString()!).OrderBy(n => n));
        }

        var bounds = document.RootElement.GetProperty("properties").GetProperty("assets")
            .GetProperty("items").GetProperty("properties").GetProperty("sourceBounds");
        Assert.Contains("0~1", bounds.GetProperty("description").GetString());
        foreach (var axis in new[] { "x", "y", "w", "h" })
        {
            Assert.Contains("0~1", bounds.GetProperty("properties").GetProperty(axis).GetProperty("description").GetString());
        }
    }

    [Fact]
    public void SpriteSourceSeedV1IsASubsetAndV2MatchesAllowedVariables()
    {
        var allowed = PromptTemplate.AllowedVariables(LlmOperationKind.GenerateSpriteSource);
        var (v1System, v1User, _, _) = SeedPrompts.GenerateSpriteSource();
        var v1 = Placeholder.Matches(v1System + v1User).Select(m => m.Groups[1].Value).ToHashSet();
        var v2Seed = SeedPrompts.GenerateSpriteSourceV2();
        var v2 = Placeholder.Matches(v2Seed.System + v2Seed.User).Select(m => m.Groups[1].Value).ToHashSet();

        Assert.Equal(new[] { "prompt" }, v1.OrderBy(v => v));
        Assert.True(v1.IsSubsetOf(allowed));
        Assert.Equal(allowed.OrderBy(v => v), v2.OrderBy(v => v));
        Assert.Contains("authoritative", v2Seed.System);
        Assert.Contains(IsometricTiles, v2Seed.System);
        Assert.True(v2Seed.Note.Length <= 500);
    }

    [Fact]
    public void EnhanceSeedUsesDescriptionAndSettingsAndReturnsOnlyPrompt()
    {
        var (system, user, schema, note) = SeedPrompts.EnhanceSpriteSource();
        var used = Placeholder.Matches(system + user).Select(m => m.Groups[1].Value).ToHashSet();

        Assert.Equal(PromptTemplate.AllowedVariables(LlmOperationKind.EnhanceSpriteSource).OrderBy(v => v), used.OrderBy(v => v));
        Assert.Contains("2:1 diamond", system);
        Assert.Contains(IsometricTiles, system);
        Assert.Contains("The generated prompt must state the view, projection and layer or tile conditions explicitly", system);
        Assert.Contains("The generated prompt must forbid text, logos, UI", system);
        Assert.Contains("tile", system);
        Assert.True(note.Length <= 500);
        using var document = JsonDocument.Parse(schema);
        Assert.Equal(new[] { "prompt" }, document.RootElement.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(new[] { "prompt" }, document.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    /// <summary>
    /// 단계가 만드는 변수는 전부 프롬프트 어딘가에 쓰여야 한다.
    ///
    /// 렌더가 남는 변수를 예외로 잡아주지 못하는 방향(자리표시자 부재)을 여기서 막는다.
    /// </summary>
    [Theory]
    [InlineData(TaskKind.RewriteDescriptions)]
    [InlineData(TaskKind.AnalyzeSprites)]
    [InlineData(TaskKind.GenerateSprite)]
    [InlineData(TaskKind.EnhanceSpriteSource)]
    public void SeedUsesEveryVariableTheStageProduces(TaskKind kind)
    {
        var (system, user, _, _) = PromptFor(kind);
        var used = Placeholder.Matches(system + user).Select(m => m.Groups[1].Value).ToHashSet();

        Assert.Equal(PromptTemplate.AllowedVariables(LlmOperation.FromTask(kind)).OrderBy(v => v), used.OrderBy(v => v));
    }

    /// <summary>
    /// 구조화 출력 <c>strict = true</c>(<c>OpenAiProvider.cs:40</c>)는 <c>properties</c> 의
    /// **모든** 키가 <c>required</c> 에 있어야 한다. 빠지면 공급자가 400 을 내고, 그 실패는
    /// 재시도 대상이 아니라 작업 전체를 죽인다 — 실 API 에서 겪었다(2026-09-01).
    ///
    /// 선택값은 `required` 에 넣되 타입을 nullable 로 둔다.
    /// </summary>
    [Theory]
    [InlineData(TaskKind.RewriteDescriptions)]
    [InlineData(TaskKind.AnalyzeSprites)]
    [InlineData(TaskKind.GenerateSprite)]
    [InlineData(TaskKind.EnhanceSpriteSource)]
    [InlineData(TaskKind.Analyze)]
    [InlineData(TaskKind.Extract)]
    [InlineData(TaskKind.Decompose)]
    [InlineData(TaskKind.Generate)]
    public void SchemaListsEveryPropertyAsRequired(TaskKind kind)
    {
        var (_, _, schema, _) = PromptFor(kind);
        using var document = JsonDocument.Parse(schema);

        foreach (var (path, node) in ObjectsIn(document.RootElement, "$"))
        {
            var properties = node.GetProperty("properties").EnumerateObject()
                .Select(p => p.Name).OrderBy(name => name);
            var required = node.TryGetProperty("required", out var r)
                ? r.EnumerateArray().Select(v => v.GetString()!).OrderBy(name => name)
                : Enumerable.Empty<string>().OrderBy(name => name);

            Assert.Equal(properties, required);
        }
    }

    /// <summary>스키마 안의 모든 object 노드 — 중첩된 items 안까지 훑는다.</summary>
    private static IEnumerable<(string Path, JsonElement Node)> ObjectsIn(JsonElement node, string path)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
                foreach (var found in ObjectsIn(child, path + "[]"))
                    yield return found;
            yield break;
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (node.TryGetProperty("properties", out _))
        {
            yield return (path, node);
        }

        foreach (var child in node.EnumerateObject())
        {
            foreach (var found in ObjectsIn(child.Value, $"{path}.{child.Name}"))
            {
                yield return found;
            }
        }
    }

    /// <summary>
    /// 노트는 <c>PromptVersions.Note</c> 열(500자)에 들어가야 한다.
    ///
    /// **넘치면 마이그레이션이 죽고 API 가 아예 안 뜬다.** 실제로 겪었다 — 경위를 길게
    /// 적었더니 "String or binary data would be truncated" 로 기동이 멈췄다. 자세한 경위는
    /// 마이그레이션 주석과 커밋 메시지에 적고, 노트는 한 줄 요약으로 둔다.
    /// </summary>
    [Theory]
    [InlineData(TaskKind.RewriteDescriptions)]
    [InlineData(TaskKind.AnalyzeSprites)]
    [InlineData(TaskKind.GenerateSprite)]
    [InlineData(TaskKind.EnhanceSpriteSource)]
    [InlineData(TaskKind.Analyze)]
    [InlineData(TaskKind.Extract)]
    [InlineData(TaskKind.Decompose)]
    [InlineData(TaskKind.Generate)]
    public void NoteFitsTheColumn(TaskKind kind)
    {
        var (_, _, _, note) = PromptFor(kind);

        Assert.True(note.Length <= 500, $"{kind} 노트가 {note.Length}자다 (상한 500)");
    }

    private static (string System, string User, string Schema, string Note) PromptFor(TaskKind kind)
        => kind switch
        {
            TaskKind.GenerateSprite => SeedPrompts.GenerateSprite(),
            TaskKind.AnalyzeSprites => SeedPrompts.AnalyzeSprites(),
            TaskKind.EnhanceSpriteSource => SeedPrompts.EnhanceSpriteSource(),
            TaskKind.RewriteDescriptions => SeedPrompts.RewriteDescriptions(),
            TaskKind.Analyze => SeedPrompts.AnalyzeV2(),
            TaskKind.Extract => SeedPrompts.CharacterExtract(),
            TaskKind.Decompose => SeedPrompts.CharacterDecompose(),
            TaskKind.Generate => SeedPrompts.CharacterGenerate(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
