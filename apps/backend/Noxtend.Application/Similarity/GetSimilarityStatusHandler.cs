using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Similarity;

namespace Noxtend.Application.Similarity;

/// <summary>
/// 화면이 그리는 유사도 현황 — 자격 사유·활성 layout·진행 중 run 과 그 평가들.
/// <paramref name="BlockingReasons"/> 가 비어 있어야 시작 버튼이 산다 (§11.1).
/// </summary>
public sealed record SimilarityStatusView(
    IReadOnlyList<string> BlockingReasons,
    Guid? ActiveLayoutId,
    SimilarityRun? OpenRun,
    IReadOnlyList<SimilarityEvaluation> OpenRunEvaluations);

/// <summary>
/// 유사도 현황 조회 (§10 GET /similarity).
///
/// Design Ref: background-similarity-tuning §11.1
///
/// **자격 사유는 machine-readable 문구로 그대로 화면에 간다** — 버튼을 왜 못 누르는지
/// 사용자가 알아야 한다. 시작 핸들러와 같은 규칙을 봐야 화면과 접수가 어긋나지 않는다.
/// </summary>
public sealed class GetSimilarityStatusHandler(
    IJobRepository jobs,
    ISceneLayoutRepository layouts,
    ISimilarityRepository similarity,
    IPromptCatalog prompts)
{
    public async Task<Result<SimilarityStatusView>> HandleAsync(Guid jobId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<SimilarityStatusView>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        var reasons = new List<string>();

        if (job.Category != AssetCategory.Background)
        {
            reasons.Add("배경 작업만 비교할 수 있습니다");
        }

        if (job.Status != JobStatus.Succeeded)
        {
            reasons.Add("제작이 완료된 작업만 비교할 수 있습니다");
        }

        var meshByPart = job.GeneratedMeshes
            .GroupBy(mesh => mesh.PartId)
            .ToDictionary(group => group.Key, group => group.OrderBy(m => m.CreatedAt).Last());
        if (job.Parts.Count == 0 || job.Parts.Any(part => !meshByPart.ContainsKey(part.Id)))
        {
            reasons.Add("3D 가 없는 파츠가 있습니다");
        }

        var layout = await layouts.GetActiveByJobAsync(jobId, ct);
        if (reasons.Count == 0)
        {
            var signature = SceneMeshSignature.Compute(job.LayoutSignatureInputs());
            if (layout is null || layout.SourceMeshSignature != signature)
            {
                reasons.Add("3D 배경 화면을 한 번 열어 배치를 준비해야 합니다");
            }
        }

        if (await prompts.GetActiveAsync(
                LlmOperationKind.SimilarityEvaluate, AssetCategory.Background, ct) is null)
        {
            reasons.Add("유사도 평가 프롬프트가 활성화되어 있지 않습니다");
        }

        var openRun = await similarity.GetOpenRunByJobAsync(jobId, ct);
        var evaluations = openRun is null
            ? []
            : await similarity.ListEvaluationsAsync(openRun.Id, ct);

        return Result<SimilarityStatusView>.Ok(
            new SimilarityStatusView(reasons, layout?.Id, openRun, evaluations));
    }
}

/// <summary>run 상세 (§10) — job 소속 검증이 404 의 근거다.</summary>
public sealed record SimilarityRunView(
    SimilarityRun Run,
    IReadOnlyList<SimilarityEvaluation> Evaluations);

public sealed class GetSimilarityRunHandler(ISimilarityRepository similarity)
{
    public async Task<Result<SimilarityRunView>> HandleAsync(
        Guid jobId, Guid runId, CancellationToken ct)
    {
        var run = await similarity.GetRunAsync(runId, ct);
        if (run is null || run.JobId != jobId)
        {
            return Result<SimilarityRunView>.Fail(
                ErrorCode.SimilarityRunNotFound, "이 작업의 실행이 아닙니다");
        }

        return Result<SimilarityRunView>.Ok(
            new SimilarityRunView(run, await similarity.ListEvaluationsAsync(run.Id, ct)));
    }
}


/// <summary>실행 이력 (§10 GET /similarity-runs) — 최신부터, 평가 포함.</summary>
public sealed class ListSimilarityRunsHandler(ISimilarityRepository similarity)
{
    public async Task<Result<IReadOnlyList<SimilarityRunView>>> HandleAsync(
        Guid jobId, CancellationToken ct)
    {
        var runs = await similarity.ListRunsByJobAsync(jobId, ct);
        var views = new List<SimilarityRunView>(runs.Count);
        foreach (var run in runs)
        {
            views.Add(new SimilarityRunView(run, await similarity.ListEvaluationsAsync(run.Id, ct)));
        }

        return Result<IReadOnlyList<SimilarityRunView>>.Ok(views);
    }
}
