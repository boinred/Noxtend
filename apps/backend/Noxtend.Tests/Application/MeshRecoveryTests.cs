using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Application.Common;
using Noxtend.Application.Mesh;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Mesh;

namespace Noxtend.Tests.Application;

/// <summary>
/// 프로세스가 죽었다 살아나도 같은 파츠에 두 번 과금하지 않는가.
///
/// Design Ref: §14.5 · Plan D-06 · NFR-01
///
/// **이것이 이 사이클에서 가장 중요한 계약이다.** 나머지는 틀리면 결과가 안 나오지만,
/// 여기가 틀리면 결과는 나오고 돈만 두 배로 나간다 — 그래서 아무도 알아채지 못한다.
///
/// 매 시나리오는 실행 상태를 중간 지점에 두고 핸들러를 **다시** 돌린다. 재기동한 워커가
/// 보는 것과 정확히 같은 상황이다.
/// </summary>
public sealed class MeshRecoveryTests
{
    /// <summary>이미 올린 방향은 다시 올리지 않는다 (§5.3).</summary>
    [Fact]
    public async Task ResumingAfterPartialUpload_OnlyUploadsWhatIsMissing()
    {
        var world = await MeshWorld.ReadyAsync();
        var run = await world.StartRunAsync();

        // 둘째까지 올리고 죽었다
        run.RecordPrepared(ViewDirection.Front, "file_f", "image/jpeg", MeshWorld.Now);
        run.RecordPrepared(ViewDirection.Right, "file_r", "image/jpeg", MeshWorld.Now);
        await world.Runs.SaveAsync(run, default);

        await world.RunAsync();

        // 남은 둘만 올라간다 — 앞의 둘을 다시 올리면 매 재시도마다 그 왕복이 반복된다
        Assert.Equal(
            [ViewDirection.Back, ViewDirection.Left],
            world.Provider.Uploaded.Order().ToArray());
    }

    /// <summary>
    /// **제출 직후 죽은 경우가 가장 위험하다** (Plan D-06).
    ///
    /// 작업이 만들어졌는지 알 수 없다. 다시 보내면 같은 파츠에 두 번 과금된다.
    /// </summary>
    [Fact]
    public async Task ResumingFromSubmitting_DoesNotSendAgain()
    {
        var world = await MeshWorld.ReadyAsync();
        var run = await world.UploadedRunAsync();

        run.BeginSubmit(MeshWorld.Now);
        run.MarkSubmissionUnknown("MESH_SUBMISSION_UNKNOWN", null, null, MeshWorld.Now);
        await world.Runs.SaveAsync(run, default);

        var outcome = await world.RunAsync();

        Assert.Equal(0, world.Provider.Submits);
        Assert.Equal(RunTaskOutcome.Failed, outcome);
    }

    /// <summary>작업 ID 가 저장돼 있으면 보내지 않고 조회만 재개한다 (FR-07).</summary>
    [Fact]
    public async Task ResumingWithAProviderTaskId_PollsWithoutSubmitting()
    {
        var world = await MeshWorld.ReadyAsync();
        var run = await world.UploadedRunAsync();

        run.BeginSubmit(MeshWorld.Now);
        run.RecordProviderTask("task_existing", MeshWorld.Now);
        await world.Runs.SaveAsync(run, default);

        await world.RunAsync();

        Assert.Equal(0, world.Provider.Submits);
        Assert.Contains("task_existing", world.Provider.Polled);
    }

