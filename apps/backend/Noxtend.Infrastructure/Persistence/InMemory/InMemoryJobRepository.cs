using System.Collections.Concurrent;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;

namespace Noxtend.Infrastructure.Persistence.InMemory;

/// <summary>
/// 인프라 없는 <see cref="IJobRepository"/> 구현.
///
/// Design Ref: §2.0 Option B 근거 — 이것이 있어야 오케스트레이션 전체가 EF InMemory
/// (실제 SQL Server 와 동작이 다르다)도 실제 DB 도 없이 검증된다.
///
/// 엔티티 인스턴스를 그대로 들고 있으므로 <c>SaveChangesAsync</c> 는 할 일이 없다.
/// 그래도 호출은 남긴다 — 유스케이스가 EF 어댑터에서 저장을 빠뜨리면 그때 드러난다.
/// </summary>
/// <param name="images">
/// 업로드 표. **원본의 저장소 키가 거기에만 있다** — 없이 두면 키를 지어내게 되고,
/// 그러면 삭제가 실제 파일을 못 치운다.
/// </param>
public sealed class InMemoryJobRepository(
    InMemoryStoredImageRepository? images = null,
    InMemorySceneLayoutRepository? sceneLayouts = null) : IJobRepository
{
    private readonly ConcurrentDictionary<Guid, PipelineJob> _jobs = new();

    public Task AddAsync(PipelineJob job, CancellationToken ct)
    {
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task<PipelineJob?> GetAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult(_jobs.GetValueOrDefault(jobId));

    public Task<PipelineJob?> ReloadAsync(Guid jobId, CancellationToken ct)
        => GetAsync(jobId, ct);

    public Task<GeneratedImage?> GetGeneratedImageAsync(Guid imageId, CancellationToken ct)
        => Task.FromResult(_jobs.Values
            .SelectMany(j => j.GeneratedImages)
            .FirstOrDefault(i => i.Id == imageId));


    public Task<GeneratedMesh?> GetGeneratedMeshAsync(Guid meshId, CancellationToken ct)
        => Task.FromResult(_jobs.Values
            .SelectMany(j => j.GeneratedMeshes)
            .FirstOrDefault(mesh => mesh.Id == meshId));

    public Task<SpriteAcceptedRequest?> GetSpriteRequestAsync(Guid requestId, CancellationToken ct)
        => Task.FromResult(_jobs.Values.Where(j => j.Sprites != null)
            .SelectMany(j => j.Sprites!.Requests).FirstOrDefault(r => r.RequestId == requestId));

    public Task<PipelineJob?> GetByTaskAsync(Guid taskId, CancellationToken ct)
        => Task.FromResult(_jobs.Values.FirstOrDefault(j => j.Tasks.Any(t => t.Id == taskId)));

    public Task<IReadOnlyList<PipelineJob>> ListAsync(
        JobListFilter filter,
        AssetCategory? category,
        int limit,
        CancellationToken ct,
        ProductionMode? productionMode = null)
    {
        var query = _jobs.Values.Where(j => filter == JobListFilter.Terminal ? j.IsTerminal : !j.IsTerminal);

        if (productionMode is { } mode) query = query.Where(j => j.ProductionMode == mode);

        if (category is { } wanted)
        {
            query = query.Where(j => j.Category == wanted);
        }

        IReadOnlyList<PipelineJob> result = query
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<int> CountAsync(JobListFilter filter, AssetCategory? category, CancellationToken ct, ProductionMode? productionMode = null)
    {
        var query = _jobs.Values.Where(j => filter == JobListFilter.Terminal ? j.IsTerminal : !j.IsTerminal);

        if (productionMode is { } mode) query = query.Where(j => j.ProductionMode == mode);

        if (category is { } wanted)
        {
            query = query.Where(j => j.Category == wanted);
        }

        return Task.FromResult(query.Count());
    }

    public Task<IReadOnlyList<PipelineJob>> ListBySourceImageAsync(
        Guid sourceImageId,
        CancellationToken ct)
    {
        IReadOnlyList<PipelineJob> result = _jobs.Values
            .Where(j => j.SourceImageId == sourceImageId)
            .OrderByDescending(j => j.CreatedAt)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PipelineJob>> ListSweepCandidatesAsync(
        DateTimeOffset now,
        CancellationToken ct)
    {
        // 상위 상태 조정 누락 작업 + 리스 만료 실행 공정 + 대기 공정
        IReadOnlyList<PipelineJob> candidates = _jobs.Values
            .Where(j => !j.IsTerminal)
            .Where(j =>
                (j.Tasks.Count > 0 && j.Tasks.All(t => t.IsTerminal)) ||
                j.Tasks.Any(t => t.Status == Domain.Job.TaskStatus.Pending || t.IsLeaseExpired(now)))
            .ToList();

        return Task.FromResult(candidates);
    }

    /// <summary>
    /// 조건부 삭제. **상태를 다시 읽는다** — 호출자가 들고 있던 사본이 낡았을 수 있다.
    /// </summary>
    public Task<DeletedJobBlobs?> DeleteIfTerminalAsync(Guid jobId, CancellationToken ct)
    {
        if (!_jobs.TryGetValue(jobId, out var job) || !job.IsTerminal)
        {
            return Task.FromResult<DeletedJobBlobs?>(null);
        }

        // 키를 먼저 모은다 — 지운 뒤에는 어느 파일이 이 작업 것인지 알 수 없다
        var imageKeys = job.GeneratedImages.Select(image => image.BlobKey).ToList();
        if (job.Sprites is { } sprites)
        {
            imageKeys.AddRange(sprites.Images.Select(image => image.BlobKey));
            imageKeys.AddRange(sprites.Exports.Select(export => export.BlobKey));
        }
        var meshKeys = job.GeneratedMeshes
            .SelectMany(mesh => mesh.Artifacts)
            .Select(artifact => artifact.BlobKey)
            .ToList();

        if (!_jobs.TryRemove(jobId, out _))
        {
            return Task.FromResult<DeletedJobBlobs?>(null);
        }

        // 조립 명세 동반 삭제 (scene-assembly FR-09)
        sceneLayouts?.RemoveByJob(jobId);

        // **원본은 공유될 수 있다** — 같은 업로드로 두 번 접수한 작업이 있으면 남긴다
        if (!_jobs.Values.Any(other => other.SourceImageId == job.SourceImageId)
            && images?.Remove(job.SourceImageId) is { } sourceKey)
        {
            imageKeys.Add(sourceKey);
        }

        return Task.FromResult<DeletedJobBlobs?>(new DeletedJobBlobs(imageKeys.Distinct().ToArray(), meshKeys));
    }



    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
