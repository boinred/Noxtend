using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Scene.Projection;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 조립 명세 revision 이 실제 SQL Server 에서 왕복하는가.
/// Design Ref: scene-assembly §3.3 · background-similarity-tuning §4.1
///
/// **순서·유니크 제약·JSON 열은 인메모리로 검증되지 않는다.** 테이블은 행 순서를
/// 보장하지 않고(part-placements 에서 실측), 필터드 유니크 인덱스는 실제 DB 위에서만 진짜다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SceneLayoutPersistenceTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(SceneLayoutPersistenceTests));

    private static readonly DateTimeOffset Now = new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);

    /// <summary>B-09 — 왕복 + 순서 + JSON 열. 일부러 뒤섞어 넣는다 — 순서는 무작위로 맞을 수 있다.</summary>
    [Fact]
    public async Task Layout_RoundTrips_WithInstancesInPlacementOrder()
    {
        await Migrate();

        var jobId = Guid.NewGuid();
        var partId = Guid.NewGuid();

        // 배치 번호를 2·0·1 순으로 뒤섞어 넣는다
        var layout = ComposeActive(jobId, revision: 1,
            [
                new SceneInstance(partId, 2, x: 3, y: 0, z: -2, rotationY: 0, scale: 1.2),
                new SceneInstance(partId, 0, x: 1, y: 0, z: -4, rotationY: 0, scale: 2.0),
                new SceneInstance(partId, 1, x: 2, y: 0, z: -3, rotationY: 0, scale: 1.5),
            ]);

        await using (var db = Context())
        {
            await new EfSceneLayoutRepository(db).AddAsync(layout, CancellationToken.None);
        }

        await using var read = Context();
        var loaded = await new EfSceneLayoutRepository(read)
            .GetActiveByJobAsync(jobId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal([0, 1, 2], loaded!.Instances.Select(i => i.Ordinal));
        Assert.Equal(2.0, loaded.Instances[0].Scale);
        Assert.Equal(layout.SourceMeshSignature, loaded.SourceMeshSignature);
        // 수치 카메라·조명 JSON 왕복 — 평가 렌더 결정성이 이 값에 달렸다 (D-03)
        Assert.Equal(layout.Camera, loaded.Camera);
        Assert.Equal(layout.Light, loaded.Light);
    }

    /// <summary>
    /// 합성 요약 JSON 왕복 (background-scale-calibration #18 §4.7).
    ///
    /// **nullable 이 계약이다** — 사이클 #18 이전에 만들어진 행에는 이 값이 없고,
    /// 읽을 때 null 로 돌아와야 화면이 배지를 감춘다.
    /// </summary>
    [Fact]
    public async Task Composition_RoundTripsAndStaysNullForOlderRows()
    {
        await Migrate();

        var withSummary = ComposeActive(Guid.NewGuid(), 1, [Instance()],
            new CompositionSummary(18, 7, 0.487, 3.94));
        var withoutSummary = ComposeActive(Guid.NewGuid(), 1, [Instance()]);

        await using (var db = Context())
        {
            var repository = new EfSceneLayoutRepository(db);
            await repository.AddAsync(withSummary, CancellationToken.None);
            await repository.AddAsync(withoutSummary, CancellationToken.None);
        }

        await using var read = Context();
        var reader = new EfSceneLayoutRepository(read);

        var loaded = await reader.GetActiveByJobAsync(withSummary.JobId, CancellationToken.None);
        var legacy = await reader.GetActiveByJobAsync(withoutSummary.JobId, CancellationToken.None);

        Assert.Equal(new CompositionSummary(18, 7, 0.487, 3.94), loaded!.Composition);
        Assert.Null(legacy!.Composition);
    }

    /// <summary>
    /// 보정 후보와 복원본은 합성을 다시 하지 않는다 — 부모의 판단을 그대로 물려받아야
    /// 화면이 "이 배치가 왜 이 크기인가" 를 계속 설명할 수 있다.
    /// </summary>
    [Fact]
    public void CandidateAndRestore_InheritTheParentComposition()
    {
        var summary = new CompositionSummary(4, 1, 0.5, 1.2);
        var parent = ComposeActive(Guid.NewGuid(), 1, [Instance()], summary);

        var candidate = SceneLayout.CreateCandidate(
            parent, 2, parent.Instances, parent.Camera, parent.Light, Now);
        var restored = SceneLayout.CreateRestored(
            parent, parent.SourceMeshSignature, 3, Now);

        Assert.Equal(summary, candidate.Composition);
        Assert.Equal(summary, restored.Composition);
    }

    /// <summary>
    /// **축별 배율이 왕복한다** (background-surface-parts #20 §4.2).
    ///
    /// 표면은 폭·높이·깊이가 다르다. 하나라도 떨어뜨리면 저장된 장면이 다른 모양으로
    /// 되살아난다 — 마이그레이션이 Scale 을 ScaleY 로 옮기므로 축이 뒤바뀌기 쉬운 자리다.
    /// </summary>
    [Fact]
    public async Task PerAxisScale_RoundTrips()
    {
        await Migrate();

        var jobId = Guid.NewGuid();
        var partId = Guid.NewGuid();
        var layout = ComposeActive(jobId, 1,
        [
            new SceneInstance(partId, 0, x: 1, y: 0, z: -4, rotationY: 0,
                scaleX: 12, scaleY: 0.4, scaleZ: 18),          // 표면
            new SceneInstance(partId, 1, x: 2, y: 0, z: -5, rotationY: 0, scale: 3),  // 낱개
        ]);

        await using (var db = Context())
        {
            await new EfSceneLayoutRepository(db).AddAsync(layout, CancellationToken.None);
        }

        await using var read = Context();
        var loaded = await new EfSceneLayoutRepository(read)
            .GetActiveByJobAsync(jobId, CancellationToken.None);

        var surface = loaded!.Instances[0];
        Assert.Equal(12, surface.ScaleX);
        Assert.Equal(0.4, surface.ScaleY);
        Assert.Equal(18, surface.ScaleZ);
        Assert.False(surface.IsUniform);

        var solid = loaded.Instances[1];
        Assert.True(solid.IsUniform);
        Assert.Equal(3, solid.Scale);
    }

    private static SceneInstance Instance()
        => new(Guid.NewGuid(), 0, x: 1, y: 0, z: -4, rotationY: 0, scale: 2.0);

    /// <summary>
    /// 활성 교대 — 기존 활성을 Superseded 로 전이하고 새 revision 을 추가한다.
    /// 두 행이 모두 남아야 이력이고, 활성은 새 쪽 하나여야 한다 (§4.1).
    /// </summary>
    [Fact]
    public async Task Supersede_KeepsHistoryAndSwapsTheActive()
    {
        await Migrate();

        var jobId = Guid.NewGuid();

        await using (var db = Context())
        {
            await new EfSceneLayoutRepository(db).AddAsync(
                ComposeActive(jobId, 1, [new SceneInstance(Guid.NewGuid(), 0, 1, 0, -1, 0, 1)]),
                CancellationToken.None);
        }

        // GLB 재생성으로 서명이 바뀌었다 — 핸들러와 같은 경로: 전이 + 추가를 한 저장으로
        await using (var db = Context())
        {
            var repo = new EfSceneLayoutRepository(db);
            var active = await repo.GetActiveByJobAsync(jobId, CancellationToken.None);
            active!.MarkSuperseded();

            var next = await repo.MaxRevisionAsync(jobId, CancellationToken.None) + 1;
            await repo.AddAsync(
                ComposeActive(jobId, next, [new SceneInstance(Guid.NewGuid(), 0, 2, 0, -2, 0, 1)]),
                CancellationToken.None);
        }

        await using var read = Context();
        var rows = await read.SceneLayouts.ToListAsync();

        Assert.Equal(2, rows.Count);
        var active2 = await new EfSceneLayoutRepository(read)
            .GetActiveByJobAsync(jobId, CancellationToken.None);
        Assert.Equal(2, active2!.Revision);
        Assert.Single(rows, r => r.State == SceneLayoutState.Superseded);
    }

    /// <summary>필터드 유니크 — 활성 둘은 DB 가 거절한다. 코드 경쟁의 마지막 방어선 (§4.1).</summary>
    [Fact]
    public async Task TwoActiveRows_AreRefusedByTheDatabase()
    {
        await Migrate();

        var jobId = Guid.NewGuid();

        await using var db = Context();
        var repo = new EfSceneLayoutRepository(db);
        await repo.AddAsync(
            ComposeActive(jobId, 1, [new SceneInstance(Guid.NewGuid(), 0, 1, 0, -1, 0, 1)]),
            CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateException>(() => repo.AddAsync(
            ComposeActive(jobId, 2, [new SceneInstance(Guid.NewGuid(), 0, 2, 0, -2, 0, 1)]),
            CancellationToken.None));
    }

    /// <summary>
    /// B-10 · SC-03 — **작업 완전 삭제가 조립 명세도 지운다** (FR-09).
    ///
    /// 남으면 지운 작업의 파츠 ID 를 가리키는 고아 명세가 쌓이고, 같은 작업 ID 가
    /// 재사용될 일은 없어도 "흔적을 남기지 않는다" 는 삭제의 약속이 깨진다.
    /// </summary>
    [Fact]
    public async Task DeletingTheJob_RemovesItsLayoutToo()
    {
        await Migrate();

        Guid jobId;
        await using (var db = Context())
        {
            var job = PipelineJob.Create(
                AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini");
            job.Fail("STAGE_FAILED", Now);
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;

            await new EfSceneLayoutRepository(db).AddAsync(
                ComposeActive(jobId, 1, [new SceneInstance(Guid.NewGuid(), 0, 1, 0, -1, 0, 1)]),
                CancellationToken.None);
        }

        await using (var db = Context())
        {
            var removed = await new EfJobRepository(db).DeleteIfTerminalAsync(jobId, CancellationToken.None);
            Assert.NotNull(removed);
        }

        await using var read = Context();
        Assert.Empty(read.SceneLayouts);
    }

    // ─── 설정 ───

    private static SceneLayout ComposeActive(
        Guid jobId, int revision, IReadOnlyList<SceneInstance> instances,
        CompositionSummary? composition = null)
        => SceneLayout.ComposeActive(
            jobId, revision, instances,
            // 왕복 검사라 서명은 임의 값이면 충분하다
            $"SIGNATURE-{Guid.NewGuid():n}",
            sourceMeshCount: instances.Count,
            SceneStaging.ComposeCamera(null, []),
            SceneStaging.ComposeLight(null),
            Now,
            composition);

    private async Task Migrate()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    /// <summary>운영과 같은 재시도 전략 — 끄면 삭제의 트랜잭션 경로가 검사를 비껴간다 (실측).</summary>
    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options);
}
