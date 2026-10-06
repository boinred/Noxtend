using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Noxtend.Application.Pipeline;
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
        var previous = db.Database.GetMigrations().Last(m => !m.EndsWith("_AddSpriteProduction"));
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
