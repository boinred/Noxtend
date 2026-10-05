using Noxtend.Application.Similarity;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Similarity;

namespace Noxtend.Api.Contracts;

/// <summary>Design Ref: background-similarity-tuning §10 — 유사도 응답 계약.</summary>
public sealed record SimilarityStatusResponse(
    bool Eligible,
    IReadOnlyList<string> BlockingReasons,
    Guid? ActiveLayoutId,
    SimilarityRunResponse? OpenRun)
{
    public static SimilarityStatusResponse From(SimilarityStatusView view)
        => new(
            view.BlockingReasons.Count == 0,
            view.BlockingReasons,
            view.ActiveLayoutId,
            view.OpenRun is null
                ? null
                : SimilarityRunResponse.From(view.OpenRun, view.OpenRunEvaluations));
}

public sealed record SimilarityRunResponse(
    Guid Id,
    Guid JobId,
    string Model,
    int MaxIterations,
    int CurrentIteration,
    int MaxCalls,
    string Status,
    string? FailureCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    /// <summary>실제 누적 비용 USD (§7.3) — LlmCall usage 가 정본. 단가 미등록 호출만 있으면 null.</summary>
    decimal? ActualCostUsd,
    IReadOnlyList<SimilarityEvaluationResponse> Evaluations)
{
    public static SimilarityRunResponse From(
        SimilarityRun run,
        IReadOnlyList<SimilarityEvaluation> evaluations,
        IReadOnlyDictionary<Guid, decimal>? costByEvaluation = null)
    {
        // 실패 호출의 비용도 포함된다 (§7.3) — usage 가 기록됐다면 실제로 쓴 돈이다
        decimal? total = null;
        var evaluationResponses = new List<SimilarityEvaluationResponse>(evaluations.Count);
        foreach (var evaluation in evaluations)
        {
            decimal? cost = costByEvaluation is not null
                && costByEvaluation.TryGetValue(evaluation.Id, out var value) ? value : null;
            if (cost is not null)
            {
                total = (total ?? 0) + cost.Value;
            }

            evaluationResponses.Add(SimilarityEvaluationResponse.From(evaluation, cost));
        }

        return new(
            run.Id, run.JobId, run.Model, run.MaxIterations, run.CurrentIteration,
            run.MaxCalls, JobResponse.Wire(run.Status), run.FailureCode,
            run.CreatedAt, run.CompletedAt, total, evaluationResponses);
    }

    public static SimilarityRunResponse From(
        SimilarityRunView view, IReadOnlyDictionary<Guid, decimal>? costByEvaluation = null)
        => From(view.Run, view.Evaluations, costByEvaluation);
}

public sealed record SimilarityEvaluationResponse(
    Guid Id,
    Guid LayoutId,
    int Sequence,
    string Kind,
    string Status,
    int AttemptCount,
    SimilarityScoreResponse? Score,
    IReadOnlyList<SimilarityAdjustmentResponse> Adjustments,
    IReadOnlyList<string> RegenerationNotes,
    /// <summary>이 평가에 실제로 쓴 비용 USD — 단가 미등록이면 null, 0 이 아니다.</summary>
    decimal? ActualCostUsd = null)
{
    public static SimilarityEvaluationResponse From(
        SimilarityEvaluation evaluation, decimal? actualCostUsd = null)
        => new(
            evaluation.Id, evaluation.LayoutId, evaluation.Sequence,
            JobResponse.Wire(evaluation.Kind), JobResponse.Wire(evaluation.Status),
            evaluation.AttemptCount,
            evaluation.Score is null ? null : SimilarityScoreResponse.From(evaluation.Score),
            [.. evaluation.Adjustments.Select(SimilarityAdjustmentResponse.From)],
            evaluation.RegenerationNotes,
            actualCostUsd);
}

/// <summary>overall 은 서버 계산값이다 (D-05) — 응답에도 그 값만 나간다.</summary>
public sealed record SimilarityScoreResponse(
    int Overall,
    IReadOnlyList<SimilarityDimensionResponse> Dimensions)
{
    public static SimilarityScoreResponse From(SimilarityScore score)
        => new(score.Overall, [.. score.Dimensions.Select(d => new SimilarityDimensionResponse(
            JobResponse.Wire(d.Kind), d.Score, d.Evidence, d.Recommendation))]);
}

public sealed record SimilarityDimensionResponse(
    string Kind, int Score, string Evidence, string Recommendation);

/// <summary>
/// 보정 제안 — 화면은 id 로만 선택하고 값은 서버 저장본이 정본이다 (§6).
/// command 는 discriminated JSON 그대로 나간다 (type 판별자).
/// </summary>
public sealed record SimilarityAdjustmentResponse(
    Guid Id,
    SceneAdjustmentCommand Command,
    double Confidence,
    string Reason)
{
    public static SimilarityAdjustmentResponse From(SimilarityAdjustment adjustment)
        => new(adjustment.Id, adjustment.Command, adjustment.Confidence, adjustment.Reason);
}

/// <summary>후보 생성 응답 — 화면이 즉시 offscreen 캡처할 후보 배치를 함께 준다 (§9.2 4단계).</summary>
public sealed record SimilarityCandidateResponse(
    SimilarityRunResponse Run,
    CandidateLayoutResponse CandidateLayout,
    SimilarityEvaluationResponse Evaluation)
{
    public static SimilarityCandidateResponse From(Application.Similarity.CreatedCandidate created)
        => new(
            SimilarityRunResponse.From(created.Run, [created.Evaluation]),
            CandidateLayoutResponse.From(created.CandidateLayout),
            SimilarityEvaluationResponse.From(created.Evaluation));
}

/// <summary>후보 revision 의 렌더 재료 — 인스턴스 변환과 수치 camera/light.</summary>
public sealed record CandidateLayoutResponse(
    Guid Id,
    int Revision,
    IReadOnlyList<CandidateInstanceResponse> Instances,
    NumericCameraResponse Camera,
    NumericLightResponse Light)
{
    public static CandidateLayoutResponse From(Noxtend.Domain.Scene.SceneLayout layout)
        => new(
            layout.Id,
            layout.Revision,
            [.. layout.Instances.Select(i => new CandidateInstanceResponse(
                i.PartId, i.Ordinal, new SceneVectorResponse(i.X, i.Y, i.Z), i.RotationY, i.Scale,
                new SceneVectorResponse(i.ScaleX, i.ScaleY, i.ScaleZ)))],
            NumericCameraResponse.From(layout.Camera),
            NumericLightResponse.From(layout.Light));
}

public sealed record CandidateInstanceResponse(
    Guid PartId, int Ordinal, SceneVectorResponse Position, double RotationY,
    /// <summary>높이 — 정규화 기준축.</summary>
    double Scale,
    /// <summary>축별 배율 (#20 §4.2) — 표면이 무너지지 않게 세 축을 그대로 나른다.</summary>
    SceneVectorResponse ScaleVector);

/// <summary>revision 이력 한 줄 (§14.1 실행 이력).</summary>
public sealed record SceneRevisionResponse(
    Guid Id, int Revision, string State, string Origin, DateTimeOffset ComposedAt)
{
    public static SceneRevisionResponse From(Application.Scene.SceneRevisionView view)
        => new(view.Id, view.Revision, JobResponse.Wire(view.State), JobResponse.Wire(view.Origin), view.ComposedAt);
}
