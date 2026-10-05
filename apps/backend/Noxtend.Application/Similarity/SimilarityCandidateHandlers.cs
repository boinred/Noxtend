using System.Buffers.Binary;
using System.Security.Cryptography;
using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Similarity;

namespace Noxtend.Application.Similarity;

/// <summary>클라이언트는 adjustment **id 만** 보낸다 — 값은 서버 저장본에서 다시 읽는다 (§6).</summary>
public sealed record CreateCandidateRequest(
    Guid JobId,
    Guid RunId,
    IReadOnlyList<Guid> AdjustmentIds);

public sealed record CreatedCandidate(
    SimilarityRun Run,
    SceneLayout CandidateLayout,
    SimilarityEvaluation Evaluation);

/// <summary>
/// 후보 revision 생성 (§9.2 1~3단계).
///
/// Design Ref: background-similarity-tuning §9.2 · §6 · D-07
///
/// 저장된 보정을 **다시 검증해** 비활성 Candidate revision 과 AwaitingRender 평가를
/// 만든다. 누적 배율 한계의 분모는 보정이 시작된 원점(Composed/Restore) revision —
/// 후보를 거듭하며 조금씩 밀면 한도를 우회할 수 있어서다.
/// </summary>
public sealed class CreateSimilarityCandidateHandler(
    ISimilarityRepository similarity,
    ISceneLayoutRepository layouts,
    IClock clock)
{
    public async Task<Result<CreatedCandidate>> HandleAsync(
        CreateCandidateRequest request, CancellationToken ct)
    {
        var run = await similarity.GetRunAsync(request.RunId, ct);
        if (run is null || run.JobId != request.JobId)
        {
            return Fail(ErrorCode.SimilarityRunNotFound, "이 작업의 실행이 아닙니다");
        }

        if (run.Status != SimilarityRunStatus.ReadyForAdjustment)
        {
            return Fail(ErrorCode.SimilarityConflict, "보정을 선택할 수 있는 상태가 아닙니다");
        }

        if (run.CurrentIteration >= run.MaxIterations)
        {
            return Fail(ErrorCode.SimilarityIterationLimit, "후보 반복을 모두 소진했습니다");
        }

        // 선택된 보정 — 최신 성공 평가의 저장본에서 id 로만 찾는다 (§6)
        var evaluations = await similarity.ListEvaluationsAsync(run.Id, ct);
        var latest = evaluations.LastOrDefault(
            e => e.Status == SimilarityEvaluationStatus.Succeeded);
        if (latest is null)
        {
            return Fail(ErrorCode.SimilarityConflict, "성공한 평가가 없습니다");
        }

        var byId = latest.Adjustments.ToDictionary(a => a.Id);
        var selected = new List<SceneAdjustmentCommand>();
        foreach (var id in request.AdjustmentIds)
        {
            if (!byId.TryGetValue(id, out var adjustment))
            {
                return Fail(ErrorCode.SimilarityConflict, "저장되지 않은 보정을 선택했습니다");
            }

            selected.Add(adjustment.Command);
        }

        // 기준은 현재 활성 — 평가된 revision 과 다르면 다른 요청이 장면을 바꾼 것이다 (§12)
        var baseline = await layouts.GetActiveByJobAsync(request.JobId, ct);
        if (baseline is null || baseline.Id != latest.LayoutId)
        {
            return Fail(ErrorCode.SceneRevisionStale, "기준 배치가 평가 이후 바뀌었습니다");
        }

        var origin = await ResolveOriginAsync(baseline, ct);

        SceneAdjustmentResult adjusted;
        try
        {
            adjusted = SceneAdjuster.Apply(baseline, origin, selected);
        }
        catch (SceneAdjustmentException ex)
        {
            return Fail(ErrorCode.SimilarityConflict, ex.Message);
        }

        var revision = await layouts.MaxRevisionAsync(request.JobId, ct) + 1;
        var candidate = SceneLayout.CreateCandidate(
            baseline, revision, adjusted.Instances, adjusted.Camera, adjusted.Light, clock.Now);

        var evaluation = SimilarityEvaluation.CreateCandidate(
            run.Id, candidate.Id, sequence: evaluations.Count + 1, clock.Now);

        run.BeginCandidate(clock.Now);

        await layouts.AddAsync(candidate, ct);
        await similarity.AddEvaluationAsync(evaluation, ct);
        await similarity.SaveChangesAsync(ct);

        return Result<CreatedCandidate>.Ok(new CreatedCandidate(run, candidate, evaluation));
    }

    /// <summary>누적 한계의 원점 — SimilarityAdjustment 사슬을 거슬러 Composed/Restore 까지.</summary>
    private async Task<SceneLayout> ResolveOriginAsync(SceneLayout baseline, CancellationToken ct)
    {
        var current = baseline;
        while (current.Origin == SceneLayoutOrigin.SimilarityAdjustment
            && current.ParentLayoutId is { } parentId)
        {
            var parent = await layouts.GetAsync(parentId, ct);
            if (parent is null)
            {
                break;
            }

            current = parent;
        }

        return current;
    }

    private static Result<CreatedCandidate> Fail(string code, string message)
        => Result<CreatedCandidate>.Fail(code, message);
}

