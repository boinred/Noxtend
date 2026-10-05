using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Domain.Ports;
using Noxtend.Tuning.Application.Golden;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Api.Controllers;

/// <summary>
/// Design Ref: §4.2 #20~23·#25 — 골든 세트와 판정.
///
/// **이 컨트롤러가 "잘 했는지" 판단의 데이터 경로다.** 골든 샘플이 기준선이고,
/// 실행 이력이 비교 대상이며, 판정이 그 결과다 (Plan D-4·D-8).
/// </summary>
[ApiController]
[Route("api/golden")]
public sealed class GoldenController(
    ListGoldenSamplesHandler list,
    CreateGoldenSampleHandler create,
    UpdateGoldenSampleHandler update,
    DeleteGoldenSampleHandler delete,
    RecordVerdictHandler recordVerdict,
    IGoldenSampleRepository samples,
    IJobRepository jobs,
    ILlmCallRepository calls,
    IVerdictRepository verdicts,
    IPromptVersionRepository prompts) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        var found = await list.HandleAsync(ct);

        return Ok(ApiResponse<IReadOnlyList<GoldenSampleResponse>>.Ok(
            found.Select(GoldenSampleResponse.From).ToList()));
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] GoldenWriteRequest request,
        CancellationToken ct)
        => ApiResults.From(
            await create.HandleAsync(request.StoredImageId, request.Name, request.ExpectedNote, ct),
            GoldenSampleResponse.From,
            StatusCodes.Status201Created);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] GoldenUpdateRequest request,
        CancellationToken ct)
        => ApiResults.From(
            await update.HandleAsync(id, request.Name, request.ExpectedNote, ct),
            GoldenSampleResponse.From);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var result = await delete.HandleAsync(id, ct);

        return result.IsSuccess
            ? NoContent()
            : ApiResults.Failure<object>(result.ErrorCode!, result.ErrorMessage!);
    }

    /// <summary>
    /// Design Ref: §4.2 #23 — 이 이미지로 돌린 실행들.
    ///
    /// **단계별 프롬프트 버전을 내역에서 꺼내 함께 싣는다.** 이것이 없으면 목록에서
    /// 무엇을 비교하는지 알 수 없다. 판정도 한 번에 조회해 N+1 을 피한다.
    ///
    /// 컨트롤러가 네 저장소를 조합하는 것이 어색해 보일 수 있지만, 이 조회는 파이프라인과
    /// 튜닝을 가로지르므로 어느 한쪽 유스케이스에 두면 그쪽이 반대편을 참조하게 된다 (§2.4).
    /// 조합은 두 컨텍스트를 모두 아는 층 — 여기 — 의 일이다.
    /// </summary>
    [HttpGet("{id:guid}/runs")]
    public async Task<IActionResult> ListRunsAsync(Guid id, CancellationToken ct)
    {
        var sample = await samples.GetAsync(id, ct);
        if (sample is null)
        {
            return ApiResults.Failure<IReadOnlyList<GoldenRunResponse>>(
                Domain.Common.ErrorCode.GoldenNotFound, "골든 샘플을 찾을 수 없습니다");
        }

        var runs = await jobs.ListBySourceImageAsync(sample.StoredImageId, ct);
        var verdictsByJob = await verdicts.GetByJobsAsync([.. runs.Select(j => j.Id)], ct);

        var responses = new List<GoldenRunResponse>();

        // 버전 id → 번호 캐시. 여러 실행이 같은 버전을 쓰는 것이 정상이다
        var versionNumbers = new Dictionary<Guid, int>();

        foreach (var job in runs)
        {
            var jobCalls = await calls.ListByJobAsync(job.Id, ct);

            // 단계마다 마지막 호출의 버전 — 재시도가 있었다면 실제로 결과를 낸 것이다
            var lastByKind = jobCalls
                .GroupBy(c => JobResponse.Wire(c.Kind))
                .ToDictionary(g => g.Key, g => g.OrderBy(c => c.At).Last().PromptVersionId);

            var promptVersions = new Dictionary<string, int>();
            foreach (var (kind, versionId) in lastByKind)
            {
                // 내역에는 id 만 있고 번호가 없다. 번호를 내역에 복사하면 두 번째 진실이
                // 되므로 여기서 되찾는다 — 실행당 단계 셋뿐이라 비용이 문제되지 않는다
                if (!versionNumbers.TryGetValue(versionId, out var number))
                {
                    number = (await prompts.GetAsync(versionId, ct))?.Version ?? 0;
                    versionNumbers[versionId] = number;
                }

                promptVersions[kind] = number;
            }

            responses.Add(new GoldenRunResponse(
                job.Id,
                JobResponse.Wire(job.Status),
                job.Tasks.FirstOrDefault()?.Model,
                promptVersions,
                job.Parts.Count,
                verdictsByJob.TryGetValue(job.Id, out var verdict)
                    ? VerdictResponse.From(verdict)
                    : null,
                job.CreatedAt));
        }

        return Ok(ApiResponse<IReadOnlyList<GoldenRunResponse>>.Ok(responses));
    }

    /// <summary>Design Ref: §4.2 #25 — 사람의 판정. 재판정은 덮어쓴다.</summary>
    [HttpPost("/api/jobs/{jobId:guid}/verdict")]
    public async Task<IActionResult> RecordVerdictAsync(
        Guid jobId,
        [FromBody] VerdictWriteRequest request,
        CancellationToken ct)
        => ApiResults.From(
            await recordVerdict.HandleAsync(jobId, request.IsPass, request.Memo, ct),
            VerdictResponse.From);

}
