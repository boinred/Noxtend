using Noxtend.Domain.Sprites;

namespace Noxtend.Domain.Job;

/// <summary>
/// 공정 — 큐에 들어가고 재시도·취소되는 실행 단위.
///
/// Design Ref: §0 용어 사전 · §3.1
///
/// 파츠가 N개면 같은 단계의 공정이 N개 생긴다. 그래서 공정은 "단계" 가 아니라
/// 단계의 인스턴스다. 클래스 이름이 <c>Task</c> 가 아닌 것은 <see cref="System.Threading.Tasks.Task"/>
/// 와 충돌하기 때문이며, 문서·API·UI 에서는 모두 "공정" 이다.
///
/// 상태 전이는 이 클래스가 소유한다. 밖에서 Status 를 직접 바꿀 수 없으므로
/// 단계가 늘어도 전이 규칙이 흩어지지 않는다.
/// </summary>
public sealed class PipelineTask
{
    private PipelineTask()
    {
        // EF Core 재구성용. 도메인 코드는 Plan() 을 쓴다
    }

    private PipelineTask(
        Guid id,
        Guid jobId,
        TaskKind kind,
        int ordinal,
        Guid? dependsOnTaskId,
        Guid? providerConfigId,
        string? model)
    {
        Id = id;
        JobId = jobId;
        Kind = kind;
        Ordinal = ordinal;
        DependsOnTaskId = dependsOnTaskId;
        ProviderConfigId = providerConfigId;
        Model = model;
        Status = TaskStatus.Pending;
    }

    public Guid Id { get; private set; }
    public Guid JobId { get; private set; }
    public TaskKind Kind { get; private set; }
    public int Ordinal { get; private set; }

    /// <summary>
    /// 선형이면 직전 공정. 팬아웃이면 같은 부모를 가리키는 형제가 여럿.
    ///
    /// 팬인(다중 부모)은 간선 테이블이 필요하다 (§2.3). 단계가 하나뿐인 지금은
    /// 간선이 0개이므로 만들지 않는다 — 쓰이지 않는 구조는 검증되지 않는다.
    /// 그때의 변경은 추가형이다.
    /// </summary>
    public Guid? DependsOnTaskId { get; private set; }

    public Guid? ProviderConfigId { get; private set; }

    /// <summary>
    /// 이 공정이 호출할 모델 id.
    ///
    /// 공급자 설정이 아니라 **공정**이 모델을 갖는다. 공급자에 모델을 두면
    /// "공급자 하나 = 모델 하나" 가 되어 같은 키로 두 모델을 비교하려면 공급자를
    /// 두 번 등록해야 했다. 공정에 두면 분해 단계가 붙을 때
    /// "추출은 Opus, 생성은 Sonnet" 이 추가 변경 없이 가능하다 (§2.3 의 연장).
    ///
    /// <see cref="ProviderConfigId"/> 와 짝이다 — 둘 다 있거나 둘 다 없다.
    /// LLM 을 부르지 않는 미래의 단계(예: 이미지 합성)는 둘 다 null 이다.
    /// </summary>
    public string? Model { get; private set; }

    /// <summary>
    /// 이 공정이 그리는 파츠. 생성·3D 재구성 공정만 값을 갖고 나머지는 <c>null</c> 이다.
    ///
    /// Design Ref: §3.1 (사이클 #7)
    ///
    /// **공정이 파츠를 가리키는 방향인 이유**: 반대로 파츠가 공정을 가리키면 재시도 때
    /// 파츠가 어느 공정을 가리켜야 하는지가 모호해진다. 공정은 시도마다 늘고 파츠는 하나다.
    /// </summary>
    public Guid? PartId { get; private set; }

    /// <summary>
    /// 생성 공정이 그리는 파츠 방향. 나머지 공정은 <c>null</c>.
    ///
    /// **3D 재구성도 <c>null</c> 이다** — 파츠 하나를 통째로 만들지 방향마다 만들지 않는다.
    /// </summary>
    public ViewDirection? ViewDirection { get; private set; }

    /// <summary>
    /// 3D 재구성 공정이 쓸 네 방향 이미지. 그 공정만 값을 갖는다.
    ///
    /// Design Ref: §4.3 · FR-04
    ///
    /// **이것이 팬인의 의존을 대신한다.** <see cref="DependsOnTaskId"/> 는 부모 하나만
    /// 가리키는데 3D 는 이미지 넷을 기다린다. 네 ID 가 채워졌다는 사실 자체가 넷이 실제로
    /// 존재한다는 증거이므로, 이 공정은 의존 없이도 바로 실행 가능하다 (§4.4).
    /// </summary>
    public MeshInputSet? MeshInputs { get; private set; }

