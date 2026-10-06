using Noxtend.Application.Sprites;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Prompt;
using Noxtend.Domain.Sprites;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Image;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Tuning.Domain.Call;
using SkiaSharp;

namespace Noxtend.Tests.Application;

public sealed class SpriteAnalysisTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    internal const string PlanJson = """
        {"view":"sideView","outputKind":"layers","assets":[{"name":"배경","order":0,
        "sourceBounds":{"x":0,"y":0,"w":1,"h":1},"requiresTransparency":false,
        "loop":false,"frameCount":8,"fps":8,"motionNotes":"{{sourceCanvas}}"}]}
        """;

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    public async Task InputSelection_RequiresExactlyOneSource(bool upload, bool job, bool image)
    {
        var f = new PipelineFixture();
        var command = Command(Guid.NewGuid(), Guid.NewGuid()) with
        {
            UploadId = upload ? Guid.NewGuid() : null,
            SourceJobId = job ? Guid.NewGuid() : null,
            SourceGeneratedImageId = image ? Guid.NewGuid() : null,
        };
        var result = await f.StartSprites.HandleAsync(command, Ct);
        Assert.Equal(ErrorCode.SpriteSettingsInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task AnalysisSuccess_StopsAtPlanReviewWithoutImageCalls()
    {
        var calls = 0;
        var imageCalls = 0;
        var f = new PipelineFixture(FakeLlmProvider.Returning(kind =>
        {
            Assert.Equal(LlmOperationKind.AnalyzeSprites, kind);
            calls++;
            return PlanJson;
        }), imageProvider: FakeImageProvider.Throwing(() =>
        {
            imageCalls++;
            return null;
        }));
        var command = await Prepare(f);
        var receipt = await f.StartSprites.HandleAsync(command, Ct);
        Assert.True(receipt.IsSuccess, receipt.ErrorMessage);
        var job = (await f.Jobs.GetAsync(receipt.Value!.JobId, Ct))!;
        Assert.Equal(new SpriteCanvas(1536, 1024), job.Sprites!.GenerationCanvas);
        Assert.Equal(new SpriteCanvas(24, 16), job.Sprites.SourceCanvas);
        Assert.Equal(7, (int)job.Tasks.Single().Kind);
        await f.RunSpriteAnalysis.HandleAsync(job.Tasks.Single().Id, Ct);
        Assert.Equal(1, calls);
        Assert.Equal(0, imageCalls);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Equal(SpritePhase.PlanReview, job.Sprites.Phase);
        Assert.Single(job.Tasks);
        Assert.Empty(job.GeneratedImages);
        Assert.Empty(job.Sprites.Images);
        Assert.Equal("{{sourceCanvas}}", job.Sprites.Assets.Single().Plan.MotionNotes);
        Assert.Contains((LlmOperationKind.AnalyzeSprites, AssetCategory.Background), f.Prompts.Queries);
        Assert.Null(ModelPriceBook.Empty.Estimate(command.Model, f.Clock.Now, 1200, 340));
    }

    [Theory]
    [InlineData("\"w\":1", "\"w\":1.1")]
    [InlineData("sideView", "unknown")]
    [InlineData("sideView", "topDown")]
    [InlineData("\"order\":0", "\"order\":0.5")]
    [InlineData("\"loop\":false,", "")]
    public async Task InvalidRoiOrUnknownView_FailsWithoutSilentCorrection(string before, string after)
    {
        var f = new PipelineFixture(FakeLlmProvider.Returning(_ => PlanJson.Replace(before, after)),
            new() { MaxAttempts = 1 });
        var receipt = await f.StartSprites.HandleAsync(await Prepare(f), Ct);
        var job = (await f.Jobs.GetAsync(receipt.Value!.JobId, Ct))!;
        await f.RunSpriteAnalysis.HandleAsync(job.Tasks.Single().Id, Ct);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Empty(job.Sprites!.Assets);
    }

    [Fact]
    public void Parser_AssignsIdsAndRejectsMalformedSchema()
    {
        var settings = new SpriteSettings(SpriteView.SideView, SpriteOutputKind.Layers);
        var first = SpritePlanParser.Parse(PlanJson, settings, new(24, 16));
        var second = SpritePlanParser.Parse(PlanJson, settings, new(24, 16));
        Assert.True(first.IsSuccess);
        Assert.NotEqual(first.Value![0].Id, second.Value![0].Id);
        Assert.False(SpritePlanParser.Parse(PlanJson.Replace("\"name\":", "\"id\":\"external\",\"name\":"), settings, new(24, 16)).IsSuccess);
        Assert.False(SpritePlanParser.Parse("{}", settings, new(24, 16)).IsSuccess);
    }

    [Fact]
    public void TemplateValues_AreDataAndNotExpanded()
    {
        Assert.Equal("{{sourceCanvas}} actual", PromptTemplate.Render("{{notes}} {{sourceCanvas}}",
            new Dictionary<string, string> { ["notes"] = "{{sourceCanvas}}", ["sourceCanvas"] = "actual" }));
        Assert.Throws<InvalidOperationException>(() => PromptTemplate.Render("{{missing}}", new Dictionary<string, string>()));
    }

    [Fact]
    public async Task SourceImageFromOtherJob_IsRejected()
    {
        var f = new PipelineFixture(imageProvider: FakeImageProvider.Returning(Png()));
        var source = await f.FanOutAsync();
        await f.RunGenerationUntilTerminalAsync(source.Tasks.First(t => t.Kind == TaskKind.Generate));
        var other = await f.StartJobAsync();
        var command = await Prepare(f);
        var result = await f.StartSprites.HandleAsync(command with
        {
            UploadId = null, SourceJobId = other.Id, SourceGeneratedImageId = source.GeneratedImages.First().Id,
        }, Ct);
        Assert.Equal(ErrorCode.JobUploadNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task SourceJobDeletion_KeepsCopiedSpriteInput()
    {
        var f = new PipelineFixture(imageProvider: FakeImageProvider.Returning(Png()));
        var source = await f.FanOutAsync();
        await f.RunGenerationUntilTerminalAsync(source.Tasks.First(t => t.Kind == TaskKind.Generate));
        var original = source.GeneratedImages.First();
        var command = (await Prepare(f)) with
        { UploadId = null, SourceJobId = source.Id, SourceGeneratedImageId = original.Id };
        var result = await f.StartSprites.HandleAsync(command, Ct);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        var job = (await f.Jobs.GetAsync(result.Value!.JobId, Ct))!;
        var copy = (await f.Images.GetAsync(job.SourceImageId, Ct))!;
        Assert.NotEqual(original.BlobKey, copy.BlobKey);
        await f.Cancel.HandleAsync(source.Id, Ct);
        Assert.True((await f.Delete.HandleAsync(source.Id, Ct)).IsSuccess);
        await using var stream = await f.Blobs.OpenReadAsync(copy.BlobKey, Ct);
        var info = await new SkiaImageTranscoder().InspectSpriteAsync(stream, 12 * 1024 * 1024, 16_777_216, Ct);
        Assert.True(info.Width > 0);
        var duplicate = await f.StartSprites.HandleAsync(command with { Model = " " + command.Model + " " }, Ct);
        Assert.Equal(result.Value!.JobId, duplicate.Value!.JobId);
        Assert.Equal(result.Value.TaskIds, duplicate.Value.TaskIds);
        var conflict = await f.StartSprites.HandleAsync(command with { Settings = command.Settings with { View = SpriteView.TopDown } }, Ct);
        Assert.Equal(ErrorCode.SpriteRevisionConflict, conflict.ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownOrUnsupportedCapability_IsRejectedBeforeCopy(bool opaque)
    {
        var f = new PipelineFixture();
        var command = await Prepare(f);
        f.Catalog.ImageModels.Clear();
        f.Catalog.ImageModels.Add(new(command.ImageModel, "unsupported", opaque ? new(false, [new(1024, 1024)]) : null));
        var before = f.Blobs.Count;
        var result = await f.StartSprites.HandleAsync(command, Ct);
        Assert.Equal(before, f.Blobs.Count);
        Assert.Equal(ErrorCode.JobImageModelUnavailable, result.ErrorCode);
        Assert.Empty(await f.Jobs.ListAsync(JobListFilter.Active, null, 100, Ct));
    }

    [Fact]
    public async Task InvalidUploadPixels_AreRejectedDespiteMime()
    {
        var f = new PipelineFixture();
        var command = await Prepare(f);
        command = command with { UploadId = await f.SeedUploadAsync() };
        Assert.False((await f.StartSprites.HandleAsync(command, Ct)).IsSuccess);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg", false)]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp", false)]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg", true)]
    public async Task Admission_UsesDecodedFormatAndOrientedDimensions(SKEncodedImageFormat format, string mime, bool rotate)
    {
        var f = new PipelineFixture();
        var command = await Prepare(f);
        using var bitmap = new SKBitmap(24, 16);
        bitmap.Erase(SKColors.Red);
        using var encoded = bitmap.Encode(format, 100);
        var bytes = encoded.ToArray();
        if (rotate)
        {
            byte[] exif = [0xff, 0xe1, 0, 34, 69, 120, 105, 102, 0, 0,
                73, 73, 42, 0, 8, 0, 0, 0, 1, 0,
                0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
            bytes = [.. bytes[..2], .. exif, .. bytes[2..]];
        }
        using var stream = new MemoryStream(bytes);
        var uploaded = await f.Upload.HandleAsync(stream, "source", mime, bytes.Length, Ct);
        var receipt = await f.StartSprites.HandleAsync(command with { UploadId = uploaded.Value!.Id }, Ct);
        Assert.True(receipt.IsSuccess, receipt.ErrorMessage);
        var job = (await f.Jobs.GetAsync(receipt.Value!.JobId, Ct))!;
        Assert.Equal(uploaded.Value.Id, job.SourceImageId);
        Assert.Equal(rotate ? new SpriteCanvas(16, 24) : new(24, 16), job.Sprites!.SourceCanvas);
        Assert.Equal(mime, (await f.Images.GetAsync(job.SourceImageId, Ct))!.ContentType);
    }

    [Fact]
    public async Task UploadMimeMismatch_IsRejectedWithoutPlanningOrCopying()
    {
        var f = new PipelineFixture();
        var command = await Prepare(f);
        using var bitmap = new SKBitmap(24, 16);
        bitmap.Erase(SKColors.Red);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
        using var stream = new MemoryStream(encoded.ToArray());
        var uploaded = await f.Upload.HandleAsync(stream, "misleading.png", "image/png", stream.Length, Ct);
        var before = f.Blobs.Count;
        var result = await f.StartSprites.HandleAsync(command with { UploadId = uploaded.Value!.Id }, Ct);
        Assert.Equal(ErrorCode.UploadUnsupportedType, result.ErrorCode);
        Assert.Equal(before, f.Blobs.Count);
        Assert.Empty(await f.Jobs.ListAsync(JobListFilter.Active, null, 100, Ct));
        Assert.Null(await f.Jobs.GetSpriteRequestAsync(command.RequestId, Ct));
        Assert.Equal("image/png", (await f.Images.GetAsync(uploaded.Value.Id, Ct))!.ContentType);
    }

    [Fact]
    public async Task TwoDSource_CopySurvivesOriginalDeletion()
    {
        var f = new PipelineFixture();
        var command = await Prepare(f);
        var source = PipelineJob.CreateSprites(command.UploadId!.Value, command.ImageProviderConfigId, command.ImageModel,
            command.Settings, new(24, 16), new(1536, 1024), f.Clock.Now).Value!;
        source.ReplaceSpritePlan(SpritePlanParser.Parse(PlanJson, command.Settings, new(24, 16)).Value!, 0);
        var input = source.ApproveSpritePlan(source.Sprites!.ReviewRevision).Value![0];
        var task = source.PlanTask(TaskKind.Generate, 0);
        source.BindSpriteFrame(task.Id, input);
        task.Claim(f.Clock.Now, TimeSpan.FromMinutes(2));
        task.Succeed(f.Clock.Now);
        using var stream = new MemoryStream(Png());
        var key = await f.Blobs.SaveAsync(stream, "image/png", Ct);
        var image = SpriteImage.Create(task.Id, input, key, f.Clock.Now);
        Assert.True(source.TryAttachSpriteImage(image));
        await f.Jobs.AddAsync(source, Ct);
        var receipt = await f.StartSprites.HandleAsync(command with
            { UploadId = null, SourceJobId = source.Id, SourceGeneratedImageId = image.Id }, Ct);
        Assert.True(receipt.IsSuccess, receipt.ErrorMessage);
        await f.Cancel.HandleAsync(source.Id, Ct);
        await f.Delete.HandleAsync(source.Id, Ct);
        var copiedJob = (await f.Jobs.GetAsync(receipt.Value!.JobId, Ct))!;
        var copy = (await f.Images.GetAsync(copiedJob.SourceImageId, Ct))!;
        Assert.NotEqual(key, copy.BlobKey);
        await using var readable = await f.Blobs.OpenReadAsync(copy.BlobKey, Ct);
        Assert.Equal(24, (await new SkiaImageTranscoder().InspectSpriteAsync(readable, 12 * 1024 * 1024, 16_777_216, Ct)).Width);
    }

    [Theory]
    [InlineData(SpriteView.SideView, SpriteOutputKind.Layers, false)]
    [InlineData(SpriteView.TopDown, SpriteOutputKind.Tiles, false)]
    [InlineData(SpriteView.Isometric, SpriteOutputKind.Tiles, false)]
    [InlineData(SpriteView.Isometric, SpriteOutputKind.Tiles, true)]
    public async Task DefaultFake_UsesSelectedSettings(SpriteView view, SpriteOutputKind kind, bool systemVariables)
    {
        var f = new PipelineFixture();
        var command = (await Prepare(f)) with { Settings = new(view, kind) };
        if (systemVariables) f.Prompts.SetForCategory(LlmOperationKind.AnalyzeSprites, AssetCategory.Background, "{{settings}} {{sourceCanvas}}");
        var receipt = await f.StartSprites.HandleAsync(command, Ct);
        var job = (await f.Jobs.GetAsync(receipt.Value!.JobId, Ct))!;
        await f.RunSpriteAnalysis.HandleAsync(job.Tasks.Single().Id, Ct);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Single(job.Sprites!.Assets);
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(24, 16);
        bitmap.Erase(SKColors.Coral);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    internal static async Task<StartSpriteJobCommand> Prepare(PipelineFixture f)
    {
        using var bitmap = new SKBitmap(24, 16);
        bitmap.Erase(SKColors.Coral);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        var upload = await f.Upload.HandleAsync(stream, "input.png", "image/png", data.Size, Ct);
        var provider = await f.SeedProviderAsync();
        f.Catalog.ImageModels.Clear();
        f.Catalog.ImageModels.Add(new(StubModelCatalog.DefaultImageModel, "sprite", new(true, [new(1024, 1024), new(1536, 1024)])));
        return Command(upload.Value!.Id, provider);
    }

    private static StartSpriteJobCommand Command(Guid upload, Guid provider)
        => new(Guid.NewGuid(), upload, null, null, provider, StubModelCatalog.DefaultModel,
            provider, StubModelCatalog.DefaultImageModel, new(SpriteView.SideView, SpriteOutputKind.Layers));
}
