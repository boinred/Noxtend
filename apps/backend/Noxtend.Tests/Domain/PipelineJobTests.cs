using Noxtend.Domain.Job;

namespace Noxtend.Tests.Domain;

/// <summary>Design Ref: §8.2 #6~9 — 작업의 결과 반영과 종료 판정.</summary>
public sealed class PipelineJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private static PipelineJob NewJob()
        => PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);

    // character-studio slice 1 — 성별·힌트 접수 계약. 값이 도메인에 보존되는지 고정한다
    [Fact]
    public void Create_PreservesGenderAndPartHints()
    {
        var hintsJson = """[{"type":"팔찌","count":3}]""";

        var job = PipelineJob.Create(
            AssetCategory.Character, Guid.NewGuid(), Now, gender: Gender.Female, partHints: hintsJson);

        Assert.Equal(Gender.Female, job.Gender);
        Assert.Equal(hintsJson, job.PartHints);
    }

    // 타 카테고리는 성별·힌트 없이 접수된다 — 기존 계약 그대로(선택 인자)
    [Fact]
    public void Create_WithoutGenderAndHints_LeavesThemNull()
    {
        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);

        Assert.Null(job.Gender);
        Assert.Null(job.PartHints);
    }

    // #6 — ApplyParts: AssetPart 생성 (순서 유지)
    [Fact]
    public void ApplyParts_CreatesPartsInOrder()
    {
        var job = NewJob();

        job.ApplyParts(["등대", "목조 부두", "어선"]);

        Assert.Equal(3, job.Parts.Count);
        // 순서가 유지되어야 사용자가 본 목록과 일치한다
        Assert.Equal(["등대", "목조 부두", "어선"], job.Parts.Select(p => p.Name));
        Assert.Equal([0, 1, 2], job.Parts.Select(p => p.Ordinal));
    }

    [Fact]
    public void ApplyParts_ReplacesPreviousPartsOnRerun()
    {
        var job = NewJob();
        job.ApplyParts(["등대"]);

        job.ApplyParts(["부두", "어선"]);

        // 누적되면 다시 분석할 때마다 파츠가 불어난다
        Assert.Equal(["부두", "어선"], job.Parts.Select(p => p.Name));
    }

    [Fact]
    public void ApplyScene_StoresTheBaseline()
    {
        var job = NewJob();

        job.ApplyScene(TestScene.Default);

        Assert.Equal("해질녘", job.Scene!.TimeOfDay);
        // 조립을 좌우하는 셋이 살아 있어야 한다 (Plan D-13)
        Assert.Equal(0.55, job.Scene.Camera.HorizonY);
        Assert.Equal("좌측 후방 15° 고도", job.Scene.Light.Direction);
        Assert.Equal("높이 3m", job.Scene.Scale.RealWorldSize);
    }

    // #7 — 모든 공정 성공 → 작업 Succeeded
    [Fact]
    public void ReconcileFromTasks_SucceedsWhenEveryTaskSucceeded()
    {
        var job = NewJob();
        var task = job.PlanTask(TaskKind.Extract, 0);
        task.Claim(Now, Lease);
        task.Succeed(Now);

        job.ReconcileFromTasks(Now);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(Now, job.CompletedAt);
    }

    [Fact]
    public void ReconcileFromTasks_StaysOpenWhileATaskIsPending()
    {
        var job = NewJob();
        var first = job.PlanTask(TaskKind.Extract, 0);
        job.PlanTask(TaskKind.Extract, 1, dependsOnTaskId: first.Id);

        first.Claim(Now, Lease);
        first.Succeed(Now);
        job.ReconcileFromTasks(Now);

        // 다음 공정이 남아 있으면 작업은 끝나지 않는다 — 오케스트레이터가 그것을 적재한다
        Assert.NotEqual(JobStatus.Succeeded, job.Status);
        Assert.Null(job.CompletedAt);
    }

    // #8 — 한 공정 실패 → 작업 Failed
    [Fact]
    public void ReconcileFromTasks_FailsWhenAnyTaskFailed()
    {
        var job = NewJob();
        var first = job.PlanTask(TaskKind.Extract, 0);
        job.PlanTask(TaskKind.Extract, 1, dependsOnTaskId: first.Id);

        first.Claim(Now, Lease);
        first.Fail("PROVIDER_CALL_FAILED", Now);

        job.ReconcileFromTasks(Now);

        // 남은 대기 공정이 있어도 실패가 이긴다
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("PROVIDER_CALL_FAILED", job.FailureReason);
    }

    // #9 — Cancel: 종료 상태에서 재취소 불가
    [Fact]
    public void Cancel_RejectedOnTerminalJob()
    {
        var job = NewJob();
        var task = job.PlanTask(TaskKind.Extract, 0);
        task.Claim(Now, Lease);
        task.Succeed(Now);
        job.ReconcileFromTasks(Now);

        Assert.Throws<InvalidOperationException>(() => job.Cancel(Now));
    }

    [Fact]
    public void Cancel_CancelsUnfinishedTasksToo()
    {
        var job = NewJob();
        var running = job.PlanTask(TaskKind.Extract, 0);
        var pending = job.PlanTask(TaskKind.Extract, 1, dependsOnTaskId: running.Id);
        running.Claim(Now, Lease);

        job.Cancel(Now);

        Assert.Equal(JobStatus.Canceled, job.Status);
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Canceled, running.Status);
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Canceled, pending.Status);
    }

    [Fact]
    public void PlanTask_RejectedOnTerminalJob()
    {
        var job = NewJob();
        job.Cancel(Now);

        Assert.Throws<InvalidOperationException>(() => job.PlanTask(TaskKind.Extract, 0));
    }

    [Fact]
    public void MarkRunning_OnlyPromotesFromPending()
    {
        var job = NewJob();
        job.MarkRunning();
        Assert.Equal(JobStatus.Running, job.Status);

        var task = job.PlanTask(TaskKind.Extract, 0);
        task.Claim(Now, Lease);
        task.Succeed(Now);
        job.ReconcileFromTasks(Now);

        // 끝난 작업을 다시 실행 중으로 되돌리지 않는다
        job.MarkRunning();
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    // workstream B §9-1 — 캐릭터는 파츠당 정면을 먼저 계획하고, 비정면 3개는 정면 공정에 의존한다
    [Fact]
    public void PlanGenerationFanOut_Character_NonFrontDependsOnFrontTask()
    {
        var providerConfigId = Guid.NewGuid();
        var job = PipelineJob.Create(
            AssetCategory.Character, Guid.NewGuid(), Now,
            imageProviderConfigId: providerConfigId, imageModel: "gpt-image-2");
        job.ApplyParts(["몸통"]);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);

        job.PlanReadyFollowUpTasks();
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        var part = job.Parts.Single();
        var generateTasks = job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToArray();
        Assert.Equal(4, generateTasks.Length);

        var front = generateTasks.Single(t => t.ViewDirection == ViewDirection.Front);
        Assert.Equal(decompose.Id, front.DependsOnTaskId);
        Assert.Equal(part.Id, front.PartId);

        var nonFront = generateTasks.Where(t => t.ViewDirection != ViewDirection.Front).ToArray();
        Assert.Equal(3, nonFront.Length);
        Assert.All(nonFront, t => Assert.Equal(front.Id, t.DependsOnTaskId));
    }

    // 배경은 지금처럼 넷 다 분해에 병렬 의존한다 — 캐릭터 분기가 새는지 회귀로 고정한다
    [Fact]
    public void PlanGenerationFanOut_PlansFrontViewOnlyFirst()
    {
        var providerConfigId = Guid.NewGuid();
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now,
            imageProviderConfigId: providerConfigId, imageModel: "gpt-image-2");
        job.ApplyParts(["등대"]);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);

        job.PlanReadyFollowUpTasks();

        // 정면(Front) 이미지만 먼저 계획한다 (selective-view-generation §2)
        var generateTasks = job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToArray();
        Assert.Single(generateTasks);

        var front = Assert.Single(generateTasks, t => t.ViewDirection == ViewDirection.Front);
        Assert.Equal(decompose.Id, front.DependsOnTaskId);
    }

    [Fact]
    public void PlanSelectedViews_PlansRequestedNonFrontViewsChainedToFront()
    {
        var providerConfigId = Guid.NewGuid();
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now,
            imageProviderConfigId: providerConfigId, imageModel: "gpt-image-2");
        job.ApplyParts(["등대"]);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        var front = job.Tasks.Single(t => t.Kind == TaskKind.Generate);

        // 사용자가 좌측(Left)과 후면(Back)만 선택 생성 요청
        var planned = job.PlanSelectedViews([ViewDirection.Left, ViewDirection.Back]);

        Assert.Equal(2, planned.Count);
        Assert.Contains(planned, t => t.ViewDirection == ViewDirection.Left);
        Assert.Contains(planned, t => t.ViewDirection == ViewDirection.Back);
        Assert.All(planned, t => Assert.Equal(front.Id, t.DependsOnTaskId));
    }

    // ─── review-gate §함정1 — 분해 성공 직후 팬아웃을 검수 승인까지 미룬다 ───

    // RequiresReview=true 인 작업은 분해가 성공해도 Generate 를 계획하지 않고 PendingReview 로 멈춘다
    [Fact]
    public void PlanReadyFollowUpTasks_WithRequiresReview_BlocksFanOutAndSetsPendingReview()
    {
        var providerConfigId = Guid.NewGuid();
        var job = PipelineJob.Create(
            AssetCategory.Character, Guid.NewGuid(), Now,
            imageProviderConfigId: providerConfigId, imageModel: "gpt-image-2",
            requiresReview: true);
        job.ApplyParts(["몸통"]);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);

        job.PlanReadyFollowUpTasks();

        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.Generate);
        Assert.Equal(JobStatus.PendingReview, job.Status);
    }

    // ReconcileFromTasks 가 "Generate 공정이 하나도 없음 = 전부 성공(공집합)"으로 오판해
    // 검수 대기 중인 작업을 조용히 Succeeded 로 확정해버리면 안 된다
    [Fact]
    public void ReconcileFromTasks_WithPendingReview_DoesNotFalselySucceedOnEmptyOutputs()
    {
        var providerConfigId = Guid.NewGuid();
        var job = PipelineJob.Create(
            AssetCategory.Character, Guid.NewGuid(), Now,
            imageProviderConfigId: providerConfigId, imageModel: "gpt-image-2",
            requiresReview: true);
        job.ApplyParts(["몸통"]);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        job.ReconcileFromTasks(Now);

        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Null(job.CompletedAt);
    }

    // review-gate-staged 사이클 0 — 검수 단계는 상자 검수에서 시작한다
    [Fact]
    public void Create_WithReview_StartsAtBoxesPhase()
    {
        var job = PipelineJob.Create(
            AssetCategory.Character, Guid.NewGuid(), Now,
            imageProviderConfigId: Guid.NewGuid(), imageModel: "gpt-image-2",
            requiresReview: true);

        Assert.Equal(ReviewPhase.Boxes, job.ReviewPhase);
    }

    // 사이클 1 T2 — 상자 확정, stale 없음 → 곧바로 서술 단계 검수 대기, 생성 없음
    [Fact]
    public void ApproveReview_NoStale_GoesStraightToDescriptionsPhase()
    {
        var job = NewPendingReviewJobWithExistingPart();

        job.ApproveReview(Now);

        Assert.Equal(ReviewPhase.Descriptions, job.ReviewPhase);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.Generate);
    }

    // 사이클 1 T5 — 서술 확정이 그제야 보류됐던 팬아웃을 계획한다
    [Fact]
    public void ConfirmDescriptions_PlansTheDeferredFanOut()
    {
        var job = NewPendingReviewJobWithExistingPart();
        job.ApproveReview(Now);

        job.ConfirmDescriptions(Now);

        Assert.Equal(ReviewPhase.Approved, job.ReviewPhase);
        Assert.Equal(1, job.Tasks.Count(t => t.Kind == TaskKind.Generate));
        Assert.Equal(JobStatus.Running, job.Status);
    }

    // ─── occludedby-recompute §함정1·2 — 서술 재작성과 팬아웃의 순서 ───

    // 재작성 대상이 있는 채로 승인한 작업. 승인 시 재작성 공정 하나가 계획되고 Generate 는
    // 아직 안 나간다
    private static PipelineJob NewApprovedJobWithStaleDescription()
    {
        var job = NewPendingReviewJobWithExistingPart();
        job.AddReviewPart("벨트", "Belt", OverlappingBelt, "가죽 벨트");
        job.ApproveReview(Now);
        return job;
    }

    private static PipelineTask RewriteTaskOf(PipelineJob job)
        => job.Tasks.Single(t => t.Kind == TaskKind.RewriteDescriptions);

    // 가려지게 된 파츠가 있으면 승인이 곧바로 팬아웃하지 않는다 — 옛 서술로 그리면
    // 벨트가 바지에 중복해서 그려진다
    [Fact]
    public void ApproveReview_WithStaleDescriptions_PlansRewriteBeforeFanOut()
    {
        var job = NewApprovedJobWithStaleDescription();

        Assert.Single(job.Tasks, t => t.Kind == TaskKind.RewriteDescriptions);
        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.Generate);
    }

    // 편집을 몇 개 했든 재작성은 승인당 한 번이다 (§입력→출력 3)
    [Fact]
    public void ApproveReview_ManyEdits_PlansExactlyOneRewrite()
    {
        var job = NewPendingReviewJobWithExistingPart();
        job.AddReviewPart("벨트", "Belt", OverlappingBelt, "가죽 벨트");
        job.AddReviewPart("버클", "Belt", new Bounds(0.32, 0.51, 0.03, 0.03), "버클");
        job.AddReviewPart("주머니", "Bottom", new Bounds(0.45, 0.6, 0.05, 0.05), "주머니");

        job.ApproveReview(Now);

        Assert.Single(job.Tasks, t => t.Kind == TaskKind.RewriteDescriptions);
    }

    // 아무것도 안 가렸으면 재작성할 것이 없다 — LLM 을 부르지 않고 서술 단계로 간다
    [Fact]
    public void ApproveReview_NoStaleDescriptions_PlansNoRewrite()
    {
        var job = NewPendingReviewJobWithExistingPart();
        job.AddReviewPart("모자", "Headwear", new Bounds(0.7, 0.05, 0.15, 0.1), "모자");

        job.ApproveReview(Now);

        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.RewriteDescriptions);
        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.Generate);
    }

    // §함정1 의 실제 구멍 — 가드를 "미완료" 로 쓰면 재작성이 실패했을 때 열린다.
    // 열리면 갱신 안 된 옛 서술로 이미지가 통째로 나가고 그 비용은 그대로 청구된다
    [Fact]
    public void PlanGenerationFanOut_RewriteFailed_StillDoesNotFanOut()
    {
        var job = NewApprovedJobWithStaleDescription();
        var rewrite = RewriteTaskOf(job);
        rewrite.Claim(Now, Lease);
        rewrite.Fail("모델 응답 형식 오류", Now);

        job.PlanReadyFollowUpTasks();

        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.Generate);
    }

    // 재작성 실패는 뒤 단계의 입력을 없애므로 작업 전체를 실패시킨다 (§함정7)
    [Fact]
    public void ReconcileFromTasks_RewriteFailed_FailsTheJob()
    {
        var job = NewApprovedJobWithStaleDescription();
        var rewrite = RewriteTaskOf(job);
        rewrite.Claim(Now, Lease);
        rewrite.Fail("모델 응답 형식 오류", Now);

        job.ReconcileFromTasks(Now);

        Assert.Equal(JobStatus.Failed, job.Status);
    }

    // 재작성이 도는 동안 작업이 종료로 빠지면 안 된다 — Generate 가 아직 없어서
    // outputs 가 공집합이고, 공집합에 대한 All 은 참이다 (§함정2)
    [Fact]
    public void ReconcileFromTasks_RewriteRunning_DoesNotSucceedWithZeroImages()
    {
        var job = NewApprovedJobWithStaleDescription();

        job.ReconcileFromTasks(Now);

        Assert.False(job.IsTerminal);
    }

    // 사이클 1 T3 — 재작성 성공 반영 후 서술 단계 검수 대기. 생성은 아직 없고, 0장 성공 확정도 없음
    [Fact]
    public void RewriteSucceeded_EntersDescriptionsPhaseWithoutFanOut()
    {
        var job = NewApprovedJobWithStaleDescription();
        Assert.Equal(JobStatus.Running, job.Status);
        var rewrite = RewriteTaskOf(job);
        rewrite.Claim(Now, Lease);
        job.ApplyDescriptionRewrites([("바지", "벨트에 가려진 바지", null)]);
        rewrite.Succeed(Now);

        job.PlanReadyFollowUpTasks();
        job.ReconcileFromTasks(Now);

        Assert.Equal(ReviewPhase.Descriptions, job.ReviewPhase);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.Generate);
    }

    // 사이클 1 T4 — 재작성 실패는 작업 실패. 재시도로 되살리면 성공 후 서술 단계 재진입
    [Fact]
    public void RewriteFailed_ThenRetried_ReentersDescriptionsPhase()
    {
        var job = NewApprovedJobWithStaleDescription();
        var rewrite = RewriteTaskOf(job);
        rewrite.Claim(Now, Lease);
        rewrite.Fail("모델 응답 형식 오류", Now);
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.Failed, job.Status);

        Assert.True(job.RetryOutputTask(rewrite.Id));
        Assert.Equal(JobStatus.Running, job.Status);
        rewrite.Claim(Now, Lease);
        job.ApplyDescriptionRewrites([("바지", "벨트에 가려진 바지", null)]);
        rewrite.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        Assert.Equal(ReviewPhase.Descriptions, job.ReviewPhase);
        Assert.Equal(JobStatus.PendingReview, job.Status);
    }

    // ─── review-gate-staged 사이클 1 — 서술 확인 단계 전이 ───

    // 서술 단계에 도달한 검수 작업 — "바지" 하나, stale 없음
    private static PipelineJob NewDescriptionsPhaseJob()
    {
        var job = NewPendingReviewJobWithExistingPart();
        job.ApproveReview(Now);
        return job;
    }

    // T5 — 서술 빈 파츠가 있으면 확정 거부. 파츠 이름을 메시지에 담아 화면이 위치를 알림
    [Fact]
    public void ConfirmDescriptions_EmptyDescription_Rejected()
    {
        var job = NewPendingReviewJobWithExistingPart();
        job.AddReviewPart("모자", "Headwear", new Bounds(0.7, 0.05, 0.15, 0.1), null);
        // stale 재작성이 모자를 빠뜨린 응답 — 서술이 빈 채로 서술 단계 진입
        job.ApproveReview(Now);
        var rewrite = RewriteTaskOf(job);
        rewrite.Claim(Now, Lease);
        rewrite.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        var ex = Assert.Throws<PartValidationException>(() => job.ConfirmDescriptions(Now));

        Assert.Equal(PartValidationError.DescriptionEmpty, ex.Error);
        Assert.Contains("모자", ex.Message);
        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.Generate);
    }

    // T6 — 상자 단계에서 서술 단계 동작 호출은 단계 불일치
    [Fact]
    public void DescriptionsActions_InBoxesPhase_ThrowPhaseMismatch()
    {
        var job = NewPendingReviewJobWithExistingPart();
        var pants = job.Parts.Single();

        Assert.Throws<ReviewPhaseMismatchException>(() => job.ConfirmDescriptions(Now));
        Assert.Throws<ReviewPhaseMismatchException>(() => job.ReturnToBoxes());
        Assert.Throws<ReviewPhaseMismatchException>(() => job.EditReviewDescription(pants.Id, "새 서술"));
        Assert.Throws<ReviewPhaseMismatchException>(() => job.EditReviewPalette(ValidPalette));
    }

    // T6 — 서술 단계에서 상자 편집·재승인은 단계 불일치. 중복 클릭이 재작성·팬아웃을 두 번 내지 않음
    [Fact]
    public void BoxActions_InDescriptionsPhase_ThrowPhaseMismatch()
    {
        var job = NewDescriptionsPhaseJob();
        var pants = job.Parts.Single();

        Assert.Throws<ReviewPhaseMismatchException>(
            () => job.AddReviewPart("모자", "Headwear", new Bounds(0.7, 0.05, 0.15, 0.1), "모자"));
        Assert.Throws<ReviewPhaseMismatchException>(
            () => job.MoveReviewPlacement(pants.Id, 0, new Bounds(0.1, 0.1, 0.1, 0.1)));
        Assert.Throws<ReviewPhaseMismatchException>(() => job.RemoveReviewPart(pants.Id));
        Assert.Throws<ReviewPhaseMismatchException>(() => job.ApproveReview(Now));
    }

    // 되돌리기 — 상자 단계로, 서술 유지
    [Fact]
    public void ReturnToBoxes_MovesBackToBoxesKeepingDescriptions()
    {
        var job = NewDescriptionsPhaseJob();

        job.ReturnToBoxes();

        Assert.Equal(ReviewPhase.Boxes, job.ReviewPhase);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Equal("다리를 덮는 긴 바지", job.Parts.Single().Description);
    }

    // T7 — 서술 편집: 교체·Human·stale 제거
    [Fact]
    public void EditReviewDescription_ReplacesAndMarksHuman()
    {
        var job = NewPendingReviewJobWithExistingPart();
        job.AddReviewPart("모자", "Headwear", new Bounds(0.7, 0.05, 0.15, 0.1), null);
        job.ApproveReview(Now);
        var rewrite = RewriteTaskOf(job);
        rewrite.Claim(Now, Lease);
        rewrite.Succeed(Now);
        job.PlanReadyFollowUpTasks();
        var hat = job.Parts.Single(p => p.Name == "모자");

        job.EditReviewDescription(hat.Id, "  챙 넓은 밀짚모자  ");

        Assert.Equal("챙 넓은 밀짚모자", hat.Description);
        Assert.Equal(DescriptionSource.Human, hat.DescriptionSource);
        Assert.DoesNotContain("모자", job.DescriptionsStale);
    }

    // T7 — 빈 서술 편집 거부
    [Fact]
    public void EditReviewDescription_Blank_Rejected()
    {
        var job = NewDescriptionsPhaseJob();

        var ex = Assert.Throws<PartValidationException>(
            () => job.EditReviewDescription(job.Parts.Single().Id, "   "));

        Assert.Equal(PartValidationError.DescriptionEmpty, ex.Error);
    }

    // 서술 출처 — 분해 결과는 Model, 재작성은 Rewritten
    [Fact]
    public void DescriptionSource_ModelThenRewritten()
    {
        var job = NewApprovedJobWithStaleDescription();
        var pants = job.Parts.Single(p => p.Name == "바지");
        Assert.Equal(DescriptionSource.Model, pants.DescriptionSource);

        job.ApplyDescriptionRewrites([("바지", "벨트에 가려진 바지", null)]);

        Assert.Equal(DescriptionSource.Rewritten, pants.DescriptionSource);
    }

    // 독립 리뷰 #3 — 재작성 응답이 요청 밖의 사람 서술 파츠를 포함해도 덮지 않음.
    // 재작성 프롬프트가 가리는 파츠 서술을 함께 넘기므로 모델이 그 파츠까지 응답할 수 있음
    [Fact]
    public void ApplyDescriptionRewrites_IgnoresPartsNotStale()
    {
        var job = NewPendingReviewJobWithExistingPart();
        job.AddReviewPart("벨트", "Belt", OverlappingBelt, "사람이 쓴 벨트");
        job.ApproveReview(Now);

        job.ApplyDescriptionRewrites([("바지", "벨트에 가려진 바지", null), ("벨트", "모델이 바꾼 벨트", null)]);

        var belt = job.Parts.Single(p => p.Name == "벨트");
        Assert.Equal("사람이 쓴 벨트", belt.Description);
        Assert.Equal(DescriptionSource.Human, belt.DescriptionSource);
        Assert.Equal("벨트에 가려진 바지", job.Parts.Single(p => p.Name == "바지").Description);
    }

    // 독립 리뷰 #7 — 장면 없는 작업의 팔레트 편집은 검수 대기 아님(409)이 아닌 팔레트 오류
    [Fact]
    public void EditReviewPalette_WithoutScene_ThrowsPaletteInvalid()
    {
        var job = NewDescriptionsPhaseJob();

        var ex = Assert.Throws<PartValidationException>(() => job.EditReviewPalette(ValidPalette));

        Assert.Equal(PartValidationError.PaletteInvalid, ex.Error);
    }

    // 상자 추가 시 사람이 쓴 서술은 Human
    [Fact]
    public void AddReviewPart_WithDescription_MarksHuman()
    {
        var job = NewPendingReviewJobWithExistingPart();

        var hat = job.AddReviewPart("모자", "Headwear", new Bounds(0.7, 0.05, 0.15, 0.1), "모자");

        Assert.Equal(DescriptionSource.Human, hat.DescriptionSource);
    }

    private static readonly PaletteEntry[] ValidPalette =
    [
        new("빨간 스카프", "#C8102E"),
        new("남색 코트", "#1F2A44"),
        new("갈색 가죽", "#6B4226"),
    ];

    // 서술 단계 + 장면 있음
    private static PipelineJob NewDescriptionsPhaseJobWithScene()
    {
        var job = NewPendingReviewJobWithExistingPart();
        job.ApplyScene(TestScene.Default);
        job.ApproveReview(Now);
        return job;
    }

    // T7b — 팔레트 통째 교체, 다른 장면 필드 불변
    [Fact]
    public void EditReviewPalette_ReplacesPaletteOnly()
    {
        var job = NewDescriptionsPhaseJobWithScene();
        var before = job.Scene!;

        job.EditReviewPalette(ValidPalette);

        Assert.Equal(ValidPalette, job.Scene!.Palette);
        // 팔레트 외 필드 불변 — 리스트는 참조 비교라 옛 팔레트로 되돌려 비교
        Assert.Equal(before, job.Scene with { Palette = before.Palette });
    }

    // T7b — 개수 범위(3~8)·이름·Hex 형식 위반 거부
    [Theory]
    [InlineData(2, "이름", "#AABBCC")]
    [InlineData(9, "이름", "#AABBCC")]
    [InlineData(3, " ", "#AABBCC")]
    [InlineData(3, "이름", "#aabbcc")]
    [InlineData(3, "이름", "AABBCC")]
    [InlineData(3, "이름", null)]
    public void EditReviewPalette_Invalid_Rejected(int count, string name, string? hex)
    {
        var job = NewDescriptionsPhaseJobWithScene();
        var entries = Enumerable.Range(0, count).Select(_ => new PaletteEntry(name, hex)).ToArray();

        var ex = Assert.Throws<PartValidationException>(() => job.EditReviewPalette(entries));

        Assert.Equal(PartValidationError.PaletteInvalid, ex.Error);
    }

    // T8 — 되돌리기 후 상자 이동으로 사람 서술 파츠가 겹쳐도 stale 미등록 → 재승인 후 사람 서술 유지
    [Fact]
    public void ReturnToBoxes_HumanDescriptionSurvivesReapproval()
    {
        var job = NewPendingReviewJobWithExistingPart();
        var hat = job.AddReviewPart("모자", "Headwear", new Bounds(0.7, 0.05, 0.15, 0.1), "모자");
        job.ApproveReview(Now);
        var pants = job.Parts.Single(p => p.Name == "바지");
        job.EditReviewDescription(pants.Id, "사람이 쓴 바지 서술");
        job.ReturnToBoxes();

        // 모자를 바지 위로 이동 — 바지가 가려짐
        job.MoveReviewPlacement(hat.Id, 0, OverlappingBelt);
        job.ApproveReview(Now);

        Assert.DoesNotContain("바지", job.DescriptionsStale);
        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.RewriteDescriptions);
        Assert.Equal("사람이 쓴 바지 서술", pants.Description);
        Assert.Equal(ReviewPhase.Descriptions, job.ReviewPhase);
    }

    // review-gate-staged 사이클 1 T1 — 되돌리기·재승인으로 재작성이 둘이면 최신 것을 본다.
    // 옛 재작성의 성공만 보고 팬아웃하면 새 서술이 반영되기 전 옛 서술로 생성이 나간다
    [Fact]
    public void PlanGenerationFanOut_LatestRewritePending_DoesNotFanOut()
    {
        var job = NewApprovedJobWithStaleDescription();
        var first = RewriteTaskOf(job);
        first.Claim(Now, Lease);
        first.Succeed(Now);
        // 두 번째 재작성 — ReturnToBoxes 이전이라 픽스처로 직접 계획
        job.PlanTask(TaskKind.RewriteDescriptions, job.Tasks.Count);

        job.PlanReadyFollowUpTasks();

        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.Generate);
    }

    // 사이클 1 T4b — 게이트 작업의 분해 실패는 검수 대기와 무관하게 작업 실패.
    // 게이트 가드가 실패 판정보다 앞이면 Running 에 영구 정지
    [Fact]
    public void ReconcileFromTasks_GatedDecomposeFailed_FailsTheJob()
    {
        var job = PipelineJob.Create(
            AssetCategory.Character, Guid.NewGuid(), Now,
            imageProviderConfigId: Guid.NewGuid(), imageModel: "gpt-image-2",
            requiresReview: true);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Fail("모델 응답 형식 오류", Now);

        job.ReconcileFromTasks(Now);

        Assert.Equal(JobStatus.Failed, job.Status);
    }

    // RequiresReview=false(기존 동작)는 지금처럼 분해 직후 자동으로 팬아웃된다 —
    // 검수 게이트가 옵트인이지 전면 교체가 아님을 회귀로 고정한다
    [Fact]
    public void PlanReadyFollowUpTasks_WithoutRequiresReview_FansOutImmediately()
    {
        var providerConfigId = Guid.NewGuid();
        var job = PipelineJob.Create(
            AssetCategory.Character, Guid.NewGuid(), Now,
            imageProviderConfigId: providerConfigId, imageModel: "gpt-image-2");
        job.ApplyParts(["몸통"]);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);

        job.PlanReadyFollowUpTasks();

        Assert.Equal(1, job.Tasks.Count(t => t.Kind == TaskKind.Generate));
        Assert.NotEqual(JobStatus.PendingReview, job.Status);
    }

    // ─── review-gate §함정6/T-03 — 검수 화면에서 사람이 사각형으로 파츠를 추가한다 ───

    // 분해까지 끝나 검수 대기 중인 작업 하나 — "바지"가 x=0.2~0.6, y=0.4~0.9 에 이미 있다
    private static PipelineJob NewPendingReviewJobWithExistingPart()
    {
        var providerConfigId = Guid.NewGuid();
        var job = PipelineJob.Create(
            AssetCategory.Character, Guid.NewGuid(), Now,
            imageProviderConfigId: providerConfigId, imageModel: "gpt-image-2",
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
        return job;
    }

    // 바지 안에 거의 다 들어가는 벨트 위치 — 겹침 판정 기준선을 넘는다
    private static readonly Bounds OverlappingBelt = new(0.3, 0.5, 0.1, 0.05);

    // 겹치는 위치에도 추가된다 (occludedby-recompute §입력→출력 1). occludes 를 생략하면
    // 기본 추정 — 겹치는 파츠 전부를 새 파츠가 가린다
    [Fact]
    public void AddReviewPart_Overlapping_DefaultsToOccludingEveryOverlap()
    {
        var job = NewPendingReviewJobWithExistingPart();

        var belt = job.AddReviewPart("벨트", "Belt", OverlappingBelt, "가죽 벨트");

        Assert.Equal("벨트", belt.Name);
        var pants = job.Parts.Single(p => p.Name == "바지");
        Assert.Equal(["벨트"], pants.OccludedBy);
        Assert.Equal(["바지"], job.DescriptionsStale);
    }

    // occludes 를 빈 목록으로 주면 "아무것도 안 가린다" 는 뜻이다 — 생략(기본 추정)과
    // 정반대 결과이므로 둘을 나눠서 고정한다 (§입력→출력 1 V-1)
    [Fact]
    public void AddReviewPart_EmptyOccludes_RecordsNoOcclusion()
    {
        var job = NewPendingReviewJobWithExistingPart();

        job.AddReviewPart("벨트", "Belt", OverlappingBelt, "가죽 벨트", occludes: []);

        Assert.Empty(job.Parts.Single(p => p.Name == "바지").OccludedBy);
        Assert.Empty(job.DescriptionsStale);
    }

    // 검수자가 일부만 체크한 경우 — 목록에 든 파츠만 갱신된다
    [Fact]
    public void AddReviewPart_SelectedOccludes_UpdatesOnlyThoseParts()
    {
        var job = NewPendingReviewJobWithExistingPart();

        job.AddReviewPart("벨트", "Belt", OverlappingBelt, "가죽 벨트", occludes: ["바지"]);

        Assert.Equal(["벨트"], job.Parts.Single(p => p.Name == "바지").OccludedBy);
    }

    // 겹치지도 않는 파츠를 가린다고 보내면 거부한다 — 통과시키면 그 파츠의 서술이
    // 근거 없이 깎인다 (§함정9)
    [Fact]
    public void AddReviewPart_OccludesNonOverlappingPart_Throws()
    {
        var job = NewPendingReviewJobWithExistingPart();
        var independent = new Bounds(0.7, 0.05, 0.15, 0.1);

        var ex = Assert.Throws<PartValidationException>(
            () => job.AddReviewPart("모자", "Headwear", independent, "모자", occludes: ["바지"]));

        Assert.Equal(PartValidationError.NotOverlapping, ex.Error);
        Assert.DoesNotContain(job.Parts, p => p.Name == "모자");
    }

    // 이름은 occludedBy·descriptionsStale·재작성 응답이 전부 쓰는 키다 — 중복을 막는다 (§함정3)
    [Fact]
    public void AddReviewPart_DuplicateName_Throws()
    {
        var job = NewPendingReviewJobWithExistingPart();

        var ex = Assert.Throws<PartValidationException>(
            () => job.AddReviewPart("바지", "Bottom", OverlappingBelt, "또 바지"));

        Assert.Equal(PartValidationError.Duplicate, ex.Error);
    }

    // 앞뒤 공백만 다른 이름도 중복이다 — 정규화 없이 통과시키면 이름 키가 어긋난다 (§함정3)
    [Fact]
    public void AddReviewPart_DuplicateNameDifferingByWhitespace_Throws()
    {
        var job = NewPendingReviewJobWithExistingPart();

        var ex = Assert.Throws<PartValidationException>(
            () => job.AddReviewPart(" 바지 ", "Bottom", OverlappingBelt, "또 바지"));

        Assert.Equal(PartValidationError.Duplicate, ex.Error);
    }

    // 저장되는 이름도 정규화된 형태여야 한다 — 그래야 이후 이름 조회가 맞는다
    [Fact]
    public void AddReviewPart_TrimsName()
    {
        var job = NewPendingReviewJobWithExistingPart();

        var part = job.AddReviewPart(" 모자 ", "Headwear", new Bounds(0.7, 0.05, 0.15, 0.1), null);

        Assert.Equal("모자", part.Name);
    }

    // 겹침 조회는 화면이 체크박스를 그리기 위한 것이다 — 상태를 바꾸지 않는다 (§입력→출력 1)
    [Fact]
    public void FindOverlappingPartNames_ReturnsOverlapsWithoutMutating()
    {
        var job = NewPendingReviewJobWithExistingPart();

        Assert.Equal(["바지"], job.FindOverlappingPartNames(OverlappingBelt));
        Assert.Empty(job.FindOverlappingPartNames(new Bounds(0.7, 0.05, 0.15, 0.1)));
        Assert.Single(job.Parts);
        Assert.Empty(job.DescriptionsStale);
    }

    // 기존 파츠와 안 겹치는 위치("완전히 빠뜨린 독립 파츠")는 정상 추가된다
    [Fact]
    public void AddReviewPart_NoOverlap_AddsThePart()
    {
        var job = NewPendingReviewJobWithExistingPart();
        var independent = new Bounds(0.7, 0.05, 0.15, 0.1);   // 바지와 전혀 안 겹치는 모자 위치

        var part = job.AddReviewPart("모자", "Headwear", independent, "챙 넓은 모자");

        Assert.Equal("모자", part.Name);
        Assert.Equal(independent, part.Placements.Single());
        Assert.Contains(job.Parts, p => p.Name == "모자");
    }

    // 프레임 밖 좌표는 기존 IsWithinFrame 규칙을 그대로 재사용해 거부한다(§함정2, 새로 안 만든다)
    [Fact]
    public void AddReviewPart_OutOfFrame_Throws()
    {
        var job = NewPendingReviewJobWithExistingPart();
        var outOfFrame = new Bounds(0.9, 0.9, 0.3, 0.3);   // x+w, y+h 가 1을 넘음

        var ex = Assert.Throws<PartValidationException>(
            () => job.AddReviewPart("장식", "Accessory", outOfFrame, "설명"));

        Assert.Equal(PartValidationError.BoundsOutOfRange, ex.Error);
    }

    // 검수 대기 상태가 아니면(승인 완료·검수 게이트 미적용) 추가 자체를 막는다
    [Fact]
    public void AddReviewPart_RejectedWhenNotPendingReview()
    {
        var job = NewJob();   // RequiresReview=false, 검수 대기 상태가 아님

        Assert.Throws<InvalidOperationException>(
            () => job.AddReviewPart("모자", "Headwear", new Bounds(0.7, 0.05, 0.15, 0.1), "설명"));
    }

    // 상자를 끌어 옮기면 그 배치만 새 좌표가 된다 — 이름·설명은 그대로 남는다
    [Fact]
    public void MoveReviewPlacement_ReplacesThatPlacement()
    {
        var job = NewPendingReviewJobWithExistingPart();
        var pants = job.Parts.Single(p => p.Name == "바지");
        var moved = new Bounds(0.25, 0.45, 0.4, 0.5);

        job.MoveReviewPlacement(pants.Id, ordinal: 0, moved);

        Assert.Equal(moved, pants.Placements.Single());
        Assert.Equal("다리를 덮는 긴 바지", pants.Description);
    }

    // 옮기면 가림 관계가 달라진다 — 벗어난 파츠의 서술은 다시 써야 한다
    [Fact]
    public void MoveReviewPlacement_AwayFromOverlap_DropsTheOccluder()
    {
        var job = NewPendingReviewJobWithExistingPart();
        var belt = job.AddReviewPart("벨트", "Belt", OverlappingBelt, "가죽 벨트");
        var pants = job.Parts.Single(p => p.Name == "바지");
        Assert.Contains("벨트", pants.OccludedBy);

        // 바지에서 완전히 벗어난 자리로 옮긴다
        job.MoveReviewPlacement(belt.Id, ordinal: 0, new Bounds(0.75, 0.05, 0.1, 0.05));

        Assert.DoesNotContain("벨트", pants.OccludedBy);
        Assert.Contains("바지", job.DescriptionsStale);
    }

    // 옮긴 자리에서 새로 겹치면 그 파츠를 가린다고 본다 — 추가 경로와 같은 추정이다
    [Fact]
    public void MoveReviewPlacement_OntoAnotherPart_AddsTheOccluder()
    {
        var job = NewPendingReviewJobWithExistingPart();
        // 처음엔 바지와 안 겹치는 자리
        var badge = job.AddReviewPart("배지", "Accessory", new Bounds(0.75, 0.05, 0.05, 0.05), "배지");
        var pants = job.Parts.Single(p => p.Name == "바지");
        Assert.DoesNotContain("배지", pants.OccludedBy);

        job.MoveReviewPlacement(badge.Id, ordinal: 0, OverlappingBelt);

        Assert.Contains("배지", pants.OccludedBy);
        Assert.Contains("바지", job.DescriptionsStale);
    }

    // 검수 화면에서 잘못 탐지된 파츠를 제외한다(§입력→출력, 편집=추가/삭제)
    [Fact]
    public void RemoveReviewPart_RemovesTheGivenPart()
    {
        var job = NewPendingReviewJobWithExistingPart();
        var pants = job.Parts.Single(p => p.Name == "바지");

        job.RemoveReviewPart(pants.Id);

        Assert.Empty(job.Parts);
    }

    // 지운 파츠 이름이 남의 occludedBy 에 유령으로 남으면 안 된다. 서술도 "벨트에 가린"
    // 상태로 남아 있으므로 그 파츠를 다시 재작성 대상으로 올린다 (§입력→출력 2)
    [Fact]
    public void RemoveReviewPart_ClearsOcclusionReferencesAndRemarksTarget()
    {
        var job = NewPendingReviewJobWithExistingPart();
        var belt = job.AddReviewPart("벨트", "Belt", OverlappingBelt, "가죽 벨트");

        job.RemoveReviewPart(belt.Id);

        Assert.Empty(job.Parts.Single(p => p.Name == "바지").OccludedBy);
        Assert.Equal(["바지"], job.DescriptionsStale);
    }

    // 지운 파츠 자신은 재작성 대상에서 빠져야 한다. 남겨두면 승인 시 "없는 파츠를 다시
    // 써라"가 되어 재작성이 실패하고, 그 실패가 작업 전체를 죽인다 (§입력→출력 2)
    [Fact]
    public void RemoveReviewPart_DropsItselfFromStaleTargets()
    {
        var job = NewPendingReviewJobWithExistingPart();
        var belt = job.AddReviewPart("벨트", "Belt", OverlappingBelt, "가죽 벨트");
        // 버클이 벨트를 가린다 — 이 시점에 "벨트"가 재작성 대상이 된다
        job.AddReviewPart("버클", "Belt", new Bounds(0.32, 0.51, 0.03, 0.03), "금속 버클",
            occludes: ["벨트"]);

        job.RemoveReviewPart(belt.Id);

        Assert.DoesNotContain("벨트", job.DescriptionsStale);
    }

    // 가림 방향은 기하와 사람의 선택만으로 정해진다 — 추가 순서(Ordinal)에 의존하지
    // 않는다. v3 가 쓰던 "나중에 추가한 쪽이 가림" 규칙으로 되돌아가지 않게 고정한다.
    //
    // **완전한 순서 비의존은 성립하지 않는다.** 버클을 먼저 그리면 그 뒤에 그린 벨트가
    // "버클이 나를 가린다" 를 표현할 방법이 없다 — 반대 방향 미지원(§제외 범위 V-2)의
    // 결과다. 여기서 고정하는 것은 무관한 파츠의 추가가 판정을 흔들지 않는다는 것이다
    [Fact]
    public void AddReviewPart_UnrelatedInsertions_DoNotChangeOcclusion()
    {
        var hat = new Bounds(0.7, 0.05, 0.15, 0.1);

        var beltFirst = NewPendingReviewJobWithExistingPart();
        beltFirst.AddReviewPart("벨트", "Belt", OverlappingBelt, "벨트");
        beltFirst.AddReviewPart("모자", "Headwear", hat, "모자");

        var hatFirst = NewPendingReviewJobWithExistingPart();
        hatFirst.AddReviewPart("모자", "Headwear", hat, "모자");
        hatFirst.AddReviewPart("벨트", "Belt", OverlappingBelt, "벨트");

        Assert.Equal(["벨트"], beltFirst.Parts.Single(p => p.Name == "바지").OccludedBy);
        Assert.Equal(["벨트"], hatFirst.Parts.Single(p => p.Name == "바지").OccludedBy);
        Assert.Equal(beltFirst.DescriptionsStale, hatFirst.DescriptionsStale);
    }

    // §구현 범위 3 — 재작성이 실패해도 사람이 다시 시도할 수 있어야 한다.
    // 화이트리스트에 없으면 복구 경로가 없어 검수자가 그린 좌표가 통째로 사라진다
    [Fact]
    public void RetryOutputTask_AllowsFailedRewrite()
    {
        var job = NewApprovedJobWithStaleDescription();
        var rewrite = RewriteTaskOf(job);
        rewrite.Claim(Now, Lease);
        rewrite.Fail("모델 응답 형식 오류", Now);

        Assert.True(job.RetryOutputTask(rewrite.Id));
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Pending, rewrite.Status);
    }

    // 설명을 안 쓰면 서술 재작성이 대신 채운다 — 좌표 기반 대체 문구는 그림을 그릴 수
    // 있는 지시가 아니다(좌표는 생성 프롬프트에 안 들어간다). 사람은 사각형과 이름만 준다
    [Fact]
    public void AddReviewPart_WithoutDescription_QueuesItselfForRewrite()
    {
        var job = NewPendingReviewJobWithExistingPart();

        var part = job.AddReviewPart(
            "모자", category: null, new Bounds(0.7, 0.05, 0.15, 0.1), description: null);

        Assert.Null(part.Description);
        Assert.Contains("모자", job.DescriptionsStale);
    }

    // 설명을 직접 쓰면 그대로 쓰고 재작성 대상이 되지 않는다 — 불필요한 호출을 만들지 않는다
    [Fact]
    public void AddReviewPart_WithDescription_DoesNotQueueItself()
    {
        var job = NewPendingReviewJobWithExistingPart();

        job.AddReviewPart("모자", "Headwear", new Bounds(0.7, 0.05, 0.15, 0.1), "챙 넓은 밀짚모자");

        Assert.DoesNotContain("모자", job.DescriptionsStale);
    }

    // 재작성 응답이 카테고리도 채운다 — 사람이 안 쓴 경우에만
    [Fact]
    public void ApplyDescriptionRewrites_FillsMissingCategory()
    {
        var job = NewPendingReviewJobWithExistingPart();
        job.AddReviewPart("모자", category: null, new Bounds(0.7, 0.05, 0.15, 0.1), null);

        job.ApplyDescriptionRewrites([("모자", "챙 넓은 밀짚모자", "Headwear")]);

        var part = job.Parts.Single(p => p.Name == "모자");
        Assert.Equal("챙 넓은 밀짚모자", part.Description);
        Assert.Equal("Headwear", part.Category);
    }

    // 사람이 쓴 카테고리는 모델이 덮지 못한다
    [Fact]
    public void ApplyDescriptionRewrites_KeepsHumanCategory()
    {
        var job = NewPendingReviewJobWithExistingPart();
        job.AddReviewPart("모자", "사람이쓴값", new Bounds(0.7, 0.05, 0.15, 0.1), null);

        job.ApplyDescriptionRewrites([("모자", "서술", "모델이쓴값")]);

        Assert.Equal("사람이쓴값", job.Parts.Single(p => p.Name == "모자").Category);
    }

    // 검수 대기 상태가 아니면 삭제도 막는다 — 추가와 대칭
    [Fact]
    public void RemoveReviewPart_RejectedWhenNotPendingReview()
    {
        var job = NewJob();

        Assert.Throws<InvalidOperationException>(() => job.RemoveReviewPart(Guid.NewGuid()));
    }

    // 없는 파츠를 지우려 하면 조용히 무시하지 않고 알려준다
    [Fact]
    public void RemoveReviewPart_UnknownPartId_Throws()
    {
        var job = NewPendingReviewJobWithExistingPart();

        Assert.Throws<KeyNotFoundException>(() => job.RemoveReviewPart(Guid.NewGuid()));
    }

    // ─── workstream B §6.1/§9-3~5 — 캐스케이드 스킵 ───

    private static PipelineJob NewCharacterFanOutJob(params string[] parts)
    {
        var providerConfigId = Guid.NewGuid();
        var job = PipelineJob.Create(
            AssetCategory.Character, Guid.NewGuid(), Now,
            imageProviderConfigId: providerConfigId, imageModel: "gpt-image-2");
        job.ApplyParts(parts);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);
        return job;
    }

    private static PipelineTask GenerateTaskFor(PipelineJob job, Guid partId, ViewDirection direction)
        => job.Tasks.Single(t =>
            t.Kind == TaskKind.Generate && t.PartId == partId && t.ViewDirection == direction);

    // §9-3 — 워커 경로: 정면 실패 → ReconcileFromTasks 가 비정면 3을 Failed(스킵)로 내리고,
    // 다른 파츠가 성공했으면 부분 성공으로 확정된다(행업하지 않는다)
    [Fact]
    public void ReconcileFromTasks_FrontFailure_CascadesSiblingsAndReachesPartialSuccess()
    {
        var job = NewCharacterFanOutJob("몸통", "다리");
        var body = job.Parts.Single(p => p.Name == "몸통");
        var legs = job.Parts.Single(p => p.Name == "다리");

        var bodyFront = GenerateTaskFor(job, body.Id, ViewDirection.Front);
        bodyFront.Claim(Now, Lease);
        bodyFront.Fail("PROVIDER_CALL_FAILED", Now);

        // 다른 파츠는 넷 다 성공 — 부분 성공이 성립할 근거
        foreach (var direction in new[]
                 { ViewDirection.Front, ViewDirection.Right, ViewDirection.Back, ViewDirection.Left })
        {
            var task = GenerateTaskFor(job, legs.Id, direction);
            task.Claim(Now, Lease);
            task.Succeed(Now);
        }

        job.ReconcileFromTasks(Now);

        // 행업하지 않고 종료 판정이 돈다 — 몸통 4장 전부 잃었지만 다리는 남았다(§6.2)
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);

        var bodySiblings = new[]
        {
            GenerateTaskFor(job, body.Id, ViewDirection.Right),
            GenerateTaskFor(job, body.Id, ViewDirection.Back),
            GenerateTaskFor(job, body.Id, ViewDirection.Left),
        };
        Assert.All(bodySiblings, t =>
        {
            Assert.Equal(Noxtend.Domain.Job.TaskStatus.Failed, t.Status);
            Assert.Equal("정면 실패로 스킵", t.FailureReason);
        });
    }

    // §9-4 — 스위퍼 경로: SweepStaleTasksHandler 도 결국 ReconcileFromTasks 를 부르므로
    // task.Fail 로 정면이 죽어도(리스 만료+한도) 같은 캐스케이드가 돈다(리뷰 지적 2 회귀 고정)
    [Fact]
    public void ReconcileFromTasks_TriggersRegardlessOfWhoFailedTheFrontTask()
    {
        var job = NewCharacterFanOutJob("몸통");
        var body = job.Parts.Single();
        var front = GenerateTaskFor(job, body.Id, ViewDirection.Front);

        // 스위퍼가 하는 것과 같은 모양 — 워커 완료 경로를 거치지 않고 직접 Fail 을 확정한다
        front.Claim(Now, Lease);
        front.Fail("재시도 한도를 초과했습니다", Now);

        job.ReconcileFromTasks(Now);

        var siblings = job.Tasks.Where(t =>
            t.Kind == TaskKind.Generate && t.ViewDirection != ViewDirection.Front);
        Assert.All(siblings, t => Assert.Equal(Noxtend.Domain.Job.TaskStatus.Failed, t.Status));
    }

    // §9-5 — 재시도 재개: RetryOutputTask(정면) 이 정면 + 스킵된 형제를 함께 Pending 으로
    // 되돌린다. 정면이 다시 성공하면 EnqueueReadyTasksAsync 가 형제를 자동 적재한다
    [Fact]
    public void RetryOutputTask_Front_ResetsCascadedSiblingsToo()
    {
        var job = NewCharacterFanOutJob("몸통");
        var body = job.Parts.Single();
        var front = GenerateTaskFor(job, body.Id, ViewDirection.Front);

        front.Claim(Now, Lease);
        front.Fail("PROVIDER_CALL_FAILED", Now);
        job.ReconcileFromTasks(Now);   // 캐스케이드로 형제 3을 Failed 로 내린다

        var reset = job.RetryOutputTask(front.Id);

        Assert.True(reset);
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Pending, front.Status);

        var siblings = job.Tasks.Where(t =>
            t.Kind == TaskKind.Generate && t.ViewDirection != ViewDirection.Front);
        Assert.All(siblings, t =>
        {
            Assert.Equal(Noxtend.Domain.Job.TaskStatus.Pending, t.Status);
            Assert.Null(t.FailureReason);
        });
    }

    // 독립 리뷰 지적(2026-08-15, 머지 차단) — 부모 정면이 아직 Failed 인 캐스케이드
    // 형제를 단독 재시도하면, 형제는 Pending 이 되지만 IsReadyToRun 은 계속 거짓이라
    // 큐에 영원히 안 실린다. 그런데 RetryOutputTask 는 true 를 돌려주고 작업을
    // Running 으로 되돌려버려 "재시도 접수됨"으로 보이는 채 아무 일도 안 일어나는
    // 거짓 재개가 된다. 정면을 먼저 재시도해야 한다(§6.1-③) — 단독 재시도는 거부한다
    [Fact]
    public void RetryOutputTask_RejectsASiblingWhoseFrontIsStillFailed()
    {
        var job = NewCharacterFanOutJob("몸통");
        var body = job.Parts.Single();
        var front = GenerateTaskFor(job, body.Id, ViewDirection.Front);
        var right = GenerateTaskFor(job, body.Id, ViewDirection.Right);

        front.Claim(Now, Lease);
        front.Fail("PROVIDER_CALL_FAILED", Now);
        job.ReconcileFromTasks(Now);   // 캐스케이드로 right 를 Failed(스킵)로 내린다
        var statusBeforeRetry = job.Status;

        var retried = job.RetryOutputTask(right.Id);

        Assert.False(retried);
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Failed, right.Status);
        // 거짓으로 Running 이 되어 아무 일도 안 일어나는 채로 열려 있으면 안 된다
        Assert.Equal(statusBeforeRetry, job.Status);
    }

    // 부모 정면이 이미 성공한 뒤라면 형제의 개별 재시도는 정상대로 허용돼야 한다
    // (§4.7 방향별 개별 재시도 보존) — 위 거부 로직이 정상 경로까지 막으면 안 된다
    [Fact]
    public void RetryOutputTask_AllowsASiblingRetry_OnceItsFrontHasSucceeded()
    {
        var job = NewCharacterFanOutJob("몸통");
        var body = job.Parts.Single();
        var front = GenerateTaskFor(job, body.Id, ViewDirection.Front);
        var right = GenerateTaskFor(job, body.Id, ViewDirection.Right);

        front.Claim(Now, Lease);
        front.Fail("PROVIDER_CALL_FAILED", Now);
        job.ReconcileFromTasks(Now);

        // 정면을 재시도해 형제까지 함께 되살리고, 이번엔 정면이 성공한다
        Assert.True(job.RetryOutputTask(front.Id));
        front.Claim(Now, Lease);
        front.Succeed(Now);

        // 형제는 이제 Pending 이다 — 실행해서 독립적으로(정면과 무관하게) 실패했다고 가정
        right.Claim(Now, Lease);
        right.Fail("PROVIDER_CALL_FAILED", Now);

        // 부모 정면이 Succeeded 이므로 이번엔 단독 재시도가 정상 허용된다
        Assert.True(job.RetryOutputTask(right.Id));
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Pending, right.Status);
    }

    // generation-rate-limiting §3② — 백오프 대기 중인 공정은 시각이 되기 전엔 준비 판정에서 빠진다
    [Fact]
    public void IsReadyToRun_FalseBeforeNotBefore_TrueOnceItPasses()
    {
        var job = NewJob();
        var task = job.PlanTask(TaskKind.Extract, ordinal: 0);
        task.Claim(Now, Lease);
        var notBefore = Now + TimeSpan.FromSeconds(5);
        task.ReleaseForRetry(notBefore);

        Assert.False(job.IsReadyToRun(task, notBefore - TimeSpan.FromMilliseconds(1)));
        Assert.True(job.IsReadyToRun(task, notBefore));
    }

    // notBefore 없이 되돌린 공정(스위퍼 경로)은 기존처럼 즉시 준비 상태다 — 회귀 방지
    [Fact]
    public void IsReadyToRun_TrueImmediately_WhenReleasedWithoutNotBefore()
    {
        var job = NewJob();
        var task = job.PlanTask(TaskKind.Extract, ordinal: 0);
        task.Claim(Now, Lease);
        task.ReleaseForRetry();

        Assert.True(job.IsReadyToRun(task, Now));
    }

    // selective-view-generation 엣지케이스 1: Failed / Canceled 작업에 대해 PlanSelectedViews 호출 시 거절
    [Fact]
    public void PlanSelectedViews_ThrowsInvalidOperationException_WhenJobIsFailedOrCanceled()
    {
        var job = NewJob();
        var task = job.PlanTask(TaskKind.Extract, 0);
        task.Claim(Now, Lease);
        task.Fail("FAIL", Now);
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.Failed, job.Status);

        Assert.Throws<InvalidOperationException>(
            () => job.PlanSelectedViews([ViewDirection.Right]));
    }

    // selective-view-generation 엣지케이스 2: 정면 뷰가 실패한 파츠는 비정면 뷰를 계획하지 않고 스킵
    [Fact]
    public void PlanSelectedViews_SkipsPart_WhenFrontTaskIsFailed()
    {
        var providerConfigId = Guid.NewGuid();
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now,
            imageProviderConfigId: providerConfigId, imageModel: "gpt-image-2");
        job.ApplyParts(["등대"]);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        var front = job.Tasks.Single(t => t.Kind == TaskKind.Generate && t.ViewDirection == ViewDirection.Front);
        front.Claim(Now, Lease);
        front.Fail("GENERATION_EMPTY_RESPONSE", Now);

        var planned = job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back]);

        Assert.Empty(planned);
    }

    // selective-view-generation 엣지케이스 3: 부분 성공 후 비정면 재개 시 FailureReason 및 CompletedAt 초기화
    [Fact]
    public void PlanSelectedViews_ResetsFailureReason_WhenReopeningToRunning()
    {
        var providerConfigId = Guid.NewGuid();
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now,
            imageProviderConfigId: providerConfigId, imageModel: "gpt-image-2");
        job.ApplyParts(["등대", "부두"]);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();
        job.MarkRunning();

        var generates = job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToList();
        generates[0].Claim(Now, Lease);
        generates[0].Succeed(Now);
        generates[1].Claim(Now, Lease);
        generates[1].Fail("FAILED_MSG", Now);
        job.ReconcileFromTasks(Now);

        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);

        var planned = job.PlanSelectedViews([ViewDirection.Right]);

        Assert.NotEmpty(planned);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.FailureReason);
    }

    // selective-view-generation 엣지케이스 4: 추가할 공정이 없을 때는 Running으로 상태 변이하지 않음
    [Fact]
    public void PlanSelectedViews_DoesNotMutateStatusToRunning_WhenNoNewTasksPlanned()
    {
        var providerConfigId = Guid.NewGuid();
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now,
            imageProviderConfigId: providerConfigId, imageModel: "gpt-image-2");
        job.ApplyParts(["등대"]);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        var front = job.Tasks.Single(t => t.Kind == TaskKind.Generate);
        front.Claim(Now, Lease);
        front.Succeed(Now);
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.Succeeded, job.Status);

        // 이미 생성된 뷰 또는 이미 요청한 뷰만 중복 요청하는 경우
        var planned = job.PlanSelectedViews([ViewDirection.Front]);

        Assert.Empty(planned);
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }
}