    public SpriteFrameInput? SpriteInput { get; private set; }
    public SpriteExportInput? SpriteExportInput { get; private set; }
    public Guid? RequestId { get; private set; }

    internal void BindSpriteInput(SpriteFrameInput input) => SpriteInput = input;
    public void BindSpriteExport(SpriteExportInput input)
    {
        if (Status != TaskStatus.Pending || SpriteInput is not null || SpriteExportInput is not null)
            throw new InvalidOperationException("대기 중인 미연결 공정만 내보내기에 연결할 수 있습니다");
        SpriteExportInput = input;
    }
    public void BindRequest(Guid requestId) => RequestId = requestId;

    public TaskStatus Status { get; private set; }
    public int AttemptCount { get; private set; }

    /// <summary>
    /// 워커가 이 시각까지 이 공정을 물고 있다고 선언한 값.
    /// 만료되면 스위퍼가 되돌린다 — Redis 장애를 유실이 아니라 지연으로 만드는 장치다.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public string? FailureReason { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// 동시성 토큰.
    ///
    /// **한 공정을 워커 둘이 든 적이 실제로 있다.** 한쪽은 3D 중복 삽입에 막혀 실패를,
    /// 다른 쪽은 성공을 썼는데, EF 가 바뀐 열만 쓰는 탓에 두 변경이 한 행에 **섞였다** —
    /// 상태는 성공인데 실패 사유가 달린 행이 남았다. 화면이 그것을 읽으면 성공한 작업에
    /// 실패 딱지가 붙는다.
    ///
    /// 나중 쓰기가 앞의 것을 모른 채 덮지 못하게 한다. `MeshRuns` 는 같은 이유로 이미
    /// 이것을 쓰고 있었고, `Tasks` 만 빠져 있었다.
    ///
    /// **그림자 속성으로 두면 안 된다** — 공정은 매번 새 컨텍스트에서 저장되는데, 그림자
    /// 값은 추적기에만 살아 있어 다음 컨텍스트가 기본값으로 비교한다. 정상 저장이 전부
    /// 충돌로 오인된다 (`MeshRunConfiguration` 이 같은 함정을 겪었다).
    /// </summary>
    public byte[]? RowVersion { get; private set; }

    /// <summary>
    /// 이 시각 전까지는 대기 중이어도 재시도하지 않는다 (generation-rate-limiting §3②).
    /// <see cref="ReleaseForRetry"/> 가 지수 백오프로 채우고, <c>Claim</c> 이 다시 물면 지운다.
    /// </summary>
    public DateTimeOffset? NotBefore { get; private set; }

    internal static PipelineTask Plan(
        Guid jobId,
        TaskKind kind,
        int ordinal,
        Guid? dependsOnTaskId,
        Guid? providerConfigId,
        string? model)
    {
        // 공급자와 모델은 짝이다. 한쪽만 있는 공정은 워커가 집는 순간 실패하므로
        // 계획 시점에 막는다 — 사용자가 이유를 아는 자리에서 알게 된다
        var hasProvider = providerConfigId is not null;
        var hasModel = !string.IsNullOrWhiteSpace(model);

        if (hasProvider != hasModel)
        {
            throw new ArgumentException(
                "공급자와 모델은 함께 지정해야 합니다. " +
                $"공급자={(hasProvider ? "있음" : "없음")}, 모델={(hasModel ? "있음" : "없음")}");
        }

        return new PipelineTask(
            Guid.NewGuid(), jobId, kind, ordinal, dependsOnTaskId, providerConfigId,
            hasModel ? model!.Trim() : null);
    }

    /// <summary>
    /// 생성 공정을 파츠에 묶는다. 계획 직후 <see cref="PipelineJob.PlanReadyFollowUpTasks"/> 만 부른다.
    /// </summary>
    internal void BindToPart(Guid partId, ViewDirection viewDirection)
    {
        PartId = partId;
        ViewDirection = viewDirection;
    }

    /// <summary>
    /// 3D 재구성 공정을 파츠와 그 입력 이미지 넷에 묶는다.
    ///
    /// 방향을 주지 않는 것이 생성과의 차이다 — 3D 는 파츠 하나에 결과 하나다.
    /// </summary>
    internal void BindToMeshInputs(Guid partId, MeshInputSet inputs)
    {
        PartId = partId;
        MeshInputs = inputs;
    }

    // 수동 재시도의 시도 번호 재사용도 이전 SQL 소유 버전으로 구분
    public bool IsOwnedBy(int attempt, DateTimeOffset now, byte[]? ownershipVersion)
        => Status == TaskStatus.Running && AttemptCount == attempt && LeaseExpiresAt > now
            && (ownershipVersion is null || RowVersion is not null && RowVersion.SequenceEqual(ownershipVersion));

