using Noxtend.Application.Similarity;
using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Similarity;
using Noxtend.Infrastructure.Llm;
using InMemorySimilarityRepo = Noxtend.Infrastructure.Persistence.InMemory.InMemorySimilarityRepository;

namespace Noxtend.Tests.Application;

/// <summary>
/// 평가 실행 유스케이스. Design Ref: background-similarity-tuning §9.1 · §9.3 · §12
///
/// **정상 경로 회귀는 fake 공급자만으로 돈다** (D-09). 여기서 보는 것은 해석이 아니라
/// 배선이다 — 기준 성공이 run 을 보정 대기(ReadyForAdjustment)로 옮기는가, 계약 위반이
/// 재시도를 거쳐 run 실패로 닫히는가, 중복 배달이 조용히 넘어가는가.
/// </summary>
public sealed class SimilarityEvaluateHandlerTests
{
    [Fact]
    public async Task BaselineSuccess_StoresTheScoreAndOpensAdjustment()
    {
        var fixture = await EvaluationFixture.CreateAsync(FakeLlmProvider.Succeeding());

        await fixture.Evaluate.HandleAsync(fixture.EvaluationId, CancellationToken.None);

        var evaluation = await fixture.Similarity.GetEvaluationAsync(
            fixture.EvaluationId, CancellationToken.None);
        Assert.Equal(SimilarityEvaluationStatus.Succeeded, evaluation!.Status);
        // fake 응답의 가중 합: 72*.25+68*.2+65*.2+70*.15+60*.1+66*.1 = 67.7 → 68
        Assert.Equal(68, evaluation.Score!.Overall);
        Assert.Equal(2, evaluation.Adjustments.Count);
        Assert.Single(evaluation.RegenerationNotes);
        Assert.Equal(StubSimilarityPromptCatalog.VersionId, evaluation.PromptVersionId);

        var run = await fixture.Similarity.GetRunAsync(fixture.RunId, CancellationToken.None);
        Assert.Equal(SimilarityRunStatus.ReadyForAdjustment, run!.Status);
    }

    /// <summary>계약 위반은 재시도(3회)를 거쳐 run 실패로 닫힌다 — 무한 유료 재시도 방지 (§9.3).</summary>
    [Fact]
    public async Task ContractViolations_RetryThenFailTheRun()
    {
        var fixture = await EvaluationFixture.CreateAsync(
            FakeLlmProvider.Returning(_ => """{ "dimensions": [] }"""));

        // 1차 시도 — 실패 후 재적재
        await fixture.Evaluate.HandleAsync(fixture.EvaluationId, CancellationToken.None);
        Assert.Equal(2, fixture.Queue.Enqueued.Count);   // 최초 1 + 재적재 1

        // 2·3차 시도 — 소진
        await fixture.Evaluate.HandleAsync(fixture.EvaluationId, CancellationToken.None);
        await fixture.Evaluate.HandleAsync(fixture.EvaluationId, CancellationToken.None);

        var run = await fixture.Similarity.GetRunAsync(fixture.RunId, CancellationToken.None);
        Assert.Equal(SimilarityRunStatus.Failed, run!.Status);
        Assert.Equal(ErrorCode.SimilarityEvaluationInvalid, run.FailureCode);

        var evaluation = await fixture.Similarity.GetEvaluationAsync(
            fixture.EvaluationId, CancellationToken.None);
        Assert.Equal(3, evaluation!.AttemptCount);
    }

    /// <summary>중복 배달 — 이미 성공한 평가는 다시 돌지 않는다. DB 상태가 정본이다 (§12).</summary>
    [Fact]
    public async Task DuplicateDelivery_IsIgnored()
    {
        var fixture = await EvaluationFixture.CreateAsync(FakeLlmProvider.Succeeding());
        await fixture.Evaluate.HandleAsync(fixture.EvaluationId, CancellationToken.None);

        await fixture.Evaluate.HandleAsync(fixture.EvaluationId, CancellationToken.None);

        var evaluation = await fixture.Similarity.GetEvaluationAsync(
            fixture.EvaluationId, CancellationToken.None);
        Assert.Equal(1, evaluation!.AttemptCount);
    }

    /// <summary>취소된 run 의 평가는 집지 않는다 — 취소 뒤 유료 호출이 나가면 안 된다.</summary>
    [Fact]
    public async Task ACanceledRun_StopsItsEvaluations()
    {
        var fixture = await EvaluationFixture.CreateAsync(FakeLlmProvider.Succeeding());
        var run = await fixture.Similarity.GetRunAsync(fixture.RunId, CancellationToken.None);
        run!.Cancel(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        await fixture.Evaluate.HandleAsync(fixture.EvaluationId, CancellationToken.None);

        var evaluation = await fixture.Similarity.GetEvaluationAsync(
            fixture.EvaluationId, CancellationToken.None);
        Assert.Equal(SimilarityEvaluationStatus.Pending, evaluation!.Status);
        Assert.Equal(0, evaluation.AttemptCount);
    }

    // ─── 설정 — 시작 핸들러로 실제 경로를 태워 만든다 ───

    private sealed class EvaluationFixture
    {
        public required EvaluateSimilarityHandler Evaluate { get; init; }
        public required InMemorySimilarityRepo Similarity { get; init; }
        public required Noxtend.Infrastructure.Queue.InMemorySimilarityQueue Queue { get; init; }
        public required Guid RunId { get; init; }
        public required Guid EvaluationId { get; init; }

        public static async Task<EvaluationFixture> CreateAsync(ILlmProvider provider)
        {
            var fixture = await SimilarityFixture.CreateAsync();

            // 원본 이미지 — 평가가 reference 로 보낸다
            var images = new Noxtend.Infrastructure.Persistence.InMemory.InMemoryStoredImageRepository();
            var source = Noxtend.Domain.Upload.StoredImage.Create(
                await SaveBlobAsync(fixture), "source.png", "image/png", 4,
                new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
            await images.AddAsync(source, CancellationToken.None);
            SetSourceImage(fixture, source.Id);

            var started = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);
            Assert.True(started.IsSuccess);

            var evaluate = new EvaluateSimilarityHandler(
                fixture.Similarity, fixture.Layouts, fixture.Jobs, images, fixture.Blobs,
                new PassThroughTranscoder(),
                new StubSimilarityPromptCatalog(hasPrompt: true),
                new SingleProviderFactory(provider), fixture.Queue,
                new FixedClock(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));

            return new EvaluationFixture
            {
                Evaluate = evaluate,
                Similarity = fixture.Similarity,
                Queue = fixture.Queue,
                RunId = started.Value!.Run.Id,
                EvaluationId = started.Value.BaselineEvaluation.Id,
            };
        }

        private static async Task<string> SaveBlobAsync(SimilarityFixture fixture)
        {
            using var stream = new MemoryStream([1, 2, 3, 4]);
            return await fixture.Blobs.SaveAsync(stream, "image/png", CancellationToken.None);
        }

        // 작업이 만들어질 때의 SourceImageId 는 임의 값 — 평가가 읽을 실제 저장 이미지로 바꾼다
        private static void SetSourceImage(SimilarityFixture fixture, Guid imageId)
            => typeof(Noxtend.Domain.Job.PipelineJob)
                .GetProperty(nameof(Noxtend.Domain.Job.PipelineJob.SourceImageId))!
                .SetValue(fixture.Job, imageId);
    }

    private sealed class SingleProviderFactory(ILlmProvider provider) : ILlmProviderFactory
    {
        public Task<ILlmProvider> CreateAsync(Guid providerConfigId, string model, CancellationToken ct)
            => Task.FromResult(provider);
    }
}
