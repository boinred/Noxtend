using Noxtend.Domain.Job;

namespace Noxtend.Domain.Mesh;

/// <summary>
/// 외부 공급자에 대한 유료 제출 한 번. 애그리게이트 루트.
///
/// Design Ref: §3.2 · §5.1~5.3 · Plan D-06 · NFR-01
///
/// **작업과 별도 애그리게이트인 이유는 DbContext 다** (§3.2). 공정 실행은 리스 갱신 루프를
/// 돌리면서 같은 컨텍스트로 작업을 추적하는데, 여기서는 2초마다 진행률을 저장해야 한다.
/// 같은 컨텍스트를 나눠 쓰면 EF 의 동시 사용 금지에 걸린다. 그래서 checkpoint 마다 짧은
/// 컨텍스트를 따로 연다.
///
/// **이 타입이 존재하는 근본 이유는 중복 과금이다.** HTTP 외부 호출에는 원자적
/// exactly-once 가 없다. POST 가 성공한 뒤 응답을 받기 전에 죽으면 작업이 만들어졌는지
/// 알 수 없고, 그때 다시 보내면 같은 파츠에 두 번 과금된다. 결과 유실보다 중복 과금을
/// 막는 쪽을 기본값으로 삼고 그 판단을 상태 전이로 못박았다.
/// </summary>
public sealed class MeshRun
{
    private readonly List<MeshRunInput> _inputs = [];
    private readonly List<MeshArtifact> _artifacts = [];

    private MeshRun()
    {
        // EF Core 재구성용
    }

