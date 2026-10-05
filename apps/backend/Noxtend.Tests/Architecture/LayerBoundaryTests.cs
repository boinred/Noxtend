using System.Reflection;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Prompt;

namespace Noxtend.Tests.Architecture;

/// <summary>
/// Design Ref: §2.4 · §8.2 G-a — 계층 방향을 문서가 아니라 테스트가 지킨다.
///
/// **왜 필요한가.** 프로젝트 참조가 이미 경계를 강제하지만, 참조를 **추가하는 것**은
/// 한 줄이고 리뷰에서 눈에 잘 안 띈다. `Noxtend.Application` 에서 프롬프트 저장소를
/// 직접 쓰고 싶은 순간이 반드시 오는데, 그때 참조를 더하면 컴파일은 되고 설계만 무너진다.
///
/// 사이클 #4 에서 반복 확인된 교훈이다: **문서에만 있는 규칙은 샌다.**
/// </summary>
public sealed class LayerBoundaryTests
{
    private static Assembly Domain => typeof(TaskKind).Assembly;
    private static Assembly Application => typeof(Noxtend.Application.Pipeline.RunTaskHandler).Assembly;
    private static Assembly TuningDomain => typeof(Tuning.Domain.Prompt.PromptVersion).Assembly;

    [Fact]
    public void Domain_ReferencesNothingButLoggingAbstractions()
    {
        var referenced = Domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Noxtend") || n.StartsWith("Microsoft.EntityFrameworkCore")
                        || n.StartsWith("StackExchange") || n.StartsWith("Azure"))
            .ToArray();

        Assert.Empty(referenced);
    }

    [Fact]
    public void Pipeline_DoesNotReferenceTuning()
    {
        // 이것이 Option B 의 핵심 계약이다 (§2.4). 파이프라인은 튜닝을 Port 로만 만난다
        foreach (var assembly in new[] { Domain, Application })
        {
            var offending = assembly.GetReferencedAssemblies()
                .Select(a => a.Name!)
                .Where(n => n.StartsWith("Noxtend.Tuning"))
                .ToArray();

            Assert.Empty(offending);
        }
    }

    [Fact]
    public void TuningDomain_KnowsPipelineButNotItsApplication()
    {
        var names = TuningDomain.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        // 방향은 튜닝 → 파이프라인이다. TaskKind 를 알아야 단계별 프롬프트가 성립한다
        Assert.Contains("Noxtend.Domain", names);

        // 유스케이스끼리는 서로 모른다
        Assert.DoesNotContain("Noxtend.Application", names);
    }

    /// <summary>
    /// G-2 (사이클 #7 §8.2) — **이미지 어댑터는 저장소를 모른다.**
    ///
    /// Option A 는 어댑터가 Blob 저장을 하게 두는 안이었고, 그러면 어댑터가 단계를 알게
    /// 되어 G-1 이 깨진다 (§2.0). 그 안으로 되돌아가는 것을 문서가 아니라 타입으로 막는다 —
    /// 되돌리는 변경은 생성자에 <c>IBlobStorage</c> 한 줄을 더하는 것이라 리뷰에서 눈에 안 띈다.
    /// </summary>
    [Fact]
    public void ImageProviders_DoNotDependOnBlobStorage()
    {
        var infrastructure = typeof(Noxtend.Infrastructure.Image.OpenAiImageProvider).Assembly;

        var offenders = infrastructure.GetTypes()
            .Where(t => typeof(Noxtend.Domain.Ports.IImageProvider).IsAssignableFrom(t)
                        && t is { IsInterface: false, IsAbstract: false })
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Any(p => typeof(Noxtend.Domain.Ports.IBlobStorage).IsAssignableFrom(p.ParameterType)))
            .Select(t => t.Name)
            .ToArray();

        Assert.Empty(offenders);
    }
}

/// <summary>
/// Design Ref: §3.4 · §8.2 G-b — 프롬프트 변수 규칙.
///
/// 시드 프롬프트가 허용되지 않은 변수를 쓰면 그 단계의 모든 실행이 실패한다.
/// 마이그레이션은 검증을 거치지 않고 직접 INSERT 하므로 여기서 잡아야 한다.
/// </summary>
public sealed class PromptVariableTests
{
    [Theory]
    [InlineData(LlmOperationKind.Analyze)]
    [InlineData(LlmOperationKind.Extract)]
    [InlineData(LlmOperationKind.Decompose)]
    public void AllowedVariables_AreDefinedForEveryStage(LlmOperationKind kind)
    {
        // 정의되지 않은 단계는 빈 집합을 받는데, 그러면 모든 변수가 거부된다.
        // 단계를 추가하고 여기를 빠뜨리는 것을 막는다
        var allowed = PromptTemplate.AllowedVariables(kind);
        Assert.NotNull(allowed);
    }

    [Fact]
    public void LaterStages_SeeEarlierStageOutputs()
    {
        // 뒤 단계일수록 앞 공정의 산출물이 쌓인다 (§3.4)
        Assert.Empty(PromptTemplate.AllowedVariables(LlmOperationKind.Analyze));
        Assert.Contains("scene", PromptTemplate.AllowedVariables(LlmOperationKind.Extract));
        Assert.Contains("scene", PromptTemplate.AllowedVariables(LlmOperationKind.Decompose));
        Assert.Contains("parts", PromptTemplate.AllowedVariables(LlmOperationKind.Decompose));
    }

    [Fact]
    public void Render_SubstitutesKnownVariables()
    {
        var result = PromptTemplate.Render("장면: {{scene}}", new Dictionary<string, string>
        {
            ["scene"] = "해질녘 항구",
        });

        Assert.Equal("장면: 해질녘 항구", result);
    }

    [Fact]
    public void Render_RejectsUnsubstitutedVariable()
    {
        // 값을 안 준 변수를 그대로 내보내면 공급자가 리터럴 중괄호를 읽는다
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PromptTemplate.Render("장면: {{scene}}", new Dictionary<string, string>()));

        Assert.Contains("scene", ex.Message);
    }

    [Fact]
    public void FindUnknownVariable_RejectsTypo()
    {
        // {{scen}} 같은 오타. 저장 시점에 막지 않으면 활성화 후 전부 실패한다
        var reason = PromptTemplate.FindUnknownVariable("장면: {{scen}}", LlmOperationKind.Extract);

        Assert.NotNull(reason);
        Assert.Contains("scen", reason);
    }

    [Fact]
    public void FindUnknownVariable_RejectsVariableFromLaterStage()
    {
        // 추출은 파츠를 아직 모른다 — 그 변수를 쓰면 순서가 뒤집힌 프롬프트다
        Assert.NotNull(PromptTemplate.FindUnknownVariable("{{parts}}", LlmOperationKind.Extract));
        Assert.Null(PromptTemplate.FindUnknownVariable("{{parts}}", LlmOperationKind.Decompose));
    }

    [Fact]
    public void FindUnknownVariable_AcceptsPlainText()
    {
        Assert.Null(PromptTemplate.FindUnknownVariable("변수가 없는 프롬프트", LlmOperationKind.Analyze));
    }
}
