using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Similarity;

namespace Noxtend.Application.Similarity;

/// <summary>
/// 평가 한 건 실행 — 워커가 큐에서 꺼낸 뒤 부르는 유스케이스.
///
/// Design Ref: background-similarity-tuning §9.1 · §9.3 · §12
///
/// **중복 배달에 안전해야 한다** (§12) — Pending 이 아닌 평가는 조용히 넘긴다.
/// DB 의 상태가 정본이고 큐는 알림일 뿐이다. transient 실패는 같은 평가 행의
/// attempt 로 다시 돌고(최대 3회), 소진하면 run 이 실패로 닫힌다.
/// 기준 평가 성공은 run 을 보정 선택 대기(ReadyForAdjustment)로 옮긴다.
/// </summary>
public sealed class EvaluateSimilarityHandler(
    ISimilarityRepository similarity,
    ISceneLayoutRepository layouts,
    IJobRepository jobs,
    IStoredImageRepository images,
    IBlobStorage blobs,
    IImageTranscoder transcoder,
    IPromptCatalog prompts,
    ILlmProviderFactory providerFactory,
    ISimilarityQueue queue,
    IClock clock)
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    public async Task HandleAsync(Guid evaluationId, CancellationToken ct)
    {
        var evaluation = await similarity.GetEvaluationAsync(evaluationId, ct);
        if (evaluation is null
            || evaluation.Status is not (SimilarityEvaluationStatus.Pending
                or SimilarityEvaluationStatus.Failed))
        {
            // 중복 배달 또는 이미 처리됨 — DB 상태가 정본이다 (§12)
            return;
        }

        var run = await similarity.GetRunAsync(evaluation.RunId, ct);
        if (run is null || run.IsTerminal)
        {
            return;
        }

        evaluation.BeginAttempt(clock.Now + Lease, clock.Now);
        await similarity.SaveChangesAsync(ct);

        try
        {
            var rawJson = await CallProviderAsync(run, evaluation, ct);
            var parsed = SimilarityEvaluationParser.Parse(rawJson);

            // 여섯 축·범위·상한 검증은 도메인이 최종 판정한다 (§5.4)
            var score = SimilarityScore.Create(parsed.Dimensions);
            var prompt = await RequirePromptAsync(ct);

            evaluation.Succeed(
                score, parsed.Adjustments, parsed.RegenerationNotes, prompt.VersionId, clock.Now);

            if (evaluation.Kind == SimilarityEvaluationKind.Baseline)
            {
                // 기준 완료 — 보정 선택은 사용자의 몫이다 (§9.1)
                run.MarkReadyForAdjustment(clock.Now);
            }
            else
            {
                await SettleCandidateAsync(run, evaluation, ct);
            }

            await similarity.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await FailAttemptAsync(run, evaluation, ex, ct);
        }
    }

    /// <summary>
    /// 후보 판정 (§9.2 5~8단계) — 채택 조건은 셋 다 만족해야 한다 (§5.3):
    /// overall +3, 축별 하락 ≤10, 기준 revision 이 여전히 활성.
    /// 채택은 활성 교대(Superseded + Adopt)와 같은 저장 단위다. 거부·stale 은
    /// 기존 활성을 절대 건드리지 않는다 (SC-03).
    /// </summary>
    private async Task SettleCandidateAsync(
        SimilarityRun run, SimilarityEvaluation evaluation, CancellationToken ct)
    {
        var candidate = await layouts.GetAsync(evaluation.LayoutId, ct)
            ?? throw new SimilarityEvaluationFormatException("후보 revision 이 사라졌습니다");
        var active = await layouts.GetActiveByJobAsync(run.JobId, ct);

        // 다른 요청이 활성을 바꿨다 — 그 배치 위의 채택은 거짓이 된다 (§12)
        if (active is null || active.Id != candidate.ParentLayoutId)
        {
            candidate.Reject();
            run.Fail(ErrorCode.SceneRevisionStale, clock.Now);
            return;
        }

        // 부모 점수 — 기준(부모 revision)을 평가한 최신 성공 결과
        var evaluations = await similarity.ListEvaluationsAsync(run.Id, ct);
        var parent = evaluations.LastOrDefault(e =>
            e.LayoutId == candidate.ParentLayoutId
            && e.Status == SimilarityEvaluationStatus.Succeeded);

        var adopt = parent?.Score is not null
            && evaluation.Score is not null
            && SimilarityScore.ShouldAdopt(parent.Score, evaluation.Score);

        if (adopt)
        {
            active.MarkSuperseded();
            candidate.Adopt();

            // 반복 여유가 있으면 다음 보정 검토, 아니면 종료 (§9.2 8단계)
            if (run.CurrentIteration < run.MaxIterations)
            {
                run.MarkReadyForAdjustment(clock.Now);
            }
            else
            {
                run.Complete(clock.Now);
            }
        }
        else
        {
            // 기준 미달 — 후보만 닫고 기존 활성은 유지. 거부는 run 을 끝낸다 (§9.2)
            candidate.Reject();
            run.Complete(clock.Now);
        }
    }

    private async Task<string> CallProviderAsync(
        SimilarityRun run, SimilarityEvaluation evaluation, CancellationToken ct)
    {
        var job = await jobs.GetAsync(run.JobId, ct)
            ?? throw new SimilarityEvaluationFormatException("작업이 사라졌습니다");

        var prompt = await RequirePromptAsync(ct);

        // reference 는 사용자의 원본, render 는 저장된 평가 렌더 — 순서가 계약이다 (§7.2).
        // 렌더를 먼저 읽는다: reference 를 **렌더와 같은 프레임**으로 정규화해야
        // 두 장의 구도 비교가 공정하다 (원본 비율 캡처 — 사용자 결정)
        var render = await LoadBlobAsync(
            evaluation.Render ?? throw new SimilarityEvaluationFormatException("렌더가 없습니다"), ct);
        var (frameWidth, frameHeight) = PngDimensions(render.Bytes);
        var reference = await LoadReferenceAsync(job.SourceImageId, frameWidth, frameHeight, ct);

        var provider = await providerFactory.CreateAsync(run.ProviderConfigId, run.Model, ct);

        var result = await provider.CompleteAsync(
            new LlmRequest(
                LlmCallContext.ForSimilarityEvaluation(
                    job.Id, evaluation.Id, prompt.VersionId, run.ProviderConfigId, run.Model),
                prompt.System,
                prompt.User,
                [new LlmImage("reference", reference), new LlmImage("render", render)],
                prompt.JsonSchema),
            ct);

        return result.RawJson;
    }

    private async Task<PromptSnapshot> RequirePromptAsync(CancellationToken ct)
        => await prompts.GetActiveAsync(
                Domain.Llm.LlmOperationKind.SimilarityEvaluate,
                Domain.Job.AssetCategory.Background, ct)
            ?? throw new SimilarityEvaluationFormatException("유사도 평가 프롬프트가 없습니다");

    /// <summary>실패 처리 — 여유가 있으면 같은 행으로 재시도, 소진하면 run 을 닫는다 (§9.3).</summary>
    private async Task FailAttemptAsync(
        SimilarityRun run, SimilarityEvaluation evaluation, Exception ex, CancellationToken ct)
    {
        evaluation.Fail(clock.Now);

        if (evaluation.AttemptCount < MaxAttempts)
        {
            await similarity.SaveChangesAsync(ct);
            await queue.EnqueueAsync(evaluation.Id, ct);
            return;
        }

        // 파서의 모양 위반도, 도메인의 여섯 축·상한 위반(ArgumentException)도 계약 위반이다
        run.Fail(
            ex is SimilarityEvaluationFormatException or ArgumentException
                ? ErrorCode.SimilarityEvaluationInvalid
                : ErrorCode.ProviderCallFailed,
            clock.Now);
        await similarity.SaveChangesAsync(ct);
    }

    /// <summary>
    /// reference 정규화 (§8.1) — EXIF 방향을 적용하고 **렌더와 같은 프레임**에
    /// contain 으로 배치한다. 원본 blob 은 바뀌지 않는다: 평가 요청에 실을 byte 만
    /// 만든다. 프레임이 다르면 비교가 공정하지 않다.
    /// </summary>
    private async Task<ImageContent> LoadReferenceAsync(
        Guid sourceImageId, int frameWidth, int frameHeight, CancellationToken ct)
    {
        var image = await images.GetAsync(sourceImageId, ct)
            ?? throw new SimilarityEvaluationFormatException("원본 이미지를 찾을 수 없습니다");

        await using var stream = await blobs.OpenReadAsync(image.BlobKey, ct);
        var normalized = await transcoder.NormalizeToFrameAsync(
            stream, frameWidth, frameHeight, "#f5f5f4", ct);
        return new ImageContent(normalized, "image/png");
    }

    /// <summary>렌더 PNG 의 IHDR 크기 — 저장 전에 검증된 값이라 여기선 실패가 계약 위반이다.</summary>
    private static (int Width, int Height) PngDimensions(byte[] bytes)
    {
        if (bytes.Length < 24)
        {
            throw new SimilarityEvaluationFormatException("렌더가 PNG 가 아닙니다");
        }

        return (
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)));
    }

    private async Task<ImageContent> LoadBlobAsync(
        EvaluationRenderArtifact artifact, CancellationToken ct)
    {
        await using var stream = await blobs.OpenReadAsync(artifact.BlobKey, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return new ImageContent(buffer.ToArray(), artifact.ContentType);
    }
}
