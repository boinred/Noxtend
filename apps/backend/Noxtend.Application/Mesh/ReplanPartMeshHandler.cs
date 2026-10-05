using Microsoft.Extensions.Logging;
using Noxtend.Application.Job;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Mesh;

/// <summary>좌우 축의 3D 전송 선택 (spec 20260917).</summary>
public enum LeftRightPlan
{
    Skip,
    Left,
    Right,
    Both,
    MirrorFromLeft,
    MirrorFromRight,
}

/// <summary>
/// 전후 축의 3D 전송 선택 (spec 20260917).
///
/// 정면은 `ReplanPartMeshHandler` 호출 전제 조건(없으면 실패)이라, "정면을 반전해
/// Back을 채운다"만 있고 "Back을 반전해 Front를 채운다"는 없다 — 정면 슬롯을
/// 대칭으로 대체할 일이 없다.
/// </summary>
public enum BackPlan
{
    Skip,
    Include,
    MirrorFromFront,
}

/// <summary>
/// 파츠별 "3D 전송 뷰 자유 선택 + 대칭"을 한 요청으로 처리한다 (spec 20260917).
///
/// **enum 해석과 합성 이미지 I/O는 여기서 한다.** 도메인(`PipelineJob`)은 I/O를 할 수
/// 없으므로 "필요하면 합성 이미지를 만든다"는 판단은 도메인 메서드 안에 둘 수 없다.
/// `HandleAsync`는 두 단계다: (1) 두 축(좌우·전후)을 모두 검증만 하고(I/O 없음), 하나라도
/// 실패하면 아무 합성도 만들지 않은 채 바로 반환한다 (2) 검증을 전부 통과해야 실제 합성
/// (Blob 반전·저장)을 실행하고, 완성된 `MeshInputSet`으로 `PipelineJob.ReplanMeshInputs`를
/// 호출해 마지막에 딱 한 번 저장한다 — 한쪽 축의 합성이 끝난 뒤 다른 쪽 축 검증에서
/// 실패해 Blob만 남는 고아를 막는다(merge-gate 리뷰 F3). 그래도 저장 자체가 실패하면
/// (RowVersion 충돌 등, F2) 이미 만든 합성 Blob을 직접 지운다 — DB에는 어차피 아무
/// 흔적도 안 남으므로(재개용 필드가 필요 없는 이유, ADR mirror-as-synthetic-generated-image
/// 참고) Blob만 맞춰주면 된다.
/// </summary>
public sealed class ReplanPartMeshHandler(
    IJobRepository jobs,
    IBlobStorage blobs,
    IImageTranscoder transcoder,
    MeshSelectionValidator selection,
    JobOrchestrator orchestrator,
    IClock clock,
    ILogger<ReplanPartMeshHandler> logger)
{
    public async Task<Result<PipelineJob>> HandleAsync(
        Guid jobId,
        Guid partId,
        Guid providerConfigId,
        string? model,
        LeftRightPlan leftRight,
        BackPlan back,
        CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        // 도메인(ReplanMeshInputs/PlanSyntheticView)은 취소된 작업에서 예외를 던진다 —
        // 여기서 먼저 걸러야 사용자가 알아들을 응답이 나간다(GenerateSelectedViewsHandler와 같은 패턴)
        if (job.Status == JobStatus.Canceled)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobAlreadyTerminal, "취소된 작업은 3D를 다시 만들 수 없습니다");
        }

        if (await selection.ValidateAsync(providerConfigId, model, ct) is { } error)
        {
            return Result<PipelineJob>.Fail(error.Code, error.Message);
        }

        // 파츠 자체가 없는 것과 "정면 이미지가 아직 없다"를 구분한다 — 전에는 둘 다
        // MeshInputMissing으로 같이 나가 사용자가 원인을 알 수 없었다(merge-gate 2차 리뷰 B4)
        if (job.Parts.All(p => p.Id != partId))
        {
            return Result<PipelineJob>.Fail(ErrorCode.PartNotFound, $"파츠를 찾을 수 없습니다: {partId}");
        }

        if (job.LatestCurrentImage(partId, ViewDirection.Front) is not { } frontId)
        {
            return Result<PipelineJob>.Fail(ErrorCode.MeshInputMissing, "정면 이미지가 없습니다");
        }

        // ── 1단계: 검증만 한다. I/O(합성 이미지 생성)는 전혀 하지 않는다. ──
        //
        // 두 축을 다 확인하고 나서야 합성을 시작해야, "좌우축은 대칭 생성에 성공해
        // Blob 이 이미 저장됐는데 전후축 검증에서 뒤늦게 실패"하는 순서로 고아 Blob이
        // 남는 일이 없다(merge-gate 리뷰 F3). 합성이 필요한 축은 여기서 표시만 해두고
        // 2단계에서 한꺼번에 실행한다.
        Guid? leftId = null;
        Guid? rightId = null;
        Guid? backId = null;
        var mirrorRightFromLeft = false;
        var mirrorLeftFromRight = false;
        var mirrorBackFromFront = false;

        switch (leftRight)
        {
            case LeftRightPlan.Skip:
                break;

            case LeftRightPlan.Left:
                if (job.LatestCurrentImage(partId, ViewDirection.Left) is not { } left)
                {
                    return Result<PipelineJob>.Fail(ErrorCode.MeshInputMissing, "Left 이미지가 없습니다");
                }
                leftId = left;
                break;

            case LeftRightPlan.Right:
                if (job.LatestCurrentImage(partId, ViewDirection.Right) is not { } right)
                {
                    return Result<PipelineJob>.Fail(ErrorCode.MeshInputMissing, "Right 이미지가 없습니다");
                }
                rightId = right;
                break;

            case LeftRightPlan.Both:
                if (job.LatestCurrentImage(partId, ViewDirection.Left) is not { } bothLeft)
                {
                    return Result<PipelineJob>.Fail(ErrorCode.MeshInputMissing, "Left 이미지가 없습니다");
                }
                if (job.LatestCurrentImage(partId, ViewDirection.Right) is not { } bothRight)
                {
                    return Result<PipelineJob>.Fail(ErrorCode.MeshInputMissing, "Right 이미지가 없습니다");
                }
                leftId = bothLeft;
                rightId = bothRight;
                break;

            case LeftRightPlan.MirrorFromLeft:
                if (job.LatestCurrentImage(partId, ViewDirection.Left) is not { } mirrorSourceLeft)
                {
                    return Result<PipelineJob>.Fail(ErrorCode.MeshInputMissing, "대칭 소스(Left) 이미지가 없습니다");
                }
                leftId = mirrorSourceLeft;
                mirrorRightFromLeft = true;
                break;

            case LeftRightPlan.MirrorFromRight:
                if (job.LatestCurrentImage(partId, ViewDirection.Right) is not { } mirrorSourceRight)
                {
                    return Result<PipelineJob>.Fail(ErrorCode.MeshInputMissing, "대칭 소스(Right) 이미지가 없습니다");
                }
                rightId = mirrorSourceRight;
                mirrorLeftFromRight = true;
                break;
        }

        switch (back)
        {
            case BackPlan.Skip:
                break;

            case BackPlan.Include:
                if (job.LatestCurrentImage(partId, ViewDirection.Back) is not { } include)
                {
                    return Result<PipelineJob>.Fail(ErrorCode.MeshInputMissing, "Back 이미지가 없습니다");
                }
                backId = include;
                break;

            case BackPlan.MirrorFromFront:
                mirrorBackFromFront = true;
                break;
        }

        // ── 2단계: 검증을 전부 통과했다 — 이제 필요한 합성만 실행한다. ──
        //
        // 여기서 저장한 Blob 키를 기록해 둔다 — 이 아래에서 실패하면(RowVersion 충돌 등)
        // DB에는 아무 흔적이 안 남지만 Blob 스토리지에는 남으므로, 직접 지워야 한다
        // (merge-gate 리뷰 F3).
        var createdBlobKeys = new List<string>();

        if (mirrorRightFromLeft)
        {
            rightId = await CreateSyntheticAsync(job, partId, ViewDirection.Right, leftId!.Value, createdBlobKeys, ct);
        }
        if (mirrorLeftFromRight)
        {
            leftId = await CreateSyntheticAsync(job, partId, ViewDirection.Left, rightId!.Value, createdBlobKeys, ct);
        }
        if (mirrorBackFromFront)
        {
            backId = await CreateSyntheticAsync(job, partId, ViewDirection.Back, frontId, createdBlobKeys, ct);
        }

        var inputs = new MeshInputSet(frontId, rightId, backId, leftId);
        job.ReplanMeshInputs(partId, providerConfigId, model!, inputs, clock.Now);

        // 대칭 I/O를 포함해 여기까지 전부 메모리에서 처리했다 — 한 번만 저장한다.
        // 같은 파츠에 동시/중복 요청이 오면 RowVersion 충돌이나(이미 Reconstruct 공정이
        // 있던 경우) 유니크 제약 위반으로(find-or-create의 create 경로) 저장이 실패할 수
        // 있다 — 둘 다 사용자가 다시 시도하면 되는 충돌이지 서버 오류가 아니다(merge-gate
        // 리뷰 F2)
        try
        {
            await jobs.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException ex)
        {
            // 여기 오는 건 SQL Server 유니크 제약 위반이나 RowVersion 충돌뿐이다
            // (EfJobRepository.SaveChangesAsync 가 그 외 DbUpdateException 은 번역하지
            // 않고 그대로 던진다) — 원인을 조용히 삼키지 않도록 남긴다(merge-gate 2차
            // 리뷰 B1)
            logger.LogInformation(
                ex, "3D 전송 뷰 재계획 충돌 — 다른 요청과 경합: job={JobId} part={PartId}", jobId, partId);

            // 저장이 안 됐으니 방금 만든 합성 이미지 Blob도 고아다 — 최선을 다해 지운다.
            // 실패해도(스토리지 일시 장애 등) 응답은 그대로 나간다 — 삭제 실패로 사용자의
            // 재시도를 막을 이유가 없다
            foreach (var blobKey in createdBlobKeys)
            {
                await blobs.DeleteAsync(blobKey, ct);
            }

            return Result<PipelineJob>.Fail(
                ErrorCode.ReplanMeshConflict,
                "다른 요청과 동시에 처리되어 충돌했습니다. 다시 시도해 주세요");
        }

        await orchestrator.StartAsync(job, ct);

        return Result<PipelineJob>.Ok(job);
    }

    /// <summary>
    /// 소스 이미지를 반전해 새 합성 이미지를 만든다.
    ///
    /// **여기서 저장하지 않는다.** `HandleAsync`가 모든 축을 다 해석한 뒤 한 번에
    /// 저장해야, 중간에 실패해도 부분 상태가 DB에 안 남는다.
    /// </summary>
    private async Task<Guid> CreateSyntheticAsync(
        PipelineJob job, Guid partId, ViewDirection targetDirection, Guid sourceImageId,
        List<string> createdBlobKeys, CancellationToken ct)
    {
        var source = await jobs.GetGeneratedImageAsync(sourceImageId, ct)
            ?? throw new InvalidOperationException($"소스 이미지를 찾을 수 없습니다: {sourceImageId}");

        await using var original = await blobs.OpenReadAsync(source.BlobKey, ct);
        await using var flipped = await transcoder.FlipHorizontallyAsync(original, source.ContentType, ct);

        using var buffer = new MemoryStream();
        await flipped.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var blobKey = await blobs.SaveAsync(buffer, source.ContentType, ct);
        createdBlobKeys.Add(blobKey);

        var task = job.PlanSyntheticView(partId, targetDirection, clock.Now);
        task.Claim(clock.Now, TimeSpan.FromMinutes(1));
        job.AttachGeneratedImage(
            partId, task.Id, blobKey, source.ContentType, buffer.Length, clock.Now, isSynthetic: true);
        task.Succeed(clock.Now);

        return job.GeneratedImages.Single(image => image.TaskId == task.Id).Id;
    }
}