    public bool IsTerminal =>
        Status is TaskStatus.Succeeded or TaskStatus.Failed or TaskStatus.Canceled;

    /// <summary>워커가 공정을 집는다. 리스를 걸고 시도 횟수를 올린다.</summary>
    public void Claim(DateTimeOffset now, TimeSpan lease)
    {
        if (Status != TaskStatus.Pending)
        {
            throw new InvalidOperationException(
                $"대기 중인 공정만 집을 수 있습니다. 현재 상태: {Status}");
        }

        Status = TaskStatus.Running;
        AttemptCount++;
        StartedAt ??= now;
        LeaseExpiresAt = now + lease;
        NotBefore = null;
    }

    /// <summary>처리 중 주기적으로 호출한다. 만료가 미래로 이동한다.</summary>
    public void RenewLease(DateTimeOffset now, TimeSpan lease)
    {
        if (Status != TaskStatus.Running)
        {
            throw new InvalidOperationException(
                $"실행 중인 공정만 리스를 갱신할 수 있습니다. 현재 상태: {Status}");
        }

        LeaseExpiresAt = now + lease;
    }

    /// <summary>
    /// 성공 확정.
    ///
    /// **Canceled 에서 호출하면 예외다.** 워커가 취소를 놓치고 결과를 쓰려 해도
    /// 여기서 막힌다 — 화면은 취소됐는데 결과가 나타나는 일이 없다 (§2.2 안전장치).
    /// </summary>
    public void Succeed(DateTimeOffset now)
    {
        if (Status != TaskStatus.Running)
        {
            throw new InvalidOperationException(
                $"실행 중인 공정만 성공할 수 있습니다. 현재 상태: {Status}");
        }

        Status = TaskStatus.Succeeded;
        LeaseExpiresAt = null;
        NotBefore = null;
        CompletedAt = now;
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException(
                $"종료된 공정은 실패로 바꿀 수 없습니다. 현재 상태: {Status}");
        }

        Status = TaskStatus.Failed;
        FailureReason = reason;
        LeaseExpiresAt = null;
        CompletedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (IsTerminal)
        {
            return; // 이미 끝난 공정의 재취소는 무시한다 — 취소는 멱등이어야 한다
        }

        Status = TaskStatus.Canceled;
        LeaseExpiresAt = null;
        CompletedAt = now;
    }

    /// <summary>
    /// 실행 중이던 공정을 다시 대기 상태로 되돌린다 — 스위퍼(리스 만료)와
    /// <c>TaskExecution.Apply</c>(실패 재시도) 둘 다 호출한다.
    /// 되돌리는 것은 상태뿐이고 <see cref="AttemptCount"/> 는 유지되므로,
    /// 한도를 넘으면 실패로 확정할 수 있다.
    /// </summary>
    /// <param name="notBefore">
    /// 주어지면 이 시각까지 <c>IsReadyToRun</c> 이 거짓을 돌려준다 (generation-rate-limiting
    /// §3② 지수 백오프). 스위퍼는 넘기지 않는다 — 리스 만료는 레이트리밋이 아니라 워커
    /// 유실이라 즉시 재시도가 맞다.
    /// </param>
    public void ReleaseForRetry(DateTimeOffset? notBefore = null)
    {
        if (Status != TaskStatus.Running)
        {
            throw new InvalidOperationException(
                $"실행 중인 공정만 회수할 수 있습니다. 현재 상태: {Status}");
        }

        Status = TaskStatus.Pending;
        LeaseExpiresAt = null;
        NotBefore = notBefore;
    }

    /// <summary>
    /// 사용자가 명시적으로 다시 돌린다 (사이클 #7 FR-08).
    ///
    /// <see cref="ReleaseForRetry"/> 와 다른 점은 **시도 횟수를 0 으로 되돌린다**는 것이다.
    /// 자동 재시도 한도는 "공급자가 흔들리는가" 를 재는 값이라 사용자의 결정과 예산을
    /// 나눠 쓰지 않는다. 실패 사유도 지운다 — 남겨 두면 성공한 뒤에도 화면에 뜬다.
    /// </summary>
    internal void ResetForManualRetry()
    {
        Status = TaskStatus.Pending;
        AttemptCount = 0;
        LeaseExpiresAt = null;
        NotBefore = null;
        FailureReason = null;
        CompletedAt = null;
    }

    public bool IsLeaseExpired(DateTimeOffset now) =>
        Status == TaskStatus.Running && LeaseExpiresAt is { } expires && expires <= now;
}
