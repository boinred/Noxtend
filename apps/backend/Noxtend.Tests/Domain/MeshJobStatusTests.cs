using Noxtend.Domain.Job;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 3D 가 섞인 작업의 결말.
///
/// Design Ref: §4.6~4.7 · Plan D-09
///
/// **3D 실패는 앞을 막는 실패가 아니다.** 앞 세 단계가 실패하면 뒤 단계의 입력이 사라져
/// 작업 전체가 실패지만, 3D 가 전부 실패해도 네 방향 이미지는 그대로 남는다. 그것만으로도
/// 사용자가 쓸 것이 있으므로 건질 것이 없다고 말하면 안 된다.
/// </summary>
public sealed class MeshJobStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    [Fact]
    public void EveryMeshSucceeds_JobSucceeds()
    {
        var job = ReadyForMesh("가로등", "벤치");

        FinishReconstructs(job, failedParts: []);
        job.ReconcileFromTasks(Now);

        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public void SomeMeshesFail_JobPartiallySucceeds()
    {
        var job = ReadyForMesh("가로등", "벤치");

        FinishReconstructs(job, failedParts: ["벤치"]);
        job.ReconcileFromTasks(Now);

        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
    }

    /// <summary>
    /// **여기가 갈림길이다.** 3D 가 전부 실패해도 이미지 여덟 장이 남아 있다.
    ///
    /// 실패로 확정하면 사용자는 아무것도 못 받았다고 읽는데, 실제로는 가장 비싼 중간
    /// 결과가 그대로 있다. 3D 만 다시 시도하면 되는 상황이다.
    /// </summary>
    [Fact]
    public void EveryMeshFails_JobStillPartiallySucceeds()
    {
        var job = ReadyForMesh("가로등", "벤치");

        FinishReconstructs(job, failedParts: ["가로등", "벤치"]);
        job.ReconcileFromTasks(Now);

        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
    }

    /// <summary>이미지도 3D 도 전부 실패하면 그때는 건질 것이 없다.</summary>
    [Fact]
    public void EveryImageAndMeshFails_JobFails()
    {
        var job = FannedOut("가로등");

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate))
        {
            task.Claim(Now, Lease);
            task.Fail("GENERATION_EMPTY_RESPONSE", Now);
        }

        job.PlanReadyFollowUpTasks();
        job.ReconcileFromTasks(Now);

        // 이미지가 없으니 3D 공정 자체가 계획되지 않는다
        Assert.DoesNotContain(job.Tasks, t => t.Kind == TaskKind.Reconstruct);
        Assert.Equal(JobStatus.Failed, job.Status);
    }

    [Fact]
    public void MeshStillRunning_JobStaysOpen()
    {
        var job = ReadyForMesh("가로등");

        job.ReconcileFromTasks(Now);

        // 3D 가 아직 안 끝났다 — 이미지가 다 성공했다고 작업을 닫으면 결과가 잘린다
        Assert.False(job.IsTerminal);
        Assert.Null(job.CompletedAt);
    }

    // ─── 수동 재시도 (§4.7) ───

    [Fact]
    public void FailedMesh_CanBeRetried()
    {
        var job = ReadyForMesh("가로등");
        FinishReconstructs(job, failedParts: ["가로등"]);
        job.ReconcileFromTasks(Now);

        var reconstruct = job.Tasks.Single(t => t.Kind == TaskKind.Reconstruct);

        Assert.True(job.RetryOutputTask(reconstruct.Id));

        // 부분 성공은 종료 상태다 — 되돌리지 않으면 오케스트레이터가 큐에 넣지 않는다
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(TaskStatus.Pending, reconstruct.Status);
        Assert.Equal(0, reconstruct.AttemptCount);
    }

    [Fact]
    public void FailedImage_CanStillBeRetried()
    {
        var job = FannedOut("가로등");
        var generate = job.Tasks.First(t => t.Kind == TaskKind.Generate);
        generate.Claim(Now, Lease);
        generate.Fail("GENERATION_EMPTY_RESPONSE", Now);

        Assert.True(job.RetryOutputTask(generate.Id));
    }

    [Fact]
    public void FailedAnalyze_CannotBeRetried()
    {
        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);
        var analyze = job.PlanTask(TaskKind.Analyze, 0);
        analyze.Claim(Now, Lease);
        analyze.Fail("PROVIDER_CALL_FAILED", Now);

        // 앞 세 단계를 다시 돌리면 그 뒤의 결과가 전부 다른 장면의 것이 된다
        Assert.False(job.RetryOutputTask(analyze.Id));
    }

    // ─── 설정 ───

    private static readonly Guid MeshProvider = Guid.NewGuid();

    private static PipelineJob FannedOut(params string[] partNames)
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now,
            Guid.NewGuid(), "gemini-image", MeshProvider, "P1-20260311");

        job.ApplyParts(partNames);
        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        return job;
    }

    /// <summary>이미지가 전부 성공해 3D 공정까지 계획된 작업.</summary>
    private static PipelineJob ReadyForMesh(params string[] partNames)
    {
        var job = FannedOut(partNames);
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToArray())
        {
            task.Claim(Now, Lease);
            job.AttachGeneratedImage(
                task.PartId!.Value, task.Id,
                $"generated/{task.PartId}-{task.ViewDirection}.png", "image/png", 2048, Now);
            task.Succeed(Now);
        }

        job.PlanReadyFollowUpTasks();
        return job;
    }

    private static void FinishReconstructs(PipelineJob job, string[] failedParts)
    {
        var failedIds = job.Parts
            .Where(part => failedParts.Contains(part.Name))
            .Select(part => part.Id)
            .ToHashSet();

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Reconstruct).ToArray())
        {
            task.Claim(Now, Lease);

            if (failedIds.Contains(task.PartId!.Value))
            {
                task.Fail("MESH_TASK_FAILED", Now);
            }
            else
            {
                task.Succeed(Now);
            }
        }
    }
}
