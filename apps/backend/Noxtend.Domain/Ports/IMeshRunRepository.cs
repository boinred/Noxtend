using Noxtend.Domain.Mesh;

namespace Noxtend.Domain.Ports;

/// <summary>
/// 외부 3D 실행 저장소.
///
/// Design Ref: §6.3 · §3.2
///
/// **작업 저장소와 나눈 이유는 DbContext 수명이다.** 공정 실행이 리스 갱신 루프를 돌리면서
/// 작업을 추적하는 동안, 여기서는 2초마다 진행률을 저장해야 한다. 같은 컨텍스트를 나눠
/// 쓰면 EF 의 동시 사용 금지에 걸리므로 구현은 checkpoint 마다 짧은 컨텍스트를 연다.
///
/// 저장 단위가 행 하나라 <c>SaveChangesAsync</c> 를 따로 두지 않는다 — 각 메서드가 스스로
/// 확정한다. 작업 저장소와 달리 여기에는 "여러 변경을 모아 한 트랜잭션" 이 없다.
/// </summary>
public interface IMeshRunRepository
{
    /// <summary>이 공정의 가장 나중 실행. 재기동한 워커가 무엇을 이어받을지 이것으로 안다.</summary>
    Task<MeshRun?> GetLatestByTaskAsync(Guid taskId, CancellationToken ct);

    Task<MeshRun?> GetByIdAsync(Guid runId, CancellationToken ct);

    /// <summary>작업의 공정별 최신 실행 — 상세 화면의 진행률이 여기서 온다 (§8.5).</summary>
    Task<IReadOnlyList<MeshRun>> ListLatestByJobAsync(Guid jobId, CancellationToken ct);

    Task AddAsync(MeshRun run, CancellationToken ct);

    /// <summary>checkpoint 저장. 동시 쓰기 충돌은 구현이 최신 행을 다시 읽어 해소한다.</summary>
    Task SaveAsync(MeshRun run, CancellationToken ct);
}
