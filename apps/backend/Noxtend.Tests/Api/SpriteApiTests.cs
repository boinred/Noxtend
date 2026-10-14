using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Noxtend.Application.Pipeline;
using Noxtend.Infrastructure.Llm;
using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Api.Controllers;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Sprites;
using Noxtend.Tests.Application;

namespace Noxtend.Tests.Api;

public sealed class SpriteApiTests
{
    [Theory]
    [InlineData(ErrorCode.SpriteWrongMode, 409)]
    [InlineData(ErrorCode.SpriteRequestConflict, 409)]
    [InlineData(ErrorCode.SpriteRevisionConflict, 409)]
    [InlineData(ErrorCode.SpriteBusy, 409)]
    [InlineData(ErrorCode.SpriteNotReady, 409)]
    [InlineData(ErrorCode.SpriteAssetNotFound, 404)]
    [InlineData(ErrorCode.SpriteSourceLimit, 409)]
    public void SpriteErrors_UseCentralHttpMapping(string code, int status)
        => Assert.Equal(status, Assert.IsType<ObjectResult>(ApiResults.Failure<object>(code, "거부")).StatusCode);

    [Fact]
    public async Task WrongModeEndpoint_RejectsWithoutPlanningTasks()
    {
        var f = new PipelineFixture();
        var job = PipelineJob.CreateSprites(Guid.NewGuid(), Guid.NewGuid(), "image",
            new(SpriteView.SideView, SpriteOutputKind.Layers), new(24, 16), new(1536, 1024), f.Clock.Now).Value!;
        await f.Jobs.AddAsync(job, default);
        var result = await Jobs(f).GetReviewAsync(job.Id, default);
        Assert.Equal(409, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(job.Tasks);
    }

    [Fact]
    public async Task StartGetPlanBaseAssetAndExport_UseAcceptedEnvelope()
    {
        var f = new PipelineFixture(FakeLlmProvider.Returning(_ => SpriteAnalysisTests.PlanJson));
        var command = await SpriteAnalysisTests.Prepare(f);
        var api = Sprites(f);
        var request = new StartSpriteJobRequest(command.RequestId, command.UploadId, null, null,
            command.ProviderConfigId, command.Model, command.ImageProviderConfigId, command.ImageModel,
            new("sideView", "layers"));
        var accepted = Receipt(await api.StartAsync(request, default));
        var replay = Receipt(await api.StartAsync(request, default));
        Assert.Equal((accepted.Id, accepted.Status, accepted.Revision), (replay.Id, replay.Status, replay.Revision));
        Assert.Equal(accepted.TaskIds, replay.TaskIds);
        Status(await api.StartAsync(request with { Settings = new("topDown", "layers") }, default), 409);
        var job = (await f.Jobs.GetAsync(accepted.Id, default))!;
        Assert.Single(job.Tasks);
        await f.RunSpriteAnalysis.HandleAsync(accepted.TaskIds.Single(), default);

        var detail = Data<JobResponse>(await Jobs(f).GetAsync(job.Id, default));
        Assert.Equal("twoD", detail.ProductionMode);
        Assert.Equal("planReview", detail.Sprite!.Phase);
        var plan = detail.Sprite.Assets.Select(asset => asset.Plan).ToArray();
        plan[0] = plan[0] with { Name = "검수 배경" };
        Receipt(await api.UpdatePlanAsync(job.Id, new(Guid.NewGuid(), detail.Sprite.ReviewRevision, plan), default));
        var approvalRequest = Mutation(job);
        var approved = Receipt(await api.ApprovePlanAsync(job.Id, approvalRequest, default));
        var approvalReplay = Receipt(await api.ApprovePlanAsync(job.Id, approvalRequest, default));
        Assert.Equal((approved.Id, approved.Status, approved.Revision), (approvalReplay.Id, approvalReplay.Status, approvalReplay.Revision));
        Assert.Equal(approved.TaskIds, approvalReplay.TaskIds);
        await FinishFrames(f, job, approved.TaskIds);
        var ids = job.Sprites!.Assets.Select(asset => asset.Id).ToArray();
        Receipt(await api.ApproveBasesAsync(job.Id, Assets(job, ids), default));
        Receipt(await api.ApproveAssetAsync(job.Id, ids.Single(), Mutation(job), default));
        var exported = Receipt(await api.ExportAsync(job.Id, Assets(job, ids), default));
        var task = job.Tasks.Single(task => task.Id == exported.TaskIds.Single());
        Status(await api.GetExportAsync(job.Id, task.SpriteExportInput!.ExportId, default), 409);
        Assert.Equal(RunTaskOutcome.Succeeded, await f.RunSpritePack.HandleAsync(task.Id, default));
        Assert.Equal("succeeded", Data<JobResponse>(await Jobs(f).GetAsync(job.Id, default)).Status);
        Assert.IsType<FileStreamResult>(await api.GetExportAsync(job.Id, task.SpriteExportInput.ExportId, default));
    }

    [Fact]
    public async Task ImageAndZipDownloads_SetVerifiedContentTypeAndServerFilename()
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Ready(f);
        var api = Sprites(f);
        var image = job.Sprites!.Images.Single();
        var png = Assert.IsType<FileStreamResult>(await api.GetImageAsync(job.Id, image.Id, default));
        Assert.Equal($"sprite-{image.Id}.png", png.FileDownloadName);
        var pngHttp = await ExecuteFile(png);
        Assert.Equal("image/png", pngHttp.Response.ContentType);
        Assert.Contains($"sprite-{image.Id}.png", pngHttp.Response.Headers.ContentDisposition.ToString());
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, ((MemoryStream)pngHttp.Response.Body).ToArray()[..4]);