    /// <summary>
    /// 입력이 달라지면 이미 성공한 실행이어도 새로 돈다 (spec 20260917, ReplanMeshInputs).
    ///
    /// "끝난 실행은 이어받지 않는다"는 원래 재시도가 항상 같은 입력이라는 전제였다.
    /// 파츠별 뷰 재선택은 그 전제를 깬다 — 입력이 다르면 재사용하면 안 된다.
    /// </summary>
    [Fact]
    public async Task DifferentInputs_StartANewRunEvenAfterArtifactsReady()
    {
        var world = await MeshWorld.ReadyAsync();
        var run = await world.UploadedRunAsync();
        run.BeginSubmit(MeshWorld.Now);
        run.RecordProviderTask("task_done", MeshWorld.Now);
        run.BeginPolling(MeshWorld.Now);
        run.BeginDownload(MeshWorld.Now);
        run.RecordArtifact(MeshArtifactKind.Glb, "meshes/x/model.glb", "model/gltf-binary", 1234, MeshWorld.Now);
        run.MarkArtifactsReady(credits: 50, MeshWorld.Now);
        await world.Runs.SaveAsync(run, default);

        var partId = world.Task.PartId!.Value;
        var frontId = world.Job.GeneratedImages.Single(i => i.PartId == partId && i.ViewDirection == ViewDirection.Front).Id;
        var backId = world.Job.GeneratedImages.Single(i => i.PartId == partId && i.ViewDirection == ViewDirection.Back).Id;
        world.Job.ReplanMeshInputs(
            partId, world.Task.ProviderConfigId!.Value, world.Task.Model!,
            new MeshInputSet(frontId, backImageId: backId), MeshWorld.Now);
        await world.Jobs.SaveChangesAsync(default);

        var outcome = await world.RunAsync();

        Assert.Equal(1, world.Provider.Submits);
        Assert.Equal(RunTaskOutcome.Succeeded, outcome);
        var latest = await world.Runs.GetLatestByTaskAsync(world.Task.Id, default);
        Assert.Equal(2, latest!.RunNumber);
    }

    /// <summary>결과까지 저장된 실행은 외부를 아예 부르지 않는다 (§5.3).</summary>
    [Fact]
    public async Task ResumingFromArtifactsReady_MakesNoProviderCall()
    {
        var world = await MeshWorld.ReadyAsync();
        var run = await world.UploadedRunAsync();

        run.BeginSubmit(MeshWorld.Now);
        run.RecordProviderTask("task_done", MeshWorld.Now);
        run.BeginPolling(MeshWorld.Now);
        run.BeginDownload(MeshWorld.Now);
        run.RecordArtifact(MeshArtifactKind.Glb, "meshes/x/model.glb", "model/gltf-binary", 1234, MeshWorld.Now);
        run.MarkArtifactsReady(credits: 50, MeshWorld.Now);
        await world.Runs.SaveAsync(run, default);

        var outcome = await world.RunAsync();

        Assert.Equal(0, world.Provider.Submits);
        Assert.Empty(world.Provider.Polled);

        // 그래도 결과는 붙어야 한다 — 저장은 끝났고 잇는 것만 남았다
        Assert.Equal(RunTaskOutcome.Succeeded, outcome);
        Assert.Single(world.Job.GeneratedMeshes);
    }

    /// <summary>
    /// 시간 초과는 **같은 외부 작업을 이어받는다** (§4.7).
    ///
    /// 새 실행을 만들면 이미 과금된 작업을 버리고 하나 더 만드는 셈이다.
    /// </summary>
    [Fact]
    public async Task RetryingAfterTimeout_ResumesTheSameProviderTask()
    {
        var world = await MeshWorld.ReadyAsync();
        var run = await world.UploadedRunAsync();

        run.BeginSubmit(MeshWorld.Now);
        run.RecordProviderTask("task_slow", MeshWorld.Now);
        run.BeginPolling(MeshWorld.Now);
        run.MarkTimedOut(MeshWorld.Now);
        await world.Runs.SaveAsync(run, default);

        world.RetryTask();
        await world.RunAsync();

        Assert.Equal(0, world.Provider.Submits);
        Assert.Contains("task_slow", world.Provider.Polled);
    }

    /// <summary>
    /// 확정 실패한 실행은 **새 번호로 다시 시작한다** (§4.7).
    ///
    /// 이전 시도의 기록이 남아야 어느 실행이 얼마를 썼는지 나중에 셀 수 있다.
    /// </summary>
    [Fact]
    public async Task RetryingAfterFailure_StartsANewRun()
    {
        var world = await MeshWorld.ReadyAsync();
        var run = await world.UploadedRunAsync();

        run.BeginSubmit(MeshWorld.Now);
        run.RecordProviderTask("task_dead", MeshWorld.Now);
        run.BeginPolling(MeshWorld.Now);
        run.Fail("MESH_TASK_FAILED", null, null, MeshWorld.Now);
        await world.Runs.SaveAsync(run, default);

        world.RetryTask();
        await world.RunAsync();

        var latest = await world.Runs.GetLatestByTaskAsync(world.Task.Id, default);

        Assert.Equal(2, latest!.RunNumber);
        Assert.Equal(1, world.Provider.Submits);
    }

