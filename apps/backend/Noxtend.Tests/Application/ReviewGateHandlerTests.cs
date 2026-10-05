using Noxtend.Domain.Common;
using Noxtend.Domain.Job;

namespace Noxtend.Tests.Application;

/// <summary>
/// review-gate 애플리케이션 계층 — GetReview/AddReviewPart/RemoveReviewPart/ApproveReview
/// 네 핸들러가 도메인 예외를 <see cref="Result{T}"/> 코드로 올바르게 접는지, 승인이
/// 실제로 큐에 적재하는지를 검증한다.
/// </summary>
public sealed class ReviewGateHandlerTests
{
    [Fact]
    public async Task GetReview_ReturnsTheJobWithItsDetectedParts()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();

        var result = await fixture.GetReview.HandleAsync(job.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(JobStatus.PendingReview, result.Value!.Status);
        Assert.NotEmpty(result.Value.Parts);
    }

    [Fact]
    public async Task GetReview_UnknownJob_ReturnsJobNotFound()
    {
        var fixture = new PipelineFixture();

        var result = await fixture.GetReview.HandleAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.JobNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task AddReviewPart_IndependentBox_AddsAndPersists()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();

        var result = await fixture.AddReviewPart.HandleAsync(
            job.Id, "모자", "Headwear", new Bounds(0.85, 0.85, 0.1, 0.1), "챙 넓은 모자",
            occludes: null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorCode);
        var reloaded = await fixture.Jobs.GetAsync(job.Id, CancellationToken.None);
        Assert.Contains(reloaded!.Parts, p => p.Name == "모자" && p.IsManuallyAdded);
    }

    // 등대(default fake decompose 파츠)의 첫 배치(x=0.05,y=0.10,w=0.12,h=0.20)와 겹치는 위치.
    // 겹쳐도 추가되고, 겹친 파츠가 서술 재작성 대상이 된다 (occludedby-recompute §입력→출력 1)
    [Fact]
    public async Task AddReviewPart_OverlappingBox_AddsAndMarksOccludedPartStale()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();

