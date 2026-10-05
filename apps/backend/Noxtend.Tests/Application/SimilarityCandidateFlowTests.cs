using Noxtend.Application.Scene;
using Noxtend.Application.Similarity;
using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Similarity;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Application;

/// <summary>
/// 후보 생성 → 렌더 업로드 → 재평가 → 채택/거부 → 복원 (§9.2 · §4.3).
///
/// **어떤 실패·거부 경로도 기존 활성 revision 을 건드리지 않는다** (SC-03) — 그것이
/// 불변 revision 을 들인 이유다. 채택만 활성 교대를 일으키고, 그마저 한 저장 단위다.
/// </summary>
public sealed class SimilarityCandidateFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>후보는 비활성으로 태어나고(D-07), run 은 렌더 업로드를 기다린다.</summary>
    [Fact]
    public async Task CreateCandidate_MakesAnInactiveRevisionAwaitingItsRender()
    {
        var flow = await CandidateFlowFixture.CreateAsync();
        var adjustmentId = flow.BaselineAdjustmentIds[0];

        var result = await flow.CreateCandidate.HandleAsync(
            new CreateCandidateRequest(flow.JobId, flow.RunId, [adjustmentId]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var created = result.Value!;
        Assert.Equal(SceneLayoutState.Candidate, created.CandidateLayout.State);
        Assert.Equal(SimilarityEvaluationStatus.AwaitingRender, created.Evaluation.Status);
        Assert.Equal(SimilarityRunStatus.AwaitingRender, created.Run.Status);

        // 기준 활성은 그대로 (SC-03)
        var active = await flow.Layouts.GetActiveByJobAsync(flow.JobId, CancellationToken.None);
        Assert.Equal(flow.BaselineLayoutId, active!.Id);
    }

    [Fact]
    public async Task CreateCandidate_RejectsAnUnknownAdjustmentId()
    {
        var flow = await CandidateFlowFixture.CreateAsync();

        var result = await flow.CreateCandidate.HandleAsync(
            new CreateCandidateRequest(flow.JobId, flow.RunId, [Guid.NewGuid()]),
            CancellationToken.None);

        Assert.Equal(ErrorCode.SimilarityConflict, result.ErrorCode);
    }

    /// <summary>렌더 업로드 — 같은 SHA 재전송은 성공, 다른 byte 는 409 (§10.1).</summary>
    [Fact]
    public async Task UploadRender_IsIdempotentBySha()
    {
        var flow = await CandidateFlowFixture.CreateAsync();
        var created = await flow.MakeCandidateAsync()
;
        var png = SimilarityFixture.Png(1024, 1024)
;
        var first = await flow.UploadRender.HandleAsync(
            flow.JobId, flow.RunId, created.Evaluation.Id, png, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(SimilarityEvaluationStatus.Pending, first.Value!.Status);

        var again = await flow.UploadRender.HandleAsync(
            flow.JobId, flow.RunId, created.Evaluation.Id, png, CancellationToken.None);
        Assert.True(again.IsSuccess);

        var different = SimilarityFixture.Png(1024, 1024);
        different[different.Length - 1] ^= 0xFF;
        var conflict = await flow.UploadRender.HandleAsync(
            flow.JobId, flow.RunId, created.Evaluation.Id, different, CancellationToken.None);
        Assert.Equal(ErrorCode.SimilarityConflict, conflict.ErrorCode);
    }

    /// <summary>개선된 후보는 활성 교대 — 반복 여유가 있으면 다음 보정 검토 (§9.2).</summary>
    [Fact]
    public async Task AnImprovedCandidate_IsAdopted()
    {
        var flow = await CandidateFlowFixture.CreateAsync(candidateUniform: 80)
;
        var created = await flow.MakeCandidateAsync();
        await flow.UploadAndEvaluateAsync(created.Evaluation.Id);

        var active = await flow.Layouts.GetActiveByJobAsync(flow.JobId, CancellationToken.None);
        Assert.Equal(created.CandidateLayout.Id, active!.Id);

        var old = await flow.Layouts.GetAsync(flow.BaselineLayoutId, CancellationToken.None);
        Assert.Equal(SceneLayoutState.Superseded, old!.State);

        var run = await flow.Similarity.GetRunAsync(flow.RunId, CancellationToken.None);
        Assert.Equal(SimilarityRunStatus.ReadyForAdjustment, run!.Status);   // maxIterations 2
    }

    /// <summary>기준 미달 후보는 거부 — 기존 활성 유지, run 종료 (§9.2 8단계).</summary>
    [Fact]
    public async Task AWorseCandidate_IsRejectedKeepingTheActive()
    {
        var flow = await CandidateFlowFixture.CreateAsync(candidateUniform: 50);
        var created = await flow.MakeCandidateAsync();
        await flow.UploadAndEvaluateAsync(created.Evaluation.Id);

        var active = await flow.Layouts.GetActiveByJobAsync(flow.JobId, CancellationToken.None);
        Assert.Equal(flow.BaselineLayoutId, active!.Id);

        var candidate = await flow.Layouts.GetAsync(created.CandidateLayout.Id, CancellationToken.None);
        Assert.Equal(SceneLayoutState.Rejected, candidate!.State);

        var run = await flow.Similarity.GetRunAsync(flow.RunId, CancellationToken.None);
        Assert.Equal(SimilarityRunStatus.Completed, run!.Status);
    }

    /// <summary>복원 = 값 복사 (§4.3) — 과거 revision 이 새 번호의 활성으로 돌아온다.</summary>
    [Fact]
    public async Task Restore_BringsAnOldRevisionBackAsANewActive()
    {
        var flow = await CandidateFlowFixture.CreateAsync(candidateUniform: 80);
        var created = await flow.MakeCandidateAsync();
        await flow.UploadAndEvaluateAsync(created.Evaluation.Id);   // 채택 — 활성이 후보로

        var restore = new RestoreSceneRevisionHandler(flow.Jobs, flow.Layouts, new FixedClock(Now));
        var result = await restore.HandleAsync(flow.JobId, flow.BaselineLayoutId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var restored = result.Value!;
        Assert.Equal(SceneLayoutState.Active, restored.State);
        Assert.Equal(SceneLayoutOrigin.Restore, restored.Origin);
        Assert.True(restored.Revision > created.CandidateLayout.Revision);

        // 채택됐던 후보는 이력으로 물러난다
        var superseded = await flow.Layouts.GetAsync(created.CandidateLayout.Id, CancellationToken.None);
        Assert.Equal(SceneLayoutState.Superseded, superseded!.State);
    }
}

/// <summary>기준 평가까지 끝난 run 위에서 후보 흐름을 돌리는 조립.</summary>
file sealed class CandidateFlowFixture
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    public required Guid JobId { get; init; }
    public required Guid RunId { get; init; }
    public required Guid BaselineLayoutId { get; init; }
    public required IReadOnlyList<Guid> BaselineAdjustmentIds { get; init; }
    public required Noxtend.Infrastructure.Persistence.InMemory.InMemoryJobRepository Jobs { get; init; }
    public required Noxtend.Infrastructure.Persistence.InMemory.InMemorySceneLayoutRepository Layouts { get; init; }
    public required Noxtend.Infrastructure.Persistence.InMemory.InMemorySimilarityRepository Similarity { get; init; }
    public required CreateSimilarityCandidateHandler CreateCandidate { get; init; }
    public required UploadCandidateRenderHandler UploadRender { get; init; }
    public required EvaluateSimilarityHandler Evaluate { get; init; }

    public async Task<CreatedCandidate> MakeCandidateAsync()
    {
        var result = await CreateCandidate.HandleAsync(
            new CreateCandidateRequest(JobId, RunId, [BaselineAdjustmentIds[0]]),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    public async Task UploadAndEvaluateAsync(Guid evaluationId)
    {
        var upload = await UploadRender.HandleAsync(
            JobId, RunId, evaluationId, SimilarityFixture.Png(1024, 1024), CancellationToken.None);
        Assert.True(upload.IsSuccess);
        await Evaluate.HandleAsync(evaluationId, CancellationToken.None);

        var evaluation = await Similarity.GetEvaluationAsync(evaluationId, CancellationToken.None);
        Assert.Equal(SimilarityEvaluationStatus.Succeeded, evaluation!.Status);
    }

    /// <param name="candidateUniform">후보 평가의 여섯 축 균일 점수 — 기준(68)과의 비교를 가른다.</param>
    public static async Task<CandidateFlowFixture> CreateAsync(int candidateUniform = 80)
    {
        var fixture = await SimilarityFixture.CreateAsync();

        // 원본 이미지 — 평가 reference
        var images = new Noxtend.Infrastructure.Persistence.InMemory.InMemoryStoredImageRepository();
        using (var stream = new MemoryStream([1, 2, 3, 4]))
        {
            var blobKey = await fixture.Blobs.SaveAsync(stream, "image/png", CancellationToken.None);
            var source = Noxtend.Domain.Upload.StoredImage.Create(
                blobKey, "source.png", "image/png", 4, Now);
            await images.AddAsync(source, CancellationToken.None);
            typeof(Noxtend.Domain.Job.PipelineJob)
                .GetProperty(nameof(Noxtend.Domain.Job.PipelineJob.SourceImageId))!
                .SetValue(fixture.Job, source.Id);
        }

        var clock = new FixedClock(Now);
        var prompts = new StubSimilarityPromptCatalog(hasPrompt: true);

        // 기준 평가는 canned 기본(68), 후보 평가는 균일 점수 — 호출 순서로 가른다
        var calls = 0;
        var provider = FakeLlmProvider.Returning(_ =>
            ++calls == 1 ? FakeLlmProvider.SimilarityEvaluateJson : UniformJson(candidateUniform));

        var evaluate = new EvaluateSimilarityHandler(
            fixture.Similarity, fixture.Layouts, fixture.Jobs, images, fixture.Blobs,
                new PassThroughTranscoder(),
            prompts, new StubFactory(provider), fixture.Queue, clock);

        // 기준 평가까지 진행
        var started = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);
        Assert.True(started.IsSuccess);
        await evaluate.HandleAsync(started.Value!.BaselineEvaluation.Id, CancellationToken.None);

        var baseline = await fixture.Similarity.GetEvaluationAsync(
            started.Value.BaselineEvaluation.Id, CancellationToken.None);
        Assert.Equal(SimilarityEvaluationStatus.Succeeded, baseline!.Status);

        return new CandidateFlowFixture
        {
            JobId = fixture.Job.Id,
            RunId = started.Value.Run.Id,
            BaselineLayoutId = fixture.ActiveLayout.Id,
            BaselineAdjustmentIds = [.. baseline.Adjustments.Select(a => a.Id)],
            Jobs = fixture.Jobs,
            Layouts = fixture.Layouts,
            Similarity = fixture.Similarity,
            CreateCandidate = new CreateSimilarityCandidateHandler(
                fixture.Similarity, fixture.Layouts, clock),
            UploadRender = new UploadCandidateRenderHandler(
                fixture.Similarity, fixture.Blobs, fixture.Queue, clock),
            Evaluate = evaluate,
        };
    }

    private static string UniformJson(int score)
    {
        var kinds = new[] { "composition", "camera", "scale", "shape", "material", "lighting" };
        var dimensions = string.Join(",", kinds.Select(kind =>
            $$"""{ "kind": "{{kind}}", "score": {{score}}, "evidence": "관찰", "recommendation": "권고" }"""));
        return $$"""{ "dimensions": [{{dimensions}}], "adjustments": [], "regenerationNotes": [] }""";
    }

    private sealed class StubFactory(ILlmProvider provider) : ILlmProviderFactory
    {
        public Task<ILlmProvider> CreateAsync(Guid providerConfigId, string model, CancellationToken ct)
            => Task.FromResult(provider);
    }
}
