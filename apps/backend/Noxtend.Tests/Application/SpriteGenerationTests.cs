using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;
using Noxtend.Application.Sprites;
using Noxtend.Application.Job;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Infrastructure.Llm;
using Noxtend.Tuning.Domain.Call;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Prompt;
using Noxtend.Domain.Sprites;
using Noxtend.Infrastructure.Image;
using SkiaSharp;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Application;

public sealed class SpriteGenerationTests
{
    [Fact]
    public async Task BaseGeneration_DoesNotStartLoopFramesBeforeApproval()
    {
        var f = new PipelineFixture();
        var job = await Prepare(f);
        var tasks = job.PlanSpriteFrames(job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value!, Guid.NewGuid());
        var task = Assert.Single(tasks);
        Assert.Equal(8, (int)task.Kind);
        Assert.Null(task.ViewDirection);
        Assert.Equal(RunTaskOutcome.Succeeded, await f.RunSpriteGeneration.HandleAsync(task.Id, default));
        Assert.Equal(SpritePhase.BaseReview, job.Sprites.Phase);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Single(job.Tasks);
        Assert.Single(job.Sprites.Images);
        Assert.All(job.Sprites.Assets.Single().Frames.Skip(1), frame => Assert.Null(frame.CurrentTaskId));
    }

    [Fact]
    public async Task EightFrameLoop_UsesFrozenBaseAndPhasesZeroThroughSevenEighths()
    {
        var provider = new CapturingProvider();
        var f = new PipelineFixture(imageProvider: provider);
        var job = await Prepare(f);
        var baseTask = Assert.Single(job.PlanSpriteFrames(job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value!, Guid.NewGuid()));
        await f.RunSpriteGeneration.HandleAsync(baseTask.Id, default);
        var asset = job.Sprites.Assets.Single();
        var baseId = asset.Frames[0].CurrentImageId;
        var tasks = job.PlanSpriteFrames(job.ApproveSpriteBases([asset.Plan.Id], job.Sprites.ReviewRevision).Value!, Guid.NewGuid());
        Assert.Equal(7, tasks.Count);
        var oldPlan = asset.Plan;
        Assert.True(job.ReplaceSpritePlan([oldPlan with { Name = "새 이름", Fps = 10 }], job.Sprites.ReviewRevision).IsSuccess);
        foreach (var task in tasks)
        {
            Assert.Equal(baseId, task.SpriteInput!.BaseImageId);
            Assert.Equal(oldPlan, task.SpriteInput.Plan);
            Assert.Equal(RunTaskOutcome.Succeeded, await f.RunSpriteGeneration.HandleAsync(task.Id, default));
        }
        var phases = provider.Requests.Select(r => JsonDocument.Parse(r.Prompt.Split('\n')[3]).RootElement.GetProperty("phase").GetDouble());
        Assert.Equal(new[] { 0d, .125, .25, .375, .5, .625, .75, .875 }, phases);
        Assert.Single(provider.Requests[0].Reference);
        Assert.All(provider.Requests.Skip(1), r =>
        {
            Assert.Equal(new[] { ReferenceRole.Original, ReferenceRole.SpriteBase }, r.Reference.Select(x => x.Role));
            Assert.Equal(provider.Requests[1].Reference[1].Content.Bytes, r.Reference[1].Content.Bytes);
            Assert.Contains("{{sourceCanvas}}", r.Prompt);
            Assert.Equal("배경", JsonDocument.Parse(r.Prompt.Split('\n')[2]).RootElement.GetProperty("name").GetString());
            Assert.DoesNotContain("새 이름", r.Prompt);
        });
        Assert.Equal(8, job.Sprites.Images.Count);
        Assert.Equal(8, asset.Frames.Select(x => x.CurrentImageId).Distinct().Count());
    }

    [Fact]
    public async Task StaleFrame_DoesNotReplaceCurrentSlot()
    {
        var f = new PipelineFixture();
        var job = await Prepare(f);
        var old = Assert.Single(job.PlanSpriteFrames(job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value!, Guid.NewGuid()));
        var input = job.RegenerateSpriteFrame(old.SpriteInput!.AssetId, 0, job.Sprites.ReviewRevision).Value!;
        var current = Assert.Single(job.PlanSpriteFrames([input], Guid.NewGuid()));
        await f.RunSpriteGeneration.HandleAsync(current.Id, default);
        var oldCurrentImageId = job.Sprites.Assets.Single().Frames[0].CurrentImageId;
        Assert.False(job.TryAttachSpriteImage(SpriteImage.Create(old.Id, old.SpriteInput, "late.png", f.Clock.Now)));
        Assert.False(job.TryAttachSpriteImage(SpriteImage.Create(current.Id, input with { PlanRevision = 900 }, "wrong.png", f.Clock.Now)));
        Assert.Equal(oldCurrentImageId, job.Sprites.Assets.Single().Frames[0].CurrentImageId);
    }

