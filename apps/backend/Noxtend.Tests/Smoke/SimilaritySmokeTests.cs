using Noxtend.Application.Similarity;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Similarity;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Smoke;

/// <summary>
/// gated 실-공급자 smoke (background-similarity-tuning §15.2 · SC-09 · D-09).
///
/// **기본은 skip 이다.** 실행 조건이 모두 있어야 돈다:
///   RUN_SIMILARITY_SMOKE=1
///   SIMILARITY_SMOKE_PROVIDER (openai|anthropic) · SIMILARITY_SMOKE_MODEL · SIMILARITY_SMOKE_KEY
///   SIMILARITY_SMOKE_FIXTURES — fixture 디렉터리:
///     {name}/reference.png · {name}/good.png · {name}/degraded.png × 최소 5개
///
/// 검증하는 것은 절대 점수가 아니라 **상대 순위**다: 같은 공급자·모델·프롬프트로
/// good 와 degraded 를 각각 평가해 good.overall > degraded.overall 인 fixture 가
/// 4/5 이상이어야 한다. 호출은 최대 10회(5 fixture × 2)이고 실행 전후로 수를 출력한다.
/// </summary>
public sealed class SimilaritySmokeTests
{
    private const int MinFixtures = 5;
    private const int MaxCalls = 10;

    [Fact]
    public async Task GoodRenders_OutrankDegradedOnes()
    {
        var gate = Environment.GetEnvironmentVariable("RUN_SIMILARITY_SMOKE");
        var providerKind = Environment.GetEnvironmentVariable("SIMILARITY_SMOKE_PROVIDER");
        var model = Environment.GetEnvironmentVariable("SIMILARITY_SMOKE_MODEL");
        var apiKey = Environment.GetEnvironmentVariable("SIMILARITY_SMOKE_KEY");
        var fixtureRoot = Environment.GetEnvironmentVariable("SIMILARITY_SMOKE_FIXTURES");

        // CI·일반 회귀에서는 조용히 지나간다 (D-09) — 유료 호출은 명시적 opt-in 뒤에만.
        // xunit 2.x 에는 동적 skip 이 없어 조기 return 으로 표현한다
        if (gate != "1" || providerKind is null || model is null
            || apiKey is null || fixtureRoot is null)
        {
            Console.WriteLine("[smoke] skip — RUN_SIMILARITY_SMOKE=1 + provider/model/key/fixtures 필요");
            return;
        }

        var fixtures = Directory.GetDirectories(fixtureRoot)
            .Where(dir => File.Exists(Path.Combine(dir, "reference.png"))
                && File.Exists(Path.Combine(dir, "good.png"))
                && File.Exists(Path.Combine(dir, "degraded.png")))
            .Take(MaxCalls / 2)
            .ToList();
        if (fixtures.Count < MinFixtures)
        {
            Console.WriteLine($"[smoke] skip — fixture 가 {MinFixtures}개 이상 필요합니다");
            return;
        }

        ILlmProvider provider = providerKind.ToLowerInvariant() switch
        {
            "openai" => new OpenAiProvider(new HttpClient(), apiKey, model),
            "anthropic" => new AnthropicProvider(apiKey, model),
            _ => throw new ArgumentOutOfRangeException(nameof(providerKind), providerKind, null),
        };

        var (system, user, schema, _) = SeedPrompts.SimilarityEvaluateBackgroundV3();
        var calls = 0;
        Console.WriteLine($"[smoke] fixtures={fixtures.Count} 예상 호출={fixtures.Count * 2}/{MaxCalls}");

        var wins = 0;
        foreach (var dir in fixtures)
        {
            var reference = await File.ReadAllBytesAsync(Path.Combine(dir, "reference.png"));
            var good = await EvaluateAsync(provider, system, user, schema, reference,
                await File.ReadAllBytesAsync(Path.Combine(dir, "good.png")), model);
            var degraded = await EvaluateAsync(provider, system, user, schema, reference,
                await File.ReadAllBytesAsync(Path.Combine(dir, "degraded.png")), model);
            calls += 2;

            Console.WriteLine(
                $"[smoke] {Path.GetFileName(dir)}: good={good.Overall} degraded={degraded.Overall}");
            if (good.Overall > degraded.Overall)
            {
                wins++;
            }
        }

        Console.WriteLine($"[smoke] 실제 호출={calls} 순위 일치={wins}/{fixtures.Count}");

        // SC-09 — 상대 순위 4/5 이상
        Assert.True(
            wins * 5 >= fixtures.Count * 4,
            $"good > degraded 가 {wins}/{fixtures.Count} — 기준(4/5) 미달");
    }

    private static async Task<SimilarityScore> EvaluateAsync(
        ILlmProvider provider,
        string system, string user, string schema,
        byte[] reference, byte[] render, string model)
    {
        var result = await provider.CompleteAsync(
            new LlmRequest(
                LlmCallContext.ForSimilarityEvaluation(
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), model),
                system, user,
                [
                    new LlmImage("reference", new ImageContent(reference, "image/png")),
                    new LlmImage("render", new ImageContent(render, "image/png")),
                ],
                schema),
            CancellationToken.None);

        var parsed = SimilarityEvaluationParser.Parse(result.RawJson);
        return SimilarityScore.Create(parsed.Dimensions);
    }
}
