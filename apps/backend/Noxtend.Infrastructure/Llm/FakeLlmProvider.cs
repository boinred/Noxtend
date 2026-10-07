using System.Text.Json;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// 결정적 Fake 공급자.
///
/// Design Ref: R-3 · §8.1 — LLM 출력은 비결정적이라 실제 호출로는 파이프라인을 검증할 수
/// 없다. 이 Fake 가 성공·예외·형식 위반·취소를 재현하므로 유스케이스·오케스트레이터·
/// 스위퍼 전체가 **인프라 없이** 검증된다.
///
/// L2/L3 에서도 쓴다 (<c>Llm__UseFake=true</c>). 실제 공급자를 붙이면 E2E 가 비용과
/// 지연과 비결정성을 동시에 갖게 된다.
///
/// **사이클 #5: 단계별로 다른 JSON 을 낸다.** Port 가 일반화되어 어댑터가 원문 문자열을
/// 돌려주므로, Fake 도 각 단계의 스키마에 맞는 응답을 만들어야 한다. 어느 단계인지는
/// <see cref="LlmCallContext.Kind"/> 로 안다.
/// </summary>
public sealed class FakeLlmProvider : ILlmProvider
{
    private readonly Exception? _failure;
    private Func<Exception?>? _next;
    private readonly TimeSpan _delay;
    private readonly IReadOnlyList<string> _partNames;
    private readonly Func<LlmOperationKind, string>? _override;

    private FakeLlmProvider(
        Exception? failure,
        TimeSpan delay,
        IReadOnlyList<string> partNames,
        Func<LlmOperationKind, string>? responseOverride)
    {
        _failure = failure;
        _delay = delay;
        _partNames = partNames;
        _override = responseOverride;
    }

    private static readonly string[] DefaultParts = ["등대", "목조 부두", "어선", "가로등"];

    public static FakeLlmProvider Succeeding(params string[] partNames)
        => new(null, TimeSpan.Zero, partNames.Length > 0 ? partNames : DefaultParts, null);

    public static FakeLlmProvider Failing(Exception failure)
        => new(failure, TimeSpan.Zero, DefaultParts, null);

    /// <summary>
    /// 호출마다 던질지 말지를 정한다 — 재시도 검증용.
    ///
    /// `null` 을 돌려주면 그 호출은 성공한다. "처음엔 실패하고 다음엔 성공" 같은
    /// 일시적 실패를 재현하는 유일한 수단이다.
    /// </summary>
    public static FakeLlmProvider Throwing(Func<Exception?> next)
        => new(null, TimeSpan.Zero, DefaultParts, null) { _next = next };

    /// <summary>
    /// 단계별 응답을 직접 정한다 — 유효성 검사 테스트가 잘못된 응답을 주입할 때 쓴다.
    /// </summary>
    public static FakeLlmProvider Returning(Func<LlmOperationKind, string> responses)
        => new(null, TimeSpan.Zero, DefaultParts, responses);

