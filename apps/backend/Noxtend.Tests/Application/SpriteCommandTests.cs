using Noxtend.Application.Sprites;
using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Infrastructure.Image;
using System.IO.Compression;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Sprites;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Application;

public sealed class SpriteCommandTests
{
    [Fact]
    public async Task SameRequestIdDifferentBody_ReturnsConflict()
    {
        var f = new PipelineFixture();
        var command = await SpriteAnalysisTests.Prepare(f);
        Assert.True((await f.StartSprites.HandleAsync(command, default)).IsSuccess);
        var result = await f.StartSprites.HandleAsync(command with { Settings = command.Settings with { View = SpriteView.TopDown } }, default);
        Assert.Equal("SPRITE_REQUEST_CONFLICT", result.ErrorCode);
    }

    [Fact]
    public async Task ActiveAnalysis_RejectsPlanEditAndOldFailedAnalysisCannotOverwritePlan()
    {
        var f = new PipelineFixture();
        var receipt = await f.StartSprites.HandleAsync(await SpriteAnalysisTests.Prepare(f), default);
        var job = (await f.Jobs.GetAsync(receipt.Value!.JobId, default))!;
        var task = job.Tasks.Single();
        var plan = new SpriteAssetPlan(Guid.NewGuid(), "배경", 0, new(0, 0, 1, 1), false);
        Assert.Equal(ErrorCode.SpriteBusy, job.ReplaceSpritePlan([plan], 0).ErrorCode);
        task.Claim(f.Clock.Now, f.Options.Lease);
        Assert.Equal(ErrorCode.SpriteBusy, job.ReplaceSpritePlan([plan], 0).ErrorCode);
        task.Fail("test", f.Clock.Now);
        job.ReconcileFromTasks(f.Clock.Now);
        Assert.True(job.RetryOutputTask(task.Id));
        Assert.Equal(TaskStatus.Pending, task.Status);
        task.Claim(f.Clock.Now, f.Options.Lease);
        task.Fail("test", f.Clock.Now);
        job.ReconcileFromTasks(f.Clock.Now);
        Assert.True(job.ReplaceSpritePlan([plan], 0).IsSuccess);
        Assert.False(job.RetryOutputTask(task.Id));
    }
    [Fact]
    public async Task DuplicateApproval_AfterRevisionChange_ReturnsReceipt()
    {
        var f = new PipelineFixture();
        var job = await Plan(f, 2);
        var context = Context(job);
        var first = await f.SpriteCommands.ApprovePlanAsync(context, default);
        Assert.True(first.IsSuccess, first.ErrorMessage);
        var firstTaskIds = job.Tasks.Select(t => t.Id).ToArray();
        await FinishFrames(f, job, first.Value!.TaskIds);
        var duplicate = await f.SpriteCommands.ApprovePlanAsync(context, default);
        Assert.Equal(first.Value.JobId, duplicate.Value!.JobId);
        Assert.Equal(first.Value.Revision, duplicate.Value.Revision);
        Assert.Equal(first.Value.TaskIds, duplicate.Value.TaskIds);
        Assert.Equal(firstTaskIds, job.Tasks.Select(t => t.Id));
        Assert.Equal(ErrorCode.SpriteRevisionConflict,
            (await f.SpriteCommands.ApprovePlanAsync(context with { RequestId = Guid.NewGuid() }, default)).ErrorCode);
        Assert.Equal(ErrorCode.SpriteRequestConflict,
            (await f.SpriteCommands.ApproveAssetAsync(context, job.Sprites!.Assets[0].Id, default)).ErrorCode);
    }