    /// <summary>취소된 작업에는 늦게 도착한 결과가 붙지 않는다 (FR-12).</summary>
    [Fact]
    public async Task CanceledJob_DoesNotGetItsMeshAttached()
    {
        var world = await MeshWorld.ReadyAsync();

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        var outcome = await world.RunAsync(canceled.Token);

        Assert.Equal(RunTaskOutcome.Canceled, outcome);
        Assert.Empty(world.Job.GeneratedMeshes);
    }

    /// <summary>관통 — 아무것도 저장돼 있지 않은 상태에서 끝까지 간다.</summary>
    [Fact]
    public async Task FreshRun_UploadsSubmitsPollsAndStoresTheResult()
    {
        var world = await MeshWorld.ReadyAsync();

        var outcome = await world.RunAsync();

        Assert.Equal(RunTaskOutcome.Succeeded, outcome);
        Assert.Equal(4, world.Provider.Uploaded.Count);
        Assert.Equal(1, world.Provider.Submits);

        var mesh = Assert.Single(world.Job.GeneratedMeshes);

        // 공급자 URL 이 아니라 우리 Blob 키여야 만료와 무관하게 내려받힌다 (NFR-07)
        Assert.StartsWith("meshes/", mesh.Model.BlobKey);
        Assert.Equal("model/gltf-binary", mesh.Model.ContentType);
    }

    /// <summary>
    /// **제출 결과를 모르는 공정은 사용자가 다시 돌릴 수 없다** (§10.5 · Plan D-06).
    ///
    /// 다시 돌리면 이미 만들어졌을지 모르는 유료 작업을 하나 더 만든다. 운영자가 공급자
    /// 대시보드에서 중복 여부를 확인하는 절차가 아직 없으므로 확정 거절한다.
    /// </summary>
    [Fact]
    public async Task RetryingAnUnknownSubmission_IsRefused()
    {
        var world = await MeshWorld.ReadyAsync();
        var run = await world.UploadedRunAsync();

        run.BeginSubmit(MeshWorld.Now);
        run.MarkSubmissionUnknown("MESH_SUBMISSION_UNKNOWN", null, null, MeshWorld.Now);
        await world.Runs.SaveAsync(run, default);

        await world.RunAsync();

        var result = await world.RetryHandler.HandleAsync(world.Job.Id, world.Task.Id, default);

        Assert.Equal(ErrorCode.MeshSubmissionUnknown, result.ErrorCode);
    }

    /// <summary>실패한 3D 공정은 다시 돌릴 수 있다 (§4.7).</summary>
    [Fact]
    public async Task RetryingAPlainFailure_IsAllowed()
    {
        var world = await MeshWorld.ReadyAsync();
        var run = await world.UploadedRunAsync();

        run.BeginSubmit(MeshWorld.Now);
        run.RecordProviderTask("task_dead", MeshWorld.Now);
        run.BeginPolling(MeshWorld.Now);
        run.Fail("MESH_TASK_FAILED", null, null, MeshWorld.Now);
        await world.Runs.SaveAsync(run, default);

        world.FailTask();

        var result = await world.RetryHandler.HandleAsync(world.Job.Id, world.Task.Id, default);

        Assert.True(result.IsSuccess);
    }

    /// <summary>같은 실행이 다시 내려받으면 같은 자리에 덮어쓴다 (§6.4).</summary>
    [Fact]
    public async Task RedownloadingWritesToTheSameKey()
    {
        var world = await MeshWorld.ReadyAsync();
        await world.RunAsync();

        var first = world.Job.GeneratedMeshes.Single().Model.BlobKey;
        var run = await world.Runs.GetLatestByTaskAsync(world.Task.Id, default);

        Assert.Equal($"meshes/{run!.Id:n}/model.glb", first);
    }

