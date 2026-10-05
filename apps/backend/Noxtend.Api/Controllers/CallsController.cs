using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Tuning.Application.Calls;

namespace Noxtend.Api.Controllers;

/// <summary>
/// Design Ref: §4.2 #24·#26 — LLM 호출 내역.
///
/// **성공만이 아니라 실패도 여기 있다.** 사이클 #4 에서 공정이 실패했을 때 남는 것이
/// 코드뿐이라 원인을 알 방법이 없었다. 로그는 흘러가지만 이 표는 남는다.
/// </summary>
[ApiController]
[Route("api")]
public sealed class CallsController(
    ListJobCallsHandler listByJob,
    GetCallStatsHandler stats) : ControllerBase
{
    [HttpGet("jobs/{jobId:guid}/calls")]
    public async Task<IActionResult> ListByJobAsync(Guid jobId, CancellationToken ct)
    {
        var (calls, prices) = await listByJob.HandleAsync(jobId, ct);

        return Ok(ApiResponse<IReadOnlyList<LlmCallResponse>>.Ok(
            calls.Select(c => LlmCallResponse.From(c, prices)).ToList()));
    }

    /// <summary>Design Ref: §2.3-8 — 보존 정책을 만들지 않는 대신 규모를 보이게 한다.</summary>
    [HttpGet("calls/stats")]
    public async Task<IActionResult> GetStatsAsync(CancellationToken ct)
    {
        var result = await stats.HandleAsync(ct);

        return Ok(ApiResponse<CallStatsResponse>.Ok(
            new CallStatsResponse(
                result.Count, result.ApproximateBytes, result.TotalCostUsd, result.UnpricedCalls)));
    }
}
