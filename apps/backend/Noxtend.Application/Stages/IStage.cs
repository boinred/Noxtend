using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Stages;

/// <summary>
/// 한 단계가 아는 것 전부 — 무엇을 묻고, 응답을 어떻게 해석하고, 어디에 반영하는가.
///
/// Design Ref: §9.2 · G-1
///
/// **단계 지식이 여기 있는 이유.** Domain 에 두면 도메인이 JSON 스키마를 알게 되고,
/// Infrastructure 에 두면 어댑터가 단계를 알게 되어 G-1·G-2 가 함께 깨진다.
/// 프롬프트 조립과 응답 파싱은 도메인 규칙이 아니라 **공급자와의 대화 방식**이다.
///
/// 넷째 단계를 더하는 비용이 이 인터페이스 구현 하나인 것이 §2.3 주장의 핵심이다.
/// </summary>
public interface IStage
{
    TaskKind Kind { get; }

    /// <summary>
    /// 앞 공정의 결과가 담긴 <paramref name="job"/> 에서 프롬프트 변수를 만든다.
    ///
    /// 허용된 변수만 채워야 한다 (<see cref="Domain.Prompt.PromptTemplate.AllowedVariables"/>) —
    /// 남는 변수가 있으면 렌더가 예외를 던진다.
    /// </summary>
    IReadOnlyDictionary<string, string> BuildVariables(PipelineJob job);

    /// <summary>이 단계가 이미지를 보는가. 지금은 셋 다 보지만 계약으로 열어둔다.</summary>
    bool NeedsImage => true;

    /// <summary>
    /// 응답 원문을 해석·검증하고, **반영만 하는** 함수를 돌려준다.
    ///
    /// **두 단계로 나눈 것이 안전장치다.** 해석과 검증은 던질 수 있고(형식 위반·유효성
    /// 위반), 돌려주는 함수는 순수 대입이라 던지지 않는다. 호출자는 이렇게 쓴다:
    ///
    /// <code>
    /// var commit = stage.Interpret(job, rawJson);   // 여기서 실패하면 공정이 실패한다
    /// task.Succeed(now);                            // 성공 확정
    /// commit(job);                                  // 반영 — 던지지 않는다
    /// </code>
    ///
    /// 한 메서드였을 때 `Succeed` 뒤에 해석이 오면서, **파싱 실패가 이미 성공 처리된
    /// 공정을 실패시키지 못하고 조용히 넘어가는** 결함이 있었다. 장면 없는 작업이
    /// 성공으로 남았다. 순서로 지키던 규칙을 타입으로 옮긴다.
    /// </summary>
    Action<PipelineJob> Interpret(PipelineJob job, string rawJson);
}

/// <summary>Design Ref: §9.2 — 단계 구현을 <see cref="TaskKind"/> 로 찾는다.</summary>
public sealed class StageRegistry(IEnumerable<IStage> stages)
{
    private readonly IReadOnlyDictionary<TaskKind, IStage> _byKind =
        stages.ToDictionary(s => s.Kind);

    public IStage For(TaskKind kind)
        => _byKind.TryGetValue(kind, out var stage)
            ? stage
            : throw new ProviderCallFailedException($"처리기가 없는 단계입니다: {kind}");
}
