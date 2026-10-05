using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Persistence;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 검수 편집과 전체 승인이 겹쳤을 때 (occludedby-recompute §구현 범위 5).
///
/// **인메모리로는 이 경합을 볼 수 없다** — 사본이 하나뿐이라 두 요청이 같은 객체를
/// 만지므로 섞일 것이 없다. `TaskConcurrencyTests` 와 같은 이유로 실 DB 로 본다.
///
/// 검수 화면에서 "추가" 직후 "전체 승인" 을 누르는 조작(더블클릭·느린 네트워크)이
/// 재현 대상이다. 승인이 팬아웃을 계획해 저장한 뒤, 옛 스냅샷을 든 추가가 저장되면
/// 그 파츠만 Generate 공정 없이 남고 이후 영원히 생기지 않는다 —
/// `PlanGenerationFanOut` 이 "이미 Generate 가 있다" 로 즉시 return 하기 때문이다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ReviewConcurrencyTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(ReviewConcurrencyTests));

    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 승인이 먼저 저장된 뒤 옛 스냅샷의 파츠 추가가 저장되면 어떻게 되는가.
    ///
    /// 동시성 토큰이 있으면 두 번째 저장이 충돌로 막힌다. 없으면 조용히 섞여
    /// **Generate 공정이 없는 파츠**가 남는다.
    /// </summary>
    [Fact]
    public async Task AddPart_AfterApproveSaved_IsRejected()
    {
        await Migrate();
        var jobId = await SeedPendingReviewJobAsync();

        await using var adding = Context();
        await using var approving = Context();

        var a = await adding.Jobs.SingleAsync(job => job.Id == jobId);
        var b = await approving.Jobs.SingleAsync(job => job.Id == jobId);

        // 승인이 먼저 저장된다 — 팬아웃이 이 시점의 파츠 목록으로 고정된다
        b.ApproveReview(Now);
        await approving.SaveChangesAsync();

        // 추가는 자기가 읽은 옛 스냅샷 기준이라 도메인 가드(PendingReview)를 통과한다
        a.AddReviewPart("벨트", "Belt", new Bounds(0.3, 0.5, 0.1, 0.05), "가죽 벨트");

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => adding.SaveChangesAsync());
    }

    /// <summary>
    /// review-gate-staged 사이클 1 (독립 리뷰 #5) — 서술 확정이 먼저 저장된 뒤 옛 스냅샷의
    /// 서술 편집이 저장되는 경우. 파츠 서술은 owned 테이블이라 Jobs 행이 안 바뀌면 충돌 검사도 없음.
    /// 막히지 않으면 팬아웃 뒤에 서술이 바뀌어 생성 입력이 섞임
    /// </summary>
    [Fact]
    public async Task EditDescription_AfterConfirmSaved_IsRejected()
    {
        await Migrate();
        var jobId = await SeedPendingReviewJobAsync();
        await using (var db = Context())
        {
            var job = await db.Jobs.SingleAsync(j => j.Id == jobId);
            job.ApproveReview(Now);
            await db.SaveChangesAsync();
        }

        await using var editing = Context();
        await using var confirming = Context();
        var a = await editing.Jobs.SingleAsync(job => job.Id == jobId);
        var b = await confirming.Jobs.SingleAsync(job => job.Id == jobId);

        b.ConfirmDescriptions(Now);
        await confirming.SaveChangesAsync();

        a.EditReviewDescription(a.Parts.Single().Id, "뒤늦은 사람 서술");

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => editing.SaveChangesAsync());
    }

    // ─── 설정 ───

    // 분해까지 끝나 검수 대기로 멈춘 작업 하나 — "바지" 가 x=0.2~0.6, y=0.4~0.9 에 있다
    private async Task<Guid> SeedPendingReviewJobAsync()
    {
        await using var db = Context();

        var job = PipelineJob.Create(
            AssetCategory.Character, Guid.NewGuid(), Now,
            imageProviderConfigId: Guid.NewGuid(), imageModel: "gpt-image-2",
            requiresReview: true);
        job.ApplyParts(["바지"]);
        job.ApplyPartDetails([
            new PartDetail(
                "바지", "Bottom", "다리를 덮는 긴 바지",
                [new Bounds(0.2, 0.4, 0.4, 0.5)], DepthOrder: 2, OccludedBy: []),
        ]);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        return job.Id;
    }

    private async Task Migrate()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
