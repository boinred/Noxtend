using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Noxtend.Domain.Upload;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Application.Pipeline;
using Noxtend.Application.Sprites;
using Noxtend.Application.Job;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Tests.Application;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Persistence.InMemory;
using Noxtend.Domain.Sprites;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;

namespace Noxtend.Tests.Infrastructure;

[Collection(SqlServerCollection.Name)]
public sealed class SpritePersistenceTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(SpritePersistenceTests));
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentAdmission_ReusesUploadOrDeletesOnlyLosingGeneratedCopy(bool generated)
    {
        var fixture = new PipelineFixture();
        var command = await SpriteAnalysisTests.Prepare(fixture);
        await using var setup = Context();
        await setup.Database.MigrateAsync();
        var upload = (await fixture.Images.GetAsync(command.UploadId!.Value, default))!;
        setup.StoredImages.Add(upload);
        string? generatedKey = null;
        if (generated)
        {
            var source = PipelineJob.CreateSprites(upload.Id, command.ImageProviderConfigId, command.ImageModel,
                command.Settings, new(24, 16), new(1536, 1024), fixture.Clock.Now).Value!;
            source.ReplaceSpritePlan(SpritePlanParser.Parse(SpriteAnalysisTests.PlanJson, command.Settings, new(24, 16)).Value!, 0);
            var input = source.ApproveSpritePlan(source.Sprites!.ReviewRevision).Value![0];
            var task = source.PlanTask(TaskKind.Generate, 0);
            source.BindSpriteFrame(task.Id, input);
            task.Claim(fixture.Clock.Now, TimeSpan.FromMinutes(2));
            task.Succeed(fixture.Clock.Now);
            await using var original = await fixture.Blobs.OpenReadAsync(upload.BlobKey, default);
            generatedKey = await fixture.Blobs.SaveAsync(original, "image/png", default);
            var image = SpriteImage.Create(task.Id, input, generatedKey, fixture.Clock.Now);
            Assert.True(source.TryAttachSpriteImage(image));
            setup.Jobs.Add(source);
            command = command with { UploadId = null, SourceJobId = source.Id, SourceGeneratedImageId = image.Id };
        }
        await setup.SaveChangesAsync();
        var blobs = new AdmissionBarrierBlob(fixture.Blobs);
        async Task<Noxtend.Domain.Common.Result<SpriteReceipt>> Submit()
        {
            await using var db = Context();
            var jobs = new EfJobRepository(db);
            var handler = new StartSpriteJobHandler(jobs, new EfStoredImageRepository(db), blobs,
                fixture.Providers, fixture.Catalog, fixture.Prompts, new SkiaImageTranscoder(),
                new JobOrchestrator(fixture.Queue, jobs, fixture.Clock), fixture.Clock, NullLogger<StartSpriteJobHandler>.Instance);
            return await handler.HandleAsync(command, default);
        }
        var receipts = await Task.WhenAll(Submit(), Submit()).WaitAsync(TimeSpan.FromSeconds(45));
        Assert.All(receipts, receipt => Assert.True(receipt.IsSuccess, receipt.ErrorMessage));
        Assert.Equal(receipts[0].Value!.JobId, receipts[1].Value!.JobId);
        setup.ChangeTracker.Clear();
        Assert.Equal(generated ? 2 : 1, await setup.Jobs.CountAsync());
        Assert.Equal(1, await setup.Jobs.SelectMany(job => job.Sprites!.Requests).CountAsync());
        Assert.Equal(generated ? 2 : 1, await setup.StoredImages.CountAsync());
        Assert.Equal(generated ? 3 : 1, fixture.Blobs.Count);
        if (generated) Assert.Single(blobs.Deleted);
        else Assert.Empty(blobs.Deleted);
        var winner = (await new EfJobRepository(setup).GetAsync(receipts[0].Value!.JobId, default))!;
        Assert.Single(winner.Tasks);
        Assert.Equal(command.RequestId, winner.Tasks[0].RequestId);
        if (generated) Assert.NotEqual(upload.Id, winner.SourceImageId);
        else Assert.Equal(upload.Id, winner.SourceImageId);
        var copy = await setup.StoredImages.SingleAsync(image => image.Id == winner.SourceImageId);
        Assert.DoesNotContain(copy.BlobKey, blobs.Deleted);
        await using var copyStream = await fixture.Blobs.OpenReadAsync(copy.BlobKey, default);
        Assert.Equal(24, (await new SkiaImageTranscoder().InspectSpriteAsync(copyStream, 12 * 1024 * 1024, 16_777_216, default)).Width);
        await using var originalInput = await fixture.Blobs.OpenReadAsync(upload.BlobKey, default);
        Assert.True(originalInput.Length > 0);
        if (generatedKey is not null)
        {
            Assert.DoesNotContain(generatedKey, blobs.Deleted);
            await using var sourceResult = await fixture.Blobs.OpenReadAsync(generatedKey, default);
            Assert.True(sourceResult.Length > 0);
        }
        var replay = await new StartSpriteJobHandler(new EfJobRepository(setup), new EfStoredImageRepository(setup), blobs,
            fixture.Providers, fixture.Catalog, fixture.Prompts, new SkiaImageTranscoder(),
            new JobOrchestrator(fixture.Queue, new EfJobRepository(setup), fixture.Clock), fixture.Clock, NullLogger<StartSpriteJobHandler>.Instance).HandleAsync(command, default);
        Assert.Equal(winner.Id, replay.Value!.JobId);
        Assert.Equal(generated ? 3 : 1, fixture.Blobs.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task UploadDeletion_RemovesInputOnlyAfterLastSpriteJob(int count)
    {
        var fixture = new PipelineFixture();
        var command = await SpriteAnalysisTests.Prepare(fixture);
        await using var db = Context();
        await db.Database.MigrateAsync();
        var upload = (await fixture.Images.GetAsync(command.UploadId!.Value, default))!;
        db.StoredImages.Add(upload);
        await db.SaveChangesAsync();
        var jobs = new EfJobRepository(db);
        var handler = new StartSpriteJobHandler(jobs, new EfStoredImageRepository(db), fixture.Blobs,
            fixture.Providers, fixture.Catalog, fixture.Prompts, new SkiaImageTranscoder(),
            new JobOrchestrator(fixture.Queue, jobs, fixture.Clock), fixture.Clock, NullLogger<StartSpriteJobHandler>.Instance);
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var result = await handler.HandleAsync(command with { RequestId = Guid.NewGuid() }, default);
            Assert.True(result.IsSuccess, result.ErrorMessage);
            ids.Add(result.Value!.JobId);
        }
        Assert.Equal(1, await db.StoredImages.CountAsync());
        Assert.Equal(1, fixture.Blobs.Count);
        var delete = new DeleteJobHandler(jobs, fixture.Blobs, fixture.MeshArtifacts,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DeleteJobHandler>.Instance);
        for (var i = 0; i < count; i++)
        {
            var job = (await jobs.GetAsync(ids[i], default))!;
            job.Cancel(fixture.Clock.Now);
            await jobs.SaveChangesAsync(default);
            Assert.True((await delete.HandleAsync(job.Id, default)).IsSuccess);
            db.ChangeTracker.Clear();
            var remaining = i + 1 < count;
            Assert.Equal(remaining ? 1 : 0, await db.StoredImages.CountAsync());
            Assert.Equal(remaining ? 1 : 0, fixture.Blobs.Count);
            if (remaining)
            {
                var survivor = (await jobs.GetAsync(ids[i + 1], default))!;
                Assert.Equal(upload.Id, survivor.SourceImageId);
                await using var readable = await fixture.Blobs.OpenReadAsync(upload.BlobKey, default);
                Assert.True(readable.Length > 0);
            }
        }
    }

    [Theory]
    [InlineData(true, false, true, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, false, false, false, true)]
    public async Task AdmissionPersistenceFailure_CleansOnlyConfirmedUncommittedCopy(
        bool failAdd, bool committed, bool canceled, bool lookupFails, bool cleanupFails)
    {
        var fixture = new PipelineFixture();
        var command = await SpriteAnalysisTests.Prepare(fixture);
        await using var setup = Context();
        await setup.Database.MigrateAsync();
        var upload = (await fixture.Images.GetAsync(command.UploadId!.Value, default))!;
        setup.StoredImages.Add(upload);
        var source = PipelineJob.CreateSprites(upload.Id, command.ImageProviderConfigId, command.ImageModel,
            command.Settings, new(24, 16), new(1536, 1024), fixture.Clock.Now).Value!;
        source.ReplaceSpritePlan(SpritePlanParser.Parse(SpriteAnalysisTests.PlanJson, command.Settings, new(24, 16)).Value!, 0);
        var input = source.ApproveSpritePlan(source.Sprites!.ReviewRevision).Value![0];
        var task = source.PlanTask(TaskKind.Generate, 0);
        source.BindSpriteFrame(task.Id, input);
        task.Claim(fixture.Clock.Now, TimeSpan.FromMinutes(2));
        task.Succeed(fixture.Clock.Now);
        await using var original = await fixture.Blobs.OpenReadAsync(upload.BlobKey, default);
        var sourceKey = await fixture.Blobs.SaveAsync(original, "image/png", default);
        var image = SpriteImage.Create(task.Id, input, sourceKey, fixture.Clock.Now);
        Assert.True(source.TryAttachSpriteImage(image));
        setup.Jobs.Add(source);
        await setup.SaveChangesAsync();
        command = command with { UploadId = null, SourceJobId = source.Id, SourceGeneratedImageId = image.Id };
        using var cancellation = new CancellationTokenSource();
        Exception failure = canceled ? new OperationCanceledException(cancellation.Token) : new IOException("injected persistence failure");
        var interceptor = new AdmissionSaveFailure(failure, committed, cancellation, canceled);
        var lookup = new AdmissionLookupFailure(interceptor, lookupFails);
        await using var db = new NoxtendDbContext(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString).AddInterceptors(interceptor, lookup).Options);
        var jobs = new EfJobRepository(db);
        IStoredImageRepository images = new EfStoredImageRepository(db);
        if (failAdd) images = new CanceledImageAdd(images, cancellation, failure);
        var logger = new AdmissionLogger();
        var blobs = new AdmissionCleanupBlob(fixture.Blobs, cleanupFails);
        var handler = new StartSpriteJobHandler(jobs, images, blobs, fixture.Providers,
            fixture.Catalog, fixture.Prompts, new SkiaImageTranscoder(),
            new JobOrchestrator(fixture.Queue, jobs, fixture.Clock), fixture.Clock, logger);
        var thrown = await Record.ExceptionAsync(() => handler.HandleAsync(command, cancellation.Token));
        Assert.Same(failure, thrown);
        Assert.Equal(!failAdd, interceptor.Reached);
        Assert.Equal(failAdd ? 0 : 1, lookup.RecoveryReads);
        Assert.Equal(committed ? 2 : 1, await setup.Jobs.CountAsync());
        Assert.Equal(committed ? 2 : 1, await setup.StoredImages.CountAsync());
        var receipt = await new EfJobRepository(setup).GetSpriteRequestAsync(command.RequestId, default);
        Assert.Equal(committed, receipt is not null);
        Assert.Equal(committed || lookupFails || cleanupFails ? 3 : 2, fixture.Blobs.Count);
        Assert.Equal(1, blobs.Saves);
        Assert.Equal(committed || lookupFails ? 0 : 1, blobs.Deletes);
        if (lookupFails || cleanupFails)
        {
            var logged = Assert.Single(logger.Errors);
            Assert.Equal(lookupFails ? "injected receipt lookup failure" : "injected cleanup failure", logged.Message);
        }
        else Assert.Empty(logger.Errors);
        await using var preservedUpload = await fixture.Blobs.OpenReadAsync(upload.BlobKey, default);
        await using var preservedSource = await fixture.Blobs.OpenReadAsync(sourceKey, default);
        Assert.True(preservedUpload.Length > 0);
        Assert.True(preservedSource.Length > 0);
        if (committed)
        {
            var saved = (await new EfJobRepository(setup).GetAsync(receipt!.JobId, default))!;
            var copy = await setup.StoredImages.SingleAsync(i => i.Id == saved.SourceImageId);
            await using var readable = await fixture.Blobs.OpenReadAsync(copy.BlobKey, default);
            Assert.Equal(24, (await new SkiaImageTranscoder().InspectSpriteAsync(readable, 12 * 1024 * 1024, 16_777_216, default)).Width);
            var replayJobs = new EfJobRepository(setup);
            var replay = await new StartSpriteJobHandler(replayJobs, new EfStoredImageRepository(setup), blobs,
                fixture.Providers, fixture.Catalog, fixture.Prompts, new SkiaImageTranscoder(),
                new JobOrchestrator(fixture.Queue, replayJobs, fixture.Clock), fixture.Clock, NullLogger<StartSpriteJobHandler>.Instance).HandleAsync(command, default);
            Assert.Equal(receipt.Receipt.JobId, replay.Value!.JobId);
            Assert.Equal(receipt.Receipt.TaskIds, replay.Value.TaskIds);
            Assert.Equal(3, fixture.Blobs.Count);
            Assert.Equal(1, blobs.Saves);
            Assert.Single(saved.Tasks);
        }
    }

    private sealed class AdmissionCleanupBlob(IBlobStorage inner, bool fail) : IBlobStorage
    {
        public int Deletes { get; private set; }
        public int Saves { get; private set; }
        public Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct)
        {
            Saves++;
            return inner.SaveAsync(content, contentType, ct);
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => inner.OpenReadAsync(key, ct);
        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Assert.Equal(CancellationToken.None, ct);
            Deletes++;
            if (fail) throw new IOException("injected cleanup failure");
            return inner.DeleteAsync(key, ct);
        }
    }

    private sealed class AdmissionLogger : ILogger<StartSpriteJobHandler>
    {
        public List<Exception> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Assert.Equal(LogLevel.Warning, logLevel);
            Errors.Add(Assert.IsAssignableFrom<Exception>(exception));
        }
    }

    private sealed class AdmissionSaveFailure(Exception failure, bool committed, CancellationTokenSource cancellation, bool canceled)
        : SaveChangesInterceptor
    {
        public bool Reached { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (committed) return ValueTask.FromResult(result);
            Reached = true;
            if (canceled) cancellation.Cancel();
            throw failure;
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken ct = default)
        {
            Reached = true;
            throw failure;
        }
    }

    private sealed class AdmissionLookupFailure(AdmissionSaveFailure save, bool fail) : DbCommandInterceptor
    {
        public int RecoveryReads { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (save.Reached && command.CommandText.Contains("SpriteRequests", StringComparison.Ordinal))
            {
                Assert.Equal(CancellationToken.None, ct);
                RecoveryReads++;
                if (fail) throw new IOException("injected receipt lookup failure");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CanceledImageAdd(IStoredImageRepository inner, CancellationTokenSource cancellation, Exception failure)
        : IStoredImageRepository
    {
        public Task AddAsync(StoredImage image, CancellationToken ct)
        {
            cancellation.Cancel();
            throw failure;
        }
        public Task<StoredImage?> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);
        public Task SaveChangesAsync(CancellationToken ct) => inner.SaveChangesAsync(ct);
    }

    [Theory]
    [InlineData(Noxtend.Domain.Llm.LlmOperationKind.AnalyzeSprites, "_SeedSpriteAnalyzePrompt")]
    [InlineData(Noxtend.Domain.Llm.LlmOperationKind.GenerateSprite, "_SeedSpriteGeneratePrompt")]
    [InlineData(Noxtend.Domain.Llm.LlmOperationKind.GenerateSpriteSource, "_SeedSpriteSourcePrompt")]
    public async Task SpritePromptMigration_AddsOnlyBackgroundAndPreservesOperatorSlot(Noxtend.Domain.Llm.LlmOperationKind kind, string suffix)
    {
        await using var db = Context();
        var target = db.Database.GetMigrations().Single(m => m.EndsWith(suffix));
        var previous = db.Database.GetMigrations().TakeWhile(m => m != target).Last();
        await db.GetService<IMigrator>().MigrateAsync(previous);
        var before = await db.PromptVersions.CountAsync();
        await db.GetService<IMigrator>().MigrateAsync(target);
        var seeded = await db.PromptVersions.SingleAsync(p => p.Kind == kind);
        Assert.Equal(AssetCategory.Background, seeded.Category);
        Assert.True(seeded.IsActive);
        Assert.Equal(before + 1, await db.PromptVersions.CountAsync());
        await db.GetService<IMigrator>().MigrateAsync(previous);
        db.ChangeTracker.Clear();
        var custom = Noxtend.Tuning.Domain.Prompt.PromptVersion.Create(
            kind, AssetCategory.Background, 1,
            "operator", "", "{}", null, Now);
        db.PromptVersions.Add(custom);
        await db.SaveChangesAsync();
        await db.GetService<IMigrator>().MigrateAsync(target);
        db.ChangeTracker.Clear();
        var preserved = await db.PromptVersions.SingleAsync(p => p.Kind == kind);
        Assert.Equal(custom.Id, preserved.Id);
        Assert.Equal("operator", preserved.System);
    }

    private sealed class AdmissionBarrierBlob(IBlobStorage inner) : IBlobStorage
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int opened;
        public System.Collections.Concurrent.ConcurrentBag<string> Deleted { get; } = [];
        public Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct) => inner.SaveAsync(content, contentType, ct);
        public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            if (Interlocked.Increment(ref opened) == 2) ready.SetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            return await inner.OpenReadAsync(key, ct);
        }
        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Deleted.Add(key);
            return inner.DeleteAsync(key, ct);
        }
    }

    [Fact]
    public async Task SpriteState_RoundTripsAllSnapshotsAndHistory()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var job = Planned();
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        var repo = new EfJobRepository(db);
        job = (await repo.ReloadAsync(job.Id, default))!;
        Assert.Equal(ProductionMode.TwoD, job.ProductionMode);
        Assert.NotNull(job.Sprites);
        var image = Complete(job, job.ApproveSpritePlan(job.Sprites.ReviewRevision).Value![0]);
        Assert.True(job.ApproveSpriteBases([image.AssetId], job.Sprites.ReviewRevision).IsSuccess);
        var export = Pack(job, [image.AssetId]);
        var request = SpriteAcceptedRequest.Create(Guid.NewGuid(), job.Id, SpriteRequestKind.Export,
            new string('a', 64), new(job.Id, job.Status, job.Sprites.ReviewRevision, job.Tasks.Select(t => t.Id).ToArray()));
        job.AcceptSpriteRequest(request);
        foreach (var task in job.Tasks) task.BindRequest(request.RequestId);
        await repo.SaveChangesAsync(default);
        job = (await repo.ReloadAsync(job.Id, default))!;
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(export.Id, job.Sprites!.CompletedExportId);
        Assert.Equal(image.Id, Assert.Single(job.Sprites.Assets).ApprovedBaseImageId);
        Assert.Equal(image.Id, Assert.Single(job.Sprites.Assets).Approval!.Snapshot.BaseImageId);
        Assert.Equal(image.BlobKey, Assert.Single(job.Sprites.Images).BlobKey);
        Assert.Equal(export.Input.ExportId, Assert.Single(job.Sprites.Exports).Manifest.Input.ExportId);
        Assert.Equal(request.Fingerprint, Assert.Single(job.Sprites.Requests).Fingerprint);
        Assert.NotNull(job.Tasks.Single(t => t.Id == image.TaskId).SpriteInput);
        Assert.NotNull(job.Tasks.Single(t => t.Id == export.TaskId).SpriteExportInput);
        Assert.All(job.Tasks, task => Assert.Equal(request.RequestId, task.RequestId));
    }

    [Fact]
    public async Task LegacyJobWithoutMesh_MigratesToThreeD()
    {
        await using var db = Context();
        var previous = db.Database.GetMigrations().TakeWhile(m => !m.EndsWith("_AddSpriteProduction")).Last();
        await db.GetService<IMigrator>().MigrateAsync(previous);
        var id = Guid.NewGuid();
        var source = Guid.NewGuid();
        const string scene = """
            {"Palette":["#FFFFFF"],"TimeOfDay":"noon","Mood":"calm","RenderingStyle":"pixel",
            "MaterialFeel":"wood","Camera":{"Type":"one-point","EyeLevel":"1m","HorizonY":0.5},
            "Light":{"Direction":"left","Temperature":"warm","ShadowHardness":"soft"},
            "Scale":{"Object":"tree","RealWorldSize":"3m"}}
            """;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Jobs (Id, Category, SourceImageId, Status, CreatedAt, RequiresReview, ReviewPhase, ReviewRevision, SceneJson)
            VALUES ({id}, 'Background', {source}, 'Pending', {Now}, 0, 'Boxes', 0, {scene})
            """);
        var taskId = Guid.NewGuid();
        var frontId = Guid.NewGuid();
        var meshInputs = $$"""{"frontImageId":"{{frontId}}"}""";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Tasks (Id, JobId, Kind, Ordinal, Status, AttemptCount, MeshInputsJson)
            VALUES ({taskId}, {id}, 'Reconstruct', 0, 'Pending', 0, {meshInputs})
            """);
        await db.Database.MigrateAsync();
        var legacy = (await new EfJobRepository(db).GetAsync(id, default))!;
        Assert.Equal(ProductionMode.ThreeD, legacy.ProductionMode);
        Assert.Null(legacy.Sprites);
        Assert.Null(legacy.MeshProviderConfigId);
        Assert.Equal("#FFFFFF", Assert.Single(legacy.Scene!.Palette).Hex);
        Assert.Equal(frontId, Assert.Single(legacy.Tasks).MeshInputs!.FrontImageId);
        Assert.Equal(meshInputs, await db.Database.SqlQuery<string>($"SELECT MeshInputsJson AS Value FROM Tasks WHERE Id = {taskId}").SingleAsync());
        Assert.Equal(scene, await db.Database.SqlQuery<string>($"SELECT SceneJson AS Value FROM Jobs WHERE Id = {id}").SingleAsync());
    }

    [Fact]
    public async Task DuplicateAssetFrameIndex_IsRejectedBySql()
    {
        var job = Planned();
        await using (var seed = Context())
        {
            await seed.Database.MigrateAsync();
            seed.Jobs.Add(job);
            await seed.SaveChangesAsync();
        }
        await using var db = Context();
        var duplicate = (SpriteFrame)Activator.CreateInstance(typeof(SpriteFrame), nonPublic: true)!;
        var entry = db.Entry(duplicate);
        entry.Property("AssetId").CurrentValue = job.Sprites!.Assets[0].Id;
        entry.Property(f => f.Index).CurrentValue = 0;
        entry.State = EntityState.Added;
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(2627, Assert.IsType<SqlException>(failure.InnerException).Number);
    }

    [Fact]
    public async Task DuplicateRequestId_RollsBackJobAndTasks()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var requestId = Guid.NewGuid();
        var first = Planned();
        Accept(first, requestId);
        db.Jobs.Add(first);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var second = Planned();
        var input = second.ApproveSpritePlan(second.Sprites!.ReviewRevision).Value![0];
        var task = second.PlanTask(TaskKind.Generate, 0);
        second.BindSpriteFrame(task.Id, input);
        Accept(second, requestId);
        db.Jobs.Add(second);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(2627, Assert.IsType<SqlException>(failure.InnerException).Number);
        db.ChangeTracker.Clear();
        Assert.False(await db.Jobs.AnyAsync(j => j.Id == second.Id));
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Tasks WHERE JobId = {second.Id}").SingleAsync());
        var found = await new EfJobRepository(db).GetSpriteRequestAsync(requestId, default);
        Assert.Equal(first.Id, found!.JobId);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ModeFilteredListAndCount_UseSamePredicate()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var first = Planned();
        var second = Planned();
        var terminal = Planned();
        terminal.Cancel(Now);
        var legacy = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);
        db.Jobs.AddRange(first, second, terminal, legacy);
        await db.SaveChangesAsync();
        Assert.Equal(JobStatus.PendingReview, first.Status);
        var memory = new InMemoryJobRepository();
        foreach (var job in new[] { first, second, terminal, legacy }) await memory.AddAsync(job, default);
        foreach (IJobRepository repo in new IJobRepository[] { new EfJobRepository(db), memory })
        {
            var page = await new ListJobsHandler(repo).HandleAsync(JobListFilter.Active, AssetCategory.Background, 1, default, ProductionMode.TwoD);
            Assert.Equal(2, page.Total);
            Assert.Equal(ProductionMode.TwoD, Assert.Single(page.Items).ProductionMode);
            Assert.Equal(1, await repo.CountAsync(JobListFilter.Terminal, null, default, ProductionMode.TwoD));
            Assert.Equal(1, await repo.CountAsync(JobListFilter.Active, null, default, ProductionMode.ThreeD));
            Assert.Equal(3, await repo.CountAsync(JobListFilter.Active, null, default));
        }
    }

    [Fact]
    public async Task GenerationEdit_ReusesSlotsAndRemovesSurplusFrames()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var job = Planned();
        var plan = job.Sprites!.Assets[0].Plan with { Loop = true, FrameCount = 8 };
        job.ReplaceSpritePlan([plan], job.Sprites.ReviewRevision);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        var repo = new EfJobRepository(db);
        job = (await repo.ReloadAsync(job.Id, default))!;
        Complete(job, job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value![0]);
        await db.SaveChangesAsync();
        plan = plan with { FrameCount = 4, MotionNotes = "바람" };
        Assert.True(job.ReplaceSpritePlan([plan], job.Sprites.ReviewRevision).IsSuccess);
        await db.SaveChangesAsync();
        job = (await repo.ReloadAsync(job.Id, default))!;
        var asset = Assert.Single(job.Sprites!.Assets);
        Assert.Equal(3, asset.PlanRevision);
        Assert.Equal(Enumerable.Range(0, 4), asset.Frames.Select(f => f.Index));
        Assert.All(asset.Frames, frame => { Assert.Null(frame.CurrentTaskId); Assert.Null(frame.CurrentImageId); });
        Assert.Single(job.Sprites.Images);
        plan = plan with { FrameCount = 8 };
        Assert.True(job.ReplaceSpritePlan([plan], job.Sprites.ReviewRevision).IsSuccess);
        await db.SaveChangesAsync();
        job = (await repo.ReloadAsync(job.Id, default))!;
        Assert.Equal(Enumerable.Range(0, 8), job.Sprites!.Assets[0].Frames.Select(f => f.Index));
    }

    [Fact]
    public async Task RemovingAsset_PreservesHistory_DeletingJobCascadesAll()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var job = Planned();
        var image = Complete(job, job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value![0]);
        job.ApproveSpriteBases([image.AssetId], job.Sprites.ReviewRevision);
        var export = Pack(job, [image.AssetId]);
        Accept(job, Guid.NewGuid());
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        var repo = new EfJobRepository(db);
        job = (await repo.ReloadAsync(job.Id, default))!;
        var replacement = job.Sprites!.Assets[0].Plan with { Id = Guid.NewGuid() };
        Assert.True(job.ReplaceSpritePlan([replacement], job.Sprites.ReviewRevision).IsSuccess);
        await db.SaveChangesAsync();
        job = (await repo.ReloadAsync(job.Id, default))!;
        Assert.Equal(image.Id, Assert.Single(job.Sprites!.Images).Id);
        Assert.Equal(export.Id, Assert.Single(job.Sprites.Exports).Id);
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM SpriteFrames WHERE AssetId = {image.AssetId}").SingleAsync());
        Assert.Null(await repo.DeleteIfTerminalAsync(job.Id, default));
        job.Cancel(Now);
        await db.SaveChangesAsync();
        var removed = (await repo.DeleteIfTerminalAsync(job.Id, default))!;
        Assert.Contains(image.BlobKey, removed.Images);
        Assert.Contains(export.BlobKey, removed.Images);
        Assert.Equal(0, await db.Database.SqlQuery<int>($"""
            SELECT (SELECT COUNT(*) FROM SpriteAssets) + (SELECT COUNT(*) FROM SpriteFrames)
                + (SELECT COUNT(*) FROM SpriteImages) + (SELECT COUNT(*) FROM SpriteExports)
                + (SELECT COUNT(*) FROM SpriteRequests) + (SELECT COUNT(*) FROM Tasks) AS Value
            """).SingleAsync());
    }

    [Fact]
    public async Task InMemoryDeletion_ReturnsSpriteBlobKeysOnce()
    {
        var job = Planned();
        var image = Complete(job, job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value![0]);
        job.ApproveSpriteBases([image.AssetId], job.Sprites.ReviewRevision);
        var export = Pack(job, [image.AssetId]);
        var repo = new InMemoryJobRepository();
        await repo.AddAsync(job, default);
        var removed = (await repo.DeleteIfTerminalAsync(job.Id, default))!;
        Assert.Equal(2, removed.Images.Count);
        Assert.Contains(image.BlobKey, removed.Images);
        Assert.Contains(export.BlobKey, removed.Images);
        Assert.Empty(removed.Meshes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedSubset_ReopenedAndReloaded_DoesNotCompleteFromOldExport(bool regenerate)
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var job = Planned();
        var first = job.Sprites!.Assets[0].Plan;
        var second = first with { Id = Guid.NewGuid(), Order = 1, RequiresTransparency = true };
        job.ReplaceSpritePlan([first, second], job.Sprites.ReviewRevision);
        foreach (var input in job.ApproveSpritePlan(job.Sprites.ReviewRevision).Value!) Complete(job, input);
        job.ApproveSpriteBases([first.Id, second.Id], job.Sprites.ReviewRevision);
        var original = Pack(job, [first.Id]);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        var repo = new EfJobRepository(db);
        job = (await repo.ReloadAsync(job.Id, default))!;
        if (regenerate)
        {
            var input = job.RegenerateSpriteFrame(second.Id, 0, job.Sprites!.ReviewRevision).Value!;
            Complete(job, input);
        }
        else
        {
            job.ReplaceSpritePlan([first, second with { Fps = 12 }], job.Sprites!.ReviewRevision);
            job.ApproveSpriteAsset(second.Id, job.Sprites.ReviewRevision);
        }
        await db.SaveChangesAsync();
        job = (await repo.ReloadAsync(job.Id, default))!;
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Null(job.CompletedAt);
        Assert.Equal(original.Id, job.Sprites!.CompletedExportId);
        Assert.True(Assert.Single(job.Sprites.Exports).IsCurrent);
        var next = Pack(job, [first.Id]);
        await db.SaveChangesAsync();
        job = (await repo.ReloadAsync(job.Id, default))!;
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        Assert.Equal(next.Id, job.Sprites!.CompletedExportId);
        Assert.NotEqual(original.Id, next.Id);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"value\":{\"width\":1,\"height\":1}}")]
    [InlineData("{\"schemaVersion\":1,\"value\":null}")]
    [InlineData("{\"schemaVersion\":1,\"value\":{\"width\":0,\"height\":1}}")]
    [InlineData("{\"schemaVersion\":1,\"value\":{\"width\":1}}")]
    public void InvalidVersionOrShape_FailsExplicitly(string json)
        => Assert.Throws<JsonException>(() => SpriteJsonSerializer.Deserialize<SpriteCanvas>(json));

    [Fact]
    public async Task CorruptedSnapshot_FailsOnDatabaseRead()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var job = Planned();
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SpriteAssets SET PlanJson = '{{}}' WHERE Id = {job.Sprites!.Assets[0].Id}");
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<JsonException>(() => new EfJobRepository(db).GetAsync(job.Id, default));
    }

    [Fact]
    public async Task StateOnlyConcurrentEdits_UseJobsRowVersion()
    {
        await using var first = Context();
        await first.Database.MigrateAsync();
        var job = Planned();
        first.Jobs.Add(job);
        await first.SaveChangesAsync();
        await using var second = Context();
        var other = (await new EfJobRepository(second).GetAsync(job.Id, default))!;
        var plan = job.Sprites!.Assets[0].Plan;
        Assert.True(job.ReplaceSpritePlan([plan with { Name = "첫 편집" }], job.Sprites.ReviewRevision).IsSuccess);
        await first.SaveChangesAsync();
        Assert.True(other.ReplaceSpritePlan([plan with { Name = "늦은 편집" }], other.Sprites!.ReviewRevision).IsSuccess);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("/absolute.png")]
    [InlineData("https://outside/image.png")]
    public async Task UnsafeBlobReference_FailsOnRead(string blobKey)
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var job = Planned();
        var image = Complete(job, job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value![0]);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SpriteImages SET BlobKey = {blobKey} WHERE Id = {image.Id}");
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<JsonException>(() => new EfJobRepository(db).GetAsync(job.Id, default));
    }

    [Fact]
    public void NullManifestFrame_FailsExplicitly()
    {
        var job = Planned();
        var image = Complete(job, job.ApproveSpritePlan(job.Sprites!.ReviewRevision).Value![0]);
        job.ApproveSpriteBases([image.AssetId], job.Sprites.ReviewRevision);
        var manifest = Pack(job, [image.AssetId]).Manifest;
        var invalid = manifest with { Assets = [manifest.Assets[0] with { Frames = [null!] }] };
        Assert.Throws<JsonException>(() => SpriteJsonSerializer.Serialize(invalid));
    }

    private static void Accept(PipelineJob job, Guid requestId)
        => job.AcceptSpriteRequest(SpriteAcceptedRequest.Create(requestId, job.Id, SpriteRequestKind.Create,
            new string('a', 64), new(job.Id, job.Status, job.Sprites!.ReviewRevision, [])));

    private static PipelineJob Planned()
    {
        var job = PipelineJob.CreateSprites(Guid.NewGuid(), Guid.NewGuid(), "fake",
            new(SpriteView.SideView, SpriteOutputKind.Layers), new(800, 600), new(1024, 1024), Now).Value!;
        Assert.True(job.ReplaceSpritePlan([new(Guid.NewGuid(), "배경", 0, new(0, 0, 1, 1), false)], 0).IsSuccess);
        return job;
    }

    private static SpriteImage Complete(PipelineJob job, SpriteFrameInput input)
    {
        var task = job.PlanTask(TaskKind.Generate, job.Tasks.Count);
        job.BindSpriteFrame(task.Id, input);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        task.Succeed(Now);
        var image = SpriteImage.Create(task.Id, input, $"sprites/{Guid.NewGuid():N}.png", Now);
        Assert.True(job.TryAttachSpriteImage(image));
        job.ReconcileFromTasks(Now);
        return image;
    }

    private static SpriteExport Pack(PipelineJob job, IReadOnlyList<Guid> ids)
    {
        var input = job.CaptureSpriteExport(ids, job.Sprites!.ReviewRevision).Value!;
        var task = job.PlanTask(TaskKind.Synthesize, job.Tasks.Count);
        task.BindSpriteExport(input);
        task.Claim(Now, TimeSpan.FromMinutes(2));
        task.Succeed(Now);
        var manifest = new SpriteManifest(1, job.Id, ProductionMode.TwoD, input, "topLeft", "pixels",
            input.Assets.Select(asset => new SpriteManifestAsset(asset, $"{asset.Id:N}/base.png",
                asset.ImageIds.Select((id, index) => new SpriteManifestFrame(id, index,
                    $"{asset.Id:N}/{index}.png", "sheet.png", 0, new(0, 0, 800, 600))).ToArray())).ToArray());
        var export = SpriteExport.Create(task.Id, input, manifest, $"sprites/{input.ExportId:N}.zip", Now);
        Assert.True(job.TryAttachSpriteExport(export));
        job.ReconcileFromTasks(Now);
        return export;
    }

    private NoxtendDbContext Context() => new(new DbContextOptionsBuilder<NoxtendDbContext>()
        .UseSqlServer(connectionString).Options);
}
