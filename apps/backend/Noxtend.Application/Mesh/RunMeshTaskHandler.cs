using Microsoft.Extensions.Logging;
using Noxtend.Application.Common;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Mesh;

/// <summary>
/// 3D 재구성 공정을 돈다.
///
/// Design Ref: §8.1~8.3 · Plan D-06·D-08 · FR-06~09
///
/// **모든 분기의 기준은 저장된 실행 상태다.** 재기동한 워커는 이 핸들러를 처음부터 다시
/// 도는데, 그때 무엇을 건너뛸지를 메모리가 아니라 DB 가 정한다 — 그것이 중복 과금을
/// 막는 유일한 방법이다.
///
/// 순서가 곧 규칙이다.
/// ① 저장된 실행을 찾거나 새로 만든다
/// ② token 이 없는 방향만 올린다
/// ③ 아직 안 보냈으면 <c>Submitting</c> 을 먼저 저장하고 보낸다
/// ④ 작업 ID 가 있으면 **보내지 않고** 조회만 재개한다
/// ⑤ 성공하면 5분 안에 자체 저장소로 옮긴다
/// </summary>
public sealed class RunMeshTaskHandler(
    IMeshRunRepository runs,
    IMeshProviderFactory providers,
    IBlobStorage images,
    IMeshArtifactStorage artifacts,
    MeshInputNormalizer normalizer,
    IJobRepository jobs,
    TaskExecution execution,
    IClock clock,
    MeshGenerationOptions options,
    ILogger<RunMeshTaskHandler> logger) : ITaskHandler
{
    /// <summary>
    /// **"작업이 만들어졌는지 모른다" 를 뜻하는 유일한 코드.**
    ///
    /// 나머지 실패 코드는 어댑터가 4xx 를 보고 "안 만들어졌다" 고 판정한 것들이다.
    /// 그 둘을 가르지 않으면 크레딧 부족 같은 확실한 거절까지 영구 차단된다 (§6).
    /// </summary>
    private const string UnknownSubmission = "MESH_SUBMISSION_UNKNOWN";

    public Task<RunTaskOutcome> HandleAsync(Guid taskId, CancellationToken ct)
        => execution.RunAsync(
            taskId,
            new TaskExecutionPolicy(options.Lease, options.LeaseRenew, MaxAttempts: 1),
            ReconstructAsync,
            Classify,
            ct);

    private async Task<Action<PipelineJob>> ReconstructAsync(
        PipelineJob job, PipelineTask task, CancellationToken ct)
    {
        if (task.Kind != TaskKind.Reconstruct
            || task.PartId is not { } partId
            || task.MeshInputs is not { } inputs
            || task.ProviderConfigId is not { } providerConfigId
            || string.IsNullOrWhiteSpace(task.Model))
        {
            throw new InvalidOperationException("3D 재구성 공정의 입력이 갖춰지지 않았습니다");
        }

        var run = await ResumeOrStartAsync(job, task, partId, inputs, providerConfigId, ct);

        // 이미 끝난 실행은 외부를 부르지 않고 결과만 붙인다 (§5.3 재기동 규칙)
        if (run.Status == MeshRunStatus.ArtifactsReady)
        {
            return Attach(run, partId, task.Id);
        }

        if (run.Status is MeshRunStatus.SubmissionUnknown)
        {
            // 운영자가 공급자 대시보드와 대조하기 전에는 다시 보내지 않는다
            throw new MeshRunHaltedException(UnknownSubmission);
        }

        var provider = await providers.CreateAsync(providerConfigId, task.Model!, ct);

        // **이미 제출된 실행은 입력을 다시 만들지 않는다.** 비내구적 핸들을 쓰는 공급자는
        // 매번 다시 만드는데, 제출 뒤에 그러면 헛일일 뿐 아니라 실행 상태까지 준비 단계로
        // 되돌린다 (D-10·D-11)
        if (run.ProviderTaskId is null)
        {
            var handles = await PrepareInputsAsync(run, provider, ct);
            await SubmitIfNeededAsync(run, provider, task.Model!, handles, ct);
        }

        await PollAsync(run, provider, ct);
        await DownloadAsync(run, provider, ct);

        return Attach(run, partId, task.Id);
    }

    /// <summary>
    /// 이 공정의 마지막 실행을 이어받거나 새로 시작한다.
    ///
    /// **끝난 실행은 이어받지 않는다.** 수동 재시도가 새 번호를 만들어야 이전 시도의
    /// 기록이 남고, 어느 실행이 얼마를 썼는지 나중에 셀 수 있다.
    /// </summary>
    private async Task<MeshRun> ResumeOrStartAsync(
        PipelineJob job,
        PipelineTask task,
        Guid partId,
        MeshInputSet inputs,
        Guid providerConfigId,
        CancellationToken ct)
    {
        var latest = await runs.GetLatestByTaskAsync(task.Id, ct);

        switch (latest)
        {
            // 결과가 저장돼 있으면 그것을 쓴다 — 붙이기만 남았다. 단, 입력이 그때와
            // 다르면 재사용하면 안 된다(spec 20260917, ReplanMeshInputs) — 파츠별 뷰
            // 재선택은 "같은 조합으로 재시도"라는 전제를 깬다
            case { Status: MeshRunStatus.ArtifactsReady } when UsesSameInputs(latest, inputs):
                return latest;

            // 아직 진행 중인 실행을 이어받는다
            case { IsTerminal: false }:
                return latest;

            // **제출 결과를 모르는 실행은 새로 시작하지 않는다** (Plan D-06).
            // 여기서 새 실행을 만들면 이미 만들어졌을지 모르는 유료 작업을 하나 더 만든다.
            // 운영자가 공급자 대시보드와 대조한 뒤에만 앞으로 나아간다
            case { Status: MeshRunStatus.SubmissionUnknown }:
                return latest;

            // **시간 초과는 같은 외부 작업을 이어서 조회한다** (§4.7). 외부에서는 아직
            // 돌고 있을 수 있고, 새로 만들면 이미 과금된 작업을 버리는 셈이다
            case { Status: MeshRunStatus.TimedOut, ProviderTaskId: not null }:
                latest.ResumeAfterTimeout(clock.Now);
                await runs.SaveAsync(latest, ct);
                return latest;
        }

        var run = MeshRun.Start(
            job.Id,
            task.Id,
            partId,
            runNumber: (latest?.RunNumber ?? 0) + 1,
            providerConfigId,
            task.Model!,
            inputs,
            // 씨앗을 우리가 정해야 재시도가 같은 조건에서 돈다 (Plan D-04)
            modelSeed: Random.Shared.Next(1, int.MaxValue),
            textureSeed: Random.Shared.Next(1, int.MaxValue),
            clock.Now);

        await runs.AddAsync(run, ct);
        return run;
    }

    /// <summary>
    /// 이 실행이 실제로 쓴 입력과 지금 공정의 입력이 같은가 (spec 20260917).
    ///
    /// 방향·이미지ID 쌍 집합을 통째로 비교한다 — 방향 개수가 달라지거나(부분 선택),
    /// 어느 한 방향의 이미지가 바뀌면(재생성·대칭) 다른 것으로 본다.
    /// </summary>
    private static bool UsesSameInputs(MeshRun run, MeshInputSet inputs)
    {
        var used = run.Inputs.Select(input => (input.ViewDirection, input.GeneratedImageId)).ToHashSet();
        var requested = inputs.Pairs().ToHashSet();
        return used.SetEquals(requested);
    }

    /// <summary>
    /// 네 방향의 입력 핸들을 갖춘다.
    ///
    /// Design Ref: §4.1 · D-10·D-11
    ///
    /// **다시 만들지 말지를 정하는 것은 토큰의 유무다.** 내구적 핸들이 저장돼 있으면
    /// 그대로 쓴다 — Tripo 는 업로드를 되풀이하지 않는다. 없으면 매번 다시 만든다 —
    /// Meshy 의 base64 는 로컬 연산이라 네트워크도 크레딧도 들지 않는다.
    ///
    /// **비내구적 핸들은 저장하지 않는다.** 열 폭을 넘고, DB 가 이미지 저장소가 되며,
    /// 본문을 남기지 않는다는 규칙(NFR-02)과도 어긋난다.
    /// </summary>
    private async Task<IReadOnlyDictionary<ViewDirection, MeshInputHandle>> PrepareInputsAsync(
        MeshRun run, IMeshProvider provider, CancellationToken ct)
    {
        var handles = new Dictionary<ViewDirection, MeshInputHandle>();

        foreach (var input in run.Inputs.ToArray())
        {
            ct.ThrowIfCancellationRequested();

            // 저장된 토큰이 있으면 그것이 곧 핸들이다
            if (!input.NeedsPreparation)
            {
                handles[input.ViewDirection] = new MeshInputHandle(
                    input.ProviderFileToken!, IsDurable: true);
                continue;
            }

            var image = await jobs.GetGeneratedImageAsync(input.GeneratedImageId, ct)
                ?? throw new InvalidOperationException(
                    $"입력 이미지를 찾을 수 없습니다: {input.GeneratedImageId}");

            await using var source = await images.OpenReadAsync(image.BlobKey, ct);

            var prepared = await normalizer.PrepareAsync(
                source, image.ContentType, input.ViewDirection, ct);

            await using (prepared.Content)
            {
                var handle = await provider.UploadInputAsync(
                    new MeshInputUpload(
                        input.ViewDirection,
                        prepared.Content,
                        prepared.ContentType,
                        prepared.SafeFileName,
                        prepared.Length),
                    ct);

                handles[input.ViewDirection] = handle;

                run.RecordPrepared(
                    input.ViewDirection,
                    handle.IsDurable ? handle.Value : null,
                    prepared.ContentType,
                    clock.Now);
            }

            // 방향마다 저장한다. 넷을 다 준비한 뒤에 한 번 저장하면 그 사이 크래시가
            // 네 장을 통째로 날린다
            await runs.SaveAsync(run, ct);
        }

        return handles;
    }

    /// <summary>
    /// **아직 안 보냈을 때만 보낸다.**
    ///
    /// 작업 ID 가 있으면 이미 유료 작업이 존재하므로 조회로 넘어간다. 이 판단을 한 곳에
    /// 모아 두는 것이 두 재시도 층(HTTP 재시도와 공정 재시도)이 제출을 겹쳐 실행하지
    /// 않게 하는 방법이다 (§8.2).
    /// </summary>
    private async Task SubmitIfNeededAsync(
        MeshRun run,
        IMeshProvider provider,
        string model,
        IReadOnlyDictionary<ViewDirection, MeshInputHandle> handles,
        CancellationToken ct)
    {
        if (run.ProviderTaskId is not null)
        {
            return;
        }

        ct.ThrowIfCancellationRequested();

        // POST 직전에 저장한다. 이 기록이 "보낼 참이었다" 를 남긴다
        run.BeginSubmit(clock.Now);
        await runs.SaveAsync(run, ct);

        // **저장된 토큰이 아니라 방금 갖춘 핸들을 쓴다.** 비내구적 핸들은 DB 에 없으므로
        // 토큰에서 재구성하면 그 공급자는 제출할 것이 없다 (D-10)
        var request = new MultiviewMeshRequest(handles, model, run.ModelSeed, run.TextureSeed);

        try
        {
            var submission = await provider.SubmitAsync(request, ct);
            run.RecordProviderTask(submission.ProviderTaskId, clock.Now);
        }
        catch (Exception ex) when (IsRateLimited(ex))
        {
            // 작업이 만들어지지 않았음이 확실한 유일한 경우다
            run.ReleaseForResubmit(clock.Now);
            await runs.SaveAsync(run, ct);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failureCode = FailureCodeOf(ex);

            // **어댑터가 "안 만들어졌다" 고 말하면 그것을 믿는다.**
            //
            // 이 자리는 원래 429 외의 모든 실패를 "모름" 으로 뭉갰다. 중복 과금을 막는
            // 기본값으로는 옳지만, 402(크레딧 부족)·400·401 처럼 **작업이 만들어지지
            // 않았음이 확실한 응답까지 모름으로 두면 그 공정은 영구히 막힌다** —
            // `ResumeOrStartAsync` 가 SubmissionUnknown 을 새 실행 금지 신호로 읽기 때문이다.
            //
            // 실측에서 실제로 났다. 크레딧이 모자라 거절된 파츠는 충전한 뒤에도 다시
            // 만들 수 없었고, 이미 값을 치른 이미지를 버리고 작업을 통째로 새로 돌려야 했다.
            //
            // 모르는 것은 여전히 모른다고 남긴다 — 5xx 와 전송 끊김은 어댑터가
            // `MESH_SUBMISSION_UNKNOWN` 을 그대로 들고 온다.
            if (failureCode == UnknownSubmission)
            {
                run.MarkSubmissionUnknown(failureCode, null, null, clock.Now);
            }
            else
            {
                run.Fail(failureCode, null, null, clock.Now);
            }

            await runs.SaveAsync(run, ct);
            throw;
        }

        await runs.SaveAsync(run, ct);
    }

    /// <summary>
    /// 완료까지 조회한다. 진행률이 **바뀔 때만** 저장한다 — 2초마다 같은 값을 쓰면
    /// 공정 하나가 DB 쓰기를 300번 만든다.
    /// </summary>
    private async Task PollAsync(MeshRun run, IMeshProvider provider, CancellationToken ct)
    {
        if (run.Status == MeshRunStatus.Downloading)
        {
            return;   // 이미 성공을 확인했다
        }

        run.BeginPolling(clock.Now);

        var deadline = clock.Now + options.RunTimeout;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var snapshot = await provider.GetTaskAsync(run.ProviderTaskId!, ct);
            var before = run.Progress;
            run.RecordProgress(snapshot.Progress, clock.Now);

            if (run.Progress != before)
            {
                await runs.SaveAsync(run, ct);
            }

            switch (snapshot.State)
            {
                case MeshTaskState.Succeeded:
                    run.BeginDownload(clock.Now);
                    await runs.SaveAsync(run, ct);
                    return;

                case MeshTaskState.Failed or MeshTaskState.Canceled:
                    run.Fail(
                        snapshot.FailureCode ?? "MESH_TASK_FAILED",
                        snapshot.ProviderCode, snapshot.ProviderRequestId, clock.Now);
                    await runs.SaveAsync(run, ct);
                    throw new MeshRunHaltedException(run.FailureCode!);
            }

            if (clock.Now >= deadline)
            {
                // **외부 작업 ID 를 보존한 채 끝낸다** — 수동 재시도가 같은 작업을 이어받는다
                run.MarkTimedOut(clock.Now);
                await runs.SaveAsync(run, ct);
                throw new MeshRunHaltedException("MESH_TIMEOUT");
            }

            await Task.Delay(options.PollInterval, ct);
        }
    }

    /// <summary>
    /// 결과를 자체 저장소로 옮긴다.
    ///
    /// **공급자 URL 이 5분이면 만료되므로 여기서 지체할 수 없다** (Plan D-08). 저장이
    /// 끝나야 공정을 성공으로 확정한다.
    /// </summary>
    private async Task DownloadAsync(MeshRun run, IMeshProvider provider, CancellationToken ct)
    {
        if (run.Status == MeshRunStatus.ArtifactsReady)
        {
            return;
        }

        await using (var result = await provider.OpenResultAsync(run.ProviderTaskId!, ct))
        {
            foreach (var part in result.Parts)
            {
                await SaveArtifactAsync(run, part, ct);
            }
        }

        // 조회로 credit 을 한 번 더 확인한다 — 완료 응답에만 실리는 경우가 있다
        var credits = (await provider.GetTaskAsync(run.ProviderTaskId!, ct)).CreditsConsumed;

        run.MarkArtifactsReady(credits, clock.Now);
        await runs.SaveAsync(run, ct);
    }

    /// <summary>
    /// 산출물 하나를 검증·저장하고 실행에 기록한다.
    ///
    /// **GLB 가 아닌 것이 거부돼도 GLB 를 버리지 않는다** (§11.3 O-03·O-04). 가장 비싼
    /// 결과가 부수적인 파일 하나 때문에 날아가면 안 된다. GLB 자체가 거부되면 예외가
    /// 그대로 올라가 실행이 확정 실패한다.
    /// </summary>
    private async Task SaveArtifactAsync(MeshRun run, MeshResultPart part, CancellationToken ct)
    {
        var maxBytes = part.Kind switch
        {
            MeshArtifactKind.Preview => options.MaxPreviewBytes,
            _ => options.MaxModelBytes,
        };

        try
        {
            var stored = await artifacts.SaveAsync(
                run.Id, part.Kind, part.Content, part.ContentType, maxBytes, ct);

            run.RecordArtifact(part.Kind, stored.BlobKey, stored.ContentType, stored.SizeBytes, clock.Now);
        }
        catch (MeshArtifactRejectedException ex) when (part.Kind != MeshArtifactKind.Glb)
        {
            logger.LogWarning(
                ex, "Artifact {Kind} rejected for mesh run {MeshRunId}", part.Kind, run.Id);
        }
    }

    /// <summary>
    /// 결과를 작업에 붙이는 **순수 함수**.
    ///
    /// <c>TaskExecution</c> 이 이것을 부르기 직전에 취소 토큰을 다시 본다. 취소가
    /// 감지되면 Blob 은 남지만 결과는 작업에 연결되지 않는다 (FR-12).
    /// </summary>
    private static Action<PipelineJob> Attach(MeshRun run, Guid partId, Guid taskId)
        => job => job.AttachGeneratedMesh(
            partId,
            taskId,
            run.Id,
            [.. run.Artifacts.Select(artifact => new MeshArtifactDescriptor(
                artifact.Kind, artifact.BlobKey, artifact.ContentType, artifact.SizeBytes))],
            run.CreditsConsumed,
            run.UpdatedAt);

    /// <summary>
    /// **자동 재시도를 거의 열지 않는다** (§8.2).
    ///
    /// 공정 재시도가 다시 도는 것은 안전하지만, 그 판단은 이미 실행 상태에 담겨 있다.
    /// 여기서 재시도를 열면 두 층이 겹쳐 유료 제출이 중복될 수 있다.
    /// </summary>
    private static TaskFailure? Classify(Exception exception) => exception switch
    {
        MeshRunHaltedException halted => TaskFailure.Fail(halted.FailureCode),
        IMeshFailure failure => TaskFailure.Fail(failure.FailureCode),
        _ => null,
    };

    /// <summary>
    /// 429 만 되돌린다 — 작업이 만들어지지 않았음이 확실한 유일한 응답이다.
    /// </summary>
    private static bool IsRateLimited(Exception exception)
        => exception is IMeshFailure { FailureCode: "MESH_RATE_LIMITED" };

    /// <summary>
    /// 실패 코드는 어댑터가 <see cref="IMeshFailure"/> 로 실어 준다.
    ///
    /// 예외 타입을 직접 알면 Application 이 Infrastructure 에 기대게 되고,
    /// 리플렉션으로 꺼내면 이름이 바뀔 때 조용히 깨진다.
    /// </summary>
    private static string FailureCodeOf(Exception exception)
        => exception is IMeshFailure failure ? failure.FailureCode : "MESH_PROVIDER_UNAVAILABLE";
}

/// <summary>
/// 실행이 더 갈 수 없다 — 이유는 이미 실행 상태에 저장돼 있다.
///
/// 예외로 나가는 이유는 <c>TaskExecution</c> 이 공정을 실패로 확정하게 하기 위해서다.
/// </summary>
public sealed class MeshRunHaltedException(string failureCode)
    : Exception($"3D 제작이 중단되었습니다: {failureCode}")
{
    public string FailureCode { get; } = failureCode;
}
