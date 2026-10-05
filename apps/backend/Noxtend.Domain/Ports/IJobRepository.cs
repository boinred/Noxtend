using Noxtend.Domain.Job;

namespace Noxtend.Domain.Ports;

/// <summary>
/// 작업 저장소.
///
/// Design Ref: §2.0 Option B 선정 근거 — <c>DbContext</c> 를 직접 쓰면 오케스트레이션
/// 테스트가 EF InMemory(실제 SQL Server 와 동작이 다르다)나 실제 DB 를 요구한다.
/// 이 Port 가 있어야 가장 위험한 로직이 **k8s 가 뜨기도 전에** 검증된다 (R-1).
/// </summary>
public interface IJobRepository
{
    Task AddAsync(PipelineJob job, CancellationToken ct);

    /// <summary>공정·파츠를 함께 싣는다. 작업 상태 판정이 공정 전체를 본다.</summary>
    Task<PipelineJob?> GetAsync(Guid jobId, CancellationToken ct);

    /// <summary>변경 추적 캐시를 버리고 DB의 최신 공정 상태를 다시 싣는 조회.</summary>
    Task<PipelineJob?> ReloadAsync(Guid jobId, CancellationToken ct);

    /// <summary>공정 id 로 그 공정이 속한 작업을 찾는다. 워커는 공정 id 만 받는다.</summary>
    Task<PipelineJob?> GetByTaskAsync(Guid taskId, CancellationToken ct);

    /// <summary>
    /// 조건에 맞는 작업이 **모두 몇 건인가** — 목록 상한과 무관하다.
    ///
    /// 홈은 열 건만 싣는다. 그래서 화면의 수와 실제 수가 다른데, 하나를 지우면
    /// 열한 번째가 올라와 "지웠는데 수가 그대로" 로 보인다. 전체를 따로 세어
    /// 그 어긋남을 화면에서 설명한다.
    /// </summary>
    Task<int> CountAsync(JobListFilter filter, AssetCategory? category, CancellationToken ct);

    /// <summary>Design Ref: §4.2 #7 — 홈 두 섹션. 진행 중 / 종료 중 하나.</summary>
    Task<IReadOnlyList<PipelineJob>> ListAsync(
        JobListFilter filter,
        AssetCategory? category,
        int limit,
        CancellationToken ct);

    /// <summary>
    /// 같은 소스 이미지로 돌린 작업들 — 최신순.
    ///
    /// Design Ref: §4.2 #23 — 골든 샘플의 실행 이력이 이것이다.
    ///
    /// **작업에 "골든 여부" 플래그를 두지 않는 대신 이 조회를 쓴다** (§3.2).
    /// 플래그를 두면 골든 샘플을 지웠을 때 어긋나는 두 번째 진실이 생긴다.
    /// </summary>
    Task<IReadOnlyList<PipelineJob>> ListBySourceImageAsync(
        Guid sourceImageId, CancellationToken ct);

    /// <summary>
    /// 스위퍼 대상 — 리스가 만료된 실행 중 공정이나 대기 중 공정을 가진 작업.
    ///
    /// 공정이 아니라 **작업**을 돌려준다. 스위퍼가 공정을 실패로 확정하면 작업 상태도
    /// 함께 판정해야 하는데 (§2.2), 공정만 받으면 작업을 건건이 되조회하게 된다.
    /// </summary>
    Task<IReadOnlyList<PipelineJob>> ListSweepCandidatesAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// 생성 이미지 한 장 (사이클 #7 §4.2 #4).
    ///
    /// **GUID 로만 찾는다** — 사용자 입력이 경로가 되는 길이 없다 (NFR-08). 작업을 통해
    /// 들어가지 않는 이유는 화면이 `&lt;img src&gt;` 에 이미지 id 만 걸기 때문이다.
    /// </summary>
    Task<GeneratedImage?> GetGeneratedImageAsync(Guid imageId, CancellationToken ct);

    /// <summary>내려받기 ID 로 3D 결과 하나 (사이클 #10 §10.4).</summary>
    Task<GeneratedMesh?> GetGeneratedMeshAsync(Guid meshId, CancellationToken ct);

    /// <summary>작업 애그리게이트 및 하위 소유 엔티티 삭제</summary>
    /// <summary>
    /// **지금도 종료 상태일 때만** 지운다. 안 지웠으면 <c>null</c>.
    ///
    /// 읽어서 확인하고 따로 지우면 그 사이에 재시도나 3D 추가가 작업을 다시 열 수 있다.
    /// 그때 삭제가 그대로 진행되면 **유료 외부 작업이 돌고 있는데 그것을 추적할 기록이
    /// 사라진다.** 조건을 삭제문에 실어 한 번에 판정한다.
    /// </summary>
    Task<DeletedJobBlobs?> DeleteIfTerminalAsync(Guid jobId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Design Ref: §4.2 #7 — status=active(대기·실행) / terminal(성공·실패·취소).</summary>
public enum JobListFilter
{
    Active,
    Terminal,
}

/// <summary>
/// 지워진 작업이 붙들고 있던 저장소 키들.
///
/// **DB 를 지우기 전에 모아야 한다.** 지운 뒤에는 어느 파일이 그 작업 것이었는지
/// 알 방법이 없다 — 키는 행에만 적혀 있다.
///
/// 두 목록으로 나눈 이유는 저장소가 둘이기 때문이다. 이미지와 3D 산출물은 다른
/// 컨테이너에 있고 검증 규칙도 다르다.
/// </summary>
public sealed record DeletedJobBlobs(
    IReadOnlyList<string> Images,
    IReadOnlyList<string> Meshes);
