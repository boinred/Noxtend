using Noxtend.Application.Generation;
using Noxtend.Application.Job;
using Noxtend.Application.Stages;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Prompt;

namespace Noxtend.Tests.Application;

/// <summary>
/// character-studio slice 3 §D-03 · §4.2 — 성별·힌트가 프롬프트 변수로 흐른다.
///
/// **키를 항상 넣는다.** 힌트는 캐릭터에서도 선택이라 값이 없을 수 있는데, 조건부로 더하면
/// 시드의 <c>{{partHints}}</c> 가 치환되지 않아 Decompose 가 예외로 죽고 작업 전체가 실패한다.
/// 값이 없으면 빈 문자열이 아니라 "없음" 폴백이다.
/// </summary>
public sealed class PromptVariableInjectionTests
{
    // Extract·Decompose 는 gender·partHints 를 모두 싣는다 — 종류는 Extract, 개수는 Decompose 로 흐른다
    [Fact]
    public async Task ExtractAndDecompose_CarryGenderAndPartHints()
    {
        var fixture = new PipelineFixture();
        var job = await StartCharacterAsync(
            fixture, [new PartHint("팔찌", 3, null), new PartHint("장갑", 2, "손목형")]);
        await fixture.RunFirstStageAsync(job);   // Analyze 가 장면을 채운다

        var extract = new ExtractStage().BuildVariables(job);
        Assert.Equal("female", extract["gender"]);
        Assert.Equal("팔찌 3, 장갑(손목형) 2", extract["partHints"]);

        var decompose = new DecomposeStage().BuildVariables(job);
        Assert.Equal("female", decompose["gender"]);
        Assert.Equal("팔찌 3, 장갑(손목형) 2", decompose["partHints"]);
    }

    // 힌트가 없으면 "없음" 폴백 — 힌트 없는 캐릭터 작업이 Decompose 에서 죽지 않는다
    [Fact]
    public async Task NoHints_RendersAbsentFallback()
    {
        var fixture = new PipelineFixture();
        var job = await StartCharacterAsync(fixture, partHints: null);
        await fixture.RunFirstStageAsync(job);

        Assert.Equal("없음", new ExtractStage().BuildVariables(job)["partHints"]);
    }

    // 생성은 gender 만 싣고 partHints 는 넣지 않는다 — 파츠 하나만 그리므로 목록은 유인이 된다(R-2)
    [Fact]
    public async Task Generate_CarriesGenderButNotPartHints()
    {
        var fixture = new PipelineFixture();
        var job = await StartCharacterAsync(fixture, [new PartHint("팔찌", 3, null)]);
        await fixture.RunAllStagesAsync(job);   // Extract 가 파츠를 채운다

        var part = job.Parts.First();
        var generate = new GenerationStage(fixture.GenerationOptions)
            .BuildVariables(job, part, ViewDirection.Front);

        Assert.Equal("female", generate["gender"]);
        Assert.False(generate.ContainsKey("partHints"));
    }

    // AllowedVariables 가 새 변수를 허용해야 admin 편집 저장이 PromptUnknownVariable 로 거절되지 않는다
    [Fact]
    public void AllowedVariables_IncludeCharacterInputs()
    {
        Assert.Contains("gender", PromptTemplate.AllowedVariables(LlmOperationKind.Extract));
        Assert.Contains("partHints", PromptTemplate.AllowedVariables(LlmOperationKind.Extract));
        Assert.Contains("gender", PromptTemplate.AllowedVariables(LlmOperationKind.Decompose));
        Assert.Contains("partHints", PromptTemplate.AllowedVariables(LlmOperationKind.Decompose));

        // 생성은 gender 만 — partHints 는 §D-03 대로 제외
        Assert.Contains("gender", PromptTemplate.AllowedVariables(LlmOperationKind.Generate));
        Assert.DoesNotContain("partHints", PromptTemplate.AllowedVariables(LlmOperationKind.Generate));
    }

    // 캐릭터 작업 접수 — 성별 필수, 힌트는 선택
    private static async Task<PipelineJob> StartCharacterAsync(
        PipelineFixture fixture, IReadOnlyList<PartHint>? partHints)
    {
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Character, uploadId, providerId, StubModelCatalog.DefaultModel,
            CancellationToken.None, gender: Gender.Female, partHints: partHints);
        Assert.True(result.IsSuccess, result.ErrorCode);

        return result.Value!;
    }
}