    /// <summary>
    /// 취소를 검증하기 위한 지연. 실제 호출이 10분 걸릴 수 있다는 사실을
    /// 테스트에서 재현하는 유일한 수단이다 (§8.1).
    /// </summary>
    public FakeLlmProvider WithDelay(TimeSpan delay)
        => new(_failure, delay, _partNames, _override);

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, ct);
        }

        ct.ThrowIfCancellationRequested();

        if (_next?.Invoke() is { } transient)
        {
            throw transient;
        }

        if (_failure is not null)
        {
            throw _failure;
        }

        var json = _override is not null
            ? _override(request.Context.Kind)
            : Canned(request);

        return new LlmResult(json, InputTokens: 1200, OutputTokens: 340);
    }

    private static string SpriteJson(LlmRequest request)
    {
        var prompt = request.System + "\n" + request.User;
        var view = prompt.Contains("\"view\":\"topDown\"", StringComparison.Ordinal) ? "topDown"
            : prompt.Contains("\"view\":\"isometric\"", StringComparison.Ordinal) ? "isometric" : "sideView";
        var outputKind = prompt.Contains("\"outputKind\":\"tiles\"", StringComparison.Ordinal) ? "tiles" : "layers";
        return JsonSerializer.Serialize(new { view, outputKind, assets = new[] {
            new { name = "배경", order = 0, sourceBounds = new { x = 0, y = 0, w = 1, h = 1 },
                requiresTransparency = outputKind == "tiles" && view == "isometric",
                loop = false, frameCount = 8, fps = 8, motionNotes = "" } } });
    }

    /// <summary>
    /// 단계별 기본 응답.
    ///
    /// 파츠 참조가 추출 순서와 일관되어야 한다 — 분해는 Pxx 를 도메인 파츠에 연결하므로
    /// Fake 도 실제 모델 계약과 같은 참조 형식을 써야 정상 경로가 돈다.
    /// </summary>
    private string Canned(LlmRequest request) => request.Context.Kind switch
    {
        LlmOperationKind.AnalyzeSprites => SpriteJson(request),
        LlmOperationKind.Analyze => AnalyzeJson(),
        LlmOperationKind.Extract => JsonSerializer.Serialize(new { parts = _partNames }),
        LlmOperationKind.Decompose => DecomposeJson(),
        LlmOperationKind.RewriteDescriptions => RewriteJson(),
        LlmOperationKind.SimilarityEvaluate => SimilarityEvaluateJson,
        _ => "{}",
    };

    /// <summary>
    /// 유사도 평가 기본 응답 — 여섯 축 + 보정 제안 + 재생성 노트 (background-similarity-tuning §5.3~5.4).
    /// 정상 경로 회귀는 이 fake 만으로 돈다 (D-09) — 실 호출은 gated smoke 뿐이다.
    /// </summary>
    internal const string SimilarityEvaluateJson = """
        {
          "dimensions": [
            { "kind": "composition", "score": 72, "evidence": "주요 구조물 배치가 유사", "recommendation": "근경 좌측 이동" },
            { "kind": "camera", "score": 68, "evidence": "시점 높이 유사", "recommendation": "pitch 소폭 하향" },
            { "kind": "scale", "score": 65, "evidence": "근경이 과대", "recommendation": "장면 배율 축소" },
            { "kind": "shape", "score": 70, "evidence": "실루엣 일치", "recommendation": "유지" },
            { "kind": "material", "score": 60, "evidence": "색감 차이", "recommendation": "재생성 검토" },
            { "kind": "lighting", "score": 66, "evidence": "광원 방향 유사", "recommendation": "고도 하향" }
          ],
          "adjustments": [
            { "type": "scaleScene", "factor": 0.9, "confidence": 0.85, "reason": "근경 과대" },
            { "type": "adjustLight", "azimuthDeltaDegrees": 5, "elevationDeltaDegrees": -5, "intensityFactor": 1.0, "confidence": 0.6, "reason": "명암 완화" }
          ],
          "regenerationNotes": ["좌측 구조물 텍스처 톤이 원본보다 차갑다"]
        }
        """;

    /// <summary>추출 fixture 첫 파츠와 동일한 numeric scale 기준.</summary>
    private string AnalyzeJson()
        => DefaultAnalyzeJson.Replace(
            "\"object\": \"가로수\"",
            $"\"object\": {JsonSerializer.Serialize(_partNames[0])}",
            StringComparison.Ordinal);

    internal const string DefaultAnalyzeJson = """
        {
          "palette": [
            { "name": "청회색 바다", "hex": "#2E5C6E" },
            { "name": "주황색 목재", "hex": "#D97A3C" },
            { "name": "옅은 안개", "hex": "#F2E8DC" }
          ],
          "timeOfDay": "해질녘",
          "mood": "고요하고 서늘함",
          "renderingStyle": "수채 느낌의 반사실",
          "materialFeel": "거친 목재와 젖은 돌",
          "camera": { "type": "one-point", "eyeLevel": "지면에서 1.6m", "horizonY": 0.55 },
          "light": { "direction": "좌측 후방 15° 고도", "temperature": "따뜻함", "shadowHardness": "부드러움" },
          "scaleReference": { "object": "가로수", "realWorldSize": "높이 3m", "heightMeters": 3 }
        }
        """;

    /// <summary>
    /// 깊이는 1부터 겹치지 않게, 좌표는 화면 안에 들어가게 만든다 —
    /// 유효성 검사를 통과하는 것이 기본 동작이어야 한다.
    /// </summary>
    private string DecomposeJson()
    {
        var parts = _partNames.Select((name, index) => new
        {
            partRef = $"P{index + 1:D2}",
            category = "구조물",
            description = $"{name} — 낡은 표면, 해수에 색이 바램",
            // 첫 파츠만 배치를 셋 준다 — "전부 켜짐" 을 검증하려면 여럿인 것이 있어야 한다
            placements = index == 0
                ? new[]
                {
                    new { x = 0.05, y = 0.10, w = 0.12, h = 0.20 },
                    new { x = 0.30, y = 0.12, w = 0.10, h = 0.18 },
                    new { x = 0.55, y = 0.11, w = 0.11, h = 0.19 },
                }
                : [new { x = 0.05, y = 0.10 + (index * 0.02), w = 0.30, h = 0.20 }],
            depthOrder = index + 1,
            occludedBy = Array.Empty<string>(),
        });

        return JsonSerializer.Serialize(new { parts });
    }

    /// <summary>
    /// 서술 재작성 응답. 어느 파츠가 대상인지 Fake 는 모르므로 아는 파츠 전부를 낸다 —
    /// 도메인 검증은 "이 작업에 있는 이름인가" 만 보므로 정상 경로가 돈다.
    /// </summary>
    private string RewriteJson()
    {
        var parts = _partNames.Select(name => new
        {
            name,
            description = $"{name} — 가린 부분을 뺀 서술",
            category = "Fake",
        });

        return JsonSerializer.Serialize(new { parts });
    }
}
