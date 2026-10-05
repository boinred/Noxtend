using Noxtend.Application.Common;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Prompt;
using Noxtend.Infrastructure.Image;
using Noxtend.Infrastructure.Llm;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Application;

/// <summary>
/// Design Ref: §8.1 #10~13 · #17 · #19 — 생성 경로를 인프라 없이 검증한다.
///
/// 실제 이미지 생성은 장당 수십 초에 돈이 들고 결과가 비결정적이라, 팬아웃·부분 성공·
/// 재시도를 그것으로 검증할 수 없다 (NFR-07).
/// </summary>
public sealed class RunGenerationTaskHandlerTests
{
    private sealed class CapturingImageProvider : IImageProvider
    {
        public ImageRequest? LastRequest { get; private set; }

        public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct)
        {
            // 공급자 공개 경계의 최종 프롬프트 포착
            LastRequest = request;
            return await FakeImageProvider.Succeeding().GenerateAsync(request, ct);
        }
    }

    private static PipelineTask FirstGeneration(PipelineJob job)
        => job.Tasks.First(t => t.Kind == TaskKind.Generate);

    /// <summary>공급자가 매번 같은 헤더 값을 실어 보낸다고 가정한 고정 응답.</summary>
    private sealed class RateLimitHeaderImageProvider(
        int remaining, TimeSpan resetAfter,
        int? remainingTokens = null, TimeSpan? tokensResetAfter = null) : IImageProvider
    {
        public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct)
        {
            var baseResult = await FakeImageProvider.Succeeding().GenerateAsync(request, ct);
            return baseResult with
            {
                RateLimitRemainingRequests = remaining,
                RateLimitResetAfter = resetAfter,
                RateLimitRemainingTokens = remainingTokens,
                RateLimitResetTokensAfter = tokensResetAfter,
            };
        }
    }

    // ─── #10 성공 경로 ───

    [Fact]
    public async Task Succeeds_AndAttachesTheImageToItsPart()
    {
        var fixture = new PipelineFixture(FakeLlmProviderFor("등대", "부두"));
        var job = await fixture.FanOutAsync();

        var task = FirstGeneration(job);
        var outcome = await fixture.RunGeneration.HandleAsync(task.Id, CancellationToken.None);

        Assert.Equal(RunTaskOutcome.Succeeded, outcome);
        Assert.Equal(TaskStatus.Succeeded, task.Status);

        var image = Assert.Single(job.GeneratedImages);
        Assert.Equal(task.PartId, image.PartId);
        Assert.Equal(task.Id, image.TaskId);

        // 파츠가 최신 이미지를 가리킨다
        var part = job.Parts.Single(p => p.Id == task.PartId);
        Assert.Equal(image.Id, part.GeneratedImageId);
    }

    // generation-rate-limiting §3① — 호출 성공 후 헤더 값이 RateLimiter에 기록되는지
    [Fact]
    public async Task RecordsRateLimitHeaders_AfterASuccessfulCall()
    {
        var provider = new RateLimitHeaderImageProvider(
            remaining: 2, resetAfter: TimeSpan.FromSeconds(45));
        var fixture = new PipelineFixture(FakeLlmProviderFor("등대"), imageProvider: provider);
        var job = await fixture.FanOutAsync();
        var task = FirstGeneration(job);

        await fixture.RunGeneration.HandleAsync(task.Id, CancellationToken.None);

        var status = await fixture.RateLimiter.GetStatusAsync(
            task.ProviderConfigId!.Value, CancellationToken.None);
        Assert.Equal(2, status!.RemainingRequests);
        Assert.Equal(fixture.Clock.Now + TimeSpan.FromSeconds(45), status.ResetsAt);
    }

    // TPM(토큰) 헤더도 같이 기록되는지 — RPM 만 기록하면 토큰 축 게이트가 항상 통과한다
    [Fact]
    public async Task RecordsTokenRateLimitHeaders_AfterASuccessfulCall()
    {
        var provider = new RateLimitHeaderImageProvider(
            remaining: 5, resetAfter: TimeSpan.FromMinutes(1),
            remainingTokens: 3000, tokensResetAfter: TimeSpan.FromSeconds(20));
        var fixture = new PipelineFixture(FakeLlmProviderFor("등대"), imageProvider: provider);
        var job = await fixture.FanOutAsync();
        var task = FirstGeneration(job);

        await fixture.RunGeneration.HandleAsync(task.Id, CancellationToken.None);

        var status = await fixture.RateLimiter.GetStatusAsync(
            task.ProviderConfigId!.Value, CancellationToken.None);
        Assert.Equal(3000, status!.RemainingTokens);
        Assert.Equal(fixture.Clock.Now + TimeSpan.FromSeconds(20), status.TokensResetAt);
    }

    // 남은 횟수가 0으로 기록돼 있으면, 다음 호출 전에 초기화 시각까지 실제로 기다린다
    [Fact]
    public async Task WaitsForResetBeforeCalling_WhenNoRequestsRemain()
    {
        var provider = new RateLimitHeaderImageProvider(
            remaining: 5, resetAfter: TimeSpan.FromMinutes(1));
        var fixture = new PipelineFixture(FakeLlmProviderFor("등대"), imageProvider: provider);
        var job = await fixture.FanOutAsync();
        var task = FirstGeneration(job);

        // 이전 호출이 "이제 없다"고 알려준 상태를 미리 만들어 둔다
        await fixture.RateLimiter.UpdateAsync(
            task.ProviderConfigId!.Value,
            new RateLimitStatus(RemainingRequests: 0, ResetsAt: fixture.Clock.Now + TimeSpan.FromMilliseconds(30)),
            CancellationToken.None);

        var before = DateTimeOffset.UtcNow;
        await fixture.RunGeneration.HandleAsync(task.Id, CancellationToken.None);
        var elapsed = DateTimeOffset.UtcNow - before;

        Assert.True(elapsed >= TimeSpan.FromMilliseconds(25));
    }

    [Fact]
    public async Task SendsAndStoresTheRequestedViewDirection()
    {
        var provider = new CapturingImageProvider();
        var fixture = new PipelineFixture(FakeLlmProviderFor("등대"), imageProvider: provider);
        var job = await fixture.FanOutAsync();
        job.PlanSelectedViews([ViewDirection.Right]);
        var task = job.Tasks.Single(task =>
            task.Kind == TaskKind.Generate && task.ViewDirection == ViewDirection.Right);

        await fixture.RunGeneration.HandleAsync(task.Id, CancellationToken.None);

        Assert.Contains("right view (90 degrees)", provider.LastRequest!.Prompt);
        Assert.Equal(ViewDirection.Right, Assert.Single(job.GeneratedImages).ViewDirection);
    }

    [Fact]
    public async Task StoresBytesInBlobStorage_NotInTheJob()
    {
        var fixture = new PipelineFixture(FakeLlmProviderFor("등대"));
        var job = await fixture.FanOutAsync();

        await fixture.RunGeneration.HandleAsync(FirstGeneration(job).Id, CancellationToken.None);

        // NFR-10 — 작업이 갖는 것은 키뿐이다. 바이트는 Blob 에만 있다
        var image = Assert.Single(job.GeneratedImages);
        Assert.NotEmpty(image.BlobKey);
        Assert.True(image.SizeBytes > 0);

        await using var stream = await fixture.Blobs.OpenReadAsync(image.BlobKey, CancellationToken.None);
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task FanOut_PlansFrontTasksPerPart_AndEnqueuesThemAll()
    {
        var fixture = new PipelineFixture(FakeLlmProviderFor("등대", "부두", "어선"));
        var job = await fixture.FanOutAsync();

        Assert.Equal(3, job.Tasks.Count(t => t.Kind == TaskKind.Generate));

        // 파츠당 정면 하나만 우선 즉시 실행 가능하다 (selective-view-generation §2)
        var enqueued = fixture.Queue.Enqueued.Count(e => e.Kind == TaskKind.Generate);
        Assert.Equal(3, enqueued);
    }

    [Fact]
    public async Task JobSucceeds_WhenEveryGenerationSucceeds()
    {
        var fixture = new PipelineFixture(FakeLlmProviderFor("등대", "부두"));
        var job = await fixture.FanOutAsync();

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToList())
        {
            await fixture.RunGeneration.HandleAsync(task.Id, CancellationToken.None);
        }

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(2, job.GeneratedImages.Count);
    }

    // ─── #11 재시도 ───

    [Fact]
    public async Task RetriesEmptyResponse_ThenFailsAtTheLimit()
    {
        var fixture = new PipelineFixture(
            FakeLlmProviderFor("등대"),
            imageProvider: FakeImageProvider.Returning([]));
        var job = await fixture.FanOutAsync();

        var task = FirstGeneration(job);
        await fixture.RunGenerationUntilTerminalAsync(task);

        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.Equal(ErrorCode.GenerationEmptyResponse, task.FailureReason);

        // 한도까지 실제로 다시 걸었는지 — 한 번에 확정하면 일시적 흔들림을 못 넘긴다
        Assert.Equal(fixture.Options.MaxAttempts, task.AttemptCount);
    }

    [Fact]
    public async Task RetriesUnsupportedFormat()
    {
        var fixture = new PipelineFixture(
            FakeLlmProviderFor("등대"),
            imageProvider: FakeImageProvider.Returning([1, 2, 3], "application/pdf"));
        var job = await fixture.FanOutAsync();

        var task = FirstGeneration(job);
        await fixture.RunGenerationUntilTerminalAsync(task);

        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.Equal(ErrorCode.GenerationUnsupportedFormat, task.FailureReason);
        Assert.Equal(fixture.Options.MaxAttempts, task.AttemptCount);
    }

    [Fact]
    public async Task SucceedsOnRetry_AfterATransientFailure()
    {
        var attempts = 0;
        var fixture = new PipelineFixture(
            FakeLlmProviderFor("등대"),
            imageProvider: FakeImageProvider.Throwing(
                () => attempts++ == 0 ? new ProviderBadResponseException("흔들림") : null));

        var job = await fixture.FanOutAsync();
        var task = FirstGeneration(job);

        await fixture.RunGenerationUntilTerminalAsync(task);

        // 첫 호출이 실패해도 두 번째가 성공하면 비싼 공정을 버리지 않는다
        Assert.Equal(TaskStatus.Succeeded, task.Status);
        Assert.Equal(2, task.AttemptCount);
    }

    // ─── #12 크기 초과는 즉시 실패 ───

    [Fact]
    public async Task FailsImmediately_WhenTheImageExceedsTheSizeLimit()
    {
        var fixture = new PipelineFixture(
            FakeLlmProviderFor("등대"),
            imageProvider: FakeImageProvider.Returning(new byte[64]),
            generationOptions: new GenerationOptions { MaxImageBytes = 16 });

        var job = await fixture.FanOutAsync();
        var task = FirstGeneration(job);

        await fixture.RunGeneration.HandleAsync(task.Id, CancellationToken.None);

        // 다시 걸어도 같은 모델이 같은 크기를 낸다 — 재시도는 비용만 태운다
        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.Equal(ErrorCode.GenerationImageTooLarge, task.FailureReason);
        Assert.Equal(1, task.AttemptCount);
    }

    [Fact]
    public async Task DoesNotStoreTheImage_WhenValidationFails()
    {
        var fixture = new PipelineFixture(
            FakeLlmProviderFor("등대"),
            imageProvider: FakeImageProvider.Returning(new byte[64]),
            generationOptions: new GenerationOptions { MaxImageBytes = 16 });

        var job = await fixture.FanOutAsync();
        await fixture.RunGeneration.HandleAsync(FirstGeneration(job).Id, CancellationToken.None);

        // 검증이 저장보다 먼저다 — 쓸 수 없는 바이트를 올리면 지워지지 않는다
        Assert.Empty(job.GeneratedImages);
    }

    // ─── #13 취소 ───

    [Fact]
    public async Task Canceled_LeavesNoImage()
    {
        var fixture = new PipelineFixture(FakeLlmProviderFor("등대"));
        var job = await fixture.FanOutAsync();

        var task = FirstGeneration(job);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await fixture.RunGeneration.HandleAsync(task.Id, cts.Token);

        Assert.Equal(TaskStatus.Canceled, task.Status);
        Assert.Empty(job.GeneratedImages);
    }

    [Fact]
    public async Task SkipsATaskWhoseJobIsAlreadyTerminal()
    {
        var fixture = new PipelineFixture(FakeLlmProviderFor("등대", "부두"));
        var job = await fixture.FanOutAsync();

        var task = FirstGeneration(job);
        job.Cancel(fixture.Clock.Now);

        var outcome = await fixture.RunGeneration.HandleAsync(task.Id, CancellationToken.None);

        // 툼스톤 — 대기 중 취소된 작업의 메시지는 큐에 남아 있다
        Assert.Equal(RunTaskOutcome.Skipped, outcome);
        Assert.Empty(job.GeneratedImages);
    }

    // ─── download-view-consistency §3.2 — 배경도 같은 참조 사슬 (SC-04·05) ───

    /// <summary>
    /// **배경 비정면도 완성된 정면을 참조 1로 받는다.** 이 사슬이 없어서 4방향이
    /// 방향마다 다른 물체로 그려졌다 — 참조 순서는 프롬프트의 "reference 1 = 정면"
    /// 문구와 맞아야 한다.
    /// </summary>
    [Fact]
    public async Task Background_NonFrontTask_ReferencesFrontThenOriginal()
    {
        var provider = new CapturingImageProvider();
        var fixture = new PipelineFixture(FakeLlmProviderFor("등대"), imageProvider: provider);
        var job = await fixture.FanOutAsync(AssetCategory.Background);

        var front = job.Tasks.Single(
            t => t.Kind == TaskKind.Generate && t.ViewDirection == ViewDirection.Front);
        await fixture.RunGeneration.HandleAsync(front.Id, CancellationToken.None);
        job.PlanSelectedViews([ViewDirection.Right]);

        var right = job.Tasks.Single(
            t => t.Kind == TaskKind.Generate && t.ViewDirection == ViewDirection.Right);
        await fixture.RunGeneration.HandleAsync(right.Id, CancellationToken.None);

        Assert.Equal(2, provider.LastRequest!.Reference.Count);
        Assert.Equal(ReferenceRole.FrontView, provider.LastRequest.Reference[0].Role);
        Assert.Equal(ReferenceRole.Original, provider.LastRequest.Reference[1].Role);
    }


    // ─── 부분 성공 통합 ───

    [Fact]
    public async Task JobPartiallySucceeds_WhenOnePartFails()
    {
        var calls = 0;
        var fixture = new PipelineFixture(
            FakeLlmProviderFor("등대", "부두"),
            // 둘째 파츠만 계속 실패시킨다 — 첫째는 첫 호출에 성공한다
            imageProvider: FakeImageProvider.Throwing(
                () => ++calls == 1 ? null : new ProviderCallFailedException("402", isTransient: false)));

        var job = await fixture.FanOutAsync();

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToList())
        {
            await fixture.RunGenerationUntilTerminalAsync(task);
        }

        // 비싼 성공분을 버리지 않는다 (Plan D-3)
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        Assert.Single(job.GeneratedImages);
    }

    // ─── #17 접수 검증 ───

    [Fact]
    public async Task Intake_RejectsAnUnknownImageProvider()
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, uploadId, providerId, StubModelCatalog.DefaultModel,
            CancellationToken.None,
            imageProviderConfigId: Guid.NewGuid(),
            imageModel: StubModelCatalog.DefaultImageModel);

        Assert.Equal(ErrorCode.JobImageProviderNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Intake_RejectsADisabledImageProvider()
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();
        var disabledId = await fixture.SeedProviderAsync(enabled: false);

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, uploadId, providerId, StubModelCatalog.DefaultModel,
            CancellationToken.None,
            imageProviderConfigId: disabledId,
            imageModel: StubModelCatalog.DefaultImageModel);

        Assert.Equal(ErrorCode.JobImageProviderDisabled, result.ErrorCode);
    }

    [Fact]
    public async Task Intake_RejectsAnImageModelOutsideTheList()
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, uploadId, providerId, StubModelCatalog.DefaultModel,
            CancellationToken.None,
            imageProviderConfigId: providerId,
            // 텍스트 모델을 이미지 자리에 넣었다 — 목록이 나뉘어 있으므로 여기서 걸린다
            imageModel: StubModelCatalog.DefaultModel);

        Assert.Equal(ErrorCode.JobImageModelUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task Intake_RejectsWhenTheGeneratePromptIsMissing()
    {
        var fixture = new PipelineFixture();
        fixture.Prompts.Remove(LlmOperationKind.Generate);

        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, uploadId, providerId, StubModelCatalog.DefaultModel,
            CancellationToken.None,
            imageProviderConfigId: providerId,
            imageModel: StubModelCatalog.DefaultImageModel);

        Assert.Equal(ErrorCode.PromptNotActive, result.ErrorCode);
    }

    [Fact]
    public async Task Intake_AllowsAJobWithoutImageGeneration()
    {
        // 이미지 공급자를 안 고르면 앞 세 단계로 끝난다 — 생성 프롬프트도 요구하지 않는다
        var fixture = new PipelineFixture();
        fixture.Prompts.Remove(LlmOperationKind.Generate);

        var job = await fixture.StartJobAsync();
        await fixture.RunAllStagesAsync(job);

        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.Generate);
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    // ─── #19 프롬프트 변수 ───

    [Fact]
    public void Generate_AllowsPartAndViewVariables()
    {
        var allowed = PromptTemplate.AllowedVariables(LlmOperationKind.Generate);

        // gender 는 slice 3 에서 더해졌다 — 베이스바디 그리기에 필수 (§D-03)
        Assert.Equal(
            ["gender", "partCategory", "partDescription", "partName", "scene", "viewDirection"],
            allowed.Order());
    }

    [Theory]
    [InlineData("occludedBy")]
    [InlineData("bounds")]
    [InlineData("parts")]
    public void Generate_RejectsVariablesThatWouldMisdirectTheModel(string variable)
    {
        // occludedBy 는 가려진 채로 그릴 유인이 되고, bounds 는 조립 단계의 정보이며,
        // parts 는 장면 전체를 따라 그릴 유인이 된다 (§3.5 · Plan R-2)
        Assert.NotNull(
            PromptTemplate.FindUnknownVariable($"{{{{{variable}}}}}", LlmOperationKind.Generate));
    }

    private static FakeLlmProvider FakeLlmProviderFor(params string[] parts)
        => FakeLlmProvider.Succeeding(parts);
}
