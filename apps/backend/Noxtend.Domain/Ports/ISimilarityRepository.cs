using Noxtend.Domain.Similarity;

namespace Noxtend.Domain.Ports;

/// <summary>
/// Design Ref: background-similarity-tuning §5 · §10 — run 과 평가는 한 경계에서 저장한다.
/// 상태 전이(채택·거부·취소)는 추적된 엔티티에 걸고 <see cref="SaveChangesAsync"/> 가
/// 한 단위로 반영한다 — 활성 교대와 run 전이가 같은 transaction 이어야 하는 이유 (§9.2).
/// </summary>
public interface ISimilarityRepository
{
    Task<SimilarityRun?> GetRunAsync(Guid runId, CancellationToken ct);

    /// <summary>job 의 비종료 run — 둘일 수 없다 (필터드 유니크, §5.1).</summary>
    Task<SimilarityRun?> GetOpenRunByJobAsync(Guid jobId, CancellationToken ct);

    /// <summary>같은 Idempotency-Key 의 재시도 접수는 기존 run 을 돌려준다 (§10).</summary>
    Task<SimilarityRun?> FindByIdempotencyKeyAsync(Guid jobId, string key, CancellationToken ct);

    Task AddRunAsync(SimilarityRun run, CancellationToken ct);

    /// <summary>job 의 실행 이력 — 최신부터 (§10 이력 요약).</summary>
    Task<IReadOnlyList<SimilarityRun>> ListRunsByJobAsync(Guid jobId, CancellationToken ct);

    Task<SimilarityEvaluation?> GetEvaluationAsync(Guid evaluationId, CancellationToken ct);

    Task<IReadOnlyList<SimilarityEvaluation>> ListEvaluationsAsync(Guid runId, CancellationToken ct);

    Task AddEvaluationAsync(SimilarityEvaluation evaluation, CancellationToken ct);

    /// <summary>lease 가 만료된 Running 평가 — 스위퍼의 회수 대상 (§12). 종료 run 은 제외.</summary>
    Task<IReadOnlyList<SimilarityEvaluation>> ListExpiredRunningAsync(
        DateTimeOffset now, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
