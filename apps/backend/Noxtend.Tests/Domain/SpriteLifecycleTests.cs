using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Sprites;

namespace Noxtend.Tests.Domain;

public sealed class SpriteLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StaticBaseApproval_RequiresExportBeforeSuccess()
    {
        var (job, plan) = Planned();
        Complete(job, Assert.Single(job.ApproveSpritePlan(Revision(job)).Value!));
        Assert.Empty(job.ApproveSpriteBases([plan.Id], Revision(job)).Value!);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Equal(SpritePhase.ExportReady, job.Sprites!.Phase);
        var input = job.CaptureSpriteExport([plan.Id], Revision(job)).Value!;
        var task = job.PlanTask(TaskKind.Synthesize, job.Tasks.Count);
        task.BindSpriteExport(input);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        var manifest = new SpriteManifest(1, job.Id, ProductionMode.TwoD, input, "topLeft", "pixels", []);
        Assert.True(job.TryAttachSpriteExport(SpriteExport.Create(task.Id, input, manifest, "exports/result.zip", Now)));
        task.Succeed(Now);
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public void LoopBaseApproval_PlansOnlyFramesOneThroughSeven()
    {
        var (job, plan) = Planned(loop: true);
        var image = Complete(job, Assert.Single(job.ApproveSpritePlan(Revision(job)).Value!));
        var inputs = job.ApproveSpriteBases([plan.Id], Revision(job)).Value!;
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, inputs.Select(x => x.FrameIndex));
        Assert.All(inputs, x => Assert.Equal(image.Id, x.BaseImageId));
        Assert.Equal(SpritePhase.FrameGeneration, job.Sprites!.Phase);
    }

    [Fact]
    public void GeneratingInputChange_InvalidatesOnlyAffectedAsset()
    {
        var (job, first) = Planned();
        var second = first with { Id = Guid.NewGuid(), Order = 1, RequiresTransparency = true };
        Assert.True(job.ReplaceSpritePlan([first, second], Revision(job)).IsSuccess);
        foreach (var input in job.ApproveSpritePlan(Revision(job)).Value!) Complete(job, input);
        Assert.True(job.ApproveSpriteBases([first.Id, second.Id], Revision(job)).IsSuccess);
        var oldSecond = job.Sprites!.Assets.Single(x => x.Plan.Id == second.Id);
        var imageId = oldSecond.Frames[0].CurrentImageId;
        Assert.True(job.ReplaceSpritePlan([first with { MotionNotes = "흔들림" }, second], Revision(job)).IsSuccess);
        var changed = job.Sprites.Assets.Single(x => x.Plan.Id == first.Id);
        Assert.Equal(2, changed.PlanRevision);
        Assert.Null(changed.Frames[0].CurrentImageId);
        Assert.Equal(1, oldSecond.PlanRevision);
        Assert.Equal(imageId, oldSecond.Frames[0].CurrentImageId);
        Assert.NotNull(oldSecond.Approval);
    }

    [Fact]
    public void MetadataChange_PreservesImagesAndInvalidatesExport()
    {
        var (job, plan) = Planned();
        Complete(job, Assert.Single(job.ApproveSpritePlan(Revision(job)).Value!));
        job.ApproveSpriteBases([plan.Id], Revision(job));
        var oldImages = job.Sprites!.Assets[0].Frames.Select(x => x.CurrentImageId).ToArray();
        var oldRevision = Revision(job);
        Assert.True(job.ReplaceSpritePlan([plan with { Fps = 12, Name = "바뀐 이름" }], oldRevision).IsSuccess);
        Assert.Equal(oldImages, job.Sprites.Assets[0].Frames.Select(x => x.CurrentImageId));
        Assert.Equal(1, job.Sprites.Assets[0].PlanRevision);
        Assert.True(Revision(job) > oldRevision);
        Assert.Null(job.Sprites.Assets[0].Approval);
        Assert.False(job.CaptureSpriteExport([plan.Id], Revision(job)).IsSuccess);
    }

    [Fact]
    public void Regeneration_UsesNewTaskAndPreservesImageHistory()
    {
        var (job, plan) = Planned(loop: true);
        var first = Complete(job, Assert.Single(job.ApproveSpritePlan(Revision(job)).Value!));
        foreach (var input in job.ApproveSpriteBases([plan.Id], Revision(job)).Value!) Complete(job, input);
        Assert.True(job.ApproveSpriteAsset(plan.Id, Revision(job)).IsSuccess);
        var retry = job.RegenerateSpriteFrame(plan.Id, 0, Revision(job)).Value!;
        var second = Complete(job, retry);
        Assert.NotEqual(first.TaskId, second.TaskId);
        Assert.Equal(9, job.Sprites!.Images.Count);
        Assert.Equal(second.Id, job.Sprites.Assets[0].Frames[0].CurrentImageId);
        Assert.All(job.Sprites.Assets[0].Frames.Skip(1), x => Assert.Null(x.CurrentImageId));
        Assert.Null(job.Sprites.Assets[0].Approval);
    }

    [Fact]
    public void WrongModeAndCanceled_RejectSpriteMutations()
    {
        var legacy = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);
        Assert.False(legacy.ReplaceSpritePlan([Plan()], 0).IsSuccess);
        var (job, plan) = Planned();
        job.Cancel(Now);
        Assert.Equal(ErrorCode.JobAlreadyTerminal, job.RegenerateSpriteFrame(plan.Id, 0, Revision(job)).ErrorCode);
        Assert.Equal(ErrorCode.JobAlreadyTerminal, job.ApproveSpritePlan(Revision(job)).ErrorCode);
        Assert.Equal(JobStatus.Canceled, job.Status);
    }

    [Fact]
    public void RunningGeneration_RejectsInputEditButAllowsMetadataAndFrozenResult()
    {
        var (job, plan) = Planned();
        var input = Assert.Single(job.ApproveSpritePlan(Revision(job)).Value!);
        var task = job.PlanTask(TaskKind.Generate, 0);
        job.BindSpriteFrame(task.Id, input);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        var oldRevision = Revision(job);
        Assert.Equal(ErrorCode.SpriteBusy, job.ReplaceSpritePlan([plan with { MotionNotes = "움직임" }], oldRevision).ErrorCode);
        Assert.Equal(ErrorCode.SpriteBusy, job.RegenerateSpriteFrame(plan.Id, 0, oldRevision).ErrorCode);
        Assert.Equal(oldRevision, Revision(job));
        Assert.True(job.ReplaceSpritePlan([plan with { Fps = 16 }], oldRevision).IsSuccess);
        Assert.Equal(8, task.SpriteInput!.Plan.Fps);
        Assert.True(job.IsCurrentTask(task));
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.True(job.TryAttachSpriteImage(SpriteImage.Create(task.Id, input, "image.png", Now)));
    }

    [Fact]
    public void NoOpAndStaleRevision_DoNotChangeState()
    {
        var (job, plan) = Planned();
        var revision = Revision(job);
        Assert.False(job.ReplaceSpritePlan([plan], revision).Value);
        Assert.Equal(revision, Revision(job));
        Assert.Equal(ErrorCode.SpriteRevisionConflict, job.ApproveSpritePlan(revision - 1).ErrorCode);
        Assert.Equal(revision, Revision(job));
        Assert.Empty(job.Tasks);
    }

    [Fact]
    public void ReplacingBase_RejectsOldFramesAndKeepsHistoryAfterPlanRemoval()
    {
        var (job, plan) = Planned(loop: true);
        Complete(job, Assert.Single(job.ApproveSpritePlan(Revision(job)).Value!));
        var input = job.ApproveSpriteBases([plan.Id], Revision(job)).Value![0];
        var oldTask = job.PlanTask(TaskKind.Generate, job.Tasks.Count);
        job.BindSpriteFrame(oldTask.Id, input);
        var fresh = job.RegenerateSpriteFrame(plan.Id, 0, Revision(job)).Value!;
        Assert.False(job.IsCurrentTask(oldTask));
        Assert.False(job.IsReadyToRun(oldTask, Now));
        Assert.False(job.TryAttachSpriteImage(SpriteImage.Create(oldTask.Id, input, "old.png", Now)));
        Complete(job, fresh);
        var replacement = plan with { Id = Guid.NewGuid() };
        Assert.True(job.ReplaceSpritePlan([replacement], Revision(job)).IsSuccess);
        Assert.Equal(2, job.Sprites!.Images.Count);
        Assert.DoesNotContain(job.Sprites.Assets, a => a.Plan.Id == plan.Id);
    }

    [Fact]
    public void StaleExport_BecomesHistoryWithoutSuccess()
    {
        var (job, plan) = Planned();
        Complete(job, Assert.Single(job.ApproveSpritePlan(Revision(job)).Value!));
        job.ApproveSpriteBases([plan.Id], Revision(job));
        var input = job.CaptureSpriteExport([plan.Id], Revision(job)).Value!;
        var task = job.PlanTask(TaskKind.Synthesize, job.Tasks.Count);
        task.BindSpriteExport(input);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        Assert.True(job.ReplaceSpritePlan([plan with { Fps = 10 }], Revision(job)).IsSuccess);
        task.Succeed(Now);
        var manifest = new SpriteManifest(1, job.Id, ProductionMode.TwoD, input, "topLeft", "pixels", []);
        Assert.False(job.TryAttachSpriteExport(SpriteExport.Create(task.Id, input, manifest, "old.zip", Now)));
        Assert.False(Assert.Single(job.Sprites!.Exports).IsCurrent);
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Null(job.CompletedAt);
    }

    [Fact]
    public void FailedGeneration_UsesCurrentUsableImagesForReview()
    {
        var (job, plan) = Planned();
        var second = plan with { Id = Guid.NewGuid(), Order = 1, RequiresTransparency = true };
        job.ReplaceSpritePlan([plan, second], Revision(job));
        var inputs = job.ApproveSpritePlan(Revision(job)).Value!;
        Complete(job, inputs[0]);
        var task = job.PlanTask(TaskKind.Generate, job.Tasks.Count);
        job.BindSpriteFrame(task.Id, inputs[1]);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        task.Fail("fake failure", Now);
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Equal(SpritePhase.BaseReview, job.Sprites!.Phase);
    }

    [Fact]
    public void FailedAllGeneration_CanExplicitlyRetryButCanceledCannotAttach()
    {
        var (job, plan) = Planned();
        var input = job.ApproveSpritePlan(Revision(job)).Value![0];
        var task = job.PlanTask(TaskKind.Generate, 0);
        job.BindSpriteFrame(task.Id, input);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        task.Fail("fake failure", Now);
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.Failed, job.Status);
        var retry = job.RegenerateSpriteFrame(plan.Id, 0, Revision(job));
        Assert.True(retry.IsSuccess);
        Assert.Equal(JobStatus.Running, job.Status);
        var retryTask = job.PlanTask(TaskKind.Generate, 1);
        job.BindSpriteFrame(retryTask.Id, retry.Value!);
        retryTask.Claim(Now, TimeSpan.FromMinutes(2));
        job.Cancel(Now);
        Assert.False(job.TryAttachSpriteImage(SpriteImage.Create(retryTask.Id, retry.Value!, "canceled.png", Now)));
        Assert.Empty(job.Sprites!.Images);
    }

    [Fact]
    public void InvalidSelectionAndExpiredLease_RejectWithoutMutation()
    {
        var (job, plan) = Planned();
        Assert.False(job.ApproveSpriteBases([Guid.NewGuid()], Revision(job)).IsSuccess);
        Assert.False(job.CaptureSpriteExport([plan.Id, plan.Id], Revision(job)).IsSuccess);
        Assert.False(job.RegenerateSpriteFrame(plan.Id, -1, Revision(job)).IsSuccess);
        var input = job.ApproveSpritePlan(Revision(job)).Value![0];
        var task = job.PlanTask(TaskKind.Generate, 0);
        job.BindSpriteFrame(task.Id, input);
        task.Claim(Now, TimeSpan.FromSeconds(1));
        Assert.False(job.TryAttachSpriteImage(SpriteImage.Create(task.Id, input, "expired.png", Now.AddSeconds(2))));
        Assert.Empty(job.Sprites!.Images);
    }

    [Fact]
    public void SqlModel_RetainsLegacyTaskMapping()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Noxtend.Infrastructure.Persistence.NoxtendDbContext>();
        Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions.UseSqlServer(options, "Server=localhost;Database=not-connected;Trusted_Connection=True;TrustServerCertificate=True");
        using var db = new Noxtend.Infrastructure.Persistence.NoxtendDbContext(options.Options);
        var type = db.Model.FindEntityType(typeof(PipelineTask))!;
        Assert.Null(type.FindNavigation(nameof(PipelineTask.SpriteInput)));
        Assert.Null(type.FindNavigation(nameof(PipelineTask.SpriteExportInput)));
        Assert.NotNull(type.FindProperty(nameof(PipelineTask.RequestId)));
        Assert.NotNull(type.FindProperty(nameof(PipelineTask.SpriteInput)));
        Assert.NotNull(type.FindProperty(nameof(PipelineTask.SpriteExportInput)));
        Assert.NotNull(type.FindProperty(nameof(PipelineTask.MeshInputs)));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void LoopApproval_UsesEffectiveCountAndRejectsApprovalBeforeAllFrames(int count)
    {
        var (job, original) = Planned(loop: true);
        var plan = original with { FrameCount = count };
        job.ReplaceSpritePlan([plan], Revision(job));
        Complete(job, job.ApproveSpritePlan(Revision(job)).Value![0]);
        var inputs = job.ApproveSpriteBases([plan.Id], Revision(job)).Value!;
        Assert.Equal(count - 1, inputs.Count);
        Assert.False(job.ApproveSpriteAsset(plan.Id, Revision(job)).IsSuccess);
        foreach (var input in inputs) Complete(job, input);
        Assert.True(job.ApproveSpriteAsset(plan.Id, Revision(job)).IsSuccess);
        Assert.Equal(count, job.Sprites!.Assets[0].Approval!.Snapshot.ImageIds.Count);
        var revision = Revision(job);
        Assert.False(job.ApproveSpriteAsset(plan.Id, revision).Value);
        Assert.Equal(revision, Revision(job));
    }

    [Fact]
    public void Reorder_RequiresTransparencyAndKeepsGenerationSlots()
    {
        var (job, back) = Planned();
        var front = back with { Id = Guid.NewGuid(), Order = 1, RequiresTransparency = true };
        job.ReplaceSpritePlan([back, front], Revision(job));
        foreach (var input in job.ApproveSpritePlan(Revision(job)).Value!) Complete(job, input);
        job.ApproveSpriteBases([back.Id, front.Id], Revision(job));
        var oldImages = job.Sprites!.Images.Select(i => i.Id).ToArray();
        Assert.Equal(ErrorCode.SpritePlanInvalid,
            job.ReplaceSpritePlan([back with { Order = 1 }, front with { Order = 0 }], Revision(job)).ErrorCode);
        Assert.True(job.ReplaceSpritePlan([back with { Order = 1, RequiresTransparency = true }, front with { Order = 0 }], Revision(job)).IsSuccess);
        Assert.Equal(new[] { front.Id, back.Id }, job.Sprites.Assets.Select(a => a.Plan.Id));
        Assert.Equal(oldImages, job.Sprites.Images.Select(i => i.Id));
        Assert.Equal(1, job.Sprites.Assets[0].PlanRevision);
        Assert.NotNull(job.Sprites.Assets[0].Frames[0].CurrentImageId);
        Assert.Equal(2, job.Sprites.Assets[1].PlanRevision);
    }

    [Fact]
    public void UnrelatedAssetEdit_PreservesInFlightAndCompletedExportEligibility()
    {
        var (job, first, second) = ApprovedPair();
        var captured = job.CaptureSpriteExport([first.Id], Revision(job)).Value!;
        var task = job.PlanTask(TaskKind.Synthesize, job.Tasks.Count);
        task.BindSpriteExport(captured);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        Assert.True(job.ReplaceSpritePlan([first, second with { Fps = 10 }], Revision(job)).IsSuccess);
        Assert.True(job.IsCurrentTask(task));
        Assert.NotNull(job.Sprites!.Assets[0].Approval);
        var copy = System.Text.Json.JsonSerializer.Deserialize<SpriteExportInput>(System.Text.Json.JsonSerializer.Serialize(captured))!;
        Assert.NotSame(captured.Assets, copy.Assets);
        var manifest = new SpriteManifest(1, job.Id, ProductionMode.TwoD, copy, "topLeft", "pixels", []);
        task.Succeed(Now);
        Assert.True(job.TryAttachSpriteExport(SpriteExport.Create(task.Id, copy, manifest, "subset.zip", Now)));
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        Assert.Equal(new[] { second.Id }, copy.ExcludedAssetIds);
        Assert.True(job.ReplaceSpritePlan([first, second with { Fps = 12 }], Revision(job)).IsSuccess);
        Assert.True(job.Sprites.Exports[0].IsCurrent);
        Assert.True(job.ReplaceSpritePlan([first with { Fps = 12 }, second with { Fps = 12 }], Revision(job)).IsSuccess);
        Assert.False(job.Sprites.Exports[0].IsCurrent);
    }

    [Fact]
    public void PlanMembershipChange_InvalidatesPackageAndPreservesItsHistory()
    {
        var (job, first, second) = ApprovedPair();
        var input = job.CaptureSpriteExport([first.Id], Revision(job)).Value!;
        var task = job.PlanTask(TaskKind.Synthesize, job.Tasks.Count);
        task.BindSpriteExport(input);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        var manifest = new SpriteManifest(1, job.Id, ProductionMode.TwoD, input, "topLeft", "pixels", []);
        Assert.True(job.TryAttachSpriteExport(SpriteExport.Create(task.Id, input, manifest, "subset.zip", Now)));
        task.Succeed(Now);
        Assert.True(job.ReplaceSpritePlan([first], Revision(job)).IsSuccess);
        Assert.False(job.IsCurrentTask(task));
        Assert.False(Assert.Single(job.Sprites!.Exports).IsCurrent);
        Assert.Equal(2, job.Sprites.Images.Count);
        Assert.Contains(second.Id, input.ExcludedAssetIds);
    }

    [Fact]
    public void EditingOtherAsset_DoesNotDiscardCurrentFrameResult()
    {
        var (job, plan) = Planned();
        var second = plan with { Id = Guid.NewGuid(), Order = 1, RequiresTransparency = true };
        job.ReplaceSpritePlan([plan, second], Revision(job));
        var inputs = job.ApproveSpritePlan(Revision(job)).Value!;
        var task = job.PlanTask(TaskKind.Generate, 0);
        job.BindSpriteFrame(task.Id, inputs[0]);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        job.ReplaceSpritePlan([plan, second with { MotionNotes = "흔들림" }], Revision(job));
        var newInputs = job.ApproveSpritePlan(Revision(job));
        Assert.True(newInputs.IsSuccess);
        Assert.Equal(second.Id, Assert.Single(newInputs.Value!).AssetId);
        Assert.True(job.TryAttachSpriteImage(SpriteImage.Create(task.Id, inputs[0], "current.png", Now)));
        Assert.Equal(1, job.Sprites!.Assets[0].PlanRevision);
        Assert.Equal(2, job.Sprites.Assets[1].PlanRevision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExcludedAssetChangeAndApproval_DoesNotBlockSuccessfulExport(bool generationEdit)
    {
        var (job, first, second) = ApprovedPair();
        var input = job.CaptureSpriteExport([first.Id], Revision(job)).Value!;
        var task = job.PlanTask(TaskKind.Synthesize, job.Tasks.Count);
        task.BindSpriteExport(input);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        var changed = generationEdit ? second with { MotionNotes = "움직임" } : second with { Fps = 12 };
        Assert.True(job.ReplaceSpritePlan([first, changed], Revision(job)).IsSuccess);
        if (generationEdit) Assert.Equal(SpritePhase.PlanReview, job.Sprites!.Phase);
        else Assert.True(job.ApproveSpriteAsset(second.Id, Revision(job)).IsSuccess);
        Assert.True(job.IsCurrentTask(task));
        var manifest = new SpriteManifest(1, job.Id, ProductionMode.TwoD, input, "topLeft", "pixels", []);
        task.Succeed(Now);
        Assert.True(job.TryAttachSpriteExport(SpriteExport.Create(task.Id, input, manifest, "subset.zip", Now)));
        job.ReconcileFromTasks(Now);
        Assert.True(Assert.Single(job.Sprites!.Exports).IsCurrent);
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        Assert.Equal(SpritePhase.Completed, job.Sprites.Phase);
        Assert.Equal(Now, job.CompletedAt);
    }

    [Fact]
    public void AssetApproval_PreservesSiblingCurrentGenerationStatus()
    {
        var (job, first, second) = ApprovedPair();
        job.ReplaceSpritePlan([first, second with { Fps = 12 }], Revision(job));
        var input = job.RegenerateSpriteFrame(first.Id, 0, Revision(job)).Value!;
        var task = job.PlanTask(TaskKind.Generate, job.Tasks.Count);
        job.BindSpriteFrame(task.Id, input);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        Assert.True(job.ApproveSpriteAsset(second.Id, Revision(job)).IsSuccess);
        Assert.True(job.IsCurrentTask(task));
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Running, task.Status);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(SpritePhase.BaseGeneration, job.Sprites!.Phase);
        Assert.Null(job.CompletedAt);
    }

    [Fact]
    public void StaticBaseApproval_PreservesSiblingCurrentGenerationStatus()
    {
        var (job, first) = Planned();
        var second = first with { Id = Guid.NewGuid(), Order = 1, RequiresTransparency = true };
        job.ReplaceSpritePlan([first, second], Revision(job));
        var inputs = job.ApproveSpritePlan(Revision(job)).Value!;
        Complete(job, inputs[1]);
        var task = job.PlanTask(TaskKind.Generate, job.Tasks.Count);
        job.BindSpriteFrame(task.Id, inputs[0]);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        Assert.Empty(job.ApproveSpriteBases([second.Id], Revision(job)).Value!);
        Assert.NotNull(job.Sprites!.Assets[1].Approval);
        Assert.True(job.IsCurrentTask(task));
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Running, task.Status);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(SpritePhase.BaseGeneration, job.Sprites.Phase);
        Assert.Null(job.CompletedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletedSubsetExport_DoesNotCloseExplicitlyReopenedReviewOrGeneration(bool regenerate)
    {
        var (job, first, second) = ApprovedPair();
        var input = job.CaptureSpriteExport([first.Id], Revision(job)).Value!;
        var pack = job.PlanTask(TaskKind.Synthesize, job.Tasks.Count);
        pack.BindSpriteExport(input);
        pack.Claim(Now, TimeSpan.FromMinutes(2));
        var manifest = new SpriteManifest(1, job.Id, ProductionMode.TwoD, input, "topLeft", "pixels", []);
        pack.Succeed(Now);
        Assert.True(job.TryAttachSpriteExport(SpriteExport.Create(pack.Id, input, manifest, "completed.zip", Now)));
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        if (regenerate)
        {
            var frameInput = job.RegenerateSpriteFrame(second.Id, 0, Revision(job)).Value!;
            var task = job.PlanTask(TaskKind.Generate, job.Tasks.Count);
            job.BindSpriteFrame(task.Id, frameInput);
            task.Claim(Now, TimeSpan.FromMinutes(2));
            job.ReconcileFromTasks(Now);
            Assert.Equal(JobStatus.Running, job.Status);
            task.Succeed(Now);
            Assert.True(job.TryAttachSpriteImage(SpriteImage.Create(task.Id, frameInput, "regenerated.png", Now)));
        }
        else
        {
            job.ReplaceSpritePlan([first, second with { Fps = 12 }], Revision(job));
            Assert.True(job.ApproveSpriteAsset(second.Id, Revision(job)).IsSuccess);
        }
        job.ReconcileFromTasks(Now);
        Assert.True(Assert.Single(job.Sprites!.Exports).IsCurrent);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Null(job.CompletedAt);
        Assert.Equal(input.ExportId, job.Sprites.CompletedExportId);
        var next = job.CaptureSpriteExport([first.Id], Revision(job)).Value!;
        var nextTask = job.PlanTask(TaskKind.Synthesize, job.Tasks.Count);
        nextTask.BindSpriteExport(next);
        nextTask.Claim(Now, TimeSpan.FromMinutes(2));
        nextTask.Succeed(Now);
        var nextManifest = new SpriteManifest(1, job.Id, ProductionMode.TwoD, next, "topLeft", "pixels", []);
        Assert.True(job.TryAttachSpriteExport(SpriteExport.Create(nextTask.Id, next, nextManifest, "next.zip", Now)));
        job.ReconcileFromTasks(Now);
        Assert.NotEqual(input.ExportId, next.ExportId);
        Assert.Equal(next.ExportId, job.Sprites.CompletedExportId);
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        Assert.Equal(SpritePhase.Completed, job.Sprites.Phase);
        Assert.Equal(2, job.Sprites.Exports.Count);
    }

    private static (PipelineJob Job, SpriteAssetPlan First, SpriteAssetPlan Second) ApprovedPair()
    {
        var (job, first) = Planned();
        var second = first with { Id = Guid.NewGuid(), Order = 1, RequiresTransparency = true };
        job.ReplaceSpritePlan([first, second], Revision(job));
        foreach (var input in job.ApproveSpritePlan(Revision(job)).Value!) Complete(job, input);
        job.ApproveSpriteBases([first.Id, second.Id], Revision(job));
        return (job, first, second);
    }

    private static int Revision(PipelineJob job) => job.Sprites!.ReviewRevision;
    private static SpriteAssetPlan Plan(bool loop = false) => new(Guid.NewGuid(), "배경", 0,
        new Bounds(0, 0, 1, 1), false, loop);
    private static (PipelineJob Job, SpriteAssetPlan Plan) Planned(bool loop = false)
    {
        var job = PipelineJob.CreateSprites(Guid.NewGuid(), Guid.NewGuid(), "fake",
            new(SpriteView.SideView, SpriteOutputKind.Layers), new(800, 600), new(1024, 1024), Now).Value!;
        var plan = Plan(loop);
        Assert.True(job.ReplaceSpritePlan([plan], 0).IsSuccess);
        return (job, plan);
    }
    private static SpriteImage Complete(PipelineJob job, SpriteFrameInput input)
    {
        var task = job.PlanTask(TaskKind.Generate, job.Tasks.Count);
        job.BindSpriteFrame(task.Id, input);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        var image = SpriteImage.Create(task.Id, input, "sprites/image.png", Now);
        task.Succeed(Now);
        Assert.True(job.TryAttachSpriteImage(image));
        job.ReconcileFromTasks(Now);
        return image;
    }
}