        var result = await fixture.AddReviewPart.HandleAsync(
            job.Id, "장식", "Accessory", new Bounds(0.06, 0.11, 0.05, 0.05), "설명",
            occludes: null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorCode);
        var reloaded = await fixture.Jobs.GetAsync(job.Id, CancellationToken.None);
        Assert.Contains(reloaded!.Parts, p => p.OccludedBy.Contains("장식"));
        Assert.Contains("등대", reloaded.DescriptionsStale);
    }

    [Fact]
    public async Task AddReviewPart_JobNotInReview_ReturnsReviewNotPending()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();   // RequiresReview=false

        var result = await fixture.AddReviewPart.HandleAsync(
            job.Id, "모자", "Headwear", new Bounds(0.85, 0.85, 0.1, 0.1), null, occludes: null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ReviewNotPending, result.ErrorCode);
    }

    [Fact]
    public async Task RemoveReviewPart_RemovesTheGivenPart()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();
        var target = job.Parts.First();

        var result = await fixture.RemoveReviewPart.HandleAsync(job.Id, target.Id, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorCode);
        var reloaded = await fixture.Jobs.GetAsync(job.Id, CancellationToken.None);
        Assert.DoesNotContain(reloaded!.Parts, p => p.Id == target.Id);
    }

    [Fact]
    public async Task RemoveReviewPart_UnknownPart_ReturnsPartNotFound()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();

        var result = await fixture.RemoveReviewPart.HandleAsync(job.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.PartNotFound, result.ErrorCode);
    }

    // review-gate-staged 사이클 1 T2 — 상자 확정은 생성을 내지 않고 서술 단계 검수 대기로
    [Fact]
    public async Task ApproveReview_NoStale_StopsAtDescriptionsPhase()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();

        var result = await fixture.ApproveReview.HandleAsync(job.Id, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorCode);
        Assert.Equal(JobStatus.PendingReview, result.Value!.Status);
        Assert.DoesNotContain(result.Value.Tasks, t => t.Kind == TaskKind.Generate);
    }

    // 승인 시 재작성 공정이 큐에 적재되고, Generate 는 아직 안 나간다 (§입력→출력 3)
    [Fact]
    public async Task ApproveReview_WithStaleDescriptions_EnqueuesRewriteBeforeGenerate()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();
        await fixture.AddReviewPart.HandleAsync(
            job.Id, "장식", "Accessory", new Bounds(0.06, 0.11, 0.05, 0.05), "설명",
            occludes: null, CancellationToken.None);

        var result = await fixture.ApproveReview.HandleAsync(job.Id, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorCode);
        Assert.Contains(result.Value!.Tasks, t => t.Kind == TaskKind.RewriteDescriptions);
        Assert.DoesNotContain(result.Value.Tasks, t => t.Kind == TaskKind.Generate);
    }

    // ─── 겹침 조회 (§입력→출력 1) — 화면이 체크박스를 그리려면 추가 전에 겹침을 알아야 한다 ───

    [Fact]
    public async Task FindOverlaps_ReturnsOverlappingNames()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();

        var result = await fixture.FindOverlaps.HandleAsync(
            job.Id, new Bounds(0.06, 0.11, 0.05, 0.05), CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorCode);
        Assert.NotEmpty(result.Value!);
    }

    // 순수 조회다 — 파츠도 재작성 대상도 늘지 않는다
    [Fact]
    public async Task FindOverlaps_DoesNotMutateTheJob()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();
        var partsBefore = job.Parts.Count;

        await fixture.FindOverlaps.HandleAsync(
            job.Id, new Bounds(0.06, 0.11, 0.05, 0.05), CancellationToken.None);

        var reloaded = await fixture.Jobs.GetAsync(job.Id, CancellationToken.None);
        Assert.Equal(partsBefore, reloaded!.Parts.Count);
        Assert.Empty(reloaded.DescriptionsStale);
    }

    // 화면 밖 좌표는 추가 시점까지 가지 않고 여기서 걸러진다 (V-4)
    [Fact]
    public async Task FindOverlaps_OutOfFrame_ReturnsBoundsOutOfRange()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();

        var result = await fixture.FindOverlaps.HandleAsync(
            job.Id, new Bounds(0.9, 0.9, 0.5, 0.5), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.PartBoundsOutOfRange, result.ErrorCode);
    }

    // 검수 대기가 아닌 작업에는 조회 자체를 막는다 — 추가와 같은 가드다 (V-6)
    [Fact]
    public async Task FindOverlaps_NotPendingReview_ReturnsReviewNotPending()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();

        var result = await fixture.FindOverlaps.HandleAsync(
            job.Id, new Bounds(0.1, 0.1, 0.1, 0.1), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ReviewNotPending, result.ErrorCode);
    }

    // occludes 를 준 대로만 반영한다 — 빈 목록이면 아무것도 안 가린다 (V-1)
    [Fact]
    public async Task AddReviewPart_EmptyOccludes_LeavesDescriptionsUntouched()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();

        var result = await fixture.AddReviewPart.HandleAsync(
            job.Id, "장식", "Accessory", new Bounds(0.06, 0.11, 0.05, 0.05), "설명",
            occludes: [], CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorCode);
        var reloaded = await fixture.Jobs.GetAsync(job.Id, CancellationToken.None);
        Assert.Empty(reloaded!.DescriptionsStale);
    }

    // 겹치지 않는 파츠를 가린다고 지목하면 400 (§함정9)
    [Fact]
    public async Task AddReviewPart_OccludesNonOverlapping_ReturnsPartNotOverlapping()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();
        var farAway = job.Parts.Last().Name;

        var result = await fixture.AddReviewPart.HandleAsync(
            job.Id, "모자", "Headwear", new Bounds(0.85, 0.85, 0.1, 0.1), "모자",
            occludes: [farAway], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.PartNotOverlapping, result.ErrorCode);
    }

    // ─── 재작성 공정을 실제로 실행한다 (occludedby-recompute §입력→출력 3) ───
    //
    // **도메인 테스트로는 이 경로가 안 잡힌다.** 그쪽은 task.Succeed() 를 직접 부르므로
    // 공급자 조회·프롬프트 렌더·응답 해석을 통째로 건너뛴다. 실제 결함(공급자 미지정,
    // 프롬프트에 변수 자리 없음)이 전부 여기서만 드러난다.

    [Fact]
    public async Task RewriteDescriptions_RunsToSuccess()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();
        await fixture.AddReviewPart.HandleAsync(
            job.Id, "장식", "Accessory", new Bounds(0.06, 0.11, 0.05, 0.05), "설명",
            occludes: null, CancellationToken.None);
        await fixture.ApproveReview.HandleAsync(job.Id, CancellationToken.None);

        var reloaded = await fixture.Jobs.GetAsync(job.Id, CancellationToken.None);
        await fixture.RunUntilTerminalAsync(reloaded!, TaskKind.RewriteDescriptions);

        var rewrite = reloaded!.Tasks.Single(t => t.Kind == TaskKind.RewriteDescriptions);
        Assert.True(
            rewrite.Status == Noxtend.Domain.Job.TaskStatus.Succeeded, rewrite.FailureReason);
        Assert.NotEqual(JobStatus.Failed, reloaded.Status);
    }

    // 사이클 1 T3 — 재작성이 성공하면 대상이 비고, 서술 단계 검수 대기로 멈춘다
    [Fact]
    public async Task RewriteDescriptions_ClearsStaleAndStopsAtDescriptionsPhase()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();
        await fixture.AddReviewPart.HandleAsync(
            job.Id, "장식", "Accessory", new Bounds(0.06, 0.11, 0.05, 0.05), "설명",
            occludes: null, CancellationToken.None);
        await fixture.ApproveReview.HandleAsync(job.Id, CancellationToken.None);

        var reloaded = await fixture.Jobs.GetAsync(job.Id, CancellationToken.None);
        await fixture.RunUntilTerminalAsync(reloaded!, TaskKind.RewriteDescriptions);
        reloaded!.PlanReadyFollowUpTasks();

        Assert.Empty(reloaded.DescriptionsStale);
        Assert.Equal(ReviewPhase.Descriptions, reloaded.ReviewPhase);
        Assert.Equal(JobStatus.PendingReview, reloaded.Status);
        Assert.DoesNotContain(reloaded.Tasks, t => t.Kind == TaskKind.Generate);
    }

    // §구현 범위 4 회귀 고정 — 재작성은 서술만 바꾼다. ApplyDetail 을 재사용하면
    // 사람이 그린 좌표가 지워지는데 팬아웃은 좌표를 안 보므로 아무도 못 잡는다
    [Fact]
    public async Task RewriteDescriptions_KeepsEveryPlacement()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();
        var drawn = new Bounds(0.06, 0.11, 0.05, 0.05);
        await fixture.AddReviewPart.HandleAsync(
            job.Id, "장식", "Accessory", drawn, "설명", occludes: null, CancellationToken.None);
        await fixture.ApproveReview.HandleAsync(job.Id, CancellationToken.None);

        var reloaded = await fixture.Jobs.GetAsync(job.Id, CancellationToken.None);
        var before = reloaded!.Parts.ToDictionary(p => p.Name, p => p.Placements.Count);
        await fixture.RunUntilTerminalAsync(reloaded, TaskKind.RewriteDescriptions);

        Assert.Equal(before, reloaded.Parts.ToDictionary(p => p.Name, p => p.Placements.Count));
        Assert.Equal(drawn, reloaded.Parts.Single(p => p.Name == "장식").Placements.Single());
    }

    [Fact]
    public async Task ApproveReview_WithoutRequiresReview_ReturnsReviewNotRequired()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();

        var result = await fixture.ApproveReview.HandleAsync(job.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ReviewNotRequired, result.ErrorCode);
    }

    // ─── review-gate-staged 사이클 1 — 서술 확인 단계 핸들러 ───

    // 상자 확정까지 마친 서술 단계 작업
    private static async Task<(PipelineFixture Fixture, PipelineJob Job)> ReachDescriptionsPhaseAsync()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();
        await fixture.ApproveReview.HandleAsync(job.Id, CancellationToken.None);
        return (fixture, (await fixture.Jobs.GetAsync(job.Id, CancellationToken.None))!);
    }

    // T5 — 서술 확정이 팬아웃을 계획하고 큐에 적재
    [Fact]
    public async Task ConfirmDescriptions_PlansFanOutAndEnqueuesTasks()
    {
        var (fixture, job) = await ReachDescriptionsPhaseAsync();
        var enqueuedBefore = fixture.Queue.Enqueued.Count;

        var result = await fixture.ReviewDescriptions.ConfirmAsync(job.Id, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorCode);
        Assert.Equal(JobStatus.Running, result.Value!.Status);
        Assert.Contains(result.Value.Tasks, t => t.Kind == TaskKind.Generate);
        Assert.True(fixture.Queue.Enqueued.Count > enqueuedBefore);
    }

    // T5 — 상자 단계에서 서술 확정 → 단계 불일치
    [Fact]
    public async Task ConfirmDescriptions_InBoxesPhase_ReturnsPhaseMismatch()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.ReachPendingReviewAsync();

        var result = await fixture.ReviewDescriptions.ConfirmAsync(job.Id, CancellationToken.None);

        Assert.Equal(ErrorCode.ReviewPhaseMismatch, result.ErrorCode);
    }

    // T6 — 서술 단계에서 상자 편집·재승인 → 단계 불일치 (옛 catch 가 ReviewNotPending 으로 삼키지 않음)
    [Fact]
    public async Task BoxActions_InDescriptionsPhase_ReturnPhaseMismatch()
    {
        var (fixture, job) = await ReachDescriptionsPhaseAsync();
        var part = job.Parts.First();

        var approve = await fixture.ApproveReview.HandleAsync(job.Id, CancellationToken.None);
        var add = await fixture.AddReviewPart.HandleAsync(
            job.Id, "모자", "Headwear", new Bounds(0.85, 0.85, 0.1, 0.1), "모자", occludes: null, CancellationToken.None);
        var move = await fixture.MoveReviewPlacement.HandleAsync(
            job.Id, part.Id, 0, new Bounds(0.1, 0.1, 0.1, 0.1), CancellationToken.None);
        var remove = await fixture.RemoveReviewPart.HandleAsync(job.Id, part.Id, CancellationToken.None);

        Assert.All(
            [approve.ErrorCode, add.ErrorCode, move.ErrorCode, remove.ErrorCode],
            code => Assert.Equal(ErrorCode.ReviewPhaseMismatch, code));
    }

    // 되돌리기 — 상자 단계로
    [Fact]
    public async Task ReturnToBoxes_MovesBackToBoxesPhase()
    {
        var (fixture, job) = await ReachDescriptionsPhaseAsync();

        var result = await fixture.ReviewDescriptions.ReturnToBoxesAsync(job.Id, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorCode);
        Assert.Equal(ReviewPhase.Boxes, result.Value!.ReviewPhase);
    }

    // T7 — 서술 편집 저장, 빈 문자열은 400 코드, 없는 파츠는 PartNotFound
    [Fact]
    public async Task EditDescription_SavesAndValidates()
    {
        var (fixture, job) = await ReachDescriptionsPhaseAsync();
        var part = job.Parts.First();

        var ok = await fixture.ReviewDescriptions.EditDescriptionAsync(
            job.Id, part.Id, "사람이 쓴 서술", CancellationToken.None);
        var blank = await fixture.ReviewDescriptions.EditDescriptionAsync(
            job.Id, part.Id, " ", CancellationToken.None);
        var missing = await fixture.ReviewDescriptions.EditDescriptionAsync(
            job.Id, Guid.NewGuid(), "서술", CancellationToken.None);

        Assert.True(ok.IsSuccess, ok.ErrorCode);
        var reloaded = await fixture.Jobs.GetAsync(job.Id, CancellationToken.None);
        Assert.Equal(DescriptionSource.Human, reloaded!.Parts.First(p => p.Id == part.Id).DescriptionSource);
        Assert.Equal(ErrorCode.PartDescriptionEmpty, blank.ErrorCode);
        Assert.Equal(ErrorCode.PartNotFound, missing.ErrorCode);
    }

    // T7b — 팔레트 교체, 형식 위반은 PaletteInvalid
    [Fact]
    public async Task EditPalette_SavesAndValidates()
    {
        var (fixture, job) = await ReachDescriptionsPhaseAsync();
        PaletteEntry[] palette = [new("빨강", "#C8102E"), new("남색", "#1F2A44"), new("갈색", "#6B4226")];

        var ok = await fixture.ReviewDescriptions.EditPaletteAsync(job.Id, palette, CancellationToken.None);
        var invalid = await fixture.ReviewDescriptions.EditPaletteAsync(
            job.Id, [new("빨강", "#C8102E")], CancellationToken.None);

        Assert.True(ok.IsSuccess, ok.ErrorCode);
        Assert.Equal(palette, ok.Value!.Scene!.Palette);
        Assert.Equal(ErrorCode.PaletteInvalid, invalid.ErrorCode);
    }
}
