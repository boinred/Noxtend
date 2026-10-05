using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Tuning.Domain.Call;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// 파이프라인의 <see cref="IPromptCatalog"/> 를 튜닝 저장소로 구현한다.
///
/// Design Ref: §2.1 · §2.4 — **여기가 두 컨텍스트가 만나는 유일한 지점이다.**
///
/// Infrastructure 는 양쪽을 다 참조하므로 이런 연결이 가능하다. 파이프라인 쪽에서
/// 튜닝 저장소를 직접 주입받았다면 프로젝트 참조가 필요했을 것이고, 그 순간 §2.4 가
/// 깨진다.
/// </summary>
internal sealed class TuningPromptCatalog(IPromptVersionRepository prompts) : IPromptCatalog
{
    /// <summary>
    /// [목적] 실행·접수·격자가 "지금 쓸 프롬프트"를 물을 때 전용→기본 폴백을 실제로 적용해 답합니다.
    /// [핵심 동작] 먼저 그 카테고리 전용 활성을 찾고, 없을 때만 기본(null) 슬롯을 한 번 더 조회합니다.
    /// [반환] 실행에 쓸 프롬프트 스냅숏, 전용·기본 둘 다 없으면 <c>null</c>.
    /// </summary>
    // 폴백 규칙이 사는 유일한 자리 (Design §3.1). 전용 → 없으면 기본(null)
    public async Task<PromptSnapshot?> GetActiveAsync(LlmOperationKind kind, AssetCategory category, CancellationToken ct)
    {
        var active = await prompts.GetActiveAsync(kind, category, ct)
                     ?? await prompts.GetActiveAsync(kind, null, ct);

        return active is null
            ? null
            : new PromptSnapshot(
                active.Id, active.Version, active.System, active.User, active.JsonSchema);
    }
}

/// <summary>
/// 파이프라인의 <see cref="ILlmCallRecorder"/> 를 튜닝 저장소로 구현한다.
///
/// Design Ref: §3.3 — **던지지 않는 것이 계약이다.** 데코레이터가 예외를 삼키긴 하지만,
/// 규칙을 한 겹 더 지키는 편이 낫다 — 다른 호출자가 생겨도 같은 보장을 받는다.
/// </summary>
internal sealed class TuningLlmCallRecorder(ILlmCallRepository calls, IClock clock) : ILlmCallRecorder
{
    public async Task RecordAsync(LlmCallEntry entry, CancellationToken ct)
    {
        var context = entry.Context;

        var call = entry.Succeeded
            ? LlmCall.Success(
                context.JobId, context.TaskId, context.SimilarityEvaluationId, context.Kind, context.PromptVersionId,
                context.ProviderConfigId, context.Model,
                entry.RequestPayload, entry.ResponsePayload ?? string.Empty,
                entry.InputTokens, entry.OutputTokens, entry.LatencyMs, clock.Now,
                entry.OutputImages)
            : LlmCall.Failure(
                context.JobId, context.TaskId, context.SimilarityEvaluationId, context.Kind, context.PromptVersionId,
                context.ProviderConfigId, context.Model,
                entry.RequestPayload, entry.FailureReason ?? "알 수 없는 오류",
                entry.LatencyMs, clock.Now);

        await calls.AddAsync(call, ct);
        await calls.SaveChangesAsync(ct);
    }
}
