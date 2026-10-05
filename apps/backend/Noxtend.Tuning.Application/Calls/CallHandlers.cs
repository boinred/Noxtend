using Noxtend.Tuning.Domain.Call;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Tuning.Application.Calls;

/// <summary>
/// 작업의 LLM 호출 이력 (FR-06).
///
/// Design Ref: §4.2 #24 — 성공·실패 모두. 실패가 특히 중요하다.
/// </summary>
public sealed class ListJobCallsHandler(ILlmCallRepository calls, IModelPriceRepository prices)
{
    /// <summary>
    /// 이력과 단가표를 함께 돌려준다.
    ///
    /// 비용은 모델·시각에 걸리는 단가 행을 찾아야 나오는데, 호출마다 DB 를 왕복하면
    /// 목록 한 번에 수십 번을 오간다. 단가표를 한 벌 읽어 목록 전체에 재사용한다.
    /// </summary>
    public async Task<(IReadOnlyList<LlmCall> Calls, ModelPriceBook Prices)> HandleAsync(
        Guid jobId, CancellationToken ct)
        => (await calls.ListByJobAsync(jobId, ct), await prices.GetBookAsync(ct));
}

/// <summary>
/// 내역 규모.
///
/// Design Ref: §2.3-8 — **보존 정책을 만들지 않는 대신 커지는 것을 보이게 한다.**
///
/// 지금 규모(수동 실행 수십 건)에서 정리 로직은 검증되지 않을 코드다. 다만 응답 전문을
/// 저장하므로 언젠가는 문제가 된다. 그때를 알아챌 수 있게 숫자만 노출한다.
/// </summary>
public sealed class GetCallStatsHandler(ILlmCallRepository calls)
{
    public Task<LlmCallStats> HandleAsync(CancellationToken ct)
        => calls.GetStatsAsync(ct);
}
