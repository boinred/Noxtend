using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 외부 유료 제출 한 번의 상태.
///
/// Design Ref: §5.1~5.3 · Plan D-06 · NFR-01
///
/// **이 타입이 존재하는 이유는 중복 과금이다.** HTTP 외부 호출에는 원자적 exactly-once 가
/// 없다. POST 가 성공한 뒤 응답을 받기 전에 프로세스가 죽으면, 작업이 만들어졌는지
/// 우리는 알 수 없다. 그래서 **결과 유실보다 중복 과금을 막는 쪽**을 기본값으로 삼고,
/// 그 판단을 상태 전이 규칙으로 못박는다.
///
/// 상태를 값으로 두면 재기동한 워커가 "지금 무엇을 해도 되는가" 를 DB 만 보고 안다.
/// </summary>
public sealed class MeshRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewRun_StartsPreparingItsFourInputs()
    {
        var run = Start();

        Assert.Equal(MeshRunStatus.PreparingInputs, run.Status);
        Assert.Equal(4, run.Inputs.Count);
        Assert.All(run.Inputs, input => Assert.Null(input.ProviderFileToken));
    }

    /// <summary>
    /// **한 번 받은 token 은 덮어쓰지 않는다** (§5.3 재기동 규칙).
    ///
    /// 재기동한 워커가 이미 올린 방향을 다시 올리면 그만큼 트래픽과 시간이 낭비된다.
    /// 유료 생성은 아니지만 네 장 중 셋을 다시 올리는 일이 매 재시도마다 반복된다.
    /// </summary>
    [Fact]
    public void UploadedDirection_KeepsItsFirstToken()
    {
        var run = Start();
        run.RecordPrepared(ViewDirection.Front, "file_first", "image/jpeg", Now);

        run.RecordPrepared(ViewDirection.Front, "file_second", "image/jpeg", Now);

        Assert.Equal("file_first", Input(run, ViewDirection.Front).ProviderFileToken);
    }

    /// <summary>
    /// 정면+비정면 2장짜리 실행도 그 둘만 준비되면 제출 가능해야 한다 (spec 20260917).
    ///
    /// `MeshInputSet`은 정면 필수 + 비정면 0장 이상으로 완화됐는데, `HasAllInputs`가
    /// 여전히 4장을 하드코딩하면 이 완화가 `MeshRun` 계층에서 무력화된다.
    /// </summary>
    [Fact]
    public void TwoInputRun_ReachesReadyToSubmit_WhenBothArePrepared()
    {
        var inputs = new MeshInputSet(Guid.NewGuid(), backImageId: Guid.NewGuid());
        var run = MeshRun.Start(
            jobId: Guid.NewGuid(), taskId: Guid.NewGuid(), partId: Guid.NewGuid(), runNumber: 1,
            providerConfigId: Guid.NewGuid(), model: "P1-20260311", inputs: inputs,
            modelSeed: 1, textureSeed: 2, now: Now);

        Assert.Equal(2, run.Inputs.Count);

        run.RecordPrepared(ViewDirection.Front, "file_f", "image/jpeg", Now);
        run.RecordPrepared(ViewDirection.Back, "file_b", "image/jpeg", Now);

        Assert.True(run.HasAllInputs);
        Assert.Equal(MeshRunStatus.ReadyToSubmit, run.Status);
    }

    [Fact]
    public void PartialUploads_DoNotReachReadyToSubmit()
    {
        var run = Start();

        run.RecordPrepared(ViewDirection.Front, "file_f", "image/jpeg", Now);
        run.RecordPrepared(ViewDirection.Right, "file_r", "image/jpeg", Now);
        run.RecordPrepared(ViewDirection.Back, "file_b", "image/jpeg", Now);

        // 세 장으로 제출하면 Tripo 가 없는 면을 지어낸다
        Assert.Equal(MeshRunStatus.Uploading, run.Status);
    }

    [Fact]
    public void FourUploads_ReachReadyToSubmit()
    {
        var run = Uploaded();

        Assert.Equal(MeshRunStatus.ReadyToSubmit, run.Status);
    }

    /// <summary>
    /// **POST 전에 `Submitting` 을 먼저 저장한다** (Plan D-06).
    ///
    /// 이 저장이 없으면 재기동한 워커는 "아직 안 보냈다" 와 "보냈는데 응답을 못 받았다" 를
    /// 구별할 수 없다. 저장해 두면 뒤의 경우가 상태로 남는다.
    /// </summary>
    [Fact]
    public void SubmittingBeforeTaskId_IsTheAmbiguousState()
    {
        var run = Uploaded();

        run.BeginSubmit(Now);

        Assert.Equal(MeshRunStatus.Submitting, run.Status);
        Assert.Null(run.ProviderTaskId);
    }

    [Fact]
    public void CannotSubmit_BeforeAllFourTokens()
    {
        var run = Start();
        run.RecordPrepared(ViewDirection.Front, "file_f", "image/jpeg", Now);

        Assert.Throws<InvalidOperationException>(() => run.BeginSubmit(Now));
    }

    /// <summary>제출 전에 외부 작업 ID 가 있을 수 없다 — 있다면 어디서 왔는지 설명되지 않는다.</summary>
    [Fact]
    public void CannotRecordProviderTask_WithoutSubmittingFirst()
    {
        var run = Uploaded();

        Assert.Throws<InvalidOperationException>(() => run.RecordProviderTask("task_1", Now));
    }

    [Fact]
    public void RecordingProviderTask_MovesToSubmitted()
    {
        var run = Submitted();

        Assert.Equal(MeshRunStatus.Submitted, run.Status);
        Assert.Equal("task_1", run.ProviderTaskId);
    }

    /// <summary>
    /// **외부 작업 ID 는 한 번만 붙는다.** 덮어쓸 수 있으면 두 번째 유료 제출의 결과가
    /// 첫 번째의 자리에 조용히 들어앉는다 — 첫 제출은 과금된 채 추적을 잃는다.
    /// </summary>
    [Fact]
    public void ProviderTaskId_IsSetOnlyOnce()
    {
        var run = Submitted();

        Assert.Throws<InvalidOperationException>(() => run.RecordProviderTask("task_2", Now));
    }

    /// <summary>
    /// 명시적 429 만 되돌린다 (§5.3).
    ///
    /// 작업이 만들어지지 않았음이 확실한 유일한 응답이다. 나머지 불확실한 실패는
    /// <see cref="MeshRunStatus.SubmissionUnknown"/> 으로 끝나고 자동 재제출하지 않는다.
    /// </summary>
    [Fact]
    public void RateLimitedSubmit_ReturnsToReadyToSubmit()
    {
        var run = Uploaded();
        run.BeginSubmit(Now);

        run.ReleaseForResubmit(Now);

        Assert.Equal(MeshRunStatus.ReadyToSubmit, run.Status);
        Assert.Null(run.ProviderTaskId);
    }

    [Fact]
    public void UnknownSubmit_DoesNotAllowAnotherAttempt()
    {
        var run = Uploaded();
        run.BeginSubmit(Now);

        run.MarkSubmissionUnknown("MESH_SUBMISSION_UNKNOWN", providerCode: null, requestId: "req_1", Now);

        Assert.Equal(MeshRunStatus.SubmissionUnknown, run.Status);
        // 다시 보내면 이미 만들어졌을지 모르는 유료 작업을 하나 더 만든다
        Assert.Throws<InvalidOperationException>(() => run.BeginSubmit(Now));
    }

    /// <summary>
    /// **토큰 없이 준비된 입력도 관문을 넘는다** (§11.1 D-04 · D-11).
    ///
    /// 이 검사가 없으면 업로드 단계가 없는 공급자는 제출에 영영 도달하지 못한다 —
    /// `HasAllInputs` 가 토큰 유무로 판정하던 때가 그랬고, 예외도 안 나고 그냥 멈춘다.
    /// </summary>
    [Fact]
    public void InputsPreparedWithoutTokens_StillReachReadyToSubmit()
    {
        var run = Start();

        foreach (var (direction, _) in Inputs.Pairs())
        {
            // 비내구적 핸들 — 요청 본문에 실릴 값이라 저장하지 않는다
            run.RecordPrepared(direction, providerFileToken: null, "image/png", Now);
        }

        Assert.Equal(MeshRunStatus.ReadyToSubmit, run.Status);
        Assert.True(run.HasAllInputs);
        Assert.All(run.Inputs, input => Assert.Null(input.ProviderFileToken));
    }

    /// <summary>
    /// 다시 준비할지는 **토큰이 정한다** (§11.1 D-05).
    ///
    /// 준비했다는 사실과 다시 쓸 수 있다는 사실이 다르므로 값을 나눈다.
    /// </summary>
    [Fact]
    public void PreparationIsRepeated_OnlyWhenNoTokenWasKept()
    {
        var durable = Start();
        durable.RecordPrepared(ViewDirection.Front, "file_f", "image/jpeg", Now);

        var transient = Start();
        transient.RecordPrepared(ViewDirection.Front, null, "image/png", Now);

        Assert.False(Input(durable, ViewDirection.Front).NeedsPreparation);
        Assert.True(Input(transient, ViewDirection.Front).NeedsPreparation);

        // 준비 자체는 둘 다 끝났다
        Assert.True(Input(durable, ViewDirection.Front).IsPrepared);
        Assert.True(Input(transient, ViewDirection.Front).IsPrepared);
    }

    /// <summary>
    /// **제출한 뒤에는 입력을 건드릴 수 없다** (§3.4).
    ///
    /// 비내구적 핸들을 쓰는 공급자는 매번 다시 준비하는데, 여기가 열려 있으면 조회 중인
    /// 실행이 준비 단계로 되돌아간다 — 그러면 이미 과금된 작업을 놓친다.
    /// </summary>
    [Fact]
    public void PreparingInputs_IsRefusedAfterSubmission()
    {
        var run = Polling();

        Assert.Throws<InvalidOperationException>(
            () => run.RecordPrepared(ViewDirection.Front, null, "image/png", Now));
    }

    // ─── polling ───

    [Fact]
    public void Progress_MovesForwardOnly()
    {
        var run = Polling();
        run.RecordProgress(64, Now);

        run.RecordProgress(12, Now);

        // 공급자가 가끔 낮은 값을 되돌려 준다. 화면의 막대가 뒤로 가면 사용자는 실패로 읽는다
        Assert.Equal(64, run.Progress);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Progress_OutsideZeroToHundred_IsRejected(int progress)
    {
        var run = Polling();

        Assert.Throws<ArgumentOutOfRangeException>(() => run.RecordProgress(progress, Now));
    }

    // ─── 결과 ───

    [Fact]
    public void ArtifactsReady_RequiresTheModelBlob()
    {
        var run = Polling();
        run.BeginDownload(Now);

        // GLB 없이 완료로 두면 공정이 성공하는데 내려받을 것이 없다
        Assert.Throws<InvalidOperationException>(() => run.MarkArtifactsReady(credits: 50, Now));
    }

    [Fact]
    public void StoringTheModel_ThenCompleting_ReachesArtifactsReady()
    {
        var run = Polling();
        run.BeginDownload(Now);
        run.RecordArtifact(MeshArtifactKind.Glb, "meshes/abc/model.glb", "model/gltf-binary", 4_821_900, Now);

        run.MarkArtifactsReady(credits: 50, Now);

        Assert.Equal(MeshRunStatus.ArtifactsReady, run.Status);
        Assert.Equal(50, run.CreditsConsumed);
    }

    /// <summary>미리보기는 없을 수 있다 — 공급자가 항상 주지는 않는다.</summary>
    [Fact]
    public void PreviewIsOptional()
    {
        var run = Polling();
        run.BeginDownload(Now);
        run.RecordArtifact(MeshArtifactKind.Glb, "meshes/abc/model.glb", "model/gltf-binary", 4_821_900, Now);
        run.MarkArtifactsReady(credits: null, Now);

        Assert.Null(run.Find(MeshArtifactKind.Preview));
    }

    // ─── 종료 ───

    [Fact]
    public void CompletedRun_RejectsFurtherTransitions()
    {
        var run = Polling();
        run.BeginDownload(Now);
        run.RecordArtifact(MeshArtifactKind.Glb, "meshes/abc/model.glb", "model/gltf-binary", 4_821_900, Now);
        run.MarkArtifactsReady(credits: 50, Now);

        Assert.Throws<InvalidOperationException>(() => run.RecordProgress(80, Now));
        Assert.Throws<InvalidOperationException>(() => run.BeginDownload(Now));
    }

    [Fact]
    public void TimedOutRun_KeepsItsProviderTaskId()
    {
        var run = Polling();

        run.MarkTimedOut(Now);

        // 같은 작업을 다시 조회해 재개할 수 있어야 한다 — 새로 만들면 또 과금이다
        Assert.Equal(MeshRunStatus.TimedOut, run.Status);
        Assert.Equal("task_1", run.ProviderTaskId);
    }

    [Fact]
    public void LocalCancel_StopsTheRunWithoutClaimingRemoteCancellation()
    {
        var run = Polling();

        run.CancelLocally(Now);

        // 원격 취소 endpoint 가 없어 외부 작업은 계속 돌 수 있다 (Plan D-07).
        // 우리가 보장하는 것은 그 결과를 연결하지 않는다는 것뿐이다
        Assert.Equal(MeshRunStatus.LocalCanceled, run.Status);
    }

    // ─── 설정 ───

    private static readonly MeshInputSet Inputs =
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static MeshRun Start()
        => MeshRun.Start(
            jobId: Guid.NewGuid(),
            taskId: Guid.NewGuid(),
            partId: Guid.NewGuid(),
            runNumber: 1,
            providerConfigId: Guid.NewGuid(),
            model: "P1-20260311",
            inputs: Inputs,
            modelSeed: 123456,
            textureSeed: 654321,
            now: Now);

    private static MeshRun Uploaded()
    {
        var run = Start();

        foreach (var (direction, _) in Inputs.Pairs())
        {
            run.RecordPrepared(direction, $"file_{direction}", "image/jpeg", Now);
        }

        return run;
    }

    private static MeshRun Submitted()
    {
        var run = Uploaded();
        run.BeginSubmit(Now);
        run.RecordProviderTask("task_1", Now);
        return run;
    }

    private static MeshRun Polling()
    {
        var run = Submitted();
        run.BeginPolling(Now);
        return run;
    }

    private static MeshRunInput Input(MeshRun run, ViewDirection direction)
        => run.Inputs.Single(input => input.ViewDirection == direction);
}
