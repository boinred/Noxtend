using Noxtend.Domain.Scene;

namespace Noxtend.Domain.Ports;

/// <summary>
/// Design Ref: background-similarity-tuning §4.1 — revision 은 불변이라 갱신 창구가 없다.
/// 추가와 상태 전이 저장만 있고, 전이는 추적된 엔티티에 걸어 <see cref="AddAsync"/> 가
/// 같은 저장 단위로 함께 반영한다 (활성 교대가 원자적이어야 하는 이유).
/// </summary>
public interface ISceneLayoutRepository
{
    /// <summary>추적 상태로 돌려준다 — 호출자가 MarkSuperseded 를 걸고 저장할 수 있게.</summary>
    Task<SceneLayout?> GetActiveByJobAsync(Guid jobId, CancellationToken ct);

    /// <summary>revision 채번용 — 없으면 0.</summary>
    Task<int> MaxRevisionAsync(Guid jobId, CancellationToken ct);

    /// <summary>id 조회 — 후보·복원이 특정 revision 을 지목한다 (§10).</summary>
    Task<SceneLayout?> GetAsync(Guid layoutId, CancellationToken ct);

    /// <summary>revision 이력 — 최신부터.</summary>
    Task<IReadOnlyList<SceneLayout>> ListByJobAsync(Guid jobId, CancellationToken ct);

    /// <summary>새 revision 추가 + 보류 중인 상태 전이까지 한 번에 저장.</summary>
    Task AddAsync(SceneLayout layout, CancellationToken ct);
}
