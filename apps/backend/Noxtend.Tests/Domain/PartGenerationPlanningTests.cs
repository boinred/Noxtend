using Noxtend.Domain.Job;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Domain;

/// <summary>
/// Design Ref: §8.1 #1~8 — 팬아웃 계획과 부분 성공 판정.
///
/// 사이클 #7 이 깨는 두 전제를 도메인 수준에서 고정한다:
/// 공정 수가 접수 시점에 정해지지 않는다는 것, 그리고 실패가 항상 작업 실패는 아니라는 것.
/// </summary>
public sealed class PartGenerationPlanningTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly Guid ImageProvider = Guid.NewGuid();
    private const string ImageModel = "gpt-image-1";

    /// <summary>분해까지 성공한 작업 — 팬아웃 직전 상태.</summary>
    private static PipelineJob JobReadyToFanOut(params string[] partNames)
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, ImageProvider, ImageModel);

        var analyze = job.PlanTask(TaskKind.Analyze, 0);
        var extract = job.PlanTask(TaskKind.Extract, 1, dependsOnTaskId: analyze.Id);
        var decompose = job.PlanTask(TaskKind.Decompose, 2, dependsOnTaskId: extract.Id);

        foreach (var task in new[] { analyze, extract, decompose })
        {
            task.Claim(Now, Lease);
            task.Succeed(Now);
        }

        job.ApplyParts(partNames);
        job.MarkRunning();
        return job;
    }

    private static PipelineTask[] GenerateTasks(PipelineJob job)
        => [.. job.Tasks.Where(t => t.Kind == TaskKind.Generate)];

    // ─── #1 팬아웃 개수와 멱등성 ───

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void PlanReadyFollowUpTasks_CreatesFrontViewTaskPerPart(int partCount)
    {
        var names = Enumerable.Range(0, partCount).Select(i => $"파츠{i}").ToArray();
        var job = JobReadyToFanOut(names);

        job.PlanReadyFollowUpTasks();

        var generated = GenerateTasks(job);
        Assert.Equal(partCount, generated.Length);

        // 파츠별 정면 생성 계약 (selective-view-generation §2)
        Assert.All(job.Parts, part => Assert.Equal(
            [ViewDirection.Front],
            generated.Where(task => task.PartId == part.Id).Select(task => task.ViewDirection)));
    }

    [Fact]
    public void PlanReadyFollowUpTasks_IsIdempotent()
    {
        var job = JobReadyToFanOut("등대", "부두", "어선");

        job.PlanReadyFollowUpTasks();
        job.PlanReadyFollowUpTasks();

        // 큐 메시지 중복 배달이나 분해 재시도로 두 번 불릴 수 있다 — 그때 공정이 두 배면 비용도 두 배다
        Assert.Equal(3, GenerateTasks(job).Length);
    }

    // ─── #2 선행 조건 ───

    [Fact]
    public void PlanReadyFollowUpTasks_DoesNothingWhenDecomposeHasNotSucceeded()
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, ImageProvider, ImageModel);
        var analyze = job.PlanTask(TaskKind.Analyze, 0);
        job.PlanTask(TaskKind.Decompose, 1, dependsOnTaskId: analyze.Id);
        job.ApplyParts(["등대"]);

        job.PlanReadyFollowUpTasks();

        Assert.Empty(GenerateTasks(job));
    }

    [Fact]
    public void PlanReadyFollowUpTasks_DoesNothingWhenNoPartsExist()
    {
        // V-2 — 분해가 이미 막지만 계획에서도 방어한다
        var job = JobReadyToFanOut();

        job.PlanReadyFollowUpTasks();

        Assert.Empty(GenerateTasks(job));
    }

    [Fact]
    public void PlanReadyFollowUpTasks_DoesNothingWhenNoImageProviderWasChosen()
    {
        // 이미지 공급자 없이 접수된 작업 — 앞 세 단계로 끝난다
        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.ApplyParts(["등대"]);

        job.PlanReadyFollowUpTasks();

        Assert.Empty(GenerateTasks(job));
    }

    // ─── #3 순서와 파츠 결속 ───

    [Fact]
    public void PlanReadyFollowUpTasks_BindsTasksToPartsInPartOrder()
    {
        var job = JobReadyToFanOut("등대", "부두", "어선");

        job.PlanReadyFollowUpTasks();

        var generated = GenerateTasks(job);
        // C-2 — 사용자가 본 파츠 순서와 진행 표시가 어긋나지 않아야 한다
        Assert.Equal(
            job.Parts.Select(part => (Guid?)part.Id),
            generated.Select(task => task.PartId));
        Assert.Equal(Enumerable.Range(3, 3), generated.Select(task => task.Ordinal));
    }

    /// <summary>
    /// **배경도 정면이 먼저다** (download-view-consistency §3.1 · SC-03).
    ///
    /// 4방향이 각자 원본만 보고 그리면 방향마다 다른 물체가 나온다 — 캐릭터가
    /// 이미 쓰는 정면 우선 사슬을 배경에도 건다. 비정면은 완성된 정면을 참조로 받는다.
    /// </summary>
    [Fact]
    public void PlanReadyFollowUpTasks_ChainsNonFrontViewsToTheFront()
    {
        var job = JobReadyToFanOut("등대", "부두");
        var decomposeId = job.Tasks.Single(t => t.Kind == TaskKind.Decompose).Id;

        job.PlanReadyFollowUpTasks();

        foreach (var part in job.Parts)
        {
            var partTasks = GenerateTasks(job).Where(t => t.PartId == part.Id).ToList();
            var front = partTasks.Single(t => t.ViewDirection == ViewDirection.Front);

            // 정면은 분해에, 비정면 셋은 정면에 의존한다 — 단일 부모 규칙 유지
            Assert.Equal(decomposeId, front.DependsOnTaskId);
            Assert.All(
                partTasks.Where(t => t.ViewDirection != ViewDirection.Front),
                t => Assert.Equal(front.Id, t.DependsOnTaskId));
        }
    }

    [Fact]
    public void PlanReadyFollowUpTasks_AssignsTheImageProviderAndModelToEveryTask()
    {
        var job = JobReadyToFanOut("등대", "부두");

        job.PlanReadyFollowUpTasks();

        // D-11 — 작업 하나에 생성 모델 하나. 파츠마다 다르면 화풍이 달라진다
        Assert.All(GenerateTasks(job), t =>
        {
            Assert.Equal(ImageProvider, t.ProviderConfigId);
            Assert.Equal(ImageModel, t.Model);
        });
    }

    [Fact]
    public void PlanReadyFollowUpTasks_LeavesEarlierTasksUnboundToParts()
    {
        var job = JobReadyToFanOut("등대");

        job.PlanReadyFollowUpTasks();

        Assert.All(
            job.Tasks.Where(t => t.Kind != TaskKind.Generate),
            t => Assert.Null(t.PartId));
    }

    // ─── #4~8 부분 성공 판정 ───

    /// <summary>팬아웃까지 마친 작업과 그 생성 공정들.</summary>
    private static (PipelineJob Job, PipelineTask[] Generate) FannedOut(int partCount)
    {
        var job = JobReadyToFanOut([.. Enumerable.Range(0, partCount).Select(i => $"파츠{i}")]);
        job.PlanReadyFollowUpTasks();
        return (job, GenerateTasks(job));
    }

    /// <summary>지정한 인덱지만 실패시키고 모든 방향 공정을 종료한다.</summary>
    private static void FinishGenerations(PipelineTask[] tasks, params int[] failedIndices)
    {
        foreach (var (task, index) in tasks.Select((task, index) => (task, index)))
        {
            task.Claim(Now, Lease);
            if (failedIndices.Contains(index))
            {
                task.Fail("GENERATION_EMPTY_RESPONSE", Now);
            }
            else
            {
                task.Succeed(Now);
            }
        }
    }

    [Fact]
    public void ReconcileFromTasks_PartiallySucceedsWhenSomeGenerationsFail()
    {
        var (job, generate) = FannedOut(3);

        FinishGenerations(generate, generate.Length - 1);

        job.ReconcileFromTasks(Now);

        // 원칙 ③ — 성공으로 뭉개면 사용자가 3장을 받았다고 믿는다
        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        Assert.Equal(Now, job.CompletedAt);
    }

    [Fact]
    public void ReconcileFromTasks_FailsWhenEveryGenerationFails()
    {
        var (job, generate) = FannedOut(2);

        foreach (var task in generate)
        {
            task.Claim(Now, Lease);
            task.Fail("GENERATION_EMPTY_RESPONSE", Now);
        }

        job.ReconcileFromTasks(Now);

        // V-3 — 건질 것이 없으면 부분 성공이 아니다
        Assert.Equal(JobStatus.Failed, job.Status);
    }

    [Fact]
    public void ReconcileFromTasks_SucceedsWhenEveryGenerationSucceeds()
    {
        var (job, generate) = FannedOut(3);

        foreach (var task in generate)
        {
            task.Claim(Now, Lease);
            task.Succeed(Now);
        }

        job.ReconcileFromTasks(Now);

        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public void ReconcileFromTasks_FailsWhenANonGenerationTaskFails()
    {
        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);
        var analyze = job.PlanTask(TaskKind.Analyze, 0);
        analyze.Claim(Now, Lease);
        analyze.Fail("PROVIDER_CALL_FAILED", Now);

        job.ReconcileFromTasks(Now);

        // FR-07 — 앞 세 단계는 뒤가 못 돈다. 부분 성공이 아니라 작업 실패다
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("PROVIDER_CALL_FAILED", job.FailureReason);
    }

    [Fact]
    public void ReconcileFromTasks_StaysOpenWhileAGenerationIsStillPending()
    {
        var (job, generate) = FannedOut(3);

        generate[0].Claim(Now, Lease);
        generate[0].Succeed(Now);
        generate[1].Claim(Now, Lease);
        generate[1].Fail("GENERATION_EMPTY_RESPONSE", Now);

        job.ReconcileFromTasks(Now);

        // 아직 안 끝난 공정이 있으면 부분 성공을 확정할 수 없다 — 남은 것이 성공할 수도 있다
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Null(job.CompletedAt);
    }

    [Fact]
    public void ReconcileFromTasks_CancelsWhenAGenerationWasCanceled()
    {
        var (job, generate) = FannedOut(2);

        generate[0].Claim(Now, Lease);
        generate[0].Succeed(Now);
        generate[1].Cancel(Now);

        job.ReconcileFromTasks(Now);

        Assert.Equal(JobStatus.Canceled, job.Status);
    }

    [Fact]
    public void PartiallySucceededIsTerminal()
    {
        var (job, generate) = FannedOut(2);

        FinishGenerations(generate, generate.Length - 1);
        job.ReconcileFromTasks(Now);

        // 부분 성공도 끝난 작업이다 — 다시 공정을 붙이거나 취소할 수 없다
        Assert.True(job.IsTerminal);
        Assert.Throws<InvalidOperationException>(() => job.Cancel(Now));
    }

    // ─── 생성 이미지 연결 ───

    [Fact]
    public void AttachGeneratedImage_LinksTheImageToItsPart()
    {
        var (job, generate) = FannedOut(2);
        var part = job.Parts[0];

        job.AttachGeneratedImage(part.Id, generate[0].Id, "generated/abc.png", "image/png", 2048, Now);

        var image = Assert.Single(job.GeneratedImages);
        Assert.Equal(part.Id, image.PartId);
        Assert.Equal(generate[0].Id, image.TaskId);
        Assert.Equal(job.Id, image.JobId);
        Assert.Equal("generated/abc.png", image.BlobKey);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(2048, image.SizeBytes);
        Assert.Equal(image.Id, part.GeneratedImageId);
    }

    [Fact]
    public void AttachGeneratedImage_KeepsOlderRowsAndRepointsThePart()
    {
        var (job, generate) = FannedOut(1);
        var part = job.Parts[0];

        job.AttachGeneratedImage(part.Id, generate[0].Id, "generated/first.png", "image/png", 100, Now);
        job.AttachGeneratedImage(part.Id, generate[0].Id, "generated/second.png", "image/png", 200, Now);

        // 이전 것을 지우지 않는 것은 골든 판정의 비교 재료이기 때문이다 (§3.1)
        Assert.Equal(2, job.GeneratedImages.Count);
        Assert.Equal(job.GeneratedImages[1].Id, part.GeneratedImageId);
    }

    [Fact]
    public void AttachGeneratedImage_RejectsAnUnknownPart()
    {
        var (job, generate) = FannedOut(1);

        Assert.Throws<InvalidOperationException>(
            () => job.AttachGeneratedImage(Guid.NewGuid(), generate[0].Id, "k", "image/png", 1, Now));
    }
}
