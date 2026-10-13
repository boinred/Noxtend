using Noxtend.Application.Pipeline;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;
using Noxtend.Infrastructure.Image;
using Noxtend.Infrastructure.Persistence;
using SkiaSharp;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Application;

public sealed class SpriteSourceTaskTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const string TextModel = "text-model";

    // ---- 보완 ----

    [Fact]
    public async Task Enhance_AppliesTrimmedPromptAndRecordsTaskContext()
    {
        var llm = new CapturingLlm(_ => """{"prompt":"  상세한 80년대 서울 골목  "}""");
        var f = new PipelineFixture(llm);
        var (job, textProvider) = await DraftJob(f);
        var task = job.PlanSourceEnhance("80년대 서울", Guid.NewGuid()).Value!;

        Assert.Equal(RunTaskOutcome.Succeeded, await f.RunSpriteSourceEnhance.HandleAsync(task.Id, Ct));

        Assert.Equal("상세한 80년대 서울 골목", job.Sprites!.SourceDraft!.Prompt);
        Assert.Equal(TaskStatus.Succeeded, task.Status);
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites.Phase);
        var request = Assert.Single(llm.Requests);
        Assert.Equal(job.Id, request.Context.JobId);
        Assert.Equal(task.Id, request.Context.TaskId);
        Assert.Equal(LlmOperationKind.EnhanceSpriteSource, request.Context.Kind);
        Assert.Equal(textProvider, request.Context.ProviderConfigId);
        Assert.Equal(TextModel, request.Context.Model);
        Assert.Equal(f.Prompts.VersionIdFor(LlmOperationKind.EnhanceSpriteSource), request.Context.PromptVersionId);
        Assert.Empty(request.Images);
        Assert.Contains("\"view\":\"sideView\"", request.System);
        Assert.Contains("80년대 서울", request.User);
        Assert.Contains((LlmOperationKind.EnhanceSpriteSource, AssetCategory.Background), f.Prompts.Queries);
    }

    [Fact]
    public async Task Enhance_UsesFrozenDescriptionAndKeepsTemplateTextLiteral()
    {
        var llm = new CapturingLlm(_ => """{"prompt":"결과"}""");
        var f = new PipelineFixture(llm);
        var (job, _) = await DraftJob(f);
        var task = job.PlanSourceEnhance("항구 {{settings}}", Guid.NewGuid()).Value!;

        await f.RunSpriteSourceEnhance.HandleAsync(task.Id, Ct);

        Assert.Contains("항구 {{settings}}", Assert.Single(llm.Requests).User);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"prompt":""}""")]
    [InlineData("""{"prompt":"   "}""")]
    [InlineData("""{"prompt":3}""")]
    [InlineData("""{"prompt":null}""")]
    [InlineData("[]")]
    public async Task Enhance_ContractViolationRetriesWithoutApplying(string raw)
    {
        var f = new PipelineFixture(new CapturingLlm(_ => raw));
        var (job, _) = await DraftJob(f);
        var task = job.PlanSourceEnhance("설명", Guid.NewGuid()).Value!;

        await f.RunSpriteSourceEnhance.HandleAsync(task.Id, Ct);

        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Equal(1, task.AttemptCount);
        Assert.Null(job.Sprites!.SourceDraft!.Prompt);
    }

    [Fact]
    public async Task Enhance_PromptOverFourThousandCharsRetries()
    {
        var f = new PipelineFixture(new CapturingLlm(_ => "{\"prompt\":\"" + new string('p', 4001) + "\"}"));
        var (job, _) = await DraftJob(f);
        var task = job.PlanSourceEnhance("설명", Guid.NewGuid()).Value!;

        await f.RunSpriteSourceEnhance.HandleAsync(task.Id, Ct);

        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Null(job.Sprites!.SourceDraft!.Prompt);
    }

    [Fact]
    public async Task Enhance_ExhaustedContractRetriesFailTheTaskButNotTheJob()
    {
        var f = new PipelineFixture(new CapturingLlm(_ => "{}"), new() { MaxAttempts = 1 });
        var (job, _) = await DraftJob(f);
        var task = job.PlanSourceEnhance("설명", Guid.NewGuid()).Value!;

        Assert.Equal(RunTaskOutcome.Failed, await f.RunSpriteSourceEnhance.HandleAsync(task.Id, Ct));

        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.NotEqual(JobStatus.Failed, job.Status);
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites!.Phase);
    }

    [Theory]
    [InlineData(true, TaskStatus.Pending)]
    [InlineData(false, TaskStatus.Failed)]
    public async Task Enhance_ProviderErrorsFollowTransience(bool transient, TaskStatus expected)
    {
        var f = new PipelineFixture(new CapturingLlm(_ => throw new ProviderCallFailedException("공급자 오류", isTransient: transient)));
        var (job, _) = await DraftJob(f);
        var task = job.PlanSourceEnhance("설명", Guid.NewGuid()).Value!;

        await f.RunSpriteSourceEnhance.HandleAsync(task.Id, Ct);

        Assert.Equal(expected, task.Status);
        Assert.Null(job.Sprites!.SourceDraft!.Prompt);
    }

    [Fact]
    public async Task Enhance_MissingActivePromptFailsWithoutProviderCall()
    {
        var llm = new CapturingLlm(_ => """{"prompt":"x"}""");
        var f = new PipelineFixture(llm);
        f.Prompts.Remove(LlmOperationKind.EnhanceSpriteSource);
        var (job, _) = await DraftJob(f);
        var task = job.PlanSourceEnhance("설명", Guid.NewGuid()).Value!;

        await f.RunSpriteSourceEnhance.HandleAsync(task.Id, Ct);

        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.Empty(llm.Requests);
    }

    [Fact]
    public async Task Enhance_CanceledDuringCallDoesNotApplyPrompt()
    {
        var f = new PipelineFixture();
        var (job, _) = await DraftJob(f);
        var task = job.PlanSourceEnhance("설명", Guid.NewGuid()).Value!;
        var llm = new CapturingLlm(_ =>
        {
            job.Cancel(f.Clock.Now);
            return """{"prompt":"늦은 결과"}""";
        });
        var handler = f.NewSpriteSourceEnhance(llm);

        Assert.Equal(RunTaskOutcome.Canceled, await handler.HandleAsync(task.Id, Ct));

        Assert.Null(job.Sprites!.SourceDraft!.Prompt);
    }

    // ---- 생성 ----

    [Fact]
    public async Task Generate_StoresOneCandidateWithInspectedFormatAndFrozenPrompt()
    {
        var image = new CapturingImage(FakeImageProvider.Succeeding());
        var f = new PipelineFixture(imageProvider: image);
        f.Prompts.SetForCategory(LlmOperationKind.GenerateSpriteSource, AssetCategory.Background,
            "설정 우선 {{settings}} / 장면 {{prompt}}");
        var (job, _) = await DraftJob(f);
        var baseline = f.Blobs.Count;
        var task = job.PlanSourceGeneration("  항구 {{literal}}  ", Guid.NewGuid()).Value!;

        Assert.Equal(RunTaskOutcome.Succeeded, await f.RunSpriteSourceGeneration.HandleAsync(task.Id, Ct));

        var candidate = Assert.Single(job.Sprites!.SourceDraft!.Candidates);
        Assert.Equal("항구 {{literal}}", candidate.Prompt);
        Assert.Equal(task.SourceInput!.Prompt, candidate.Prompt);
        Assert.Equal("image/png", candidate.ContentType);
        Assert.Equal((64, 48), (candidate.Width, candidate.Height));
        Assert.Equal(f.Clock.Now, candidate.CreatedAt);
        Assert.Equal(baseline + 1, f.Blobs.Count);
        await using (var blob = await f.Blobs.OpenReadAsync(candidate.BlobKey, Ct)) Assert.True(blob.Length > 0);
        // 영속화 serializer가 거절하지 않는 키여야 후보가 저장된다
        Assert.Equal(candidate.BlobKey, SpriteJsonSerializer.ValidateBlobKey(candidate.BlobKey));
        Assert.True(candidate.BlobKey.Length <= 512 && candidate.ContentType.Length <= 64);
        Assert.Null(job.SourceImageId);
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites.Phase);

        var request = Assert.Single(image.Requests);
        Assert.Empty(request.Reference);
        Assert.Equal(ImageBackground.Opaque, request.Background);
        Assert.Equal("64x48", request.Size);
        Assert.Contains("항구 {{literal}}", request.Prompt);
        Assert.Contains("\"view\":\"sideView\"", request.Prompt);
        Assert.Equal(job.Id, request.Context.JobId);
        Assert.Equal(task.Id, request.Context.TaskId);
        Assert.Null(request.Context.PartId);
        Assert.Null(request.Context.SourceGenerationId);
        Assert.Equal(TaskKind.GenerateSpriteSource, request.Context.Kind);
        Assert.Equal(job.ImageProviderConfigId, request.Context.ProviderConfigId);
        Assert.Equal(StubModelCatalog.DefaultImageModel, request.Context.Model);
        Assert.Equal(f.Prompts.VersionIdFor(LlmOperationKind.GenerateSpriteSource), request.Context.PromptVersionId);
        Assert.Contains((LlmOperationKind.GenerateSpriteSource, AssetCategory.Background), f.Prompts.Queries);
    }

    [Fact]
    public async Task Generate_RecordsJpegAsInspectedType()
    {
        var f = new PipelineFixture(imageProvider: FakeImageProvider.Returning(
            SpriteGenerationTests.Png(32, 20, SKEncodedImageFormat.Jpeg), "image/jpeg"));
        var (job, _) = await DraftJob(f);
        var task = job.PlanSourceGeneration("장면", Guid.NewGuid()).Value!;

        await f.RunSpriteSourceGeneration.HandleAsync(task.Id, Ct);

        var candidate = Assert.Single(job.Sprites!.SourceDraft!.Candidates);
        Assert.Equal("image/jpeg", candidate.ContentType);
        Assert.Equal((32, 20), (candidate.Width, candidate.Height));
    }

    [Fact]
    public async Task Generate_UsesPromptFrozenAtPlanning()
    {
        var image = new CapturingImage(FakeImageProvider.Succeeding());
        var f = new PipelineFixture(imageProvider: image);
        var (job, _) = await DraftJob(f);
        var task = job.PlanSourceGeneration("고정 프롬프트", Guid.NewGuid()).Value!;

        await f.RunSpriteSourceGeneration.HandleAsync(task.Id, Ct);

        Assert.Contains("고정 프롬프트", Assert.Single(image.Requests).Prompt);
        Assert.Equal("고정 프롬프트", job.Sprites!.SourceDraft!.Candidates.Single().Prompt);
    }

    [Fact]
    public async Task Generate_TwoImagesFailWithoutPaidRetryOrBlob()
    {
        var image = new CapturingImage(FakeImageProvider.Returning(SpriteGenerationTests.Png(64, 48), imageCount: 2));
        var f = new PipelineFixture(imageProvider: image);
        var (job, _) = await DraftJob(f);
        var baseline = f.Blobs.Count;
        var task = job.PlanSourceGeneration("장면", Guid.NewGuid()).Value!;

        Assert.Equal(RunTaskOutcome.Failed, await f.RunSpriteSourceGeneration.HandleAsync(task.Id, Ct));

        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.Equal(1, task.AttemptCount);
        Assert.Single(image.Requests);
        Assert.Empty(job.Sprites!.SourceDraft!.Candidates);
        Assert.Equal(baseline, f.Blobs.Count);
        Assert.NotEqual(JobStatus.Failed, job.Status);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("image/png")]
    public async Task Generate_NonImageBytesFailWithoutPaidRetry(string contentType)
    {
        var image = new CapturingImage(FakeImageProvider.Returning("not an image"u8.ToArray(), contentType));
        var f = new PipelineFixture(imageProvider: image);
        var (job, _) = await DraftJob(f);
        var baseline = f.Blobs.Count;
        var task = job.PlanSourceGeneration("장면", Guid.NewGuid()).Value!;

        await f.RunSpriteSourceGeneration.HandleAsync(task.Id, Ct);

        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.Single(image.Requests);
        Assert.Empty(job.Sprites!.SourceDraft!.Candidates);
        Assert.Equal(baseline, f.Blobs.Count);
    }

    [Theory]
    [InlineData(true, TaskStatus.Pending)]
    [InlineData(false, TaskStatus.Failed)]
    public async Task Generate_ProviderErrorsFollowTransience(bool transient, TaskStatus expected)
    {
        var f = new PipelineFixture(imageProvider: FakeImageProvider.Throwing(
            () => new ProviderCallFailedException("공급자 오류", isTransient: transient)));
        var (job, _) = await DraftJob(f);
        var task = job.PlanSourceGeneration("장면", Guid.NewGuid()).Value!;

        await f.RunSpriteSourceGeneration.HandleAsync(task.Id, Ct);

        Assert.Equal(expected, task.Status);
        Assert.Empty(job.Sprites!.SourceDraft!.Candidates);
    }

    [Fact]
    public async Task Generate_CanceledAfterSaveCleansUpOrphanBlob()
    {
        var f = new PipelineFixture();
        var (job, _) = await DraftJob(f);
        var baseline = f.Blobs.Count;
        var task = job.PlanSourceGeneration("장면", Guid.NewGuid()).Value!;
        var image = new CapturingImage(FakeImageProvider.Succeeding(), () => job.Cancel(f.Clock.Now));
        var handler = f.NewSpriteSourceGeneration(image);

        Assert.Equal(RunTaskOutcome.Canceled, await handler.HandleAsync(task.Id, Ct));

        Assert.Empty(job.Sprites!.SourceDraft!.Candidates);
        Assert.Equal(baseline, f.Blobs.Count);
    }

    [Fact]
    public async Task Generate_CandidateCapReachedDuringCallCleansUpSavedBlob()
    {
        var f = new PipelineFixture();
        var (job, _) = await DraftJob(f);
        for (var i = 0; i < SpriteSourceDraft.MaxCandidates - 1; i++)
        {
            var earlier = job.PlanSourceGeneration($"장면 {i}", Guid.NewGuid()).Value!;
            Assert.Equal(RunTaskOutcome.Succeeded, await f.RunSpriteSourceGeneration.HandleAsync(earlier.Id, Ct));
        }
        var baseline = f.Blobs.Count;
        var task = job.PlanSourceGeneration("마지막 장면", Guid.NewGuid()).Value!;
        // 호출 중 다른 경로가 마지막 자리를 채운 경합 재현
        var image = new CapturingImage(FakeImageProvider.Succeeding(), () => Assert.True(job.TryAddSourceCandidate(task.Id,
            new(Guid.NewGuid(), "images/raced", "image/png", 64, 48, "마지막 장면", f.Clock.Now))));

        Assert.Equal(RunTaskOutcome.Succeeded, await f.NewSpriteSourceGeneration(image).HandleAsync(task.Id, Ct));

        var candidates = job.Sprites!.SourceDraft!.Candidates;
        Assert.Equal(SpriteSourceDraft.MaxCandidates, candidates.Count);
        Assert.Equal("images/raced", candidates[^1].BlobKey);
        Assert.Equal(baseline, f.Blobs.Count);
    }

    [Fact]
    public async Task Generate_PublishedCandidateKeepsItsBlob()
    {
        var f = new PipelineFixture();
        var (job, _) = await DraftJob(f);
        var task = job.PlanSourceGeneration("장면", Guid.NewGuid()).Value!;

        await f.RunSpriteSourceGeneration.HandleAsync(task.Id, Ct);

        var candidate = Assert.Single(job.Sprites!.SourceDraft!.Candidates);
        await using var blob = await f.Blobs.OpenReadAsync(candidate.BlobKey, Ct);
        Assert.True(blob.Length > 0);
    }

    // ---- 보조 ----

    private static async Task<(PipelineJob Job, Guid TextProvider)> DraftJob(PipelineFixture f)
    {
        var providerId = await f.SeedProviderAsync();
        var draft = new SpriteSourceDraft("80년대 서울", null, providerId, TextModel, []);
        var job = PipelineJob.CreateSpriteDraft(providerId, StubModelCatalog.DefaultImageModel,
            new(SpriteView.SideView, SpriteOutputKind.Layers), new(64, 48), new(64, 48), draft, f.Clock.Now).Value!;
        await f.Jobs.AddAsync(job, Ct);
        return (job, providerId);
    }

    internal sealed class CapturingLlm(Func<LlmRequest, string> respond) : ILlmProvider
    {
        public List<LlmRequest> Requests { get; } = [];
        public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new LlmResult(respond(request), 10, 10));
        }
    }

    internal sealed class CapturingImage(IImageProvider inner, Action? afterCall = null) : IImageProvider
    {
        public List<ImageRequest> Requests { get; } = [];
        public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var result = await inner.GenerateAsync(request, ct);
            afterCall?.Invoke();
            return result;
        }
    }
}
