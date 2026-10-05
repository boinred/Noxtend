using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Persistence.Repositories;

/// <summary>
/// Design Ref: §6.3 · §9.2
///
/// **호출마다 컨텍스트를 새로 연다.** 공정 실행이 리스 갱신 루프와 작업 추적으로 자기
/// 컨텍스트를 이미 쓰고 있어서, 폴링 checkpoint 가 그것을 함께 쓰면 EF 의 동시 사용
/// 금지에 걸린다. 컨텍스트는 짧게 열고 바로 닫는 것이 여기서는 비용이 아니라 요건이다.
/// </summary>
public sealed class EfMeshRunRepository(IDbContextFactory<NoxtendDbContext> contexts)
    : IMeshRunRepository
{
    public async Task<MeshRun?> GetLatestByTaskAsync(Guid taskId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        return await db.MeshRuns
            .Where(run => run.TaskId == taskId)
            .OrderByDescending(run => run.RunNumber)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<MeshRun?> GetByIdAsync(Guid runId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        return await db.MeshRuns.FirstOrDefaultAsync(run => run.Id == runId, ct);
    }

    /// <summary>
    /// 공정마다 가장 나중 실행 하나씩.
    ///
    /// 상세 화면이 진행률을 보여주려면 공정당 하나면 된다 — 이전 실행은 재시도 이력이라
    /// 화면에 나오지 않는다.
    /// </summary>
    public async Task<IReadOnlyList<MeshRun>> ListLatestByJobAsync(Guid jobId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        return await db.MeshRuns
            .Where(run => run.JobId == jobId)
            .GroupBy(run => run.TaskId)
            .Select(group => group.OrderByDescending(run => run.RunNumber).First())
            .ToListAsync(ct);
    }

    public async Task AddAsync(MeshRun run, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        db.MeshRuns.Add(run);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// **`Update` 로 통째로 붙인다.** 이 인스턴스는 다른 컨텍스트에서 왔거나 아예 컨텍스트
    /// 밖에서 상태가 바뀐 것이라, 추적 중인 사본이 없다.
    ///
    /// **산출물은 그 뒤에 손으로 상태를 잡아 준다.** `Update` 는 그래프 전체를
    /// <see cref="EntityState.Modified"/> 로 표시하는데, 산출물 행은 내려받기가 끝난
    /// 뒤에야 생긴다 — 없는 행을 <c>UPDATE</c> 하면 0행이 영향받고 EF 는 그것을
    /// **동시성 충돌로 읽는다.** 정상 저장이 전부 실패한다.
    ///
    /// 입력(<c>MeshRunInputs</c>)은 실행을 만들 때 네 행이 함께 들어가 이 문제가 없다.
    /// 산출물만 나중에 생기므로 여기만 나뉜다.
    /// </summary>
    public async Task SaveAsync(MeshRun run, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        // 이미 저장된 종류를 먼저 읽는다. 스칼라 투영이라 소유 엔티티 추적 규칙에 걸리지 않는다
        var stored = await db.MeshRuns
            .Where(existing => existing.Id == run.Id)
            .SelectMany(existing => existing.Artifacts)
            .Select(artifact => artifact.Kind)
            .ToListAsync(ct);

        db.MeshRuns.Update(run);

        foreach (var entry in db.ChangeTracker.Entries<MeshArtifact>())
        {
            entry.State = stored.Contains(entry.Entity.Kind)
                ? EntityState.Modified
                : EntityState.Added;
        }

        await db.SaveChangesAsync(ct);
    }
}
