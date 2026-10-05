using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Similarity;

namespace Noxtend.Infrastructure.Persistence.Repositories;

public sealed class EfSimilarityRepository(NoxtendDbContext db) : ISimilarityRepository
{
    public Task<SimilarityRun?> GetRunAsync(Guid runId, CancellationToken ct)
        => db.SimilarityRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);

    // 비종료 상태 셋은 필터드 유니크 인덱스의 필터와 같아야 한다
    public Task<SimilarityRun?> GetOpenRunByJobAsync(Guid jobId, CancellationToken ct)
        => db.SimilarityRuns.FirstOrDefaultAsync(
            r => r.JobId == jobId &&
                (r.Status == SimilarityRunStatus.Evaluating
                    || r.Status == SimilarityRunStatus.ReadyForAdjustment
                    || r.Status == SimilarityRunStatus.AwaitingRender),
            ct);

    public Task<SimilarityRun?> FindByIdempotencyKeyAsync(
        Guid jobId, string key, CancellationToken ct)
        => db.SimilarityRuns.FirstOrDefaultAsync(
            r => r.JobId == jobId && r.IdempotencyKey == key, ct);

    public Task AddRunAsync(SimilarityRun run, CancellationToken ct)
    {
        db.SimilarityRuns.Add(run);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SimilarityRun>> ListRunsByJobAsync(Guid jobId, CancellationToken ct)
        => await db.SimilarityRuns
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public Task<SimilarityEvaluation?> GetEvaluationAsync(Guid evaluationId, CancellationToken ct)
        => db.SimilarityEvaluations.FirstOrDefaultAsync(e => e.Id == evaluationId, ct);

    public async Task<IReadOnlyList<SimilarityEvaluation>> ListEvaluationsAsync(
        Guid runId, CancellationToken ct)
        => await db.SimilarityEvaluations
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);

    public Task AddEvaluationAsync(SimilarityEvaluation evaluation, CancellationToken ct)
    {
        db.SimilarityEvaluations.Add(evaluation);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SimilarityEvaluation>> ListExpiredRunningAsync(
        DateTimeOffset now, CancellationToken ct)
        => await db.SimilarityEvaluations
            .Where(e => e.Status == SimilarityEvaluationStatus.Running
                && e.LeaseExpiresAt != null && e.LeaseExpiresAt < now
                && db.SimilarityRuns.Any(r => r.Id == e.RunId
                    && (r.Status == SimilarityRunStatus.Evaluating
                        || r.Status == SimilarityRunStatus.ReadyForAdjustment
                        || r.Status == SimilarityRunStatus.AwaitingRender)))
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);
}
