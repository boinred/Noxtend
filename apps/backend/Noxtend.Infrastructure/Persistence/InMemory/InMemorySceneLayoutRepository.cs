using System.Collections.Concurrent;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Scene;

namespace Noxtend.Infrastructure.Persistence.InMemory;

public sealed class InMemorySceneLayoutRepository : ISceneLayoutRepository
{
    private readonly ConcurrentDictionary<Guid, SceneLayout> _byId = new();

    public Task<SceneLayout?> GetActiveByJobAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult(_byId.Values.FirstOrDefault(
            l => l.JobId == jobId && l.State == SceneLayoutState.Active));

    public Task<SceneLayout?> GetAsync(Guid layoutId, CancellationToken ct)
        => Task.FromResult(_byId.GetValueOrDefault(layoutId));

    public Task<IReadOnlyList<SceneLayout>> ListByJobAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SceneLayout>>(
            [.. _byId.Values.Where(l => l.JobId == jobId).OrderByDescending(l => l.Revision)]);

    public Task<int> MaxRevisionAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult(_byId.Values
            .Where(l => l.JobId == jobId)
            .Select(l => (int?)l.Revision)
            .Max() ?? 0);

    public Task AddAsync(SceneLayout layout, CancellationToken ct)
    {
        _byId[layout.Id] = layout;
        return Task.CompletedTask;
    }

    /// <summary>작업 완전 삭제의 동반 삭제 창구 (FR-09).</summary>
    public void RemoveByJob(Guid jobId)
    {
        foreach (var layout in _byId.Values.Where(l => l.JobId == jobId).ToList())
        {
            _byId.TryRemove(layout.Id, out _);
        }
    }
}
