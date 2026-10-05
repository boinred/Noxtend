using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Persistence.InMemory;

/// <summary>
/// 인프라 없는 <see cref="IMeshRunRepository"/>.
///
/// Design Ref: §2.0
///
/// 재기동 시나리오를 컨테이너 없이 초 단위로 돌리기 위한 것이다 — 그 속도가 아니면
/// 크래시 지점을 여덟 군데나 짚어 보지 못한다.
///
/// **저장된 인스턴스를 그대로 돌려준다.** 실제 구현은 DB 를 거쳐 새 객체가 나오지만,
/// 여기서 확인하는 것은 "무엇이 저장돼 있는가" 이지 매핑이 아니다 — 그쪽은
/// <c>MeshPersistenceTests</c> 가 실제 SQL Server 로 본다.
/// </summary>
public sealed class InMemoryMeshRunRepository : IMeshRunRepository
{
    private readonly List<MeshRun> runs = [];

    public Task<MeshRun?> GetLatestByTaskAsync(Guid taskId, CancellationToken ct)
        => Task.FromResult(runs
            .Where(run => run.TaskId == taskId)
            .OrderByDescending(run => run.RunNumber)
            .FirstOrDefault());

    public Task<MeshRun?> GetByIdAsync(Guid runId, CancellationToken ct)
        => Task.FromResult(runs.FirstOrDefault(run => run.Id == runId));

    public Task<IReadOnlyList<MeshRun>> ListLatestByJobAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<MeshRun>>(
        [
            .. runs
                .Where(run => run.JobId == jobId)
                .GroupBy(run => run.TaskId)
                .Select(group => group.OrderByDescending(run => run.RunNumber).First()),
        ]);

    public Task AddAsync(MeshRun run, CancellationToken ct)
    {
        runs.Add(run);
        return Task.CompletedTask;
    }

    public Task SaveAsync(MeshRun run, CancellationToken ct)
    {
        // 같은 인스턴스라 이미 반영돼 있다. 없으면 추가한다 — 실제 구현의 Update 와 같은 뜻이다
        if (!runs.Contains(run))
        {
            runs.Add(run);
        }

        return Task.CompletedTask;
    }
}