    [Fact]
    public async Task AuthenticationQuotaAndDecodeOverflow_DoNotRetry()
    {
        foreach (var provider in new[]
        {
            FakeImageProvider.Failing(new ProviderCallFailedException("401", isTransient: false)),
            FakeImageProvider.Failing(new ProviderCallFailedException("quota", isTransient: false)),
            FakeImageProvider.Returning(Png(4097, 4097)),
            FakeImageProvider.Returning(Png(1, 1)),
            FakeImageProvider.Returning(Png(64, 64), "image/jpeg"),
            FakeImageProvider.Returning(Png(64, 64, SKEncodedImageFormat.Jpeg)),
            FakeImageProvider.Returning([1, 2, 3]),
        })
        {
            var f = new PipelineFixture(imageProvider: provider);
            var job = await Prepare(f);
            var task = Assert.Single(job.PlanSpriteFrames(job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value!, Guid.NewGuid()));
            await f.RunSpriteGeneration.HandleAsync(task.Id, default);
            Assert.Equal(TaskStatus.Failed, task.Status);
            Assert.Equal(1, task.AttemptCount);
            Assert.Empty(job.Sprites.Images);
            Assert.Equal(1, f.Blobs.Count);
        }
    }

    [Fact]
    public async Task SpriteRecording_UsesSpriteOperationAndNoImageBytes()
    {
        var recorder = new Recorder();
        var capture = new CapturingProvider();
        var provider = new RecordingImageProvider(capture, recorder, NullLogger<RecordingImageProvider>.Instance);
        var f = new PipelineFixture(imageProvider: provider);
        var job = await Prepare(f);
        var task = Assert.Single(job.PlanSpriteFrames(job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value!, Guid.NewGuid()));
        await f.RunSpriteGeneration.HandleAsync(task.Id, default);
        var recorded = Assert.Single(recorder.Entries);
        Assert.Equal(LlmOperationKind.GenerateSprite, recorded.Context.Kind);
        Assert.Equal(6, (int)recorded.Context.Kind);
        Assert.DoesNotContain(Convert.ToBase64String(capture.Requests[0].Reference[0].Content.Bytes), recorded.RequestPayload);
        Assert.Null(capture.Requests[0].Context.PartId);
        Assert.Equal(ImageBackground.Opaque, capture.Requests[0].Background);
        Assert.Equal(new[] { "asset", "frame", "outputCanvas", "settings", "sourceCanvas" }, PromptTemplate.AllowedVariables(LlmOperationKind.GenerateSprite).Order());
    }

    [Fact]
    public async Task RateLimit429_RetriesWithBackoffAndSharesGateWith3D()
    {
        var limited = new PipelineFixture(imageProvider: FakeImageProvider.Failing(new ProviderCallFailedException("429", isTransient: true)));
        var job = await Prepare(limited);
        var task = Assert.Single(job.PlanSpriteFrames(job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value!, Guid.NewGuid()));
        Assert.Equal(RunTaskOutcome.Failed, await limited.RunSpriteGeneration.HandleAsync(task.Id, default));
        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Equal(1, task.AttemptCount);
        Assert.True(task.NotBefore > limited.Clock.Now);

        var provider = new HeadersProvider();
        var f = new PipelineFixture(imageProvider: provider);
        var threeD = await f.FanOutAsync();
        var sourceTask = threeD.Tasks.First(t => t.Kind == TaskKind.Generate);
        await f.RunGeneration.HandleAsync(sourceTask.Id, default);
        var command = await SpriteAnalysisTests.Prepare(f);
        var sprites = PipelineJob.CreateSprites(command.UploadId!.Value, sourceTask.ProviderConfigId!.Value, sourceTask.Model!,
            command.Settings, new(24, 16), new(64, 64), f.Clock.Now).Value!;
        sprites.ReplaceSpritePlan([new(Guid.NewGuid(), "배경", 0, new(0, 0, 1, 1), false)], 0);
        await f.Jobs.AddAsync(sprites, default);
        var spriteTask = Assert.Single(sprites.PlanSpriteFrames(sprites.ApproveSpritePlan(sprites.Sprites!.ReviewRevision).Value!, Guid.NewGuid()));
        var before = DateTimeOffset.UtcNow;
        await f.RunSpriteGeneration.HandleAsync(spriteTask.Id, default);
        Assert.True(DateTimeOffset.UtcNow - before >= TimeSpan.FromMilliseconds(150));
        var status = await f.RateLimiter.GetStatusAsync(sourceTask.ProviderConfigId.Value, default);
        Assert.Equal(0, status!.RemainingRequests);
        Assert.Equal(1500, status.RemainingTokens);
        Assert.Equal(f.Clock.Now + TimeSpan.FromMilliseconds(200), status.ResetsAt);
        Assert.Equal(f.Clock.Now + TimeSpan.FromMilliseconds(200), status.TokensResetAt);
        Assert.Equal(2, provider.Calls);
    }