/// <summary>
/// 후보 렌더 업로드 (§10 PUT render) — idempotent.
///
/// 같은 SHA-256 재전송은 성공으로, 접수 뒤 다른 byte 는 409 로 (§10.1).
/// 렌더가 붙으면 run 이 평가로 재개되고 큐에 올라간다 — 화면을 떠나도 서버가 끝낸다 (§9.2).
/// </summary>
public sealed class UploadCandidateRenderHandler(
    ISimilarityRepository similarity,
    IBlobStorage blobs,
    ISimilarityQueue queue,
    IClock clock)
{
    // StartSimilarityRunHandler 와 같은 프레임 규칙 — 긴 변 1024, 비율은 원본을 따른다
    private const int FrameLongSide = 1024;
    private const int FrameMinSide = 256;
    private const long MaxRenderBytes = 8 * 1024 * 1024;

    public async Task<Result<SimilarityEvaluation>> HandleAsync(
        Guid jobId, Guid runId, Guid evaluationId, byte[] renderBytes, CancellationToken ct)
    {
        var run = await similarity.GetRunAsync(runId, ct);
        if (run is null || run.JobId != jobId)
        {
            return Fail(ErrorCode.SimilarityRunNotFound, "이 작업의 실행이 아닙니다");
        }

        var evaluation = await similarity.GetEvaluationAsync(evaluationId, ct);
        if (evaluation is null || evaluation.RunId != runId)
        {
            return Fail(ErrorCode.SimilarityRunNotFound, "이 실행의 평가가 아닙니다");
        }

        var sha = Convert.ToHexString(SHA256.HashData(renderBytes));

        // idempotent 재전송 — 같은 byte 는 성공, 다른 byte 는 충돌 (§10.1)
        if (evaluation.Render is not null)
        {
            return evaluation.Render.Sha256 == sha
                ? Result<SimilarityEvaluation>.Ok(evaluation)
                : Fail(ErrorCode.SimilarityConflict, "이미 다른 렌더가 접수되었습니다");
        }

        if (renderBytes.LongLength > MaxRenderBytes)
        {
            return Fail(ErrorCode.SimilarityRenderTooLarge, "렌더는 8 MiB 까지입니다");
        }

        if (!IsPng(renderBytes, out var width, out var height)
            || Math.Max(width, height) != FrameLongSide || Math.Min(width, height) < FrameMinSide)
        {
            return Fail(
                ErrorCode.SimilarityRenderInvalid,
                $"렌더는 긴 변 {FrameLongSide} PNG 여야 합니다 (수신: {width}×{height})");
        }

        using var stream = new MemoryStream(renderBytes);
        var blobKey = await blobs.SaveAsync(stream, "image/png", ct);

        evaluation.AttachRender(
            new EvaluationRenderArtifact(blobKey, "image/png", renderBytes.LongLength, sha));
        run.ResumeEvaluating(clock.Now);
        await similarity.SaveChangesAsync(ct);

        await queue.EnqueueAsync(evaluation.Id, ct);

        return Result<SimilarityEvaluation>.Ok(evaluation);
    }

    private static bool IsPng(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(signature)
            || !"IHDR"u8.SequenceEqual(bytes.AsSpan(12, 4)))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16));
        height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
        return true;
    }

    private static Result<SimilarityEvaluation> Fail(string code, string message)
        => Result<SimilarityEvaluation>.Fail(code, message);
}
