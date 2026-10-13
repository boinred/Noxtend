using Noxtend.Application.Pipeline;
using Noxtend.Application.Sprites;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Sprites;
using Noxtend.Infrastructure.Image;
using SkiaSharp;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Application;

public sealed class SpriteSourceCommandTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly SpriteCanvas[] Sizes = [new(1024, 1024), new(1536, 1024)];

    [Fact]
    public void PickSize_PrefersLandscapeOrSquareThenLargestArea()
    {
        Assert.Equal(new(1024, 1024), SpriteSourceSizes.Pick([new(1024, 1536), new(1024, 1024)]));
        Assert.Equal(new(1024, 1536), SpriteSourceSizes.Pick([new(1024, 1536)]));
        Assert.Equal(new(1792, 1024), SpriteSourceSizes.Pick([new(1024, 1024), new(1536, 1024), new(1792, 1024)]));
    }

    [Fact]
    public async Task CreateDraft_PlansEnhanceWithProvisionalCanvasAndReplaysReceipt()
    {
        var f = new PipelineFixture();
        var command = await Prepare(f);

        var receipt = await f.SpriteSource.CreateDraftAsync(command with { Description = "  80년대 서울  " }, Ct);

        Assert.True(receipt.IsSuccess, receipt.ErrorMessage);
        var job = (await f.Jobs.GetAsync(receipt.Value!.JobId, Ct))!;
        Assert.Null(job.SourceImageId);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites!.Phase);
        Assert.Equal(new SpriteCanvas(1536, 1024), job.Sprites.SourceCanvas);
        Assert.Equal(SpriteRules.GenerationCanvas(SpriteRules.OutputCanvas(command.Settings, new(1536, 1024)), Sizes),
            job.Sprites.GenerationCanvas);
        var draft = job.Sprites.SourceDraft!;
        Assert.Equal(("80년대 서울", command.ProviderConfigId, command.Model), (draft.Description, draft.TextProviderConfigId, draft.TextModel));
        var task = Assert.Single(job.Tasks);
        Assert.Equal(TaskKind.EnhanceSpriteSource, task.Kind);
        Assert.Equal([task.Id], receipt.Value.TaskIds);
        Assert.Equal(job.Sprites.ReviewRevision, receipt.Value.Revision);
        Assert.Contains(f.Queue.Enqueued, e => e.TaskId == task.Id);

        var replay = await f.SpriteSource.CreateDraftAsync(command with { Description = "  80년대 서울  " }, Ct);
        Assert.Equal((receipt.Value.JobId, receipt.Value.Revision), (replay.Value!.JobId, replay.Value.Revision));
        Assert.Equal(receipt.Value.TaskIds, replay.Value.TaskIds);
        Assert.Equal(ErrorCode.SpriteRequestConflict, (await f.SpriteSource.CreateDraftAsync(command, Ct)).ErrorCode);
        Assert.Single(job.Tasks);
    }

    [Theory]
    [InlineData("textDisabled", ErrorCode.JobProviderDisabled)]
    [InlineData("textModel", ErrorCode.JobModelUnavailable)]
    [InlineData("imageModel", ErrorCode.JobImageModelUnavailable)]
    [InlineData("enhancePrompt", ErrorCode.PromptNotActive)]
    [InlineData("generatePrompt", ErrorCode.PromptNotActive)]
    [InlineData("analyzePrompt", ErrorCode.PromptNotActive)]
    [InlineData("emptyDescription", ErrorCode.SpriteSettingsInvalid)]
    [InlineData("longDescription", ErrorCode.SpriteSettingsInvalid)]
    public async Task CreateDraft_ValidatesProvidersCapabilitiesPromptsAndDescription(string input, string error)
    {
        var f = new PipelineFixture();
        var command = await Prepare(f);
        switch (input)
        {
            case "textDisabled": command = command with { ProviderConfigId = await f.SeedProviderAsync(enabled: false) }; break;
            case "textModel": command = command with { Model = "missing" }; break;
            case "imageModel": f.Catalog.ImageModels[0] = new(StubModelCatalog.DefaultImageModel, "sprite", new(false, Sizes)); break;
            case "enhancePrompt": f.Prompts.Remove(LlmOperationKind.EnhanceSpriteSource); break;
            case "generatePrompt": f.Prompts.Remove(LlmOperationKind.GenerateSpriteSource); break;
            case "analyzePrompt": f.Prompts.Remove(LlmOperationKind.AnalyzeSprites); break;
            case "emptyDescription": command = command with { Description = "   " }; break;
            case "longDescription": command = command with { Description = new string('d', 1001) }; break;
        }

        Assert.Equal(error, (await f.SpriteSource.CreateDraftAsync(command, Ct)).ErrorCode);
        Assert.Empty(f.Queue.Enqueued);
    }

    [Theory]
    [InlineData("enhance")]
    [InlineData("prompt")]
    [InlineData("generate")]
    [InlineData("select")]
    public async Task EveryCommand_ReplaysReceiptAndRejectsConflictsAndStaleRevision(string operation)
    {
        var f = new PipelineFixture();
        var job = await Draft(f);
        Guid? candidate = operation == "select" ? await AddCandidate(f, job) : null;
        var context = SpriteCommandTests.Context(job);
        var bodies = new[] { "첫 본문", "다른 본문" };

        var first = await Run(f, operation, context, bodies[0], candidate);
        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.Equal(job.Sprites!.ReviewRevision, first.Value!.Revision);
        Assert.True(first.Value.Revision > context.ExpectedRevision);
        var replay = await Run(f, operation, context, bodies[0], candidate);
        Assert.Equal(first.Value.JobId, replay.Value!.JobId);
        Assert.Equal(first.Value.Revision, replay.Value.Revision);
        Assert.Equal(first.Value.TaskIds, replay.Value.TaskIds);
        var other = operation == "select" ? Guid.NewGuid() : (Guid?)null;
        Assert.Equal(ErrorCode.SpriteRequestConflict, (await Run(f, operation, context, bodies[1], other ?? candidate)).ErrorCode);
        Assert.Equal(ErrorCode.SpriteRevisionConflict,
            (await Run(f, operation, context with { RequestId = Guid.NewGuid() }, bodies[0], candidate)).ErrorCode);
    }

    [Theory]
    [InlineData("enhance")]
    [InlineData("prompt")]
    [InlineData("generate")]
    [InlineData("select")]
    public async Task EveryCommand_RejectsBusyNotReadyAndCanceled(string operation)
    {
        var f = new PipelineFixture();
        var busy = await Draft(f);
        var candidate = await AddCandidate(f, busy);
        Assert.True((await f.SpriteSource.GenerateAsync(SpriteCommandTests.Context(busy), "생성 중", Ct)).IsSuccess);
        var blobs = f.Blobs.Count;
        var revision = busy.Sprites!.ReviewRevision;
        Assert.Equal(ErrorCode.SpriteBusy, (await Run(f, operation, SpriteCommandTests.Context(busy), "본문", candidate)).ErrorCode);
        Assert.Equal(revision, busy.Sprites.ReviewRevision);
        Assert.Equal(blobs, f.Blobs.Count);
        Assert.Null(busy.SourceImageId);

        var ready = await SpriteCommandTests.Plan(f);
        Assert.Equal(ErrorCode.SpriteNotReady, (await Run(f, operation, SpriteCommandTests.Context(ready), "본문", Guid.NewGuid())).ErrorCode);

        var canceled = await Draft(f);
        var canceledCandidate = await AddCandidate(f, canceled);
        canceled.Cancel(f.Clock.Now);
        Assert.Equal(ErrorCode.JobAlreadyTerminal,
            (await Run(f, operation, SpriteCommandTests.Context(canceled), "본문", canceledCandidate)).ErrorCode);
    }

    [Theory]
    [InlineData("enhance")]
    [InlineData("select")]
    public async Task EnhanceAndSelect_RecheckDraftTextProvider(string operation)
    {
        var f = new PipelineFixture();
        var job = await Draft(f);
        var candidate = await AddCandidate(f, job);
        var provider = (await f.Providers.GetAsync(job.Sprites!.SourceDraft!.TextProviderConfigId, Ct))!;
        provider.Update(provider.DisplayName, provider.Kind, null, null, isEnabled: false, f.Clock.Now);

        Assert.Equal(ErrorCode.JobProviderDisabled, (await Run(f, operation, SpriteCommandTests.Context(job), "본문", candidate)).ErrorCode);
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites.Phase);
    }

    [Theory]
    [InlineData("disabled", ErrorCode.JobImageProviderDisabled)]
    [InlineData("noTransparency", ErrorCode.JobImageModelUnavailable)]
    [InlineData("sizeGone", ErrorCode.JobImageModelUnavailable)]
    [InlineData("modelGone", ErrorCode.JobImageModelUnavailable)]
    public async Task Generate_RechecksImageProviderAndModelAtAcceptance(string change, string error)
    {
        var f = new PipelineFixture();
        var job = await Draft(f);
        var imageProvider = (await f.Providers.GetAsync(job.ImageProviderConfigId!.Value, Ct))!;
        switch (change)
        {
            case "disabled": imageProvider.Update(imageProvider.DisplayName, imageProvider.Kind, null, null, isEnabled: false, f.Clock.Now); break;
            case "noTransparency": f.Catalog.ImageModels[0] = new(StubModelCatalog.DefaultImageModel, "sprite", new(false, Sizes)); break;
            case "sizeGone": f.Catalog.ImageModels[0] = new(StubModelCatalog.DefaultImageModel, "sprite", new(true, [new(1024, 1024)])); break;
            case "modelGone": f.Catalog.ImageModels.Clear(); break;
        }
        var (revision, tasks) = (job.Sprites!.ReviewRevision, job.Tasks.Count);

        Assert.Equal(error, (await f.SpriteSource.GenerateAsync(SpriteCommandTests.Context(job), "장면", Ct)).ErrorCode);

        Assert.Equal((revision, tasks), (job.Sprites.ReviewRevision, job.Tasks.Count));
        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.GenerateSpriteSource);
    }

    [Fact]
    public async Task Generate_SavesBodyPromptAndPlansWithThatSnapshot()
    {
        var f = new PipelineFixture();
        var job = await Draft(f);
        Assert.NotNull(job.Sprites!.SourceDraft!.Prompt);

        var receipt = await f.SpriteSource.GenerateAsync(SpriteCommandTests.Context(job), "  화면에서 고친 프롬프트  ", Ct);

        Assert.True(receipt.IsSuccess, receipt.ErrorMessage);
        Assert.Equal("화면에서 고친 프롬프트", job.Sprites.SourceDraft!.Prompt);
        var task = job.Tasks.Single(t => t.Id == receipt.Value!.TaskIds.Single());
        Assert.Equal(TaskKind.GenerateSpriteSource, task.Kind);
        Assert.Equal("화면에서 고친 프롬프트", task.SourceInput!.Prompt);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(RunTaskOutcome.Succeeded, await f.RunSpriteSourceGeneration.HandleAsync(task.Id, Ct));
        Assert.Equal("화면에서 고친 프롬프트", Assert.Single(job.Sprites.SourceDraft!.Candidates).Prompt);
    }

    [Fact]
    public async Task SavePrompt_IdenticalPromptKeepsRevisionButReturnsReceipt()
    {
        var f = new PipelineFixture();
        var job = await Draft(f);
        var revision = job.Sprites!.ReviewRevision;

        var receipt = await f.SpriteSource.SavePromptAsync(SpriteCommandTests.Context(job), $" {job.Sprites.SourceDraft!.Prompt} ", Ct);

        Assert.True(receipt.IsSuccess, receipt.ErrorMessage);
        Assert.Equal(revision, receipt.Value!.Revision);
        Assert.Empty(receipt.Value.TaskIds);
        Assert.Equal(revision, job.Sprites.ReviewRevision);
    }

    [Theory]
    [InlineData("prompt", 4001)]
    [InlineData("generate", 0)]
    [InlineData("enhance", 1001)]
    public async Task LengthViolations_AreSettingsInvalid(string operation, int length)
    {
        var f = new PipelineFixture();
        var job = await Draft(f);
        Assert.Equal(ErrorCode.SpriteSettingsInvalid,
            (await Run(f, operation, SpriteCommandTests.Context(job), new string('x', length), null)).ErrorCode);
    }

    [Fact]
    public async Task Generate_NinthCandidateIsSourceLimit()
    {
        var f = new PipelineFixture();
        var job = await Draft(f);
        for (var i = 0; i < SpriteSourceDraft.MaxCandidates; i++)
        {
            var receipt = await f.SpriteSource.GenerateAsync(SpriteCommandTests.Context(job), "장면", Ct);
            Assert.Equal(RunTaskOutcome.Succeeded, await f.RunSpriteSourceGeneration.HandleAsync(receipt.Value!.TaskIds.Single(), Ct));
        }
        var revision = job.Sprites!.ReviewRevision;

        Assert.Equal(ErrorCode.SpriteSourceLimit, (await f.SpriteSource.GenerateAsync(SpriteCommandTests.Context(job), "장면", Ct)).ErrorCode);
        Assert.Equal(revision, job.Sprites.ReviewRevision);
        Assert.Equal(SpriteSourceDraft.MaxCandidates, job.Sprites.SourceDraft!.Candidates.Count);
    }

    [Fact]
    public async Task Select_CopiesIndependentBlobAndRecomputesCanvasFromActualSize()
    {
        var f = new PipelineFixture();
        var job = await Draft(f);
        // 잠정 1536x1024와 다른 실제 크기
        var candidateId = await AddCandidate(f, job, SpriteGenerationTests.Png(48, 32), "image/png", 1536, 1024);
        var candidate = job.Sprites!.SourceDraft!.Candidates.Single();
        var blobs = f.Blobs.Count;

        var receipt = await f.SpriteSource.SelectAsync(SpriteCommandTests.Context(job), candidateId, Ct);

        Assert.True(receipt.IsSuccess, receipt.ErrorMessage);
        var source = (await f.Images.GetAsync(job.SourceImageId!.Value, Ct))!;
        Assert.NotEqual(candidate.BlobKey, source.BlobKey);
        Assert.Equal("image/png", source.ContentType);
        Assert.Equal(blobs + 1, f.Blobs.Count);
        await using (var copy = await f.Blobs.OpenReadAsync(source.BlobKey, Ct)) Assert.True(copy.Length > 0);
        Assert.Equal(new SpriteCanvas(48, 32), job.Sprites.SourceCanvas);
        Assert.Equal(SpriteRules.GenerationCanvas(SpriteRules.OutputCanvas(job.Sprites.Settings, new(48, 32)), Sizes),
            job.Sprites.GenerationCanvas);
        Assert.Equal(SpritePhase.Analyzing, job.Sprites.Phase);
        var analysis = job.Tasks.Single(t => t.Id == receipt.Value!.TaskIds.Single());
        Assert.Equal(TaskKind.AnalyzeSprites, analysis.Kind);
        Assert.Equal((job.Sprites.SourceDraft!.TextProviderConfigId, job.Sprites.SourceDraft.TextModel),
            (analysis.ProviderConfigId!.Value, analysis.Model!));
        Assert.Contains(f.Queue.Enqueued, e => e.TaskId == analysis.Id);
        Assert.Single(job.Sprites.SourceDraft.Candidates);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("mime")]
    [InlineData("large")]
    public async Task Select_InspectionFailureIs400AndKeepsDraft(string input)
    {
        var f = new PipelineFixture();
        var job = await Draft(f);
        var (bytes, error) = input switch
        {
            "garbage" => ("not an image"u8.ToArray(), ErrorCode.UploadUnsupportedType),
            "mime" => (SpriteGenerationTests.Png(48, 32, SKEncodedImageFormat.Jpeg), ErrorCode.UploadUnsupportedType),
            _ => (new byte[12 * 1024 * 1024 + 1], ErrorCode.UploadTooLarge),
        };
        var candidateId = await AddCandidate(f, job, bytes, "image/png", 48, 32);
        var (revision, blobs, canvas) = (job.Sprites!.ReviewRevision, f.Blobs.Count, job.Sprites.SourceCanvas);

        Assert.Equal(error, (await f.SpriteSource.SelectAsync(SpriteCommandTests.Context(job), candidateId, Ct)).ErrorCode);

        Assert.Null(job.SourceImageId);
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites.Phase);
        Assert.Single(job.Sprites.SourceDraft!.Candidates);
        Assert.Equal((revision, blobs, canvas), (job.Sprites.ReviewRevision, f.Blobs.Count, job.Sprites.SourceCanvas));
    }

    [Fact]
    public async Task Select_UnknownOrForeignCandidateIsUploadNotFound()
    {
        var f = new PipelineFixture();
        var job = await Draft(f);
        await AddCandidate(f, job);
        var foreign = await Draft(f);
        var foreignCandidate = await AddCandidate(f, foreign);

        Assert.Equal(ErrorCode.JobUploadNotFound, (await f.SpriteSource.SelectAsync(SpriteCommandTests.Context(job), Guid.NewGuid(), Ct)).ErrorCode);
        Assert.Equal(ErrorCode.JobUploadNotFound, (await f.SpriteSource.SelectAsync(SpriteCommandTests.Context(job), foreignCandidate, Ct)).ErrorCode);
        Assert.Null(job.SourceImageId);
    }

    internal static Task<Result<SpriteReceipt>> Run(PipelineFixture f, string operation, SpriteCommandContext context,
        string body, Guid? candidateId)
        => operation switch
        {
            "enhance" => f.SpriteSource.EnhanceAsync(context, body, Ct),
            "prompt" => f.SpriteSource.SavePromptAsync(context, body, Ct),
            "generate" => f.SpriteSource.GenerateAsync(context, body, Ct),
            _ => f.SpriteSource.SelectAsync(context, candidateId!.Value, Ct),
        };

    internal static async Task<CreateSpriteDraftCommand> Prepare(PipelineFixture f)
    {
        var provider = await f.SeedProviderAsync();
        f.Catalog.ImageModels.Clear();
        f.Catalog.ImageModels.Add(new(StubModelCatalog.DefaultImageModel, "sprite", new(true, Sizes)));
        return new(Guid.NewGuid(), "80년대 서울", provider, StubModelCatalog.DefaultModel,
            provider, StubModelCatalog.DefaultImageModel, new(SpriteView.SideView, SpriteOutputKind.Layers));
    }

    // 보완 공정까지 끝나 프롬프트가 있는 원본 준비 작업
    internal static async Task<PipelineJob> Draft(PipelineFixture f)
    {
        var receipt = await f.SpriteSource.CreateDraftAsync(await Prepare(f), Ct);
        Assert.True(receipt.IsSuccess, receipt.ErrorMessage);
        Assert.Equal(RunTaskOutcome.Succeeded, await f.RunSpriteSourceEnhance.HandleAsync(receipt.Value!.TaskIds.Single(), Ct));
        var job = (await f.Jobs.GetAsync(receipt.Value.JobId, Ct))!;
        Assert.Equal(JobStatus.PendingReview, job.Status);
        return job;
    }

    // 임의 바이트의 후보를 공정 결과로 붙인다
    internal static async Task<Guid> AddCandidate(PipelineFixture f, PipelineJob job, byte[]? bytes = null,
        string contentType = "image/png", int width = 48, int height = 32)
    {
        var task = job.PlanSourceGeneration(job.Sprites!.SourceDraft!.Prompt ?? "장면", Guid.NewGuid()).Value!;
        task.Claim(f.Clock.Now, f.Options.Lease);
        var key = await f.Blobs.SaveAsync(new MemoryStream(bytes ?? SpriteGenerationTests.Png(width, height)), contentType, Ct);
        var candidate = new SpriteSourceCandidate(Guid.NewGuid(), key, contentType, width, height, task.SourceInput!.Prompt!, f.Clock.Now);
        Assert.True(job.TryAddSourceCandidate(task.Id, candidate));
        task.Succeed(f.Clock.Now);
        job.ReconcileFromTasks(f.Clock.Now);
        Assert.Equal(TaskStatus.Succeeded, task.Status);
        return candidate.Id;
    }
}
