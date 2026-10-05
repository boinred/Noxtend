using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Scene;

namespace Noxtend.Infrastructure.Persistence.Repositories;

public sealed class EfSceneLayoutRepository(NoxtendDbContext db) : ISceneLayoutRepository
{
    // 추적 조회 — 활성 교대(MarkSuperseded)가 다음 저장에 함께 실린다
    public Task<SceneLayout?> GetActiveByJobAsync(Guid jobId, CancellationToken ct)
        => db.SceneLayouts.FirstOrDefaultAsync(
            l => l.JobId == jobId && l.State == SceneLayoutState.Active, ct);

    public Task<SceneLayout?> GetAsync(Guid layoutId, CancellationToken ct)
        => db.SceneLayouts.FirstOrDefaultAsync(l => l.Id == layoutId, ct);

    public async Task<IReadOnlyList<SceneLayout>> ListByJobAsync(Guid jobId, CancellationToken ct)
        => await db.SceneLayouts
            .Where(l => l.JobId == jobId)
            .OrderByDescending(l => l.Revision)
            .ToListAsync(ct);

    public async Task<int> MaxRevisionAsync(Guid jobId, CancellationToken ct)
        => await db.SceneLayouts
            .Where(l => l.JobId == jobId)
            .Select(l => (int?)l.Revision)
            .MaxAsync(ct) ?? 0;

    public async Task AddAsync(SceneLayout layout, CancellationToken ct)
    {
        db.SceneLayouts.Add(layout);
        await db.SaveChangesAsync(ct);
    }
}