        var receipt = Receipt(await api.ExportAsync(job.Id, Assets(job, [job.Sprites.Assets.Single().Id]), default));
        await f.RunSpritePack.HandleAsync(receipt.TaskIds.Single(), default);
        var export = job.Sprites.Exports.Single();
        var zip = Assert.IsType<FileStreamResult>(await api.GetExportAsync(job.Id, export.Id, default));
        Assert.Equal($"sprites-{export.Id}.zip", zip.FileDownloadName);
        var zipHttp = await ExecuteFile(zip);
        Assert.Equal("application/zip", zipHttp.Response.ContentType);
        Assert.Contains($"sprites-{export.Id}.zip", zipHttp.Response.Headers.ContentDisposition.ToString());
        Assert.Equal(new byte[] { 80, 75 }, ((MemoryStream)zipHttp.Response.Body).ToArray()[..2]);
        Assert.Equal("private, max-age=31536000, immutable", api.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task ForeignAssetImageAndExportIds_AreRejected()
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Ready(f);
        var foreign = await SpriteCommandTests.Ready(f);
        var api = Sprites(f);
        var foreignAsset = foreign.Sprites!.Assets.Single().Id;
        Status(await api.ApproveAssetAsync(job.Id, foreignAsset, Mutation(job), default), 404);
        Status(await api.RegenerateAsync(job.Id, foreignAsset, 0, Mutation(job), default), 404);
        Status(await api.ApproveBasesAsync(job.Id, Assets(job, [foreignAsset]), default), 404);
        Status(await api.ExportAsync(job.Id, Assets(job, [foreignAsset]), default), 404);
        Status(await api.GetImageAsync(job.Id, foreign.Sprites.Images.Single().Id, default), 404);
        var exported = Receipt(await api.ExportAsync(foreign.Id, Assets(foreign, [foreignAsset]), default));
        await f.RunSpritePack.HandleAsync(exported.TaskIds.Single(), default);
        Status(await api.GetExportAsync(job.Id, foreign.Sprites.Exports.Single().Id, default), 404);
        Status(await api.GetImageAsync(Guid.NewGuid(), job.Sprites!.Images.Single().Id, default), 404);
        Status(await api.GetExportAsync(job.Id, Guid.NewGuid(), default), 404);
        Status(await api.ApprovePlanAsync(Guid.NewGuid(), Mutation(job), default), 404);
        Status(await api.RegenerateAsync(job.Id, job.Sprites.Assets.Single().Id, 1, Mutation(job), default), 400);
        var image = job.Sprites.Images.Single();
        await f.Blobs.DeleteAsync(image.BlobKey, default);
        Assert.IsType<NotFoundResult>(await api.GetImageAsync(job.Id, image.Id, default));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("plan")]
    [InlineData("base")]
    [InlineData("regenerate")]
    [InlineData("asset")]
    [InlineData("export")]
    public async Task EveryMutation_RejectsStaleRevisionAndWrongMode(string operation)
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Plan(f);
        var api = Sprites(f);
        var plans = SpriteResponse.From(job.Sprites!).Assets.Select(asset => asset.Plan).ToArray();
        var ids = job.Sprites!.Assets.Select(asset => asset.Id).ToArray();
        async Task<IActionResult> Call(Guid id, int revision) => operation switch
        {
            "update" => await api.UpdatePlanAsync(id, new(Guid.NewGuid(), revision, plans), default),
            "plan" => await api.ApprovePlanAsync(id, new(Guid.NewGuid(), revision), default),
            "base" => await api.ApproveBasesAsync(id, new(Guid.NewGuid(), revision, ids), default),
            "regenerate" => await api.RegenerateAsync(id, ids[0], 0, new(Guid.NewGuid(), revision), default),
            "asset" => await api.ApproveAssetAsync(id, ids[0], new(Guid.NewGuid(), revision), default),
            _ => await api.ExportAsync(id, new(Guid.NewGuid(), revision, ids), default),
        };
        var count = job.Tasks.Count;
        var queue = f.Queue.Enqueued.Count;
        Status(await Call(job.Id, job.Sprites.ReviewRevision - 1), 409);
        var legacy = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), f.Clock.Now);
        await f.Jobs.AddAsync(legacy, default);
        Status(await Call(legacy.Id, 0), 409);
        Assert.Equal(count, job.Tasks.Count);
        Assert.Empty(legacy.Tasks);
        Assert.Equal(queue, f.Queue.Enqueued.Count);
        Status(await api.GetImageAsync(legacy.Id, Guid.NewGuid(), default), 409);
        Status(await api.GetExportAsync(legacy.Id, Guid.NewGuid(), default), 409);
    }

    [Fact]
    public async Task EveryLegacyThreeDRoute_RejectsTwoDBeforePlanningOrMapping()
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Plan(f);
        var api = Jobs(f);
        var id = job.Id;
        var part = Guid.NewGuid();
        var queue = f.Queue.Enqueued.Count;
        var count = job.Tasks.Count;
        var blobCount = f.Blobs.Count;
        IActionResult[] results = [
            await api.AddMeshAsync(id, new(Guid.Empty, null), default),
            await api.GetReviewAsync(id, default),
            await api.FindOverlapsAsync(id, new(null!), default),
            await api.AddReviewPartAsync(id, new("part", null, null!), default),
            await api.RemoveReviewPartAsync(id, part, default),
            await api.MoveReviewPlacementAsync(id, part, 0, null!, default),
            await api.ApproveReviewAsync(id, default),
            await api.ConfirmDescriptionsAsync(id, default),
            await api.ReturnToBoxesAsync(id, default),
            await api.EditReviewDescriptionAsync(id, part, new("description"), default),
            await api.EditReviewPaletteAsync(id, new(null!), default),
            await api.GenerateViewsAsync(id, new([]), default),
            await api.ReturnToDescriptionsAsync(id, new(part), default),
            await api.ReplanPartMeshAsync(id, new(part, Guid.Empty, null, "unknown", "unknown"), default),
        ];
        Assert.All(results, result => Status(result, 409));
        Assert.Equal(count, job.Tasks.Count);
        Assert.Equal(queue, f.Queue.Enqueued.Count);
        Assert.Equal(blobCount, f.Blobs.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("threeD")]
    [InlineData("twoD")]
    public async Task ListMode_FiltersItemsAndTotalTogether(string? mode)
    {
        var f = new PipelineFixture();
        await SpriteCommandTests.Plan(f);
        await SpriteCommandTests.Plan(f);
        await f.Jobs.AddAsync(PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), f.Clock.Now), default);
        var page = Data<JobListResponse>(await Jobs(f).ListAsync("active", "background", 1, default, mode));
        Assert.Single(page.Items);
        Assert.Equal(mode is null ? 3 : mode == "twoD" ? 2 : 1, page.Total);
        if (mode is not null) Assert.Equal(mode, page.Items.Single().ProductionMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("twoD,threeD")]
    public async Task InvalidModeQuery_Is400(string mode)
        => Status(await Jobs(new PipelineFixture()).ListAsync("active", null, 10, default, mode), 400);

    [Theory]
    [InlineData("0", "layers", "both")]
    [InlineData("sideView", "99", "both")]
    [InlineData("sideView", "layers", "0")]
    [InlineData("sideView,topDown", "layers", "both")]
    public async Task InvalidSettingsNames_Are400(string view, string output, string repeat)
    {
        var f = new PipelineFixture();
        Status(await Sprites(f).StartAsync(new(Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(), "model",
            Guid.NewGuid(), "image", new(view, output, Repeat: repeat)), default), 400);
        Assert.Empty(f.Queue.Enqueued);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"id\":\"10000000-0000-4000-8000-000000000001\",\"sourceBounds\":null}]")]
    [InlineData("[{\"id\":\"10000000-0000-4000-8000-000000000001\",\"sourceBounds\":{\"x\":1e400,\"y\":0,\"w\":1,\"h\":1}}]")]
    [InlineData("[{\"id\":\"10000000-0000-4000-8000-000000000001\",\"sourceBounds\":{\"x\":\"NaN\",\"y\":0,\"w\":1,\"h\":1}}]")]
    [InlineData("[{\"id\":\"10000000-0000-4000-8000-000000000001\",\"sourceBounds\":{\"x\":\"Infinity\",\"y\":0,\"w\":1,\"h\":1}}]")]
    public async Task JsonBinding_NullAndNonfiniteRoiCannotReachFingerprint(string assets)
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Plan(f);
        var queue = f.Queue.Enqueued.Count;
        var revision = job.Sprites!.ReviewRevision;
        var json = $$"""{"requestId":"{{Guid.NewGuid()}}","expectedRevision":{{revision}},"assets":{{assets}}}""";
        var binding = await Bind<SpritePlanRequest>(json);
        if (assets.Contains("1e400"))
        {
            Assert.False(binding.HasError);
            Assert.True(double.IsPositiveInfinity(((SpritePlanRequest)binding.Model!).Assets![0].SourceBounds!.X));
        }
        if (!binding.HasError) Status(await Sprites(f).UpdatePlanAsync(job.Id, (SpritePlanRequest)binding.Model!, default), 400);
        Assert.Equal(revision, job.Sprites.ReviewRevision);
        Assert.Single(job.Sprites.Requests);
        Assert.Empty(job.Tasks);
        Assert.Equal(queue, f.Queue.Enqueued.Count);
    }

    [Theory]
    [InlineData("{\"settings\":null}")]
    [InlineData("{\"settings\":{\"view\":0,\"outputKind\":\"layers\"}}")]
    [InlineData("{\"blobKey\":\"images/foreign\"}")]
    [InlineData("{\"sourceUrl\":\"https://example.com/input.png\"}")]
    public async Task JsonBinding_RejectsNullSettingsNumbersAndStorageAddresses(string json)
    {
        var f = new PipelineFixture();
        var unknownAddress = json.Contains("blobKey") || json.Contains("sourceUrl");
        if (unknownAddress)
        {
            var valid = JsonSerializer.Serialize(new StartSpriteJobRequest(Guid.NewGuid(), Guid.NewGuid(), null, null,
                Guid.NewGuid(), "model", Guid.NewGuid(), "image", new("sideView", "layers")),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            json = valid[..^1] + "," + json[1..];
            Assert.False((await Bind<StartSpriteJobRequest>(valid)).HasError);
        }
        var binding = await Bind<StartSpriteJobRequest>(json);
        if (unknownAddress) Assert.True(binding.HasError);
        if (!binding.HasError) Status(await Sprites(f).StartAsync((StartSpriteJobRequest)binding.Model!, default), 400);
        Assert.Empty(f.Queue.Enqueued);
        Assert.Equal(0, f.Blobs.Count);
    }

    [Theory]
    [InlineData("layer", 202)]
    [InlineData("object", 400)]
    [InlineData("unknown", 400)]
    [InlineData("0", 400)]
    [InlineData("Layer", 400)]
    public async Task JsonBinding_RolesReachPlanValidation(string role, int status)
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Plan(f);
        var plan = job.Sprites!.Assets.Single().Plan;
        var json = $$"""{"requestId":"{{Guid.NewGuid()}}","expectedRevision":{{job.Sprites.ReviewRevision}},"grid":null,"assets":[{"id":"{{plan.Id}}","name":"changed","order":0,"sourceBounds":{"x":0,"y":0,"w":1,"h":1},"requiresTransparency":false,"role":"{{role}}"}]}""";
        var binding = await Bind<SpritePlanRequest>(json);
        Assert.False(binding.HasError);
        var result = await Sprites(f).UpdatePlanAsync(job.Id, (SpritePlanRequest)binding.Model!, default);
        Status(result, status);
        if (status == 400) Assert.Equal(ErrorCode.SpritePlanInvalid, DataError(result));
        else Assert.Null(job.Sprites.Assets.Single().Plan.Role);
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("0")]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    public async Task JsonBinding_InvalidGridIs400BeforeFingerprint(string width)
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Plan(f);
        var json = $$$"""{"requestId":"{{{Guid.NewGuid()}}}","expectedRevision":{{{job.Sprites!.ReviewRevision}}},"assets":[],"grid":{"cellWidth":{{{width}}}}}""";
        var binding = await Bind<SpritePlanRequest>(json);
        Assert.False(binding.HasError);
        var revision = job.Sprites.ReviewRevision;
        Status(await Sprites(f).UpdatePlanAsync(job.Id, (SpritePlanRequest)binding.Model!, default), 400);
        Assert.Equal(revision, job.Sprites.ReviewRevision);
    }

    [Fact]
    public async Task LegacySpriteResponse_ExposesEffectiveRoleCanvasAndScale()
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Plan(f);
        typeof(SpriteAsset).GetProperty(nameof(SpriteAsset.Canvas))!.SetValue(job.Sprites!.Assets.Single(), null);
        var json = JsonSerializer.Serialize(SpriteResponse.From(job.Sprites), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var asset = doc.RootElement.GetProperty("assets")[0];
        Assert.Equal("layer", asset.GetProperty("plan").GetProperty("role").GetString());
        Assert.Equal(job.Sprites.OutputCanvas.Width, asset.GetProperty("canvas").GetProperty("width").GetInt32());
        Assert.Equal(1, asset.GetProperty("scale").GetDouble());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("grid").ValueKind);
    }

    private static string DataError(IActionResult result)
        => JsonSerializer.SerializeToElement(Assert.IsType<ObjectResult>(result).Value, ServerJson()).GetProperty("error").GetProperty("code").GetString()!;

    [Fact]
    public async Task NullAssetCollections_Are400()
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Plan(f);
        Status(await Sprites(f).ApproveBasesAsync(job.Id, Assets(job, null!), default), 400);
        Status(await Sprites(f).ExportAsync(job.Id, Assets(job, null!), default), 400);
    }

    [Fact]
    public async Task JsonEnvelope_ContainsSpriteRevisionAndLegacyFields()
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Ready(f);
        var result = Assert.IsType<ObjectResult>(await Jobs(f).GetAsync(job.Id, default));
        var json = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("twoD", data.GetProperty("productionMode").GetString());
        Assert.Equal(job.Sprites!.ReviewRevision, data.GetProperty("sprite").GetProperty("reviewRevision").GetInt32());
        Assert.Equal("sideView", data.GetProperty("sprite").GetProperty("settings").GetProperty("view").GetString());
        Assert.Equal("both", data.GetProperty("sprite").GetProperty("assets")[0].GetProperty("approval")
            .GetProperty("snapshot").GetProperty("repeat").GetString());
        Assert.True(data.TryGetProperty("tasks", out _));
        Assert.True(data.TryGetProperty("parts", out _));
        Assert.True(data.TryGetProperty("models", out _));
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("error").ValueKind);
        Assert.DoesNotContain("blobKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fingerprint", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(job.Sprites.Images, image => Assert.DoesNotContain(image.BlobKey, json));
    }

    [Fact]
    public async Task FailedConsumedExportId_DoesNotAdvertiseStoredZip()
    {
        var f = new PipelineFixture(options: new() { MaxAttempts = 1 });
        var job = await SpriteCommandTests.Ready(f);
        var receipt = Receipt(await Sprites(f).ExportAsync(job.Id, Assets(job, [job.Sprites!.Assets.Single().Id]), default));
        var task = job.Tasks.Single(task => task.Id == receipt.TaskIds.Single());
        task.Claim(f.Clock.Now, f.Options.Lease);
        task.Fail("package failure", f.Clock.Now);
        job.ReconcileFromTasks(f.Clock.Now);
        Assert.Equal(task.SpriteExportInput!.ExportId, job.Sprites.CompletedExportId);
        var dto = Data<JobResponse>(await Jobs(f).GetAsync(job.Id, default));
        Assert.Null(dto.Sprite!.CompletedExportId);
        Assert.Empty(dto.Sprite.Exports);
        Assert.Equal("packaging", dto.Sprite.Phase);
        Status(await Sprites(f).GetExportAsync(job.Id, task.SpriteExportInput.ExportId, default), 409);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("enhance")]
    [InlineData("prompt")]
    [InlineData("select")]
    public async Task SourceRequests_RejectUnknownFields(string kind)
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var (type, json) = kind switch
        {
            "draft" => (typeof(CreateSpriteDraftRequest), JsonSerializer.Serialize(new CreateSpriteDraftRequest(Guid.NewGuid(), "설명",
                Guid.NewGuid(), "model", Guid.NewGuid(), "image", new("sideView", "layers")), web)),
            "enhance" => (typeof(SpriteSourceEnhanceRequest), JsonSerializer.Serialize(new SpriteSourceEnhanceRequest(Guid.NewGuid(), 0, "설명"), web)),
            "prompt" => (typeof(SpriteSourcePromptRequest), JsonSerializer.Serialize(new SpriteSourcePromptRequest(Guid.NewGuid(), 0, "프롬프트"), web)),
            _ => (typeof(SpriteSourceSelectRequest), JsonSerializer.Serialize(new SpriteSourceSelectRequest(Guid.NewGuid(), 0, Guid.NewGuid()), web)),
        };
        Assert.False((await Bind(type, json)).HasError);
        Assert.True((await Bind(type, json[..^1] + ",\"blobKey\":\"images/foreign\"}")).HasError);
    }

    [Fact]
    public async Task SourceDraftFlow_UsesAcceptedEnvelopeAndSerializesSource()
    {
        var f = new PipelineFixture();
        var command = await SpriteSourceCommandTests.Prepare(f);
        var api = Sprites(f);
        var request = new CreateSpriteDraftRequest(command.RequestId, command.Description, command.ProviderConfigId,
            command.Model, command.ImageProviderConfigId, command.ImageModel, new("sideView", "layers"));
        var created = Receipt(await api.CreateDraftAsync(request, default));
        Assert.Equal("running", created.Status);
        Assert.Equal(created.TaskIds, Receipt(await api.CreateDraftAsync(request, default)).TaskIds);
        Status(await api.CreateDraftAsync(request with { Description = "다른 설명" }, default), 409);
        Status(await api.CreateDraftAsync(request with { RequestId = Guid.NewGuid(), Settings = null }, default), 400);
        await f.RunSpriteSourceEnhance.HandleAsync(created.TaskIds.Single(), default);
        var job = (await f.Jobs.GetAsync(created.Id, default))!;

        var saved = Receipt(await api.SaveSourcePromptAsync(job.Id, new(Guid.NewGuid(), job.Sprites!.ReviewRevision, "저장한 프롬프트"), default));
        Assert.Empty(saved.TaskIds);
        Status(await api.EnhanceSourceAsync(job.Id, new(Guid.NewGuid(), saved.Revision - 1, "설명"), default), 409);
        var generated = Receipt(await api.GenerateSourceAsync(job.Id, new(Guid.NewGuid(), saved.Revision, "생성 프롬프트"), default));
        Status(await api.EnhanceSourceAsync(job.Id, new(Guid.NewGuid(), generated.Revision, "설명"), default), 409);
        await f.RunSpriteSourceGeneration.HandleAsync(generated.TaskIds.Single(), default);
        var candidate = job.Sprites.SourceDraft!.Candidates.Single();

        var result = Assert.IsType<ObjectResult>(await Jobs(f).GetAsync(job.Id, default));
        var json = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("sourceImageId").ValueKind);
        Assert.Equal("sourceDraft", data.GetProperty("sprite").GetProperty("phase").GetString());
        var source = data.GetProperty("sprite").GetProperty("source");
        Assert.Equal("80년대 서울", source.GetProperty("description").GetString());
        Assert.Equal("생성 프롬프트", source.GetProperty("prompt").GetString());
        var wire = source.GetProperty("candidates").EnumerateArray().Single();
        Assert.Equal(["id", "prompt", "width", "height", "createdAt"], wire.EnumerateObject().Select(p => p.Name));
        Assert.Equal(candidate.Id, wire.GetProperty("id").GetGuid());
        Assert.Equal(candidate.Width, wire.GetProperty("width").GetInt32());
        Assert.DoesNotContain(candidate.BlobKey, json);
        Assert.DoesNotContain("blobKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(Data<JobListResponse>(await Jobs(f).ListAsync("active", null, 10, default)).Items.Single().SourceImageId);

        var image = Assert.IsType<FileStreamResult>(await api.GetSourceCandidateAsync(job.Id, candidate.Id, default));
        Assert.Equal(candidate.ContentType, (await ExecuteFile(image)).Response.ContentType);
        Assert.Equal("private, max-age=31536000, immutable", api.Response.Headers.CacheControl);

        var selected = Receipt(await api.SelectSourceAsync(job.Id, new(Guid.NewGuid(), job.Sprites.ReviewRevision, candidate.Id), default));
        Assert.Equal(SpritePhase.Analyzing, job.Sprites.Phase);
        Assert.NotNull(job.SourceImageId);
        Assert.Single(selected.TaskIds);
        Assert.Equal(candidate.Id, Data<JobResponse>(await Jobs(f).GetAsync(job.Id, default)).Sprite!.Source!.Candidates.Single().Id);
    }

    [Fact]
    public async Task SourceCandidateImage_RequiresOwningJob()
    {
        var f = new PipelineFixture();
        var job = await SpriteSourceCommandTests.Draft(f);
        var own = await SpriteSourceCommandTests.AddCandidate(f, job);
        var foreign = await SpriteSourceCommandTests.Draft(f);
        var foreignCandidate = await SpriteSourceCommandTests.AddCandidate(f, foreign);
        var api = Sprites(f);
        Assert.IsType<FileStreamResult>(await api.GetSourceCandidateAsync(job.Id, own, default));
        Status(await api.GetSourceCandidateAsync(job.Id, foreignCandidate, default), 404);
        Status(await api.GetSourceCandidateAsync(job.Id, Guid.NewGuid(), default), 404);
        Status(await api.GetSourceCandidateAsync(Guid.NewGuid(), own, default), 404);
        var uploaded = await SpriteCommandTests.Plan(f);
        Status(await api.GetSourceCandidateAsync(uploaded.Id, own, default), 404);
    }

    [Fact]
    public void SyncSourceGenerationRoute_IsRemoved()
    {
        Assert.DoesNotContain(typeof(UploadsController).GetMethods(), method => method.GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Cast<HttpPostAttribute>().Any(post => post.Template == "generate"));
        Assert.Null(typeof(JobResponse).Assembly.GetType("Noxtend.Api.Contracts.GenerateSpriteSourceRequest"));
    }

    [Theory]
    [InlineData("tiles")]
    [InlineData("layers")]
    public async Task JsonBinding_RequestFixtureStoresRolesAndGrid(string scenario)
    {
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(FixturePath("sprite-roles.request.json")));
        var binding = await Bind<SpritePlanRequest>(fixture.RootElement.GetProperty(scenario).GetRawText());
        Assert.False(binding.HasError);
        var request = (SpritePlanRequest)binding.Model!;
        var f = new PipelineFixture();
        var command = await SpriteAnalysisTests.Prepare(f);
        var settings = new SpriteSettings(SpriteView.SideView, scenario == "tiles" ? SpriteOutputKind.Tiles : SpriteOutputKind.Layers);
        var job = PipelineJob.CreateSprites(command.UploadId!.Value, command.ImageProviderConfigId, command.ImageModel,
            settings, new(800, 600), new(1536, 1024), f.Clock.Now).Value!;
        await f.Jobs.AddAsync(job, default);
        Receipt(await Sprites(f).UpdatePlanAsync(job.Id, request, default));
        Assert.Equal(request.Grid?.CellWidth, job.Sprites!.Grid?.CellWidth);
        Assert.Equal(request.Assets!.Select(a => a.Role), SpriteResponse.From(job.Sprites).Assets.Select(a => a.Plan.Role));
    }

    [Theory]
    [InlineData(SpriteOutputKind.Layers)]
    [InlineData(SpriteOutputKind.Tiles)]
    public async Task JsonBinding_OmittedRoleAndGridUseLegacyDefaultsAndUnknownFieldsFail(SpriteOutputKind kind)
    {
        var f = new PipelineFixture();
        var command = await SpriteAnalysisTests.Prepare(f);
        var job = PipelineJob.CreateSprites(command.UploadId!.Value, command.ImageProviderConfigId, command.ImageModel,
            new(SpriteView.SideView, kind), new(800, 600), new(1536, 1024), f.Clock.Now).Value!;
        await f.Jobs.AddAsync(job, default);
        var id = FixedId(61);
        var json = $$"""{"requestId":"{{Guid.NewGuid()}}","expectedRevision":{{job.Sprites!.ReviewRevision}},"assets":[{"id":"{{id}}","name":"legacy","order":0,"sourceBounds":{"x":0,"y":0,"w":1,"h":1},"requiresTransparency":false}]}""";
        var binding = await Bind<SpritePlanRequest>(json);
        Assert.False(binding.HasError);
        Receipt(await Sprites(f).UpdatePlanAsync(job.Id, (SpritePlanRequest)binding.Model!, default));
        Assert.Null(job.Sprites.Grid);
        Assert.Null(job.Sprites.Assets.Single().Plan.Role);
        Assert.True((await Bind<SpritePlanRequest>(json[..^1] + ",\"unknown\":1}")).HasError);
        Assert.True((await Bind<SpritePlanRequest>(json.Replace("\"requiresTransparency\":false", "\"requiresTransparency\":false,\"unknown\":1"))).HasError);
        Assert.True((await Bind<SpritePlanRequest>(json[..^1] + ",\"grid\":{\"cellWidth\":0.25,\"unknown\":1}}")).HasError);
    }

    [Fact]
    public async Task ServerRolesFixture_MatchesActualResponseMapperAndJsonOptions()
    {
        var mixed = FixtureJob(1, SpriteOutputKind.Tiles);
        SpriteAssetPlan[] plans = [
            new(FixedId(11), "배경", 0, new(0, 0, 1, 1), false, Role: SpriteAssetRole.Backdrop),
            new(FixedId(12), "타일", 1, new(0, 0, .25, .25), true, Role: SpriteAssetRole.Tile),
            new(FixedId(13), "오브젝트", 2, new(0, 0, .125, .25), true, Role: SpriteAssetRole.Object),
        ];
        Assert.True(mixed.ReplaceSpritePlan(plans, new(.1), [new(1024, 1024)], 0).IsSuccess);
        var legacy = FixtureJob(2, SpriteOutputKind.Layers);
        Assert.True(legacy.ReplaceSpritePlan([new(FixedId(21), "구형 레이어", 0, new(0, 0, 1, 1), false)], 0).IsSuccess);
        Set(legacy.Sprites!.Assets.Single(), nameof(SpriteAsset.Canvas), null);
        var approved = FixtureJob(3, SpriteOutputKind.Tiles);
        Assert.True(approved.ReplaceSpritePlan([new(FixedId(31), "구형 타일", 0, new(0, 0, .25, .25), false)], 0).IsSuccess);
        var asset = approved.Sprites!.Assets.Single();
        Set(asset, nameof(SpriteAsset.Canvas), null);
        var snapshot = new SpriteApprovedAsset(asset.Id, asset.Plan.Name, 0, 8, false, asset.Anchor,
            SpriteRepeat.Both, SpriteTileLayout.Square, FixedId(32), [FixedId(32)]);
        Set(asset, nameof(SpriteAsset.ApprovedBaseImageId), snapshot.BaseImageId);
        Set(asset, nameof(SpriteAsset.Approval), new SpriteAssetApproval(1, snapshot));
        Set(asset.Frames.Single(), nameof(SpriteFrame.CurrentImageId), snapshot.BaseImageId);
        var empty = FixtureJob(4, SpriteOutputKind.Tiles);
        var responses = new Dictionary<string, ApiResponse<JobResponse>> {
            ["mixedJob"] = ApiResponse<JobResponse>.Ok(JobResponse.From(new(mixed, []))),
            ["legacyJob"] = ApiResponse<JobResponse>.Ok(JobResponse.From(new(legacy, []))),
            ["legacyApproval"] = ApiResponse<JobResponse>.Ok(JobResponse.From(new(approved, []))),
            ["gridNull"] = ApiResponse<JobResponse>.Ok(JobResponse.From(new(empty, []))),
        };
        var options = ServerJson();
        options.WriteIndented = true;
        var json = JsonSerializer.Serialize(responses, options) + "\n";
        var path = FixturePath("sprite-roles.server.json");
        if (Environment.GetEnvironmentVariable("NOXTEND_REGENERATE_SPRITE_ROLES_FIXTURE") == "1")
            await File.WriteAllTextAsync(path, json);
        var expected = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path));
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(expected, System.Text.Json.Nodes.JsonNode.Parse(json)),
            "서버 계약 fixture 불일치: 인접 문서의 명시적 재생성 명령 확인");
        Assert.Equal(snapshot, asset.Approval!.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedExportId_RequiresCurrentExportAfterGridOrNameEdit(bool rename)
    {
        var job = FixtureJob(5, SpriteOutputKind.Tiles);
        var plan = new SpriteAssetPlan(FixedId(51), "타일", 0, new(0, 0, .25, .25), false);
        Assert.True(job.ReplaceSpritePlan([plan], new(.25), [], 0).IsSuccess);
        var input = job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value!.Single();
        var task = job.PlanSpriteFrames([input], FixedId(52)).Single();
        task.Claim(FixtureNow, TimeSpan.FromMinutes(2));
        Assert.True(job.TryAttachSpriteImage(SpriteImage.Create(task.Id, input, "fixture.png", FixtureNow)));
        task.Succeed(FixtureNow);
        job.ReconcileFromTasks(FixtureNow);
        Assert.True(job.ApproveSpriteBases([plan.Id], job.Sprites.ReviewRevision).IsSuccess);
        var old = CompleteExport(job);
        Assert.Equal(old, SpriteResponse.From(job.Sprites).CompletedExportId);
        if (rename) plan = plan with { Name = "새 이름" };
        Assert.True(job.ReplaceSpritePlan([plan], new(rename ? .25 : .5), [], job.Sprites.ReviewRevision).IsSuccess);
        Assert.Equal(old, job.Sprites.CompletedExportId);
        Assert.Null(SpriteResponse.From(job.Sprites).CompletedExportId);
        if (rename) Assert.True(job.ApproveSpriteAsset(plan.Id, job.Sprites.ReviewRevision).IsSuccess);
        var next = CompleteExport(job);
        Assert.NotEqual(old, next);
        Assert.Equal(next, SpriteResponse.From(job.Sprites).CompletedExportId);
    }

    private static Guid CompleteExport(PipelineJob job)
    {
        var input = job.CaptureSpriteExport([job.Sprites!.Assets.Single().Id], job.Sprites.ReviewRevision).Value!;
        var task = job.PlanTask(TaskKind.PackSprites, job.Tasks.Count);
        task.BindSpriteExport(input);
        task.Claim(FixtureNow, TimeSpan.FromMinutes(2));
        Assert.True(job.TryAttachSpriteExport(SpriteExport.Create(task.Id, input,
            new(input.SchemaVersion, job.Id, ProductionMode.TwoD, input, "topLeft", "pixels", []), "fixture.zip", FixtureNow)));
        task.Succeed(FixtureNow);
        job.ReconcileFromTasks(FixtureNow);
        return input.ExportId;
    }
    private static readonly DateTimeOffset FixtureNow = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
    private static Guid FixedId(int id) => Guid.Parse($"10000000-0000-4000-8000-{id:000000000000}");
    private static void Set(object target, string property, object? value)
        => target.GetType().GetProperty(property)!.SetValue(target, value);
    private static PipelineJob FixtureJob(int id, SpriteOutputKind kind)
    {
        var job = PipelineJob.CreateSprites(FixedId(100), FixedId(101), "fixture-model",
            new(SpriteView.SideView, kind), new(800, 600), new(1024, 1024), FixtureNow).Value!;
        Set(job, nameof(PipelineJob.Id), FixedId(id));
        return job;
    }
    private static JsonSerializerOptions ServerJson()
    {
        var services = new ServiceCollection();
        services.AddLogging().AddControllers().AddJsonOptions(ApiJson.Configure);
        using var provider = services.BuildServiceProvider();
        return new(provider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions);
    }
    private static string FixturePath(string filename)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "apps", "backend", "Noxtend.slnx")))
            directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("apps/backend/Noxtend.slnx 저장소 루트를 찾을 수 없습니다");
        return Path.Combine(directory.FullName, "apps", "frontend", "src", "infra", "api", "fixtures", filename);
    }

    private static SpriteJobsController Sprites(PipelineFixture f) => new(f.StartSprites, f.SpriteCommands, f.SpriteSource, f.Jobs, f.Blobs)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };
    private static SpriteMutationRequest Mutation(PipelineJob job) => new(Guid.NewGuid(), job.Sprites!.ReviewRevision);
    private static SpriteAssetsRequest Assets(PipelineJob job, IReadOnlyList<Guid> ids) => new(Guid.NewGuid(), job.Sprites!.ReviewRevision, ids);
    private static void Status(IActionResult result, int expected) => Assert.Equal(expected, Assert.IsType<ObjectResult>(result).StatusCode);
    private static T Data<T>(IActionResult result) => Assert.IsType<ApiResponse<T>>(Assert.IsAssignableFrom<ObjectResult>(result).Value).Data!;
    private static SpriteAcceptedResponse Receipt(IActionResult result)
    {
        Status(result, 202);
        return Data<SpriteAcceptedResponse>(result);
    }
    private static async Task FinishFrames(PipelineFixture f, PipelineJob job, IReadOnlyList<Guid> ids)
    {
        var source = (await f.Images.GetAsync(job.SourceImageId!.Value, default))!;
        foreach (var id in ids)
        {
            var task = job.Tasks.Single(task => task.Id == id);
            task.Claim(f.Clock.Now, f.Options.Lease);
            task.Succeed(f.Clock.Now);
            await using var stream = await f.Blobs.OpenReadAsync(source.BlobKey, default);
            var key = await f.Blobs.SaveAsync(stream, "image/png", default);
            Assert.True(job.TryAttachSpriteImage(SpriteImage.Create(id, task.SpriteInput!, key, f.Clock.Now)));
        }
        job.ReconcileFromTasks(f.Clock.Now);
    }
    private static Task<InputFormatterResult> Bind<T>(string json) => Bind(typeof(T), json);
    private static async Task<InputFormatterResult> Bind(Type type, string json)
    {
        var services = new ServiceCollection();
        services.AddLogging().AddControllers().AddJsonOptions(ApiJson.Configure);
        using var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Request.ContentType = "application/json";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var metadata = provider.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(type);
        var context = new InputFormatterContext(http, "", new ModelStateDictionary(), metadata,
            (stream, encoding) => new StreamReader(stream, encoding));
        return await provider.GetRequiredService<IOptions<MvcOptions>>().Value.InputFormatters
            .OfType<SystemTextJsonInputFormatter>().Single().ReadRequestBodyAsync(context, Encoding.UTF8);
    }
    private static async Task<DefaultHttpContext> ExecuteFile(FileStreamResult file)
    {
        var services = new ServiceCollection();
        services.AddLogging().AddControllers().AddJsonOptions(ApiJson.Configure);
        using var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.Body = new MemoryStream();
        await file.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        return http;
    }

    internal static JobsController Jobs(PipelineFixture f) => new(
        f.Start, f.Get, f.List, f.Cancel, f.Delete, f.Retry, f.AddMesh,
        f.GetReview, f.AddReviewPart, f.FindOverlaps, f.RemoveReviewPart, f.MoveReviewPlacement,
        f.ApproveReview, f.ReviewDescriptions, f.GenerateViews, f.ReturnToDescriptions, f.ReplanPartMesh);
}