    [Fact]
    public async Task UpdatePlan_NormalizesIdOrderAndFingerprintIncludesRouteAndRevision()
    {
        var f = new PipelineFixture();
        var job = await Plan(f, 2);
        var context = Context(job);
        var plans = job.Sprites!.Assets.Select(a => a.Plan with { Name = a.Plan.Name + " 수정" }).ToArray();
        var first = await f.SpriteCommands.UpdatePlanAsync(context, plans, default);
        var duplicate = await f.SpriteCommands.UpdatePlanAsync(context, plans.Reverse().ToArray(), default);
        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value, duplicate.Value);
        Assert.Equal(ErrorCode.SpriteRequestConflict, (await f.SpriteCommands.UpdatePlanAsync(
            context with { ExpectedRevision = job.Sprites.ReviewRevision }, plans, default)).ErrorCode);
        var other = await Plan(f);
        Assert.Equal(ErrorCode.SpriteRequestConflict, (await f.SpriteCommands.UpdatePlanAsync(
            context with { JobId = other.Id }, plans, default)).ErrorCode);
    }

    [Fact]
    public async Task PartialExport_RequiresExplicitApprovedAssetSelection()
    {
        var f = new PipelineFixture();
        var job = await Ready(f, 2);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Equal(ErrorCode.SpritePlanInvalid, (await f.SpriteCommands.ExportAsync(Context(job), [], default)).ErrorCode);
        var asset = job.Sprites!.Assets[0];
        var exported = await f.SpriteCommands.ExportAsync(Context(job), [asset.Id], default);
        Assert.True(exported.IsSuccess, exported.ErrorMessage);
        var task = job.Tasks.Single(t => t.Id == exported.Value!.TaskIds.Single());
        Assert.Equal(9, (int)task.Kind);
        Assert.Single(task.SpriteExportInput!.ExcludedAssetIds);
        Assert.Equal(RunTaskOutcome.Succeeded, await f.RunSpritePack.HandleAsync(task.Id, default));
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        var package = Assert.Single(job.Sprites.Exports);
        Assert.True(package.IsCurrent);
        await using var stream = await f.Blobs.OpenReadAsync(package.BlobKey, default);
        using var zip = new ZipArchive(stream);
        Assert.Contains(zip.Entries, entry => entry.FullName == "manifest.json");
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains(job.Sprites.Assets[1].Id.ToString()));
    }

    [Fact]
    public async Task StalePackage_IsHistoryOnly()
    {
        var f = new PipelineFixture();
        var job = await Ready(f);
        var exported = await f.SpriteCommands.ExportAsync(Context(job), [job.Sprites!.Assets[0].Id], default);
        await f.RunSpritePack.HandleAsync(exported.Value!.TaskIds.Single(), default);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        var old = Assert.Single(job.Sprites.Exports);
        var plan = job.Sprites.Assets[0].Plan;
        Assert.True((await f.SpriteCommands.UpdatePlanAsync(Context(job), [plan with { Fps = 10 }], default)).IsSuccess);
        Assert.False(old.IsCurrent);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Null(job.CompletedAt);
        job.ReconcileFromTasks(f.Clock.Now);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        await using var history = await f.Blobs.OpenReadAsync(old.BlobKey, default);
        Assert.True(history.Length > 0);
    }

    [Fact]
    public async Task PackRetry_UsesFrozenInputAndMakesNoImageCalls()
    {
        var packImageCallCount = 0;
        var f = new PipelineFixture(imageProvider: FakeImageProvider.Throwing(() =>
        {
            packImageCallCount++;
            return new InvalidOperationException("pack must not generate images");
        }));
        var job = await Ready(f);
        var context = Context(job);
        var ids = job.Sprites!.Assets.Select(a => a.Id).ToArray();
        var exported = await f.SpriteCommands.ExportAsync(context, ids, default);
        var task = job.Tasks.Single(t => t.Id == exported.Value!.TaskIds.Single());
        var input = task.SpriteExportInput;
        task.Claim(f.Clock.Now, f.Options.Lease);
        task.Fail("test", f.Clock.Now);
        job.ReconcileFromTasks(f.Clock.Now);
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        var approval = job.Sprites.Assets[0].Approval;
        var retried = await f.Retry.HandleAsync(job.Id, task.Id, default);
        Assert.True(retried.IsSuccess);
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Null(job.CompletedAt);
        Assert.Same(input, task.SpriteExportInput);
        Assert.Same(approval, job.Sprites.Assets[0].Approval);
        Assert.Equal(RunTaskOutcome.Succeeded, await f.RunSpritePack.HandleAsync(task.Id, default));
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(f.Prompts.Queries);
        Assert.Equal(0, packImageCallCount);
        Assert.Equal(input!.ExportId, job.Sprites.Exports.Single().Id);
        var duplicate = await f.SpriteCommands.ExportAsync(context, ids, default);
        Assert.Equal(exported.Value!.TaskIds, duplicate.Value!.TaskIds);
        Assert.Equal(exported.Value.Revision, duplicate.Value.Revision);
    }

    [Fact]
    public async Task RegenerationReopensTerminalWithNewTaskAndPreservesOtherApproval()
    {
        var f = new PipelineFixture();
        var job = await Ready(f, 2);
        var ids = job.Sprites!.Assets.Select(a => a.Id).ToArray();
        var exported = await f.SpriteCommands.ExportAsync(Context(job), ids, default);
        await f.RunSpritePack.HandleAsync(exported.Value!.TaskIds.Single(), default);
        var approval = job.Sprites.Assets[1].Approval;
        var before = job.Tasks.Count;
        var context = Context(job);
        var result = await f.SpriteCommands.RegenerateAsync(context, ids[0], 0, default);
        Assert.True(result.IsSuccess);
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Null(job.CompletedAt);
        Assert.Same(approval, job.Sprites.Assets[1].Approval);
        Assert.Null(job.Sprites.Assets[0].Approval);
        Assert.Equal(before + 1, job.Tasks.Count);
        Assert.Equal(result.Value!.TaskIds, (await f.SpriteCommands.RegenerateAsync(context, ids[0], 0, default)).Value!.TaskIds);
        Assert.Equal(ErrorCode.SpriteRequestConflict, (await f.SpriteCommands.RegenerateAsync(context, ids[1], 0, default)).ErrorCode);
        Assert.Equal(ErrorCode.SpriteRequestConflict, (await f.SpriteCommands.RegenerateAsync(context, ids[0], 1, default)).ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageMadeStaleOrCanceledDuringBlobSave_IsNotPublished(bool cancel)
    {
        var f = new PipelineFixture();
        var job = await Ready(f);
        var result = await f.SpriteCommands.ExportAsync(Context(job), [job.Sprites!.Assets[0].Id], default);
        var before = f.Blobs.Count;
        var blobs = new MutatingBlob(f.Blobs, () =>
        {
            if (cancel) job.Cancel(f.Clock.Now);
            else Assert.True(job.ReplaceSpritePlan([job.Sprites.Assets[0].Plan with { Fps = 10 }], job.Sprites.ReviewRevision).IsSuccess);
        });
        var handler = new RunSpritePackTaskHandler(f.Jobs, blobs, new SpritePackageWriter(new SkiaImageTranscoder()),
            f.Execution, f.Clock, f.Options, NullLogger<RunSpritePackTaskHandler>.Instance);
        Assert.Equal(cancel ? RunTaskOutcome.Canceled : RunTaskOutcome.Skipped,
            await handler.HandleAsync(result.Value!.TaskIds.Single(), default));
        Assert.Empty(job.Sprites.Exports);
        Assert.Equal(before, f.Blobs.Count);
        Assert.Single(job.Sprites.Images);
        var temporary = Assert.IsType<FileStream>(blobs.Content);
        Assert.False(temporary.CanRead);
        Assert.False(File.Exists(temporary.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedFrameRetry_ReusesCurrentTaskAndRejectsReplacedSlotAndCanceledJob(bool otherSuccess)
    {
        var f = new PipelineFixture();
        var job = await Plan(f, otherSuccess ? 2 : 1);
        var result = await f.SpriteCommands.ApprovePlanAsync(Context(job), default);
        var task = job.Tasks[0];
        task.Claim(f.Clock.Now, f.Options.Lease);
        task.Fail("test", f.Clock.Now);
        job.ReconcileFromTasks(f.Clock.Now);
        if (otherSuccess) await FinishFrames(f, job, [job.Tasks[1].Id]);
        Assert.Equal(otherSuccess ? JobStatus.PendingReview : JobStatus.Failed, job.Status);
        Assert.True((await f.Retry.HandleAsync(job.Id, task.Id, default)).IsSuccess);
        Assert.Equal(otherSuccess ? JobStatus.Running : JobStatus.Pending, job.Status);
        task.Claim(f.Clock.Now, f.Options.Lease);
        task.Fail("test", f.Clock.Now);
        job.ReconcileFromTasks(f.Clock.Now);
        var regenerated = await f.SpriteCommands.RegenerateAsync(Context(job), job.Sprites!.Assets[0].Id, 0, default);
        Assert.True(regenerated.IsSuccess);
        Assert.Equal(ErrorCode.TaskNotRetryable, (await f.Retry.HandleAsync(job.Id, task.Id, default)).ErrorCode);
        job.Cancel(f.Clock.Now);
        Assert.Equal(ErrorCode.JobAlreadyTerminal, (await f.SpriteCommands.ExportAsync(Context(job), [job.Sprites.Assets[0].Id], default)).ErrorCode);
        Assert.Equal(ErrorCode.TaskNotRetryable, (await f.Retry.HandleAsync(job.Id, task.Id, default)).ErrorCode);
    }

    [Fact]
    public async Task DelayedAnalysis_DoesNotOverwriteManualPlan()
    {
        var provider = new DelayedAnalysis();
        var f = new PipelineFixture(provider);
        var receipt = await f.StartSprites.HandleAsync(await SpriteAnalysisTests.Prepare(f), default);
        var job = (await f.Jobs.GetAsync(receipt.Value!.JobId, default))!;
        var run = f.RunSpriteAnalysis.HandleAsync(job.Tasks.Single().Id, default);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var plan = new SpriteAssetPlan(Guid.NewGuid(), "사용자 계획", 0, new(0, 0, 1, 1), false);
        var edit = await f.SpriteCommands.UpdatePlanAsync(Context(job), [plan], default);
        Assert.Equal(ErrorCode.SpriteBusy, edit.ErrorCode);
        Assert.Empty(job.Sprites!.Assets);
        provider.Release.SetResult();
        Assert.Equal(RunTaskOutcome.Succeeded, await run);
        Assert.Equal("배경", job.Sprites.Assets.Single().Plan.Name);
        Assert.True((await f.SpriteCommands.UpdatePlanAsync(Context(job), [plan], default)).IsSuccess);
        Assert.Equal("사용자 계획", job.Sprites.Assets.Single().Plan.Name);
    }

    [Fact]
    public async Task PackFailure_PreservesPngAndEndsPartiallySucceeded()
    {
        var f = new PipelineFixture(options: new() { MaxAttempts = 1 });
        var job = await Ready(f);
        var receipt = await f.SpriteCommands.ExportAsync(Context(job), [job.Sprites!.Assets[0].Id], default);
        var before = f.Blobs.Count;
        var handler = new RunSpritePackTaskHandler(f.Jobs, new FailingPackageSave(f.Blobs),
            new SpritePackageWriter(new SkiaImageTranscoder()), f.Execution, f.Clock, f.Options,
            NullLogger<RunSpritePackTaskHandler>.Instance);
        Assert.Equal(RunTaskOutcome.Failed, await handler.HandleAsync(receipt.Value!.TaskIds.Single(), default));
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        Assert.Equal("SPRITE_PACK_FAILED", job.FailureReason);
        Assert.Empty(job.Sprites.Exports);
        Assert.Equal(before, f.Blobs.Count);
        await using var png = await f.Blobs.OpenReadAsync(job.Sprites.Images.Single().BlobKey, default);
        Assert.True(png.Length > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsumedSubsetPackFailure_DoesNotCloseExcludedAssetRegeneration(bool editMetadata)
    {
        var imageCalls = 0;
        var f = new PipelineFixture(options: new() { MaxAttempts = 1 }, imageProvider: FakeImageProvider.Throwing(() =>
        {
            imageCalls++;
            return new InvalidOperationException("pack must not generate images");
        }));
        var job = await Ready(f, 2);
        var included = job.Sprites!.Assets[0];
        var excluded = job.Sprites.Assets[1];
        var receipt = await f.SpriteCommands.ExportAsync(Context(job), [included.Id], default);
        var pack = job.Tasks.Single(t => t.Id == receipt.Value!.TaskIds.Single());
        var frozen = pack.SpriteExportInput!;
        var failing = new RunSpritePackTaskHandler(f.Jobs, new FailingPackageSave(f.Blobs),
            new SpritePackageWriter(new SkiaImageTranscoder()), f.Execution, f.Clock, f.Options,
            NullLogger<RunSpritePackTaskHandler>.Instance);
        Assert.Equal(RunTaskOutcome.Failed, await failing.HandleAsync(pack.Id, default));
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        Assert.Equal("SPRITE_PACK_FAILED", job.FailureReason);
        if (editMetadata)
        {
            var plans = job.Sprites.Assets.Select(a => a.Plan.Id == excluded.Id
                ? a.Plan with { Name = "수정한 제외 대상", Fps = 10 } : a.Plan).ToArray();
            Assert.True((await f.SpriteCommands.UpdatePlanAsync(Context(job), plans, default)).IsSuccess);
            job.ReconcileFromTasks(f.Clock.Now);
            Assert.Equal(JobStatus.PendingReview, job.Status);
            Assert.Null(job.FailureReason);
        }
        var regenerated = await f.SpriteCommands.RegenerateAsync(Context(job), excluded.Id, 0, default);
        Assert.True(regenerated.IsSuccess);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.FailureReason);
        await FinishFrames(f, job, regenerated.Value!.TaskIds);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Equal(SpritePhase.BaseReview, job.Sprites.Phase);
        Assert.Null(job.FailureReason);
        Assert.Null(job.CompletedAt);
        Assert.Equal(frozen.ExportId, job.Sprites.CompletedExportId);
        Assert.Same(included.Approval!.Snapshot, frozen.Assets.Single());
        var count = job.Tasks.Count;
        Assert.True((await f.Retry.HandleAsync(job.Id, pack.Id, default)).IsSuccess);
        Assert.Null(job.Sprites.CompletedExportId);
        Assert.Same(frozen, pack.SpriteExportInput);
        Assert.Equal(RunTaskOutcome.Failed, await failing.HandleAsync(pack.Id, default));
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        Assert.Equal(frozen.ExportId, job.Sprites.CompletedExportId);
        Assert.Equal(SpritePhase.Packaging, job.Sprites.Phase);
        Assert.True((await f.Retry.HandleAsync(job.Id, pack.Id, default)).IsSuccess);
        Assert.Null(job.Sprites.CompletedExportId);
        Assert.Equal(RunTaskOutcome.Succeeded, await f.RunSpritePack.HandleAsync(pack.Id, default));
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        Assert.Equal(SpritePhase.Completed, job.Sprites.Phase);
        Assert.Equal(frozen.ExportId, job.Sprites.Exports.Single().Id);
        Assert.Equal(frozen.ExportId, job.Sprites.CompletedExportId);
        Assert.Null(job.FailureReason);
        Assert.Equal(count, job.Tasks.Count);
        Assert.Equal(0, imageCalls);
        Assert.Empty(f.Prompts.Queries);
    }

    [Fact]
    public async Task BasesApproval_NormalizesSelectionOrderAndAssetApprovalRequiresAllFrames()
    {
        var f = new PipelineFixture();
        var job = await Plan(f, 2);
        var plans = job.Sprites!.Assets.Select(a => a.Plan with { Loop = true, FrameCount = 4 }).ToArray();
        Assert.True((await f.SpriteCommands.UpdatePlanAsync(Context(job), plans, default)).IsSuccess);
        var bases = await f.SpriteCommands.ApprovePlanAsync(Context(job), default);
        await FinishFrames(f, job, bases.Value!.TaskIds);
        var context = Context(job);
        var ids = plans.Select(p => p.Id).ToArray();
        var frames = await f.SpriteCommands.ApproveBasesAsync(context, ids, default);
        Assert.Equal(6, frames.Value!.TaskIds.Count);
        Assert.Equal(frames.Value.TaskIds, (await f.SpriteCommands.ApproveBasesAsync(context, ids.Reverse().ToArray(), default)).Value!.TaskIds);
        Assert.Equal(ErrorCode.SpriteNotReady, (await f.SpriteCommands.ApproveAssetAsync(Context(job), ids[0], default)).ErrorCode);
        await FinishFrames(f, job, frames.Value.TaskIds);
        var approval = await f.SpriteCommands.ApproveAssetAsync(Context(job), ids[0], default);
        Assert.True(approval.IsSuccess);
        Assert.NotNull(job.Sprites.Assets.First(a => a.Id == ids[0]).Approval);
        Assert.Equal(ErrorCode.SpriteNotReady, (await f.SpriteCommands.ExportAsync(Context(job), ids, default)).ErrorCode);
        var exported = await f.SpriteCommands.ExportAsync(Context(job), [ids[0]], default);
        Assert.True(exported.IsSuccess);
    }

    [Fact]
    public async Task DeleteJob_RemovesSpritePngAndZip()
    {
        var f = new PipelineFixture();
        var job = await Ready(f);
        var result = await f.SpriteCommands.ExportAsync(Context(job), [job.Sprites!.Assets[0].Id], default);
        await f.RunSpritePack.HandleAsync(result.Value!.TaskIds.Single(), default);
        Assert.Equal(3, f.Blobs.Count);
        Assert.True((await f.Delete.HandleAsync(job.Id, default)).IsSuccess);
        Assert.Equal(0, f.Blobs.Count);
    }

    [Fact]
    public async Task DeleteJob_PreservesSharedUploadAfterRemovingOwnPngAndZip()
    {
        var f = new PipelineFixture();
        var job = await Ready(f);
        var shared = PipelineJob.CreateSprites(job.SourceImageId, job.ImageProviderConfigId!.Value, job.ImageModel!,
            job.Sprites!.Settings, job.Sprites.SourceCanvas, job.Sprites.GenerationCanvas, f.Clock.Now).Value!;
        await f.Jobs.AddAsync(shared, default);
        var result = await f.SpriteCommands.ExportAsync(Context(job), [job.Sprites.Assets[0].Id], default);
        await f.RunSpritePack.HandleAsync(result.Value!.TaskIds.Single(), default);
        Assert.Equal(3, f.Blobs.Count);
        Assert.True((await f.Delete.HandleAsync(job.Id, default)).IsSuccess);
        Assert.Equal(1, f.Blobs.Count);
        var upload = (await f.Images.GetAsync(shared.SourceImageId, default))!;
        await using var readable = await f.Blobs.OpenReadAsync(upload.BlobKey, default);
        Assert.True(readable.Length > 0);
        shared.Cancel(f.Clock.Now);
        Assert.True((await f.Delete.HandleAsync(shared.Id, default)).IsSuccess);
        Assert.Equal(0, f.Blobs.Count);
    }

    internal static SpriteCommandContext Context(PipelineJob job) => new(job.Id, Guid.NewGuid(), job.Sprites!.ReviewRevision);
    internal static async Task<PipelineJob> Plan(PipelineFixture f, int count = 1)
    {
        var command = await SpriteAnalysisTests.Prepare(f);
        var job = PipelineJob.CreateSprites(command.UploadId!.Value, command.ImageProviderConfigId, command.ImageModel,
            command.Settings, new(24, 16), new(1536, 1024), f.Clock.Now).Value!;
        await f.Jobs.AddAsync(job, default);
        var plans = Enumerable.Range(0, count).Select(i => new SpriteAssetPlan(Guid.NewGuid(), "대상" + i, i,
            new(0, 0, 1, 1), i > 0)).ToArray();
        Assert.True((await f.SpriteCommands.UpdatePlanAsync(Context(job), plans, default)).IsSuccess);
        return job;
    }
    internal static async Task<PipelineJob> Ready(PipelineFixture f, int count = 1)
    {
        var job = await Plan(f, count);
        var receipt = await f.SpriteCommands.ApprovePlanAsync(Context(job), default);
        Assert.True(receipt.IsSuccess, receipt.ErrorMessage);
        await FinishFrames(f, job, receipt.Value!.TaskIds);
        Assert.True((await f.SpriteCommands.ApproveBasesAsync(Context(job), job.Sprites!.Assets.Select(a => a.Id).ToArray(), default)).IsSuccess);
        return job;
    }
    private static async Task FinishFrames(PipelineFixture f, PipelineJob job, IReadOnlyList<Guid> taskIds)
    {
        var source = (await f.Images.GetAsync(job.SourceImageId, default))!;
        foreach (var id in taskIds)
        {
            var task = job.Tasks.Single(t => t.Id == id);
            task.Claim(f.Clock.Now, f.Options.Lease);
            task.Succeed(f.Clock.Now);
            await using var image = await f.Blobs.OpenReadAsync(source.BlobKey, default);
            var key = await f.Blobs.SaveAsync(image, "image/png", default);
            Assert.True(job.TryAttachSpriteImage(SpriteImage.Create(id, task.SpriteInput!, key, f.Clock.Now)));
        }
        job.ReconcileFromTasks(f.Clock.Now);
    }
    private sealed class DelayedAnalysis : ILlmProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(ct);
            return new(SpriteAnalysisTests.PlanJson, null, null);
        }
    }
    internal sealed class FailingPackageSave(IBlobStorage inner) : IBlobStorage
    {
        public Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct)
            => throw new IOException("injected package blob failure");
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => inner.OpenReadAsync(key, ct);
        public Task DeleteAsync(string key, CancellationToken ct) => inner.DeleteAsync(key, ct);
    }
    private sealed class MutatingBlob(IBlobStorage inner, Action mutation) : IBlobStorage
    {
        public Stream? Content { get; private set; }
        public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct)
        {
            Content = content;
            var key = await inner.SaveAsync(content, contentType, ct);
            mutation();
            return key;
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => inner.OpenReadAsync(key, ct);
        public Task DeleteAsync(string key, CancellationToken ct) => inner.DeleteAsync(key, ct);
    }

}
