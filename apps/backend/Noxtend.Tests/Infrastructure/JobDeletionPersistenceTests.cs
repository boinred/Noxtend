using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Upload;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;
using Noxtend.Tuning.Domain.Call;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 작업 완전 삭제가 실제 SQL Server 에서 되는가.
///
/// **인메모리로는 이 삭제를 검증할 수 없다.** 여기서 깨지는 것은 거의 다 제약이다 —
/// 작업을 먼저 지우면 그것을 가리키는 `MeshRuns`·`LlmCalls` 의 외래 키가 걸리고,
/// 소유 엔티티는 cascade 가 실제로 붙어 있어야 함께 지워진다. 인메모리 저장소는
/// 그 어느 것도 알지 못해 전부 통과시킨다.
///
/// 지난 사이클에서 같은 자리를 두 번 놓쳤다 — 검사가 있다는 것과 그 검사가 실제 경로를
/// 본다는 것은 다르다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class JobDeletionPersistenceTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(JobDeletionPersistenceTests));

    private static readonly DateTimeOffset Now = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// **끝난 작업은 딸린 것까지 전부 사라진다.**
    ///
    /// 목록에서만 치우면 3D 실행·비용 원장이 남아 흔적이 되고, 저장소 파일은
    /// 아무도 가리키지 않는 채로 용량을 먹는다.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesTheJobWithEverythingItOwns()
    {
        await Migrate();

        var (jobId, sourceImageId) = await SeedAsync();

        await using var db = Context();
        var blobs = await new EfJobRepository(db).DeleteIfTerminalAsync(jobId, CancellationToken.None);

        Assert.NotNull(blobs);

        await using var read = Context();
        Assert.Empty(read.Jobs);
        Assert.Empty(read.MeshRuns);
        Assert.Empty(read.LlmCalls);

        // 원본을 쓰는 작업이 더는 없다 — 업로드 행도 함께 사라진다
        Assert.Empty(read.StoredImages.Where(image => image.Id == sourceImageId));
    }

    /// <summary>
    /// **저장소가 지울 파일 키를 돌려준다.** 트랜잭션이 끝나면 무엇을 지워야 하는지
    /// 알 길이 없으므로, 커밋 전에 모아 두지 않으면 파일이 통째로 고아가 된다.
    /// </summary>
    [Fact]
    public async Task Delete_ReturnsTheBlobKeysToRemove()
    {
        await Migrate();

        var (jobId, _) = await SeedAsync();

        await using var db = Context();
        var blobs = await new EfJobRepository(db).DeleteIfTerminalAsync(jobId, CancellationToken.None);

        Assert.NotNull(blobs);
        Assert.Contains("generated/front.png", blobs!.Images);
        Assert.Contains("uploads/source.png", blobs.Images);
        Assert.Contains("meshes/part.glb", blobs.Meshes);
    }

    /// <summary>
    /// **진행 중이면 아무것도 지우지 않는다.**
    ///
    /// 판정과 삭제가 따로 돌면 그 사이에 재시도가 작업을 다시 열 수 있고, 그때 삭제가
    /// 진행되면 유료 외부 작업이 돌고 있는데 추적할 기록이 사라진다. 조건을 삭제문에
    /// 실었으므로 DB 한 번에 판정돼야 한다.
    /// </summary>
    [Fact]
    public async Task Delete_RefusesARunningJob_AndRollsBack()
    {
        await Migrate();

        var (jobId, _) = await SeedAsync(terminal: false);

        await using var db = Context();
        var blobs = await new EfJobRepository(db).DeleteIfTerminalAsync(jobId, CancellationToken.None);

        Assert.Null(blobs);

        // 롤백이 안 되면 작업만 남고 원본이 사라진 상태가 된다
        await using var read = Context();
        Assert.Single(read.Jobs);
        Assert.Single(read.StoredImages);
        Assert.Single(read.MeshRuns);
    }

    /// <summary>
    /// **원본은 공유될 수 있다.** 같은 업로드로 두 번 접수한 작업이 실제로 있다 —
    /// 확인 없이 지우면 남은 작업의 원본 이미지가 사라진다.
    /// </summary>
    [Fact]
    public async Task Delete_KeepsTheSourceImage_WhenAnotherJobStillUsesIt()
    {
        await Migrate();

        var (jobId, sourceImageId) = await SeedAsync();

        // 같은 업로드로 접수한 둘째 작업 — 이쪽은 지우지 않는다
        await using (var seed = Context())
        {
            seed.Jobs.Add(NewJob(sourceImageId));
            await seed.SaveChangesAsync();
        }

        await using var db = Context();
        var blobs = await new EfJobRepository(db).DeleteIfTerminalAsync(jobId, CancellationToken.None);

        Assert.NotNull(blobs);
        Assert.DoesNotContain("uploads/source.png", blobs!.Images);

        await using var read = Context();
        Assert.Single(read.StoredImages);
    }

    // ─── 설정 ───

    /// <summary>작업 하나 + 원본 + 생성 이미지 + 3D 실행 + 비용 원장.</summary>
    private async Task<(Guid JobId, Guid SourceImageId)> SeedAsync(bool terminal = true)
    {
        var source = StoredImage.Create("uploads/source.png", "source.png", "image/png", 1024, Now);

        await using var db = Context();
        db.StoredImages.Add(source);

        var job = NewJob(source.Id);

        // 분해까지 끝내야 파츠가 생기고, 그래야 생성 이미지를 붙일 수 있다
        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, TimeSpan.FromMinutes(1));
        decompose.Succeed(Now);
        job.ApplyParts(["석조 다리"]);
        job.PlanReadyFollowUpTasks();

        var part = job.Parts.Single();
        var generate = job.Tasks.First(
            task => task.Kind == TaskKind.Generate && task.ViewDirection == ViewDirection.Front);

        job.AttachGeneratedImage(part.Id, generate.Id, "generated/front.png", "image/png", 2048, Now);

        if (terminal)
        {
            job.Fail("STAGE_FAILED", Now);
        }

        db.Jobs.Add(job);

        // 3D 실행 — 결과물 키가 여기에만 있다
        var run = MeshRun.Start(
            job.Id, generate.Id, part.Id, runNumber: 1,
            providerConfigId: Guid.NewGuid(), model: "meshy-7",
            inputs: new MeshInputSet(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            modelSeed: 1, textureSeed: 2, now: Now);

        run.RecordArtifact(MeshArtifactKind.Glb, "meshes/part.glb", "model/gltf-binary", 4096, Now);
        db.MeshRuns.Add(run);

        // 비용 원장 — 함께 지운다고 정한 것이 실제로 지워지는지 본다
        db.LlmCalls.Add(LlmCall.Success(
            job.Id, decompose.Id, similarityEvaluationId: null, LlmOperationKind.Decompose,
            Guid.NewGuid(), Guid.NewGuid(), "gemini", "{}", "{}", 10, 20, 100, Now));

        await db.SaveChangesAsync();

        return (job.Id, source.Id);
    }

    private static PipelineJob NewJob(Guid sourceImageId)
        => PipelineJob.Create(AssetCategory.Background, sourceImageId, Now, Guid.NewGuid(), "gemini");

    private async Task Migrate()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// **운영과 같은 재시도 전략을 켠다** (`InfrastructureServiceCollectionExtensions`).
    ///
    /// 이것을 빼면 삭제가 여는 트랜잭션이 여기서는 그냥 통과하고 운영에서만 터진다 —
    /// `SqlServerRetryingExecutionStrategy` 는 사용자가 연 트랜잭션을 거절한다.
    /// 실제로 그렇게 놓쳤다. 검사가 있다는 것과 그 검사가 실제 경로를 본다는 것은 다르다.
    /// </summary>
    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options);
}