    // ─── 핸들 내구성 (§11.3 O-01·O-02 · D-10·D-11) ───

    /// <summary>
    /// **비내구적 핸들을 쓰는 공급자는 재기동 후 다시 준비하고 제출까지 간다** (O-01).
    ///
    /// 저장된 토큰이 없으므로 그것으로 요청을 재구성하면 빈손이다. 이 경로가 깨지면
    /// 예외도 안 나고 네 장이 빠진 요청이 나간다 — 그리고 그 요청은 과금된다.
    /// </summary>
    [Fact]
    public async Task NonDurableHandles_ArePreparedAgainAndActuallySubmitted()
    {
        var world = await MeshWorld.ReadyAsync(durableHandles: false);

        // 준비만 끝내고 죽은 상태를 만든다 — 토큰은 남지 않는다
        var run = await world.StartRunAsync();
        foreach (var input in run.Inputs.ToArray())
        {
            run.RecordPrepared(input.ViewDirection, null, "image/png", MeshWorld.Now);
        }

        await world.Runs.SaveAsync(run, default);

        await world.RunAsync();

        // 넷 다 다시 준비됐고, 제출이 그 핸들 넷을 들고 갔다
        Assert.Equal(4, world.Provider.Uploaded.Count);
        Assert.Equal(4, world.Provider.LastSubmitted.Count);
        Assert.All(
            world.Provider.LastSubmitted.Values,
            handle => Assert.False(string.IsNullOrWhiteSpace(handle.Value)));
    }

    /// <summary>
    /// **내구적 핸들은 재기동 후에도 다시 만들지 않는다** (O-02 · 회귀).
    ///
    /// Tripo 는 업로드가 실제 왕복이라 이것이 헛일이면 매 재시도마다 네 장이 다시 올라간다.
    /// </summary>
    [Fact]
    public async Task DurableHandles_AreNotRebuiltOnResume()
    {
        var world = await MeshWorld.ReadyAsync();
        await world.UploadedRunAsync();

        await world.RunAsync();

        Assert.Empty(world.Provider.Uploaded);
        Assert.Equal(4, world.Provider.LastSubmitted.Count);
        Assert.Equal("file_Front", world.Provider.LastSubmitted[ViewDirection.Front].Value);
    }

    /// <summary>
    /// **이미 제출된 실행은 입력을 다시 만들지 않는다.**
    ///
    /// 비내구적 공급자는 매번 다시 준비하는데, 제출 뒤에 그러면 헛일일 뿐 아니라 실행
    /// 상태를 준비 단계로 되돌려 이미 과금된 작업을 놓친다.
    /// </summary>
    [Fact]
    public async Task SubmittedRun_DoesNotPrepareInputsAgain()
    {
        var world = await MeshWorld.ReadyAsync(durableHandles: false);

        var run = await world.StartRunAsync();
        foreach (var input in run.Inputs.ToArray())
        {
            run.RecordPrepared(input.ViewDirection, null, "image/png", MeshWorld.Now);
        }

        run.BeginSubmit(MeshWorld.Now);
        run.RecordProviderTask("task_already", MeshWorld.Now);
        await world.Runs.SaveAsync(run, default);

        await world.RunAsync();

        Assert.Empty(world.Provider.Uploaded);
        Assert.Equal(0, world.Provider.Submits);
    }

    /// <summary>
    /// FBX 를 내는 공급자의 결과는 산출물 셋이 된다 (§11.3).
    ///
    /// GLB 를 내는 것과 같은 경로를 지나므로 형식이 늘어도 오케스트레이션이 안 바뀐다.
    /// </summary>
    [Fact]
    public async Task ProviderThatEmitsFbx_StoresThreeArtifacts()
    {
        var world = await MeshWorld.ReadyAsync(producesFbx: true);

        await world.RunAsync();

        var mesh = world.Job.GeneratedMeshes.Single();

        Assert.True(mesh.HasFbx);
        Assert.True(mesh.HasPreview);
        Assert.Equal(3, mesh.Artifacts.Count);
    }
}