    private sealed class HeadersProvider : IImageProvider
    {
        public int Calls { get; private set; }
        public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct)
        {
            Calls++;
            return (await FakeImageProvider.Succeeding().GenerateAsync(request, ct)) with
            {
                RateLimitRemainingRequests = 0, RateLimitResetAfter = TimeSpan.FromMilliseconds(200),
                RateLimitRemainingTokens = 1500, RateLimitResetTokensAfter = TimeSpan.FromMilliseconds(200),
            };
        }
    }

    internal static async Task<PipelineJob> Prepare(PipelineFixture f, int assets = 1)
    {
        var command = await SpriteAnalysisTests.Prepare(f);
        var job = PipelineJob.CreateSprites(command.UploadId!.Value, command.ImageProviderConfigId, command.ImageModel,
            command.Settings, new(24, 16), new(64, 64), f.Clock.Now).Value!;
        var plans = Enumerable.Range(0, assets).Select(i => new SpriteAssetPlan(Guid.NewGuid(), "배경", i,
            new(0, 0, 1, 1), i > 0, true, MotionNotes: "{{sourceCanvas}}")).ToArray();
        Assert.True(job.ReplaceSpritePlan(plans, 0).IsSuccess);
        await f.Jobs.AddAsync(job, default);
        return job;
    }

    internal static byte[] Png(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.Transparent);
        bitmap.SetPixel(0, 0, SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        return data.ToArray();
    }

    private sealed class CapturingProvider : IImageProvider
    {
        public List<ImageRequest> Requests { get; } = [];
        public Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return FakeImageProvider.Succeeding().GenerateAsync(request, ct);
        }
    }
    private sealed class Recorder : ILlmCallRecorder
    {
        public List<LlmCallEntry> Entries { get; } = [];
        public Task RecordAsync(LlmCallEntry entry, CancellationToken ct) { Entries.Add(entry); return Task.CompletedTask; }
    }
}

