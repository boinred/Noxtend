using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Application.Similarity;
using Noxtend.Domain.Ports;
using Noxtend.Tuning.Application.Calls;
using Noxtend.Tuning.Domain.Call;

namespace Noxtend.Api.Controllers;

/// <summary>
/// 유사도 실행 창구 (§10).
///
/// Design Ref: background-similarity-tuning §10 · §13
///
/// 시작은 multipart — 기준 렌더가 요청에 실린다. **렌더 검증은 전부 서버가 다시 한다**
/// (§13): 형식·해상도·크기를 클라이언트에 맡기지 않는다.
/// </summary>
[ApiController]
[Route("api/jobs/{jobId:guid}")]
public sealed class SimilarityController(
    GetSimilarityStatusHandler status,
    GetSimilarityRunHandler runDetail,
    ListSimilarityRunsHandler runList,
    ListJobCallsHandler jobCalls,
    StartSimilarityRunHandler start,
    CreateSimilarityCandidateHandler createCandidate,
    UploadCandidateRenderHandler uploadRender,
    RetrySimilarityRunHandler retry,
    CancelSimilarityRunHandler cancel,
    CompleteSimilarityRunHandler complete,
    EstimateSimilarityCostHandler estimate,
    IClock clock) : ControllerBase
{
    /// <summary>보수적 비용 추정 (§11.2) — 단가 미등록이면 금액 대신 null 이다.</summary>
    [HttpGet("similarity/estimate")]
    public async Task<IActionResult> GetEstimateAsync(
        Guid jobId,
        [FromQuery] string model,
        [FromQuery] int maxIterations,
        CancellationToken ct)
    {
        _ = jobId;   // 경로 일관성 — 추정 자체는 작업 데이터를 읽지 않는다
        var view = await estimate.HandleAsync(model, maxIterations, clock.Now, ct);
        return new ObjectResult(ApiResponse<SimilarityCostEstimate>.Ok(view));
    }

    /// <summary>자격·활성 layout·진행 중 run — 버튼 상태의 근거 (§11.1).</summary>
    [HttpGet("similarity")]
    public async Task<IActionResult> GetStatusAsync(Guid jobId, CancellationToken ct)
        => ApiResults.From(
            await status.HandleAsync(jobId, ct), SimilarityStatusResponse.From);

    /// <summary>실행 이력 — 최신부터, 실제 비용 포함 (§10 · §7.3).</summary>
    [HttpGet("similarity-runs")]
    public async Task<IActionResult> ListRunsAsync(Guid jobId, CancellationToken ct)
    {
        var costs = await CostByEvaluationAsync(jobId, ct);
        return ApiResults.From(
            await runList.HandleAsync(jobId, ct),
            views => views.Select(view => SimilarityRunResponse.From(view, costs)).ToList());
    }

    [HttpGet("similarity-runs/{runId:guid}")]
    public async Task<IActionResult> GetRunAsync(Guid jobId, Guid runId, CancellationToken ct)
    {
        var costs = await CostByEvaluationAsync(jobId, ct);
        return ApiResults.From(
            await runDetail.HandleAsync(jobId, runId, ct),
            view => SimilarityRunResponse.From(view, costs));
    }

    /// <summary>
    /// 평가별 실제 비용 (§7.3) — LlmCall usage 와 단가표로 계산한다. 실패 호출도
    /// usage 가 기록됐다면 포함이다: 실제로 쓴 돈이기 때문이다.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, decimal>> CostByEvaluationAsync(
        Guid jobId, CancellationToken ct)
    {
        var (calls, prices) = await jobCalls.HandleAsync(jobId, ct);
        var costs = new Dictionary<Guid, decimal>();
        foreach (var call in calls)
        {
            if (call.SimilarityEvaluationId is not { } evaluationId)
            {
                continue;
            }

            var cost = prices.Estimate(
                call.Model, call.At, call.InputTokens, call.OutputTokens, call.OutputImages);
            if (cost is not null)
            {
                costs[evaluationId] = costs.GetValueOrDefault(evaluationId) + cost.Value;
            }
        }

        return costs;
    }

    /// <summary>기준 렌더와 함께 run 시작 — 202. Idempotency-Key 헤더가 필수다 (§10.1).</summary>
    [HttpPost("similarity-runs")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<IActionResult> StartAsync(
        Guid jobId,
        [FromForm] Guid providerConfigId,
        [FromForm] string model,
        [FromForm] int maxIterations,
        [FromForm] Guid layoutId,
        IFormFile? render,
        CancellationToken ct)
    {
        if (Request.Headers.TryGetValue("Idempotency-Key", out var key) is false
            || string.IsNullOrWhiteSpace(key))
        {
            return ApiResults.Failure<SimilarityRunResponse>(
                Domain.Common.ErrorCode.SimilarityConflict, "Idempotency-Key 헤더가 필요합니다");
        }

        if (render is null)
        {
            return ApiResults.Failure<SimilarityRunResponse>(
                Domain.Common.ErrorCode.SimilarityRenderInvalid, "기준 렌더가 없습니다");
        }

        using var buffer = new MemoryStream();
        await render.CopyToAsync(buffer, ct);

        var result = await start.HandleAsync(
            new StartSimilarityRunRequest(
                jobId, providerConfigId, model, maxIterations, layoutId,
                key.ToString(), buffer.ToArray(), render.ContentType),
            ct);

        return ApiResults.From(
            result,
            started => SimilarityRunResponse.From(started.Run, [started.BaselineEvaluation]),
            StatusCodes.Status202Accepted);   // 시작 시점에는 아직 호출이 없어 비용 0

    }

    /// <summary>선택 보정으로 후보 revision 생성 — id 만 받는다, 값은 서버 저장본 (§6).</summary>
    [HttpPost("similarity-runs/{runId:guid}/candidates")]
    public async Task<IActionResult> CreateCandidateAsync(
        Guid jobId, Guid runId, [FromBody] CreateCandidateBody body, CancellationToken ct)
        => ApiResults.From(
            await createCandidate.HandleAsync(
                new CreateCandidateRequest(jobId, runId, body.AdjustmentIds), ct),
            SimilarityCandidateResponse.From,
            StatusCodes.Status201Created);

    /// <summary>후보 렌더 idempotent 업로드 — 같은 SHA 재전송은 성공 (§10.1).</summary>
    [HttpPut("similarity-runs/{runId:guid}/evaluations/{evaluationId:guid}/render")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<IActionResult> UploadRenderAsync(
        Guid jobId, Guid runId, Guid evaluationId, IFormFile? render, CancellationToken ct)
    {
        if (render is null)
        {
            return ApiResults.Failure<SimilarityEvaluationResponse>(
                Domain.Common.ErrorCode.SimilarityRenderInvalid, "렌더가 없습니다");
        }

        using var buffer = new MemoryStream();
        await render.CopyToAsync(buffer, ct);

        return ApiResults.From(
            await uploadRender.HandleAsync(jobId, runId, evaluationId, buffer.ToArray(), ct),
            evaluation => SimilarityEvaluationResponse.From(evaluation),
            StatusCodes.Status202Accepted);
    }

    /// <summary>실패 평가의 저장 렌더 재시도 (§9.3) — 새 캡처 없이 유료 재호출이다.</summary>
    [HttpPost("similarity-runs/{runId:guid}/retry")]
    public async Task<IActionResult> RetryAsync(Guid jobId, Guid runId, CancellationToken ct)
        => ApiResults.From(
            await retry.HandleAsync(jobId, runId, ct),
            run => SimilarityRunResponse.From(run, []),
            StatusCodes.Status202Accepted);

    /// <summary>향후 반복·미시작 평가 중단 (§9.3) — 이미 나간 호출의 비용은 남는다.</summary>
    [HttpPost("similarity-runs/{runId:guid}/cancel")]
    public async Task<IActionResult> CancelAsync(Guid jobId, Guid runId, CancellationToken ct)
    {
        var result = await cancel.HandleAsync(jobId, runId, ct);
        return result.IsSuccess
            ? new StatusCodeResult(StatusCodes.Status204NoContent)
            : ApiResults.Failure<object>(result.ErrorCode!, result.ErrorMessage!);
    }

    /// <summary>현재 결과로 실행 종료 (§9.3).</summary>
    [HttpPost("similarity-runs/{runId:guid}/complete")]
    public async Task<IActionResult> CompleteAsync(Guid jobId, Guid runId, CancellationToken ct)
    {
        var result = await complete.HandleAsync(jobId, runId, ct);
        return result.IsSuccess
            ? new StatusCodeResult(StatusCodes.Status204NoContent)
            : ApiResults.Failure<object>(result.ErrorCode!, result.ErrorMessage!);
    }
}

/// <summary>후보 생성 본문 — 선택한 보정 id 목록뿐이다 (§6).</summary>
public sealed record CreateCandidateBody(IReadOnlyList<Guid> AdjustmentIds);
