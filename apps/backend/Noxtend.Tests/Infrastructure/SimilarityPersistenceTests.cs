using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Similarity;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 유사도 실행·평가가 실제 SQL Server 에서 왕복하는가.
/// Design Ref: background-similarity-tuning §5 · §13 · 테스트 설계 §18.2
///
/// **필터드 유니크·idempotency·row version 경쟁은 인메모리로 검증되지 않는다** —
/// 셋 다 동시 요청 사이에서 애플리케이션 검사가 지는 지점이고, DB 가 마지막 방어선이다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SimilarityPersistenceTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(SimilarityPersistenceTests));

    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>평가 한 건의 JSON 왕복 — 점수·discriminated 보정 명령·렌더 참조까지.</summary>
    [Fact]
    public async Task Evaluation_RoundTripsScoreAdjustmentsAndRender()
    {
        await Migrate();

        var run = Run(Guid.NewGuid());
        var render = new EvaluationRenderArtifact(
            "similarity/r/render-1.png", "image/png", 456_789, new string('B', 64));
        var evaluation = SimilarityEvaluation.CreateBaseline(run.Id, Guid.NewGuid(), render, Now);
        evaluation.BeginAttempt(Now.AddMinutes(5), Now);
        evaluation.Succeed(
            Score(70),
            [new SimilarityAdjustment(
                Guid.NewGuid(), new MoveInstance(Guid.NewGuid(), 0, 0.4, -0.2), 0.85, "근경 이동")],
            ["하늘 톤을 낮춘다"],
            Guid.NewGuid(),
            Now);

        await using (var db = Context())
        {
            var repo = new EfSimilarityRepository(db);
            await repo.AddRunAsync(run, CancellationToken.None);
            await repo.AddEvaluationAsync(evaluation, CancellationToken.None);
            await repo.SaveChangesAsync(CancellationToken.None);
        }

        await using var read = Context();
        var loaded = await new EfSimilarityRepository(read)
            .GetEvaluationAsync(evaluation.Id, CancellationToken.None);

        Assert.Equal(70, loaded!.Score!.Overall);
        var move = Assert.IsType<MoveInstance>(Assert.Single(loaded.Adjustments).Command);
        Assert.Equal(0.4, move.DeltaX);
        Assert.Equal(render, loaded.Render);
        Assert.Equal(["하늘 톤을 낮춘다"], loaded.RegenerationNotes);
    }

    /// <summary>비종료 run 은 job 당 하나 — 둘째 접수는 DB 가 거절한다 (§5.1).</summary>
    [Fact]
    public async Task TwoOpenRunsForOneJob_AreRefusedByTheDatabase()
    {
        await Migrate();
        var jobId = Guid.NewGuid();

        await using var db = Context();
        var repo = new EfSimilarityRepository(db);
        await repo.AddRunAsync(Run(jobId, key: "k1"), CancellationToken.None);
        await repo.SaveChangesAsync(CancellationToken.None);

        await repo.AddRunAsync(Run(jobId, key: "k2"), CancellationToken.None);
        await Assert.ThrowsAsync<DbUpdateException>(
            () => repo.SaveChangesAsync(CancellationToken.None));
    }

    /// <summary>종료된 run 은 필터 밖 — 완료 후 새 run 접수는 허용된다.</summary>
    [Fact]
    public async Task ANewRunAfterCompletion_IsAllowed()
    {
        await Migrate();
        var jobId = Guid.NewGuid();

        var first = Run(jobId, key: "k1");
        first.Complete(Now);

        await using var db = Context();
        var repo = new EfSimilarityRepository(db);
        await repo.AddRunAsync(first, CancellationToken.None);
        await repo.AddRunAsync(Run(jobId, key: "k2"), CancellationToken.None);
        await repo.SaveChangesAsync(CancellationToken.None);

        var open = await repo.GetOpenRunByJobAsync(jobId, CancellationToken.None);
        Assert.Equal("k2", open!.IdempotencyKey);
    }

    /// <summary>같은 (JobId, Idempotency-Key) 재접수는 DB 가 거절한다 (§10).</summary>
    [Fact]
    public async Task DuplicateIdempotencyKeys_AreRefusedByTheDatabase()
    {
        await Migrate();
        var jobId = Guid.NewGuid();

        var first = Run(jobId, key: "same");
        first.Complete(Now);   // 비종료 유니크와 겹치지 않게 종료해 둔다

        await using var db = Context();
        var repo = new EfSimilarityRepository(db);
        await repo.AddRunAsync(first, CancellationToken.None);
        await repo.SaveChangesAsync(CancellationToken.None);

        await repo.AddRunAsync(Run(jobId, key: "same"), CancellationToken.None);
        await Assert.ThrowsAsync<DbUpdateException>(
            () => repo.SaveChangesAsync(CancellationToken.None));
    }

    /// <summary>상태 전이 경쟁 — 채택과 취소가 겹치면 한쪽만 이긴다 (row version, §13).</summary>
    [Fact]
    public async Task ConcurrentRunTransitions_OnlyOneWins()
    {
        await Migrate();
        var run = Run(Guid.NewGuid());

        await using (var db = Context())
        {
            var repo = new EfSimilarityRepository(db);
            await repo.AddRunAsync(run, CancellationToken.None);
            await repo.SaveChangesAsync(CancellationToken.None);
        }

        // 두 컨텍스트가 같은 행을 읽고 서로 다른 전이를 저장한다
        await using var first = Context();
        await using var second = Context();
        var a = await new EfSimilarityRepository(first).GetRunAsync(run.Id, CancellationToken.None);
        var b = await new EfSimilarityRepository(second).GetRunAsync(run.Id, CancellationToken.None);

        a!.Complete(Now);
        await first.SaveChangesAsync();

        b!.Cancel(Now);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    // ─── 설정 ───

    private static SimilarityRun Run(Guid jobId, string key = "key-1")
        => SimilarityRun.Start(jobId, Guid.NewGuid(), "gpt-test", 2, key, Now);

    private static SimilarityScore Score(int uniform)
        => SimilarityScore.Create(
            [.. Enum.GetValues<SimilarityDimensionKind>()
                .Select(kind => new SimilarityDimension(kind, uniform, "관찰", "권고"))]);

    private async Task Migrate()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options);
}