[Collection(Noxtend.Tests.Infrastructure.SqlServerCollection.Name)]
public sealed class SpriteGenerationTestsSql(Noxtend.Tests.Infrastructure.SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(SpriteGenerationTestsSql));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnershipChangesAfterFinalReload_DoNotPublishImage(bool reclaim)
    {
        var f = new PipelineFixture();
        var job = await Seed(f);
        var interceptor = new BeforeSave(async db =>
        {
            if (!db.ChangeTracker.Entries<PipelineTask>().Any(e => e.Entity.Status == TaskStatus.Succeeded)) return;
            await using var other = Context();
            var repository = new EfJobRepository(other);
            var latest = (await repository.GetAsync(job.Id, default))!;
            if (reclaim)
            {
                latest.Tasks[0].ReleaseForRetry();
                latest.Tasks[0].Claim(f.Clock.Now, TimeSpan.FromMinutes(1));
            }
            else latest.Cancel(f.Clock.Now);
            await repository.SaveChangesAsync(default);
        });
        await using var worker = Context(interceptor);
        var outcome = await Handler(f, worker).HandleAsync(job.Tasks[0].Id, default);
        Assert.Equal(reclaim ? RunTaskOutcome.Skipped : RunTaskOutcome.Canceled, outcome);
        await using var verify = Context();
        var saved = (await new EfJobRepository(verify).GetAsync(job.Id, default))!;
        Assert.Equal(reclaim ? JobStatus.Running : JobStatus.Canceled, saved.Status);
        Assert.Empty(saved.Sprites!.Images);
        Assert.Equal(1, f.Blobs.Count);
        Assert.Equal(1, interceptor.Invocations);
    }

    [Fact]
    public async Task CancelBetweenFinalRenewalAndCommit_DoesNotPublishImage()
    {
        var f = new PipelineFixture(generationOptions: new() { LeaseRenewSeconds = .2 });
        var job = await Seed(f);
        var renewed = new AfterRenewal();
        var provider = new BlockingProvider();
        await using var db = Context(renewed);
        var running = Handler(f, db, provider).HandleAsync(job.Tasks[0].Id, default);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await renewed.Saved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var other = Context();
        var repository = new EfJobRepository(other);
        var current = (await repository.GetAsync(job.Id, default))!;
        current.Cancel(f.Clock.Now);
        await repository.SaveChangesAsync(default);
        provider.Complete.SetResult();
        Assert.Equal(RunTaskOutcome.Canceled, await running);
        var saved = (await repository.ReloadAsync(job.Id, default))!;
        Assert.Empty(saved.Sprites!.Images);
        Assert.Equal(1, f.Blobs.Count);
        Assert.Equal(TaskStatus.Canceled, saved.Tasks[0].Status);
    }

    [Fact]
    public async Task ReclaimedAttempt_DoesNotPublishOrFailNewOwner()
    {
        foreach (var fail in new[] { false, true })
        {
            var f = new PipelineFixture(generationOptions: new() { LeaseRenewSeconds = .1 });
            var job = await Seed(f);
            var provider = new BlockingProvider(fail);
            await using var worker = Context();
            var running = Handler(f, worker, provider).HandleAsync(job.Tasks[0].Id, default);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var other = Context();
            var repository = new EfJobRepository(other);
            var latest = (await repository.GetAsync(job.Id, default))!;
            latest.Tasks[0].ReleaseForRetry();
            latest.Tasks[0].Claim(f.Clock.Now, TimeSpan.FromMinutes(1));
            await repository.SaveChangesAsync(default);
            var lease = latest.Tasks[0].LeaseExpiresAt;
            await provider.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            provider.Complete.SetResult();
            Assert.Equal(RunTaskOutcome.Skipped, await running);
            var saved = (await repository.ReloadAsync(job.Id, default))!;
            Assert.Equal(2, saved.Tasks[0].AttemptCount);
            Assert.Equal(TaskStatus.Running, saved.Tasks[0].Status);
            Assert.Equal(lease, saved.Tasks[0].LeaseExpiresAt);
            Assert.Empty(saved.Sprites!.Images);
        }
    }

    [Fact]
    public async Task ParallelAssets_RetryRowversionCommitWithoutRepeatingProvider()
    {
        var f = new PipelineFixture();
        var job = await Seed(f, 2);
        var reached = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Barrier(NoxtendDbContext db)
        {
            if (!db.ChangeTracker.Entries<PipelineTask>().Any(e => e.Entity.Status == TaskStatus.Succeeded)) return;
            if (Interlocked.Increment(ref reached) == 2) both.TrySetResult();
            await both.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        var first = new BlockingProvider();
        var second = new BlockingProvider();
        await using var db1 = Context(new BeforeSave(Barrier));
        await using var db2 = Context(new BeforeSave(Barrier));
        var run1 = Handler(f, db1, first).HandleAsync(job.Tasks[0].Id, default);
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var run2 = Handler(f, db2, second).HandleAsync(job.Tasks[1].Id, default);
        await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        first.Complete.SetResult();
        second.Complete.SetResult();
        Assert.All(await Task.WhenAll(run1, run2), outcome => Assert.Equal(RunTaskOutcome.Succeeded, outcome));
        await using var verify = Context();
        var saved = (await new EfJobRepository(verify).GetAsync(job.Id, default))!;
        Assert.Equal(2, saved.Sprites!.Images.Count);
        Assert.All(saved.Tasks, task => Assert.Equal(TaskStatus.Succeeded, task.Status));
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        Assert.Equal(3, f.Blobs.Count);
        Assert.Equal(JobStatus.PendingReview, saved.Status);
    }

    [Fact]
    public async Task RecordingDuringRenewal_UsesIndependentContextAndSavesOnce()
    {
        var f = new PipelineFixture(generationOptions: new() { LeaseRenewSeconds = .1 });
        var job = await Seed(f);
        var provider = new BlockingProvider();
        var renewal = new HoldReload();
        var recorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var calls = new EfLlmCallRepository(new ContextFactory(connectionString));
        var recorder = new SignalRecorder(new TuningLlmCallRecorder(calls, f.Clock), recorded);
        var recording = new RecordingImageProvider(provider, recorder, NullLogger<RecordingImageProvider>.Instance);
        await using var db = Context(renewal);
        var running = Handler(f, db, recording).HandleAsync(job.Tasks[0].Id, default);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        renewal.Armed = true;
        await renewal.Held.Task.WaitAsync(TimeSpan.FromSeconds(10));
        provider.Complete.SetResult();
        await recorded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await provider.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(running.IsCompleted);
        renewal.Release.SetResult();
        Assert.Equal(RunTaskOutcome.Succeeded, await running);
        await using var verify = Context();
        var call = Assert.Single(await verify.LlmCalls.Where(c => c.JobId == job.Id).ToListAsync());
        Assert.Equal(LlmOperationKind.GenerateSprite, call.Kind);
        Assert.True(call.Succeeded);
        var persisted = (await new EfJobRepository(verify).GetAsync(job.Id, default))!;
        Assert.Single(persisted.Sprites!.Images);
        Assert.Equal(TaskStatus.Succeeded, persisted.Tasks[0].Status);
        Assert.Equal(1, persisted.Tasks[0].AttemptCount);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task CallRepository_AddDefersSaveAndOwnsDisposableContext()
    {
        var f = new PipelineFixture();
        var job = await Seed(f);
        var calls = new EfLlmCallRepository(new ContextFactory(connectionString));
        var call = LlmCall.Success(job.Id, job.Tasks[0].Id, null, LlmOperationKind.GenerateSprite,
            Guid.NewGuid(), job.ImageProviderConfigId!.Value, job.ImageModel!, "request", "response", 1, 1, 1, f.Clock.Now);
        await calls.AddAsync(call, default);
        await using var verify = Context();
        Assert.Empty(await verify.LlmCalls.Where(c => c.JobId == job.Id).ToListAsync());
        await calls.SaveChangesAsync(default);
        Assert.Single(await verify.LlmCalls.Where(c => c.JobId == job.Id).ToListAsync());
        calls.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => calls.ListByJobAsync(job.Id, default));
    }

    [Fact]
    public async Task PublishedImage_SurvivesReconcileFailureAfterCommit()
    {
        var f = new PipelineFixture();
        var job = await Seed(f);
        await using var db = Context(new FailAfterPublication());
        await Assert.ThrowsAsync<IOException>(() => Handler(f, db).HandleAsync(job.Tasks[0].Id, default));
        await using var verify = Context();
        var saved = (await new EfJobRepository(verify).GetAsync(job.Id, default))!;
        var image = Assert.Single(saved.Sprites!.Images);
        await using var stream = await f.Blobs.OpenReadAsync(image.BlobKey, default);
        Assert.True(stream.Length > 0);
        Assert.Equal(TaskStatus.Succeeded, saved.Tasks[0].Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThreeDManualRetry_ReusedAttemptCountDoesNotAcceptOldResult(bool renew)
    {
        var f = new PipelineFixture();
        var job = await f.FanOutAsync();
        var task = job.Tasks.First(t => t.Kind == TaskKind.Generate);
        await using (var setup = Context())
        {
            await setup.Database.MigrateAsync();
            setup.StoredImages.Add((await f.Images.GetAsync(job.SourceImageId, default))!);
            setup.Jobs.Add(job);
            await setup.SaveChangesAsync();
        }
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = new HoldReload();
        await using var worker = Context(hold);
        var jobs = new EfJobRepository(worker);
        var execution = new TaskExecution(jobs, new(f.Queue, jobs, f.Clock), f.Clock, f.Scheduler, NullLogger<TaskExecution>.Instance);
        var running = execution.RunAsync(task.Id, new(TimeSpan.FromMinutes(6), renew ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromMinutes(1), 3),
            async (current, owned, ct) =>
            {
                using var registration = ct.Register(() => canceled.TrySetResult());
                started.SetResult();
                await complete.Task;
                return target => target.AttachGeneratedImage(owned.PartId!.Value, owned.Id, "old.png", "image/png", 12, f.Clock.Now);
            }, _ => null, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (renew)
        {
            hold.Armed = true;
            await hold.Held.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await using var other = Context();
        var repository = new EfJobRepository(other);
        var latest = (await repository.GetAsync(job.Id, default))!;
        var replaced = latest.Tasks.Single(t => t.Id == task.Id);
        replaced.Fail("lease expired", f.Clock.Now);
        await repository.SaveChangesAsync(default);
        Assert.True(latest.RetryOutputTask(task.Id));
        await repository.SaveChangesAsync(default);
        replaced.Claim(f.Clock.Now, TimeSpan.FromMinutes(6));
        Assert.Equal(1, replaced.AttemptCount);
        await repository.SaveChangesAsync(default);
        if (renew)
        {
            hold.Release.SetResult();
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        complete.SetResult();
        Assert.Equal(RunTaskOutcome.Skipped, await running);
        var saved = (await repository.ReloadAsync(job.Id, default))!;
        Assert.Empty(saved.GeneratedImages);
        Assert.Equal(TaskStatus.Running, saved.Tasks.Single(t => t.Id == task.Id).Status);
    }

    private async Task<PipelineJob> Seed(PipelineFixture f, int assets = 1)
    {
        var job = await SpriteGenerationTests.Prepare(f, assets);
        job.PlanSpriteFrames(job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value!, Guid.NewGuid());
        await using var db = Context();
        await db.Database.MigrateAsync();
        db.StoredImages.Add((await f.Images.GetAsync(job.SourceImageId, default))!);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private NoxtendDbContext Context(params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString).AddInterceptors(interceptors).Options);

    private static RunSpriteGenerationTaskHandler Handler(PipelineFixture f,
        NoxtendDbContext db, IImageProvider? provider = null)
    {
        var repository = new EfJobRepository(db);
        var orchestrator = new JobOrchestrator(f.Queue, repository, f.Clock);
        var execution = new TaskExecution(repository, orchestrator, f.Clock, f.Scheduler, NullLogger<TaskExecution>.Instance);
        return new(repository, new EfStoredImageRepository(db), f.Blobs,
            new PipelineFixture.StubImageProviderFactory(provider ?? FakeImageProvider.Succeeding()), f.Prompts,
            new SkiaImageTranscoder(), execution, f.Clock, f.Options,
            f.GenerationOptions, f.RateLimitGate, NullLogger<RunSpriteGenerationTaskHandler>.Instance);
    }

    private sealed class BeforeSave(Func<NoxtendDbContext, Task> action)
        : SaveChangesInterceptor
    {
        public int Invocations { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = (NoxtendDbContext)eventData.Context!;
            if (Invocations == 0 && db.ChangeTracker.Entries<PipelineTask>().Any(e => e.Entity.Status == TaskStatus.Succeeded))
            {
                Invocations++;
                await action(db);
            }
            return result;
        }
    }

    private sealed class AfterRenewal : SaveChangesInterceptor
    {
        private int saves;
        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref saves) == 2) Saved.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<NoxtendDbContext>
    {
        public NoxtendDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<NoxtendDbContext>().UseSqlServer(connectionString).Options);
    }

    private sealed class SignalRecorder(ILlmCallRecorder inner, TaskCompletionSource saved) : ILlmCallRecorder
    {
        public async Task RecordAsync(LlmCallEntry entry, CancellationToken ct)
        {
            await inner.RecordAsync(entry, ct);
            saved.SetResult();
        }
    }

    private sealed class HoldReload : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("FROM [Jobs]"))
            {
                Armed = false;
                Held.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            return result;
        }
    }

    private sealed class FailAfterPublication : SaveChangesInterceptor
    {
        private bool thrown;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!thrown && eventData.Context!.ChangeTracker.Entries<PipelineTask>().Any(e => e.Entity.Status == TaskStatus.Succeeded))
            {
                thrown = true;
                throw new IOException("saved but response lost");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class BlockingProvider(bool fail = false) : IImageProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct)
        {
            Calls++;
            ct.Register(() => Canceled.TrySetResult());
            Started.TrySetResult();
            await Complete.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (fail) throw new ProviderCallFailedException("401");
            return await FakeImageProvider.Succeeding().GenerateAsync(request, default);
        }
    }
}