    private MeshRun(
        Guid id, Guid jobId, Guid taskId, Guid partId, int runNumber,
        Guid providerConfigId, string model, int modelSeed, int textureSeed, DateTimeOffset now)
    {
        Id = id;
        JobId = jobId;
        TaskId = taskId;
        PartId = partId;
        RunNumber = runNumber;
        ProviderConfigId = providerConfigId;
        Model = model;
        ModelSeed = modelSeed;
        TextureSeed = textureSeed;
        Status = MeshRunStatus.PreparingInputs;
        StartedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid JobId { get; private set; }

    /// <summary>이 실행을 낳은 3D 재구성 공정.</summary>
    public Guid TaskId { get; private set; }

    public Guid PartId { get; private set; }

    /// <summary>같은 공정의 몇 번째 실행인가. 수동 재시도가 새 번호를 만든다 (§4.7).</summary>
    public int RunNumber { get; private set; }

    public Guid ProviderConfigId { get; private set; }
    public string Model { get; private set; } = string.Empty;

    /// <summary>
    /// 재현용 난수 씨앗.
    ///
    /// 공급자가 정하게 두면 같은 입력으로 다시 돌려도 다른 결과가 나와, 실패가 입력 탓인지
    /// 운 탓인지 가릴 수 없다. 우리가 만들어 저장하면 재시도가 같은 조건에서 돈다.
    /// </summary>
    public int ModelSeed { get; private set; }

    public int TextureSeed { get; private set; }

    public MeshRunStatus Status { get; private set; }

    /// <summary>공급자 쪽 작업 ID. 이것이 있으면 재기동해도 다시 제출하지 않는다 (FR-07).</summary>
    public string? ProviderTaskId { get; private set; }

    public int Progress { get; private set; }
    public int? CreditsConsumed { get; private set; }

    /// <summary>진단용 공급자 오류 코드. 사용자에게 보이지 않는다 (§13.1).</summary>
    public int? LastProviderCode { get; private set; }

    /// <summary>공급자 문의용 요청 ID. 원문 메시지 대신 이것만 남긴다.</summary>
    public string? LastProviderRequestId { get; private set; }

    /// <summary>사용자에게 보여도 안전한 실패 코드 (§7.6).</summary>
    public string? FailureCode { get; private set; }

    /// <summary>
    /// 내려받아 저장한 산출물들 (§3.2 · D-08).
    ///
    /// **형식별 열이 아니라 목록인 것이 요점이다.** Meshy 는 GLB 와 FBX 를 함께 내고
    /// 공급자마다 주는 것이 다르다. 열로 두면 형식이 늘 때마다 스키마·저장·내려받기가
    /// 함께 넓어진다.
    /// </summary>
    public IReadOnlyList<MeshArtifact> Artifacts => _artifacts;

    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// 동시 쓰기 감지용 토큰 (§5.1).
    ///
    /// **그림자 속성이 아니라 실제 필드여야 한다.** checkpoint 는 매번 새 컨텍스트에서
    /// 저장되므로 인스턴스가 컨텍스트를 옮겨 다닌다. 값이 인스턴스에 없으면 새 컨텍스트가
    /// 기본값으로 비교해 **정상 저장까지 충돌로 오인한다** — 실제로 모든 3D 실행이 첫
    /// checkpoint 에서 죽었다.
    ///
    /// DB 가 채우므로 도메인은 읽지도 쓰지도 않는다.
    /// </summary>
    public byte[]? RowVersion { get; private set; }

    public IReadOnlyList<MeshRunInput> Inputs => _inputs;

    /// <summary>더 진행할 수 없는 상태.</summary>
    public bool IsTerminal =>
        Status is MeshRunStatus.ArtifactsReady or MeshRunStatus.Failed
               or MeshRunStatus.SubmissionUnknown or MeshRunStatus.TimedOut
               or MeshRunStatus.ResultExpired or MeshRunStatus.LocalCanceled;

    /// <summary>
    /// 이 실행이 쓰기로 한 방향이 전부 준비됐는가 — 제출의 전제다.
    ///
    /// **네 방향 고정이 아니다** (spec 20260917). `MeshInputSet`이 정면 필수 + 비정면
    /// 0장 이상으로 완화됐고, `_inputs`는 `Start()`에서 실제로 쓰기로 한 방향 수만큼만
    /// 채워진다 — 그래서 "넷"이 아니라 "이 실행이 가진 만큼"이 기준이다.
    ///
    /// **토큰이 아니라 준비 여부를 본다** (D-11). 토큰으로 판정하면 업로드 단계가 없는
    /// 공급자는 이 관문을 영원히 못 넘는다.
    /// </summary>
    public bool HasAllInputs => _inputs.Count > 0 && _inputs.All(input => input.IsPrepared);

    public static MeshRun Start(
        Guid jobId,
        Guid taskId,
        Guid partId,
        int runNumber,
        Guid providerConfigId,
        string model,
        MeshInputSet inputs,
        int modelSeed,
        int textureSeed,
        DateTimeOffset now)
    {
        var run = new MeshRun(
            Guid.NewGuid(), jobId, taskId, partId, runNumber,
            providerConfigId, model, modelSeed, textureSeed, now);

        // 이 실행이 실제로 쓰는 방향 수만큼만 행을 만든다(정면 필수 + 비정면 0장 이상).
        // 재기동한 워커는 빈 token 을 가진 행만 찾으면 된다
        run._inputs.AddRange(inputs.Pairs()
            .Select(pair => new MeshRunInput(run.Id, pair.Direction, pair.ImageId)));

        return run;
    }

    // ─── 입력 준비 ───

    /// <summary>
    /// 방향 하나의 입력이 공급자에게 보낼 수 있는 상태가 됐음을 기록한다.
    ///
    /// <paramref name="providerFileToken"/> 이 <c>null</c> 이면 **다시 쓸 수 없는 핸들**이라는
    /// 뜻이다 (D-10). 준비했다는 사실만 남고, 다음 재개 때 다시 만든다.
    /// </summary>
    public void RecordPrepared(
        ViewDirection direction, string? providerFileToken, string uploadContentType, DateTimeOffset now)
    {
        EnsureNotTerminal();

        // **제출한 뒤에는 입력을 건드릴 수 없다.** 비내구적 핸들을 쓰는 공급자는 매번
        // 다시 준비하는데, 여기가 열려 있으면 조회 중인 실행이 준비 단계로 되돌아간다
        if (Status is not (MeshRunStatus.PreparingInputs or MeshRunStatus.Uploading
            or MeshRunStatus.ReadyToSubmit))
        {
            throw new InvalidOperationException($"입력을 준비할 수 없는 상태입니다: {Status}");
        }

        var input = _inputs.FirstOrDefault(i => i.ViewDirection == direction)
            ?? throw new InvalidOperationException($"이 실행에 없는 방향입니다: {direction}");

        input.RecordPrepared(providerFileToken, uploadContentType, now);

        // 넷이 다 차야 제출 가능이다. 그 전에는 준비 중이다
        Status = HasAllInputs ? MeshRunStatus.ReadyToSubmit : MeshRunStatus.Uploading;
        Touch(now);
    }

    // ─── 제출 ───

    /// <summary>
    /// **POST 직전에 부른다** (Plan D-06). 이 저장이 곧 "보낼 참이었다" 는 기록이다.
    /// </summary>
    public void BeginSubmit(DateTimeOffset now)
    {
        EnsureNotTerminal();

        if (!HasAllInputs)
        {
            throw new InvalidOperationException(
                $"네 방향이 모두 올라간 뒤에만 제출할 수 있습니다. 현재 상태: {Status}");
        }

        if (Status is not (MeshRunStatus.ReadyToSubmit or MeshRunStatus.Uploading))
        {
            throw new InvalidOperationException($"제출할 수 없는 상태입니다: {Status}");
        }

        Status = MeshRunStatus.Submitting;
        Touch(now);
    }

    /// <summary>
    /// 응답에서 받은 외부 작업 ID 를 즉시 저장한다.
    ///
    /// **한 번만 붙는다.** 덮어쓸 수 있으면 두 번째 유료 제출의 결과가 첫 번째 자리에
    /// 조용히 들어앉고, 첫 제출은 과금된 채 추적을 잃는다.
    /// </summary>
    public void RecordProviderTask(string providerTaskId, DateTimeOffset now)
    {
        if (Status != MeshRunStatus.Submitting)
        {
            throw new InvalidOperationException(
                $"제출을 시작한 뒤에만 작업 ID 를 붙일 수 있습니다. 현재 상태: {Status}");
        }

        if (ProviderTaskId is not null)
        {
            throw new InvalidOperationException("외부 작업 ID 는 다시 붙일 수 없습니다");
        }

        ProviderTaskId = providerTaskId;
        Status = MeshRunStatus.Submitted;
        SubmittedAt = now;
        Touch(now);
    }

    /// <summary>
    /// 명시적 429 — 작업이 만들어지지 않았음이 확실한 유일한 응답이라 되돌린다 (§5.3).
    /// </summary>
    public void ReleaseForResubmit(DateTimeOffset now)
    {
        if (Status != MeshRunStatus.Submitting || ProviderTaskId is not null)
        {
            throw new InvalidOperationException($"되돌릴 수 없는 상태입니다: {Status}");
        }

        Status = MeshRunStatus.ReadyToSubmit;
        Touch(now);
    }

    // ─── 조회 ───

    public void BeginPolling(DateTimeOffset now)
    {
        EnsureNotTerminal();

        if (Status is not (MeshRunStatus.Submitted or MeshRunStatus.Polling))
        {
            throw new InvalidOperationException($"조회할 수 없는 상태입니다: {Status}");
        }

        Status = MeshRunStatus.Polling;
        Touch(now);
    }

    /// <summary>
    /// 진행률은 **앞으로만 간다.** 공급자가 가끔 낮은 값을 되돌려 주는데, 화면의 막대가
    /// 뒤로 가면 사용자는 실패로 읽는다.
    /// </summary>
    public void RecordProgress(int progress, DateTimeOffset now)
    {
        EnsureNotTerminal();
        ArgumentOutOfRangeException.ThrowIfLessThan(progress, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(progress, 100);

        if (progress <= Progress)
        {
            return;
        }

        Progress = progress;
        Touch(now);
    }

    // ─── 결과 ───

    public void BeginDownload(DateTimeOffset now)
    {
        EnsureNotTerminal();

        if (Status is not (MeshRunStatus.Polling or MeshRunStatus.Submitted or MeshRunStatus.Downloading))
        {
            throw new InvalidOperationException($"내려받을 수 없는 상태입니다: {Status}");
        }

        Status = MeshRunStatus.Downloading;
        Touch(now);
    }

    /// <summary>
    /// 산출물 하나를 저장했음을 기록한다.
    ///
    /// **같은 종류를 다시 기록하면 덮어쓴다.** 저장소가 실행 ID 로 결정된 키를 쓰므로
    /// 내려받다 죽어 다시 시도해도 Blob 은 같은 자리다 — 행이 둘이면 그 사실과 어긋난다.
    /// </summary>
    public void RecordArtifact(
        MeshArtifactKind kind, string blobKey, string contentType, long sizeBytes, DateTimeOffset now)
    {
        EnsureNotTerminal();

        var artifact = new MeshArtifact(kind, blobKey, contentType, sizeBytes, now);

        _artifacts.RemoveAll(existing => existing.Kind == kind);
        _artifacts.Add(artifact);
        Touch(now);
    }

    public MeshArtifact? Find(MeshArtifactKind kind)
        => _artifacts.FirstOrDefault(artifact => artifact.Kind == kind);

    /// <summary>
    /// 결과가 자체 저장소에 들어갔음을 확정한다.
    ///
    /// **GLB 없이는 완료가 아니다.** 여기를 통과하면 공정이 성공으로 확정되는데,
    /// 그러고도 내려받을 것이 없으면 사용자가 성공을 보고 빈손이 된다.
    /// </summary>
    public void MarkArtifactsReady(int? credits, DateTimeOffset now)
    {
        EnsureNotTerminal();

        if (Find(MeshArtifactKind.Glb) is null)
        {
            throw new InvalidOperationException("GLB 를 저장한 뒤에만 완료할 수 있습니다");
        }

        CreditsConsumed = credits;
        Progress = 100;
        Status = MeshRunStatus.ArtifactsReady;
        CompletedAt = now;
        Touch(now);
    }

    // ─── 실패와 취소 ───

    public void Fail(string failureCode, int? providerCode, string? requestId, DateTimeOffset now)
        => Finish(MeshRunStatus.Failed, failureCode, providerCode, requestId, now);

    /// <summary>제출 결과를 알 수 없다 — 자동 재제출을 막는 것이 이 상태의 전부다.</summary>
    public void MarkSubmissionUnknown(
        string failureCode, int? providerCode, string? requestId, DateTimeOffset now)
        => Finish(MeshRunStatus.SubmissionUnknown, failureCode, providerCode, requestId, now);

    /// <summary>최대 대기 시간 초과. 외부 작업 ID 는 그대로 두어 수동 재시도가 재개할 수 있게 한다.</summary>
    public void MarkTimedOut(DateTimeOffset now)
        => Finish(MeshRunStatus.TimedOut, "MESH_TIMEOUT", null, null, now);

    /// <summary>
    /// 시간 초과된 실행을 **같은 외부 작업으로** 되살린다 (§5.3 · §4.7).
    ///
    /// **이것이 시간 초과를 다른 실패와 가르는 이유다.** 새 실행을 만들면 이미 과금된
    /// 작업을 버리고 하나 더 만드는 셈이다. 외부에서는 아직 돌고 있을 수 있으므로
    /// 그 결과를 기다리는 편이 늘 싸다.
    ///
    /// 자동으로는 일어나지 않는다 — 사용자가 다시 시도를 눌렀을 때만이다.
    /// </summary>
    public void ResumeAfterTimeout(DateTimeOffset now)
    {
        if (Status != MeshRunStatus.TimedOut || ProviderTaskId is null)
        {
            throw new InvalidOperationException($"재개할 수 없는 실행입니다: {Status}");
        }

        Status = MeshRunStatus.Polling;
        FailureCode = null;
        CompletedAt = null;
        Touch(now);
    }

    public void MarkResultExpired(DateTimeOffset now)
        => Finish(MeshRunStatus.ResultExpired, "MESH_RESULT_EXPIRED", null, null, now);

    public void CancelLocally(DateTimeOffset now)
        => Finish(MeshRunStatus.LocalCanceled, null, null, null, now);

    private void Finish(
        MeshRunStatus status, string? failureCode, int? providerCode, string? requestId, DateTimeOffset now)
    {
        EnsureNotTerminal();

        Status = status;
        FailureCode = failureCode;
        LastProviderCode = providerCode ?? LastProviderCode;
        LastProviderRequestId = requestId ?? LastProviderRequestId;
        CompletedAt = now;
        Touch(now);
    }

    private void EnsureNotTerminal()
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"이미 끝난 실행입니다. 현재 상태: {Status}");
        }
    }

    private void Touch(DateTimeOffset now) => UpdatedAt = now;
}
