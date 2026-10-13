using System.Text.Json;
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
