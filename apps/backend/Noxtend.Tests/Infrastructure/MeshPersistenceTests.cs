using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 3D 실행과 결과가 실제 SQL Server 에서 왕복하는가.
///
/// Design Ref: §5.5 · §9.4 · §14.6
///
/// **여기서 보는 것은 대부분 제약이다.** 중복 과금을 막는 장치가 두 겹인데(도메인 검사와
/// 유니크 인덱스), 아래층은 실제 DB 가 아니면 확인할 수 없다. 도메인만 보는 테스트는
/// 워커 둘이 각자 읽은 사본으로 동시에 계획하는 경우를 통과시킨다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class MeshPersistenceTests(SqlServerFixture sql)
{
    // xUnit 이 테스트마다 클래스를 새로 만든다 — 이 필드가 곧 "테스트당 DB 하나" 다
    private readonly string connectionString = sql.FreshDatabase(nameof(MeshPersistenceTests));

    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task MeshRun_RoundTripsWithItsFourInputs()
    {
        await Migrate();

        var run = NewRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        run.RecordPrepared(ViewDirection.Front, "file_f", "image/jpeg", Now);
        run.RecordPrepared(ViewDirection.Right, "file_r", "image/png", Now);

        await using (var db = Context())
        {
            db.MeshRuns.Add(run);
            await db.SaveChangesAsync();
        }

        await using var read = Context();
        var loaded = await read.MeshRuns.SingleAsync();

        Assert.Equal(4, loaded.Inputs.Count);
        Assert.Equal("file_f", loaded.Inputs.Single(i => i.ViewDirection == ViewDirection.Front).ProviderFileToken);
        // 아직 안 올린 방향은 비어 있어야 재개한 워커가 그것만 올린다
        Assert.Null(loaded.Inputs.Single(i => i.ViewDirection == ViewDirection.Back).ProviderFileToken);
        Assert.Equal(MeshRunStatus.Uploading, loaded.Status);
    }

    /// <summary>
    /// **외부 작업 ID 는 전역에서 하나뿐이다** (§5.5).
    ///
    /// 같은 ID 가 실행 둘에 붙으면 유료 작업 하나의 결과가 어느 쪽 것인지 알 수 없고,
    /// 둘 다 성공으로 확정하면 같은 파츠에 mesh 가 둘 생긴다.
    /// </summary>
    [Fact]
    public async Task ProviderTaskId_CannotBeSharedByTwoRuns()
    {
        await Migrate();
        var jobId = Guid.NewGuid();

        await Save(Submitted(NewRun(jobId, Guid.NewGuid(), Guid.NewGuid()), "task_same"));

        var clash = Submitted(NewRun(jobId, Guid.NewGuid(), Guid.NewGuid()), "task_same");

        await Assert.ThrowsAsync<DbUpdateException>(() => Save(clash));
    }

    /// <summary>같은 공정의 실행 번호는 겹치지 않는다 — 재시도 이력이 서로를 덮으면 안 된다.</summary>
    [Fact]
    public async Task RunNumber_IsUniquePerTask()
    {
        await Migrate();
        var taskId = Guid.NewGuid();

        await Save(NewRun(Guid.NewGuid(), taskId, Guid.NewGuid()));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => Save(NewRun(Guid.NewGuid(), taskId, Guid.NewGuid())));
    }

    /// <summary>
    /// **멱등의 아래층** (§4.4).
    ///
    /// 도메인 검사는 같은 aggregate 안에서만 유효하다. 이미지 워커 둘이 각자 읽은 사본으로
    /// 같은 파츠의 3D 를 동시에 계획하면 도메인은 둘 다 통과시키고, 여기서 막혀야 한다.
    /// </summary>
    [Fact]
    public async Task Part_CannotGetTwoReconstructTasks()
    {
        await Migrate();

        // 이 작업에는 이미 그 파츠의 3D 공정이 하나 있다
        var job = await PersistJobReadyForMesh();
        var partId = job.Parts.Single().Id;

        await using var db = Context();

        // 다른 워커가 자기 사본으로 계획해 넣는 상황 — 오케스트레이터를 거치지 않고
        // 경합의 결과만 재현한다
        var second = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [Tasks] ([Id], [JobId], [Kind], [Ordinal], [Status], [AttemptCount], [PartId])
            VALUES (NEWID(), @jobId, 'Reconstruct', 99, 'Pending', 0, @partId)
            """,
            new SqlParameter("@jobId", job.Id),
            new SqlParameter("@partId", partId)));

        Assert.IsType<SqlException>(second);
    }

    /// <summary>생성 공정은 파츠마다 넷이다 — 필터가 3D 만 걸러야 그것이 막히지 않는다.</summary>
    [Fact]
    public async Task Part_StillGetsFourGenerateTasks()
    {
        await Migrate();

        var job = await PersistJobReadyForMesh();

        await using var db = Context();
        var generates = await db.Jobs
            .AsSplitQuery()
            .Where(j => j.Id == job.Id)
            .SelectMany(j => j.Tasks)
            .CountAsync(task => task.Kind == TaskKind.Generate);

        Assert.Equal(4, generates);
    }

    [Fact]
    public async Task GeneratedMesh_RoundTripsWithTheJob()
    {
        await Migrate();

        var job = await PersistJobReadyForMesh();
        var reconstruct = job.Tasks.Single(t => t.Kind == TaskKind.Reconstruct);

        await using (var db = Context())
        {
            var tracked = await db.Jobs.AsSplitQuery().SingleAsync(j => j.Id == job.Id);
            tracked.AttachGeneratedMesh(
                reconstruct.PartId!.Value, reconstruct.Id, Guid.NewGuid(),
                MeshArtifacts.GlbAndPreview("meshes/abc/model.glb", 4_821_900, "meshes/abc/preview.png", 12_000, "image/png"),
                credits: 50, Now);
            await db.SaveChangesAsync();
        }

        await using var read = Context();
        var loaded = await read.Jobs.AsSplitQuery().SingleAsync(j => j.Id == job.Id);
        var mesh = Assert.Single(loaded.GeneratedMeshes);

        Assert.Equal("model/gltf-binary", mesh.Model.ContentType);
        Assert.True(mesh.HasPreview);
        Assert.Equal(50, mesh.CreditsConsumed);
    }

    /// <summary>
    /// 빈 산출물이 성공으로 남으면 화면은 완료를 보여주고 내려받기는 0바이트를 준다.
    /// </summary>
    [Fact]
    public async Task EmptyArtifact_IsRejectedByTheDatabase()
    {
        await Migrate();
        var meshId = await PersistMeshAsync();

        await using var db = Context();

        var failure = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [GeneratedMeshArtifacts]
              ([GeneratedMeshId], [Kind], [BlobKey], [ContentType], [SizeBytes], [CreatedAt])
            VALUES (@meshId, 'Fbx', 'meshes/x/model.fbx', 'application/octet-stream', 0,
                    SYSDATETIMEOFFSET())
            """,
            new SqlParameter("@meshId", meshId)));

        Assert.IsType<SqlException>(failure);
    }

    /// <summary>
    /// **같은 종류가 두 번 들어갈 수 없다** (§3.2).
    ///
    /// 저장소가 실행 ID 로 결정된 Blob 키를 쓰므로 재시도는 같은 자리를 덮어쓴다. 행이
    /// 둘이면 그 사실과 어긋나고, 어느 쪽이 살아 있는 Blob 인지 알 수 없게 된다.
    /// </summary>
    [Fact]
    public async Task SameArtifactKind_CannotAppearTwice()
    {
        await Migrate();
        var meshId = await PersistMeshAsync();

        await using var db = Context();

        var failure = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [GeneratedMeshArtifacts]
              ([GeneratedMeshId], [Kind], [BlobKey], [ContentType], [SizeBytes], [CreatedAt])
            VALUES (@meshId, 'Glb', 'meshes/x/other.glb', 'model/gltf-binary', 10,
                    SYSDATETIMEOFFSET())
            """,
            new SqlParameter("@meshId", meshId)));

        Assert.IsType<SqlException>(failure);
    }

    /// <summary>산출물 셋을 가진 결과 하나를 만들고 그 id 를 돌려준다.</summary>
    private async Task<Guid> PersistMeshAsync()
    {
        var job = await PersistJobReadyForMesh();
        var reconstruct = job.Tasks.Single(t => t.Kind == TaskKind.Reconstruct);

        await using var db = Context();
        var tracked = await db.Jobs.AsSplitQuery().SingleAsync(j => j.Id == job.Id);

        tracked.AttachGeneratedMesh(
            reconstruct.PartId!.Value, reconstruct.Id, Guid.NewGuid(),
            MeshArtifacts.All("meshes/abc", 4_821_900), credits: 30, Now);

        await db.SaveChangesAsync();

        return tracked.GeneratedMeshes.Single().Id;
    }

    /// <summary>
    /// 산출물 셋이 실제 SQL Server 를 왕복한다 (§11.1 D-01).
    ///
    /// **같은 컨텍스트에서 읽고 쓰면 통과한다** — `RowVersion` 사고가 그랬다. 새 컨텍스트로
    /// 다시 읽어야 매핑이 정말 맞는지 안다.
    /// </summary>
    [Fact]
    public async Task ThreeArtifacts_RoundTripThroughFreshContexts()
    {
        await Migrate();
        var meshId = await PersistMeshAsync();

        // 소유 엔티티는 소유자 없이 조회할 수 없다 — 작업을 읽고 그 안에서 고른다
        await using var read = Context();
        var job = await read.Jobs.AsSplitQuery()
            .SingleAsync(j => j.GeneratedMeshes.Any(mesh => mesh.Id == meshId));

        var loaded = job.GeneratedMeshes.Single(mesh => mesh.Id == meshId);

        Assert.Equal(3, loaded.Artifacts.Count);
        Assert.True(loaded.HasFbx);
        Assert.True(loaded.HasPreview);
        Assert.Equal("meshes/abc/model.glb", loaded.Model.BlobKey);
        Assert.Equal("application/octet-stream", loaded.Find(MeshArtifactKind.Fbx)!.ContentType);
    }

    /// <summary>
    /// 컬렉션이 다섯이 됐다 — 조인 하나로 읽으면 행이 곱해진다.
    ///
    /// 사이클 #9 에서 배치가 붙으며 실제로 API 파드가 죽었다. mesh 가 늘면서 같은 위험이
    /// 다시 커지므로, 결과가 있는 작업에서도 조회가 성립하는지 확인한다.
    /// </summary>
    [Fact]
    public async Task JobWithMeshes_LoadsWithoutCartesianExplosion()
    {
        await Migrate();
        var job = await PersistJobReadyForMesh();
        var reconstruct = job.Tasks.Single(t => t.Kind == TaskKind.Reconstruct);

        await using (var db = Context())
        {
            var tracked = await db.Jobs.AsSplitQuery().SingleAsync(j => j.Id == job.Id);
            tracked.AttachGeneratedMesh(
                reconstruct.PartId!.Value, reconstruct.Id, Guid.NewGuid(),
                MeshArtifacts.Glb("meshes/abc/model.glb", 4_821_900), null, Now);
            await db.SaveChangesAsync();
        }

        await using var read = Context();
        var loaded = await new EfJobRepository(read).GetAsync(job.Id, default);

        Assert.NotNull(loaded);
        // 곱이 나면 같은 행이 여러 번 실려 개수가 부풀어 오른다
        Assert.Single(loaded.GeneratedMeshes);
        Assert.Equal(4, loaded.GeneratedImages.Count);
        Assert.Single(loaded.Parts);
    }

    /// <summary>
    /// 같은 실행에 둘이 동시에 쓰면 나중 쓰기가 거절된다 (§5.1).
    ///
    /// **폴링 루프와 사용자 재시도가 같은 행에 쓴다.** 나중 쓰기가 앞의 것을 모르고
    /// 덮으면 저장된 file token 이나 외부 작업 ID 가 사라지고, 그러면 재기동한 워커가
    /// "아직 안 보냈다" 로 읽어 유료 작업을 하나 더 만든다.
    /// </summary>
    [Fact]
    public async Task ConcurrentCheckpoints_AreRejectedNotSilentlyOverwritten()
    {
        await Migrate();

        var run = NewRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Save(run);

        // 두 컨텍스트가 같은 행을 각자 읽는다 — 폴링 워커와 재시도 요청이 그렇다
        await using var first = Context();
        await using var second = Context();

        var a = await first.MeshRuns.SingleAsync();
        var b = await second.MeshRuns.SingleAsync();

        a.RecordPrepared(ViewDirection.Front, "file_a", "image/jpeg", Now);
        await first.SaveChangesAsync();

        b.RecordPrepared(ViewDirection.Right, "file_b", "image/jpeg", Now);

        // 조용히 덮이면 첫 쓰기가 사라진다. 예외로 드러나야 호출자가 다시 읽는다
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    /// <summary>
    /// **컨텍스트를 매번 새로 열어도 checkpoint 가 저장된다** (§6.3).
    ///
    /// 이것이 실제 경로다. 공정 실행은 리스 갱신 루프가 자기 컨텍스트를 붙들고 있어
    /// checkpoint 마다 짧은 컨텍스트를 따로 연다 — 같은 인스턴스가 여러 컨텍스트를
    /// 옮겨 다닌다.
    ///
    /// 앞선 검사들은 **한 컨텍스트 안에서** 읽고 썼다. 그러면 동시성 토큰이 추적기에
    /// 살아 있어 통과하지만, 실제 경로의 실패를 재지 못한다 — 실제로 모든 3D 실행이
    /// 첫 저장에서 죽었다.
    /// </summary>
    [Fact]
    public async Task CheckpointsSurviveAcrossFreshContexts()
    {
        await Migrate();

        var run = NewRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var repository = new EfMeshRunRepository(Contexts());

        await repository.AddAsync(run, default);

        // 방향마다 저장한다 — 실행 핸들러가 하는 그대로다
        foreach (var direction in new[]
                 { ViewDirection.Front, ViewDirection.Right, ViewDirection.Back, ViewDirection.Left })
        {
            run.RecordPrepared(direction, $"file_{direction}", "image/jpeg", Now);
            await repository.SaveAsync(run, default);
        }

        run.BeginSubmit(Now);
        await repository.SaveAsync(run, default);

        run.RecordProviderTask("task_1", Now);
        await repository.SaveAsync(run, default);

        var loaded = await repository.GetLatestByTaskAsync(run.TaskId, default);

        Assert.Equal(MeshRunStatus.Submitted, loaded!.Status);
        Assert.Equal("task_1", loaded.ProviderTaskId);
        Assert.All(loaded.Inputs, input => Assert.True(input.IsPrepared));
    }

    /// <summary>
    /// **산출물은 나중에 생긴다 — 그때도 저장돼야 한다** (§3.2).
    ///
    /// 이것은 실측에서 난 결함이다. `SaveAsync` 가 `Update` 로 그래프 전체를 `Modified`
    /// 로 표시하는데, 산출물 행은 내려받기가 끝난 뒤에야 생긴다. 없는 행을 `UPDATE` 하면
    /// 0행이 영향받고 EF 는 그것을 **동시성 충돌로 읽는다** — 3D 실행이 전부 마지막
    /// 저장에서 죽었다.
    ///
    /// 입력은 실행을 만들 때 네 행이 함께 들어가 이 문제가 없다. **산출물만 나중에
    /// 생기므로 여기만 나뉘고**, 인메모리 저장소는 EF 의 attach 규칙을 흉내내지 않아
    /// 이 결함을 통과시킨다.
    /// </summary>
    [Fact]
    public async Task ArtifactsRecordedAfterTheRunExists_AreSaved()
    {
        await Migrate();

        var run = NewRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var repository = new EfMeshRunRepository(Contexts());

        await repository.AddAsync(run, default);

        run.RecordArtifact(
            MeshArtifactKind.Glb, "meshes/abc/model.glb", "model/gltf-binary", 4_821_900, Now);
        run.RecordArtifact(
            MeshArtifactKind.Fbx, "meshes/abc/model.fbx", "application/octet-stream", 3_000_000, Now);

        // 실행 핸들러가 하는 그대로다 — 새 컨텍스트에서 저장한다
        await repository.SaveAsync(run, default);

        var loaded = await repository.GetLatestByTaskAsync(run.TaskId, default);

        Assert.Equal(2, loaded!.Artifacts.Count);
        Assert.Equal("meshes/abc/model.glb", loaded.Find(MeshArtifactKind.Glb)!.BlobKey);
        Assert.Equal("meshes/abc/model.fbx", loaded.Find(MeshArtifactKind.Fbx)!.BlobKey);
    }

    /// <summary>
    /// 같은 종류를 다시 기록하면 덮어쓴다 — 저장소가 결정된 키를 쓰므로 Blob 도 같은 자리다.
    /// </summary>
    [Fact]
    public async Task ReRecordingAnArtifact_UpdatesInsteadOfDuplicating()
    {
        await Migrate();

        var run = NewRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var repository = new EfMeshRunRepository(Contexts());

        await repository.AddAsync(run, default);

        run.RecordArtifact(MeshArtifactKind.Glb, "meshes/abc/model.glb", "model/gltf-binary", 100, Now);
        await repository.SaveAsync(run, default);

        // 내려받다 죽어 다시 시도한 경우다
        run.RecordArtifact(MeshArtifactKind.Glb, "meshes/abc/model.glb", "model/gltf-binary", 4_821_900, Now);
        await repository.SaveAsync(run, default);

        var loaded = await repository.GetLatestByTaskAsync(run.TaskId, default);

        Assert.Single(loaded!.Artifacts);
        Assert.Equal(4_821_900, loaded.Find(MeshArtifactKind.Glb)!.SizeBytes);
    }

    /// <summary>충돌이 없으면 그냥 저장된다 — 위 검사가 정상 경로를 막지 않는지 본다.</summary>
    [Fact]
    public async Task SequentialCheckpoints_Succeed()
    {
        await Migrate();

        var run = NewRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Save(run);

        await using (var db = Context())
        {
            var loaded = await db.MeshRuns.SingleAsync();
            loaded.RecordPrepared(ViewDirection.Front, "file_a", "image/jpeg", Now);
            await db.SaveChangesAsync();
        }

        await using var read = Context();
        var final = await read.MeshRuns.SingleAsync();

        Assert.Equal("file_a", final.Inputs.Single(i => i.ViewDirection == ViewDirection.Front).ProviderFileToken);
        Assert.Equal(MeshRunStatus.Uploading, final.Status);
    }

    // ─── 설정 ───

    private static MeshRun NewRun(Guid jobId, Guid taskId, Guid partId)
        => MeshRun.Start(
            jobId, taskId, partId, runNumber: 1,
            providerConfigId: Guid.NewGuid(), model: "P1-20260311",
            inputs: new MeshInputSet(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            modelSeed: 1, textureSeed: 2, now: Now);

    private static MeshRun Submitted(MeshRun run, string providerTaskId)
    {
        foreach (var (direction, _) in run.Inputs.Select(i => (i.ViewDirection, i.GeneratedImageId)))
        {
            run.RecordPrepared(direction, $"file_{direction}", "image/jpeg", Now);
        }

        run.BeginSubmit(Now);
        run.RecordProviderTask(providerTaskId, Now);
        return run;
    }

    /// <summary>이미지 넷이 성공해 3D 공정까지 계획된 작업을 저장한다.</summary>
    private async Task<PipelineJob> PersistJobReadyForMesh()
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now,
            Guid.NewGuid(), "gemini-image", Guid.NewGuid(), "P1-20260311");

        job.ApplyParts(["가로등"]);
        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToArray())
        {
            task.Claim(Now, Lease);
            job.AttachGeneratedImage(
                task.PartId!.Value, task.Id,
                $"generated/{task.ViewDirection}.png", "image/png", 2048, Now);
            task.Succeed(Now);
        }

        job.PlanReadyFollowUpTasks();

        await using var db = Context();
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        return job;
    }

    private async Task Save(MeshRun run)
    {
        await using var db = Context();
        db.MeshRuns.Add(run);
        await db.SaveChangesAsync();
    }

    private async Task Migrate()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    /// <summary>저장소가 쓰는 것과 같은 팩토리 — 호출마다 새 컨텍스트를 연다.</summary>
    private IDbContextFactory<NoxtendDbContext> Contexts() => new Factory(connectionString);

    private sealed class Factory(string connectionString) : IDbContextFactory<NoxtendDbContext>
    {
        public NoxtendDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<NoxtendDbContext>()
                .UseSqlServer(connectionString)
                .Options);
    }

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
