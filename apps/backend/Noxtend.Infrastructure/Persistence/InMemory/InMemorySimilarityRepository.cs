using System.Collections.Concurrent;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Similarity;

namespace Noxtend.Infrastructure.Persistence.InMemory;

public sealed class InMemorySimilarityRepository : ISimilarityRepository
{
    private readonly ConcurrentDictionary<Guid, SimilarityRun> _runs = new();
    private readonly ConcurrentDictionary<Guid, SimilarityEvaluation> _evaluations = new();

    public Task<SimilarityRun?> GetRunAsync(Guid runId, CancellationToken ct)
        => Task.FromResult(_runs.GetValueOrDefault(runId));

    public Task<SimilarityRun?> GetOpenRunByJobAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult(_runs.Values.FirstOrDefault(r => r.JobId == jobId && !r.IsTerminal));

    public Task<SimilarityRun?> FindByIdempotencyKeyAsync(
        Guid jobId, string key, CancellationToken ct)
        => Task.FromResult(_runs.Values.FirstOrDefault(
            r => r.JobId == jobId && r.IdempotencyKey == key));

    public Task AddRunAsync(SimilarityRun run, CancellationToken ct)
    {
        _runs[run.Id] = run;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SimilarityRun>> ListRunsByJobAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SimilarityRun>>(
            [.. _runs.Values.Where(r => r.JobId == jobId).OrderByDescending(r => r.CreatedAt)]);

    public Task<SimilarityEvaluation?> GetEvaluationAsync(Guid evaluationId, CancellationToken ct)
        => Task.FromResult(_evaluations.GetValueOrDefault(evaluationId));

    public Task<IReadOnlyList<SimilarityEvaluation>> ListEvaluationsAsync(
        Guid runId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SimilarityEvaluation>>(
            [.. _evaluations.Values.Where(e => e.RunId == runId).OrderBy(e => e.Sequence)]);

    public Task AddEvaluationAsync(SimilarityEvaluation evaluation, CancellationToken ct)
    {
        _evaluations[evaluation.Id] = evaluation;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SimilarityEvaluation>> ListExpiredRunningAsync(
        DateTimeOffset now, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SimilarityEvaluation>>(
            [.. _evaluations.Values.Where(e =>
                e.Status == SimilarityEvaluationStatus.Running
                && e.LeaseExpiresAt is { } lease && lease < now
                && _runs.TryGetValue(e.RunId, out var run) && !run.IsTerminal)]);

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>작업 완전 삭제의 동반 삭제 창구 (FR-09 와 같은 규칙).</summary>
    public void RemoveByJob(Guid jobId)
    {
        foreach (var run in _runs.Values.Where(r => r.JobId == jobId).ToList())
        {
            _runs.TryRemove(run.Id, out _);
            foreach (var evaluation in _evaluations.Values.Where(e => e.RunId == run.Id).ToList())
            {
                _evaluations.TryRemove(evaluation.Id, out _);
            }
        }
    }
}
