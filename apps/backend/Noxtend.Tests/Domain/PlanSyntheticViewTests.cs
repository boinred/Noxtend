using Noxtend.Domain.Job;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 대칭(합성 이미지) 생성용 공정 계획 (spec 20260917, ADR mirror-as-synthetic-generated-image).
///
/// **`TaskKind.Generate`가 아니라 `TaskKind.Synthesize`를 쓴다.** `PlanSelectedViews`는
/// "그 파츠·방향에 `Generate` 공정이 존재하면" 건너뛰는데, 합성용으로 `Generate`를
/// 재사용하면 사용자가 이후 그 방향의 진짜 이미지를 영구히 못 만들게 된다.
/// </summary>
public sealed class PlanSyntheticViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    [Fact]
    public void PlansASynthesizeTask_BoundToThePartAndTargetDirection()
    {
        var job = FannedOut("칼");
        var partId = job.Parts.Single().Id;

        var task = job.PlanSyntheticView(partId, ViewDirection.Right, Now);

        Assert.Equal(TaskKind.Synthesize, task.Kind);
        Assert.Equal(partId, task.PartId);
        Assert.Equal(ViewDirection.Right, task.ViewDirection);
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Pending, task.Status);
    }

    /// <summary>
    /// 합성용 공정이 있어도 같은 방향의 진짜 이미지 생성 계획을 막지 않는다 — 이게 이
    /// 기능 전체의 존재 이유다.
    /// </summary>
    [Fact]
    public void DoesNotBlockPlanningARealGenerateTask_ForTheSameDirection()
    {
        var job = FannedOut("칼");
        var partId = job.Parts.Single().Id;
        job.PlanSyntheticView(partId, ViewDirection.Right, Now);

        var planned = job.PlanSelectedViews([ViewDirection.Right]);

        var realTask = Assert.Single(planned);
        Assert.Equal(TaskKind.Generate, realTask.Kind);
        Assert.Equal(ViewDirection.Right, realTask.ViewDirection);
    }

    [Fact]
    public void UnknownPart_Throws()
    {
        var job = FannedOut("칼");

        Assert.Throws<InvalidOperationException>(
            () => job.PlanSyntheticView(Guid.NewGuid(), ViewDirection.Right, Now));
    }

    [Fact]
    public void TerminalJob_Reopens()
    {
        var job = FannedOut("칼");
        var partId = job.Parts.Single().Id;
        job.ReconcileFromTasks(Now);
        Assert.True(job.IsTerminal);

        job.PlanSyntheticView(partId, ViewDirection.Right, Now);

        Assert.Equal(JobStatus.Running, job.Status);
    }

    private static PipelineJob FannedOut(params string[] partNames)
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini-image");

        job.ApplyParts(partNames);
        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate && t.ViewDirection == ViewDirection.Front))
        {
            task.Claim(Now, Lease);
            task.Succeed(Now);
        }

        return job;
    }
}
