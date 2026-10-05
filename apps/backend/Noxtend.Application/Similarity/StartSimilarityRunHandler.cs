using System.Buffers.Binary;
using System.Security.Cryptography;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Similarity;

namespace Noxtend.Application.Similarity;

public sealed record StartSimilarityRunRequest(
    Guid JobId,
    Guid ProviderConfigId,
    string Model,
    int MaxIterations,
    Guid LayoutId,
    string IdempotencyKey,
    byte[] RenderBytes,
    string RenderContentType);

/// <summary>시작 응답 — run 과 함께 기준 평가도 준다 (화면이 폴링 대상을 안다).</summary>
public sealed record StartedSimilarityRun(SimilarityRun Run, SimilarityEvaluation BaselineEvaluation);

/// <summary>
/// 유사도 실행 시작 — multipart 기준 렌더와 함께 (§10.1).
///
/// Design Ref: background-similarity-tuning §10.1 · §11.1 · §13
///
/// **모든 유료 호출은 명시적 시작 뒤에만.** 자격(§11.1)이 시작보다 먼저고, 같은
/// Idempotency-Key 의 재접수는 기존 run 을 돌려줘 유료 호출이 늘지 않는다.
/// 렌더는 서버가 다시 검증한다 — 형식·해상도·크기를 클라이언트에 맡기지 않는다.
/// </summary>
public sealed class StartSimilarityRunHandler(
    IJobRepository jobs,
    ISceneLayoutRepository layouts,
    ISimilarityRepository similarity,
    IProviderConfigRepository providers,
    IPromptCatalog prompts,
    IBlobStorage blobs,
    ISimilarityQueue queue,
    IClock clock)
{
    // 프레임 계약 — 긴 변 1024 고정, 비율은 원본을 따른다 (§8.1 의 정사각 고정을
    // 사용자 결정으로 대체: 원본과 같은 비율이어야 레터박스 없이 비교가 맞다)
    private const int FrameLongSide = 1024;
    private const int FrameMinSide = 256;
    private const long MaxRenderBytes = 8 * 1024 * 1024;

    public async Task<Result<StartedSimilarityRun>> HandleAsync(
        StartSimilarityRunRequest request, CancellationToken ct)
    {
        var job = await jobs.GetAsync(request.JobId, ct);
        if (job is null)
        {
            return Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        // ─── 자격 (§11.1) — 사유는 버튼 비활성 문구로 그대로 쓰인다 ───

        if (job.Category != AssetCategory.Background)
        {
            return Fail(ErrorCode.SimilarityNotReady, "배경 작업만 비교할 수 있습니다");
        }

        if (job.Status != JobStatus.Succeeded)
        {
            return Fail(ErrorCode.SimilarityNotReady, "제작이 완료된 작업만 비교할 수 있습니다");
        }

        var meshByPart = job.GeneratedMeshes
            .GroupBy(mesh => mesh.PartId)
            .ToDictionary(group => group.Key, group => group.OrderBy(m => m.CreatedAt).Last());
        if (job.Parts.Any(part => !meshByPart.ContainsKey(part.Id)))
        {
            return Fail(ErrorCode.SimilarityNotReady, "3D 가 없는 파츠가 있습니다");
        }

        var config = await providers.GetAsync(request.ProviderConfigId, ct);
        if (config is null || !config.IsEnabled
            || !ProviderCapabilities.Supports(config.Kind, ProviderCapability.SimilarityEvaluation))
        {
            return Fail(ErrorCode.SimilarityNotReady, "이 공급자는 유사도 평가를 지원하지 않습니다");
        }

        if (await prompts.GetActiveAsync(
                LlmOperationKind.SimilarityEvaluate, AssetCategory.Background, ct) is null)
        {
            return Fail(ErrorCode.SimilarityNotReady, "유사도 평가 프롬프트가 활성화되어 있지 않습니다");
        }

        // 기준 layout — 활성이고, 요청이 가리킨 그 revision 이고, 현재 mesh 조합의 것이어야 한다
        var layout = await layouts.GetActiveByJobAsync(request.JobId, ct);
        var signature = SceneMeshSignature.Compute(job.LayoutSignatureInputs());
        if (layout is null || layout.Id != request.LayoutId
            || layout.SourceMeshSignature != signature)
        {
            return Fail(ErrorCode.SceneRevisionStale, "기준 배치가 현재 3D 조합의 것이 아닙니다");
        }

        // ─── Idempotency (§10.1) — 같은 key + 같은 payload 는 기존 run ───

        var renderSha = Convert.ToHexString(SHA256.HashData(request.RenderBytes));

        var existing = await similarity.FindByIdempotencyKeyAsync(
            request.JobId, request.IdempotencyKey, ct);
        if (existing is not null)
        {
            var evaluations = await similarity.ListEvaluationsAsync(existing.Id, ct);
            var baseline = evaluations.FirstOrDefault(
                e => e.Kind == SimilarityEvaluationKind.Baseline);

            var samePayload = existing.ProviderConfigId == request.ProviderConfigId
                && existing.Model == request.Model
                && existing.MaxIterations == request.MaxIterations
                && baseline is not null
                && baseline.LayoutId == request.LayoutId
                && baseline.Render?.Sha256 == renderSha;

            return samePayload
                ? Result<StartedSimilarityRun>.Ok(new StartedSimilarityRun(existing, baseline!))
                : Fail(ErrorCode.SimilarityConflict, "같은 Idempotency-Key 로 다른 요청이 접수되었습니다");
        }

        if (await similarity.GetOpenRunByJobAsync(request.JobId, ct) is not null)
        {
            return Fail(ErrorCode.SimilarityConflict, "진행 중인 비교가 이미 있습니다");
        }

        // ─── 렌더 검증 (§13) — 저장 전에 전부 ───

        if (request.RenderBytes.LongLength > MaxRenderBytes)
        {
            return Fail(ErrorCode.SimilarityRenderTooLarge, "렌더는 8 MiB 까지입니다");
        }

        if (!IsPng(request.RenderBytes, out var width, out var height))
        {
            return Fail(ErrorCode.SimilarityRenderInvalid, "렌더는 PNG 여야 합니다");
        }

        if (Math.Max(width, height) != FrameLongSide || Math.Min(width, height) < FrameMinSide)
        {
            return Fail(
                ErrorCode.SimilarityRenderInvalid,
                $"렌더는 긴 변 {FrameLongSide}·짧은 변 {FrameMinSide} 이상이어야 합니다 (수신: {width}×{height})");
        }

        // ─── run + 기준 평가 생성 (유료 호출은 아직 없다 — 워커가 큐에서 꺼낼 때다) ───

        SimilarityRun run;
        try
        {
            run = SimilarityRun.Start(
                request.JobId, request.ProviderConfigId, request.Model,
                request.MaxIterations, request.IdempotencyKey, clock.Now);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Fail(ErrorCode.SimilarityIterationLimit, "후보 반복은 1..3회입니다");
        }

        using var stream = new MemoryStream(request.RenderBytes);
        var blobKey = await blobs.SaveAsync(stream, "image/png", ct);

        var evaluation = SimilarityEvaluation.CreateBaseline(
            run.Id, layout.Id,
            new EvaluationRenderArtifact(
                blobKey, "image/png", request.RenderBytes.LongLength, renderSha),
            clock.Now);

        await similarity.AddRunAsync(run, ct);
        await similarity.AddEvaluationAsync(evaluation, ct);
        await similarity.SaveChangesAsync(ct);

        await queue.EnqueueAsync(evaluation.Id, ct);

        return Result<StartedSimilarityRun>.Ok(new StartedSimilarityRun(run, evaluation));
    }

    /// <summary>PNG signature + IHDR 크기 — pixel 을 디코드하지 않고 정체만 본다.</summary>
    private static bool IsPng(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(signature))
        {
            return false;
        }

        // IHDR 은 signature 바로 뒤가 규격이다 — 길이(4) + "IHDR"(4) + 폭(4) + 높이(4)
        if (!"IHDR"u8.SequenceEqual(bytes.AsSpan(12, 4)))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16));
        height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
        return true;
    }

    private static Result<StartedSimilarityRun> Fail(string code, string message)
        => Result<StartedSimilarityRun>.Fail(code, message);
}
