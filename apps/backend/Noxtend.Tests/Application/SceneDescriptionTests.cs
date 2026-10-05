using Noxtend.Application.Stages;
using Noxtend.Domain.Job;

namespace Noxtend.Tests.Application;

/// <summary>
/// B-12 — 후속 공정이 받는 장면에 이름과 색이 **둘 다** 실린다 (FR-13 · Design §9).
///
/// 파츠 생성 프롬프트는 "장면의 팔레트를 유지하라" 고 지시한다. 이름만 가면 모델이 색을
/// 다시 상상하고, 색만 가면 그 색이 무엇의 색인지 모른다. 팔레트를 구조화한 이유의 절반이
/// 화면이고 나머지 절반이 여기다.
///
/// `StageJson.Describe` 를 직접 부르지 않고 `BuildVariables` 로 보는 이유는 그것이
/// 프롬프트가 실제로 받는 값이기 때문이다.
/// </summary>
public sealed class SceneDescriptionTests
{
    [Fact]
    public async Task PromptVariables_CarryBothNameAndHex()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        await fixture.RunFirstStageAsync(job);

        var scene = new ExtractStage().BuildVariables(job)["scene"];

        Assert.Contains("청회색 바다", scene, StringComparison.Ordinal);
        Assert.Contains("#2E5C6E", scene, StringComparison.Ordinal);
    }
}
