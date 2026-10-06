using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Application.Job;
using Noxtend.Application.Pipeline;
using Noxtend.Application.Sprites;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Sprites;
using Noxtend.Infrastructure.Image;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;
using Noxtend.Tests.Application;

namespace Noxtend.Tests.Infrastructure;

[Collection(SqlServerCollection.Name)]
public sealed class SpriteRequestConcurrencyTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(SpriteRequestConcurrencyTests));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRequest_CreatesOneTaskSet(bool distinctRequest)
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Plan(f, 2);
        await Seed(f, job);
        var context = SpriteCommandTests.Context(job);
        var barrier = new SaveBarrier();
        async Task<Result<SpriteReceipt>> Submit(Guid requestId)
        {
            await using var db = Context(barrier);
            var jobs = new EfJobRepository(db);
            return await Handler(f, jobs).ApprovePlanAsync(context with { RequestId = requestId }, default);
        }
        var results = await Task.WhenAll(Submit(context.RequestId), Submit(distinctRequest ? Guid.NewGuid() : context.RequestId))
            .WaitAsync(TimeSpan.FromSeconds(45));
        if (distinctRequest)
        {
            Assert.Single(results, r => r.IsSuccess);
            Assert.Equal(ErrorCode.SpriteRevisionConflict, results.Single(r => !r.IsSuccess).ErrorCode);
        }
        else
        {
            Assert.All(results, r => Assert.True(r.IsSuccess, r.ErrorMessage));
            Assert.Equal(results[0].Value!.JobId, results[1].Value!.JobId);
            Assert.Equal(results[0].Value!.Revision, results[1].Value!.Revision);
            Assert.Equal(results[0].Value!.TaskIds, results[1].Value!.TaskIds);
        }
        await using var verification = Context();
        var saved = (await new EfJobRepository(verification).GetAsync(job.Id, default))!;
        Assert.Equal(2, saved.Tasks.Count);
        Assert.Equal(2, saved.Sprites!.Requests.Count);
        var first = results.First(r => r.IsSuccess).Value!;
        Assert.Equal(first.TaskIds, saved.Tasks.OrderBy(t => t.Ordinal).Select(t => t.Id));
        var duplicate = await Handler(f, new EfJobRepository(verification)).ApprovePlanAsync(
            context with { RequestId = saved.Tasks[0].RequestId!.Value }, default);
        Assert.Equal(first.TaskIds, duplicate.Value!.TaskIds);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WinnerCommittedBetweenRequestAndJobRead_ReturnsSameReceipt(bool canceled, bool noOp)
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Plan(f);
        await Seed(f, job);
        var context = SpriteCommandTests.Context(job);
        var gate = new JobReadGate(job.Id);
        await using var delayed = Context(gate);
        var plans = job.Sprites!.Assets.Select(a => a.Plan).ToArray();
        var pending = noOp ? Handler(f, new EfJobRepository(delayed)).UpdatePlanAsync(context, plans, default)
            : Handler(f, new EfJobRepository(delayed)).ApprovePlanAsync(context, default);
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var winnerDb = Context();
        var winnerJobs = new EfJobRepository(winnerDb);
        var first = noOp ? await Handler(f, winnerJobs).UpdatePlanAsync(context, plans, default)
            : await Handler(f, winnerJobs).ApprovePlanAsync(context, default);
        Assert.True(first.IsSuccess);
        if (canceled)
        {
            (await winnerJobs.GetAsync(job.Id, default))!.Cancel(f.Clock.Now);
            await winnerJobs.SaveChangesAsync(default);
        }
        gate.Release.SetResult();
        var duplicate = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(duplicate.IsSuccess, duplicate.ErrorMessage);
        Assert.Equal(first.Value!.JobId, duplicate.Value!.JobId);
        Assert.Equal(first.Value.Revision, duplicate.Value.Revision);
        Assert.Equal(first.Value.TaskIds, duplicate.Value.TaskIds);
        var saved = (await new EfJobRepository(winnerDb).ReloadAsync(job.Id, default))!;
        if (noOp) Assert.Empty(saved.Tasks);
        else Assert.Single(saved.Tasks);
    }

    [Fact]
    public async Task SameRequestIdDifferentBody_ReturnsConflict()
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Plan(f);
        await Seed(f, job);
        var context = SpriteCommandTests.Context(job);
        var plan = job.Sprites!.Assets[0].Plan;
        var barrier = new SaveBarrier();
        async Task<Result<SpriteReceipt>> Submit(string name)
        {
            await using var db = Context(barrier);
            return await Handler(f, new EfJobRepository(db)).UpdatePlanAsync(context, [plan with { Name = name }], default);
        }
        var results = await Task.WhenAll(Submit("첫 계획"), Submit("다른 계획")).WaitAsync(TimeSpan.FromSeconds(45));
        Assert.Single(results, r => r.IsSuccess);
        Assert.Equal(ErrorCode.SpriteRequestConflict, results.Single(r => !r.IsSuccess).ErrorCode);
        await using var verification = Context();
        var saved = (await new EfJobRepository(verification).GetAsync(job.Id, default))!;
        Assert.Empty(saved.Tasks);
        Assert.Equal(2, saved.Sprites!.Requests.Count);
        Assert.Equal(context.ExpectedRevision + 1, saved.Sprites.ReviewRevision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackSaveResponseFailure_ProtectsCommittedBlobAndPreservesPng(bool committed)
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Ready(f);
        var receipt = await f.SpriteCommands.ExportAsync(SpriteCommandTests.Context(job), [job.Sprites!.Assets[0].Id], default);
        await Seed(f, job);
        var before = f.Blobs.Count;
        var failure = new PackSaveFailure(committed);
        await using var db = Context(failure);
        var jobs = new EfJobRepository(db);
        var orchestrator = new JobOrchestrator(f.Queue, jobs, f.Clock);
        var execution = new TaskExecution(jobs, orchestrator, f.Clock, f.Scheduler, NullLogger<TaskExecution>.Instance);
        var handler = new RunSpritePackTaskHandler(jobs, f.Blobs, new SpritePackageWriter(new SkiaImageTranscoder()),
            execution, f.Clock, f.Options, NullLogger<RunSpritePackTaskHandler>.Instance);
        await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(receipt.Value!.TaskIds.Single(), default));
        Assert.True(failure.Reached);
        await using var verification = Context();
        var saved = (await new EfJobRepository(verification).GetAsync(job.Id, default))!;
        Assert.Equal(committed ? 1 : 0, saved.Sprites!.Exports.Count);
        Assert.Equal(before + (committed ? 1 : 0), f.Blobs.Count);
        await using var png = await f.Blobs.OpenReadAsync(saved.Sprites.Images.Single().BlobKey, default);
        Assert.True(png.Length > 0);
        if (committed)
        {
            await using var zip = await f.Blobs.OpenReadAsync(saved.Sprites.Exports.Single().BlobKey, default);
            Assert.True(zip.Length > 0);
        }
    }

    [Fact]
    public async Task PackDelete_RemovesOwnPngAndZipButPreservesSharedUpload()
    {
        var f = new PipelineFixture();
        var job = await SpriteCommandTests.Ready(f);
        var receipt = await f.SpriteCommands.ExportAsync(SpriteCommandTests.Context(job), [job.Sprites!.Assets[0].Id], default);
        await Seed(f, job);
        await using var db = Context();
        var shared = PipelineJob.CreateSprites(job.SourceImageId, job.ImageProviderConfigId!.Value, job.ImageModel!,
            job.Sprites.Settings, job.Sprites.SourceCanvas, job.Sprites.GenerationCanvas, f.Clock.Now).Value!;
        db.Jobs.Add(shared);
        await db.SaveChangesAsync();
        var jobs = new EfJobRepository(db);
        var orchestrator = new JobOrchestrator(f.Queue, jobs, f.Clock);
        var execution = new TaskExecution(jobs, orchestrator, f.Clock, f.Scheduler, NullLogger<TaskExecution>.Instance);
        var handler = new RunSpritePackTaskHandler(jobs, f.Blobs, new SpritePackageWriter(new SkiaImageTranscoder()),
            execution, f.Clock, f.Options, NullLogger<RunSpritePackTaskHandler>.Instance);
        Assert.Equal(RunTaskOutcome.Succeeded, await handler.HandleAsync(receipt.Value!.TaskIds.Single(), default));
        Assert.Equal(3, f.Blobs.Count);
        var delete = new DeleteJobHandler(jobs, f.Blobs, f.MeshArtifacts, NullLogger<DeleteJobHandler>.Instance);
        Assert.True((await delete.HandleAsync(job.Id, default)).IsSuccess);
        Assert.Equal(1, f.Blobs.Count);
        Assert.Equal(1, await db.StoredImages.CountAsync());
        var upload = await db.StoredImages.SingleAsync();
        await using var source = await f.Blobs.OpenReadAsync(upload.BlobKey, default);
        Assert.True(source.Length > 0);
        var remaining = (await jobs.ReloadAsync(shared.Id, default))!;
        remaining.Cancel(f.Clock.Now);
        await jobs.SaveChangesAsync(default);
        Assert.True((await delete.HandleAsync(shared.Id, default)).IsSuccess);
        Assert.Equal(0, f.Blobs.Count);
        Assert.Equal(0, await db.StoredImages.CountAsync());
    }

    [Fact]
    public async Task ConsumedPackFailureAndRetry_ResetPersistAcrossReload()
    {
        var imageCalls = 0;
        var f = new PipelineFixture(options: new() { MaxAttempts = 1 }, imageProvider: FakeImageProvider.Throwing(() =>
        {
            imageCalls++;
            return new InvalidOperationException("pack must not generate images");
        }));
        var job = await SpriteCommandTests.Ready(f, 2);
        var receipt = await f.SpriteCommands.ExportAsync(SpriteCommandTests.Context(job), [job.Sprites!.Assets[0].Id], default);
        var taskId = receipt.Value!.TaskIds.Single();
        var frozen = job.Tasks.Single(t => t.Id == taskId).SpriteExportInput!;
        var frozenJson = JsonSerializer.Serialize(frozen);
        await Seed(f, job);
        await using var db = Context();
        var jobs = new EfJobRepository(db);
        var orchestrator = new JobOrchestrator(f.Queue, jobs, f.Clock);
        var execution = new TaskExecution(jobs, orchestrator, f.Clock, f.Scheduler, NullLogger<TaskExecution>.Instance);
        var failing = new RunSpritePackTaskHandler(jobs, new SpriteCommandTests.FailingPackageSave(f.Blobs),
            new SpritePackageWriter(new SkiaImageTranscoder()), execution, f.Clock, f.Options,
            NullLogger<RunSpritePackTaskHandler>.Instance);
        var successful = new RunSpritePackTaskHandler(jobs, f.Blobs, new SpritePackageWriter(new SkiaImageTranscoder()),
            execution, f.Clock, f.Options, NullLogger<RunSpritePackTaskHandler>.Instance);
        var retry = new RetryTaskHandler(jobs, f.MeshRuns, orchestrator);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.Equal(RunTaskOutcome.Failed, await failing.HandleAsync(taskId, default));
            await using (var check = Context())
            {
                var saved = (await new EfJobRepository(check).GetAsync(job.Id, default))!;
                Assert.Equal(frozen.ExportId, saved.Sprites!.CompletedExportId);
                Assert.Equal(JobStatus.PartiallySucceeded, saved.Status);
                Assert.Equal(SpritePhase.Packaging, saved.Sprites.Phase);
                Assert.Empty(saved.Sprites.Exports);
                Assert.Equal(frozenJson, JsonSerializer.Serialize(saved.Tasks.Single(t => t.Id == taskId).SpriteExportInput));
                await using var png = await f.Blobs.OpenReadAsync(saved.Sprites.Images[0].BlobKey, default);
                Assert.True(png.Length > 0);
            }
            Assert.True((await retry.HandleAsync(job.Id, taskId, default)).IsSuccess);
            await using (var check = Context())
            {
                var reset = (await new EfJobRepository(check).GetAsync(job.Id, default))!;
                Assert.Null(reset.Sprites!.CompletedExportId);
                Assert.Null(reset.CompletedAt);
                Assert.Null(reset.FailureReason);
                Assert.Equal(JobStatus.Pending, reset.Status);
                Assert.Equal(SpritePhase.Packaging, reset.Sprites.Phase);
                Assert.Equal(frozenJson, JsonSerializer.Serialize(reset.Tasks.Single(t => t.Id == taskId).SpriteExportInput));
            }
        }
        Assert.Equal(RunTaskOutcome.Succeeded, await successful.HandleAsync(taskId, default));
        var completed = (await jobs.ReloadAsync(job.Id, default))!;
        Assert.Equal(frozen.ExportId, completed.Sprites!.CompletedExportId);
        Assert.Equal(frozen.ExportId, completed.Sprites.Exports.Single().Id);
        Assert.Equal(SpritePhase.Completed, completed.Sprites.Phase);
        Assert.Equal(JobStatus.PartiallySucceeded, completed.Status);
        Assert.Null(completed.FailureReason);
        Assert.Equal(job.Tasks.Count, completed.Tasks.Count);
        Assert.Equal(0, imageCalls);
        Assert.Empty(f.Prompts.Queries);
    }

    private async Task Seed(PipelineFixture f, PipelineJob job)
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        db.StoredImages.Add((await f.Images.GetAsync(job.SourceImageId, default))!);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
    }
    private NoxtendDbContext Context(params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<NoxtendDbContext>().UseSqlServer(connectionString).AddInterceptors(interceptors).Options);
    private static SpriteCommandsHandler Handler(PipelineFixture f, EfJobRepository jobs)
        => new(jobs, new JobOrchestrator(f.Queue, jobs, f.Clock), f.Providers, f.Catalog);
    private sealed class JobReadGate(Guid jobId) : DbCommandInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (!Started.Task.IsCompleted && command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, jobId)))
            {
                Started.SetResult();
                await Release.Task.WaitAsync(ct);
            }
            return result;
        }
    }
    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private int calls;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref calls) > 2) return result;
            if (calls == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            return result;
        }
    }
    private sealed class PackSaveFailure(bool committed) : SaveChangesInterceptor
    {
        public bool Reached { get; private set; }
        private bool HasPackage(DbContext? db) => db!.ChangeTracker.Entries<SpriteExport>().Any();
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!committed && !Reached && HasPackage(data.Context))
            {
                Reached = true;
                throw new IOException("injected package persistence failure");
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (committed && !Reached && HasPackage(data.Context))
            {
                Reached = true;
                throw new IOException("injected package save response failure");
            }
            return ValueTask.FromResult(result);
        }
    }
}
