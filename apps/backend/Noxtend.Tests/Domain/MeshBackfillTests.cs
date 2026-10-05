using Noxtend.Domain.Job;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 끝난 작업에 3D 를 뒤늦게 붙인다.
///
/// Design Ref: §4.1~4.3 · Plan D-01
///
/// **이 기능의 값어치는 이미지를 다시 만들지 않는 것이다.** 전체 재실행 비용의 99.5% 가
/// 이미 갖고 있는 이미지 40장이라, 그것을 건드리지 않는다는 사실이 검사의 중심에 있다.
/// </summary>
public sealed class MeshBackfillTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly Guid MeshProvider = Guid.NewGuid();
    private const string MeshModel = "P1-20260311";

    [Fact]
    public void FinishedJob_GetsOneReconstructPerReadyPart()
    {
        var job = Finished("가로등", "벤치");

        Assert.True(job.AddMeshProduction(MeshProvider, MeshModel, Later));

        Assert.Equal(2, Reconstructs(job).Length);
        Assert.True(job.ProducesMeshes);
        Assert.Equal(MeshModel, job.MeshModel);
    }

    /// <summary>
    /// **이미지 공정을 하나도 건드리지 않는다** (NFR-01).
    ///
    /// 이 기능이 존재하는 이유가 그것이다. 하나라도 다시 대기로 돌아가면 워커가 집어
    /// 이미지를 새로 만들고, 아끼려던 $1.34 가 그대로 나간다.
    /// </summary>
    [Fact]
    public void ImageTasksAreUntouched()
    {
        var job = Finished("가로등");
        var before = Snapshot(job);

        job.AddMeshProduction(MeshProvider, MeshModel, Later);

        Assert.Equal(before, Snapshot(job));
    }

    [Fact]
    public void ReopeningClearsTheFinishedMarks()
    {
        var job = Finished("가로등");

        job.AddMeshProduction(MeshProvider, MeshModel, Later);

        // 되돌리지 않으면 오케스트레이터가 종료 판정에서 먼저 빠져나가 큐에 넣지 않는다
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.FailureReason);
    }

    [Fact]
    public void PartWithoutFourImages_GetsNoReconstruct()
    {
        var job = Finished(
            false,
            ("가로등", Directions),
            ("벤치", [ViewDirection.Front, ViewDirection.Right]));

        var partial = job.Parts.Single(part => part.Name == "벤치").Id;

        job.AddMeshProduction(MeshProvider, MeshModel, Later);

        // 세 장으로 제출하면 공급자가 없는 면을 지어낸다
        Assert.DoesNotContain(Reconstructs(job), task => task.PartId == partial);
    }

    // ─── 거절 규칙 (§4.1) ───

    [Fact]
    public void JobThatAlreadyChoseMesh_IsRefused()
    {
        var job = Finished(meshSelected: true, ("가로등", Directions));

        // 한 작업에 모델 조합이 둘이 되면 어느 mesh 가 무엇인지 알 수 없다
        Assert.False(job.AddMeshProduction(MeshProvider, MeshModel, Later));
    }

    [Fact]
    public void JobWithNoReadyPart_IsRefused()
    {
        var job = Finished(false, ("벤치", [ViewDirection.Front]));

        Assert.False(job.AddMeshProduction(MeshProvider, MeshModel, Later));
        Assert.False(job.ProducesMeshes);
    }

    [Fact]
    public void JobWithoutParts_IsRefused()
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini-image");

        Assert.False(job.AddMeshProduction(MeshProvider, MeshModel, Later));
    }

    /// <summary>
    /// 취소는 "끝났다" 가 아니라 "하지 말라" 다 (D-08).
    ///
    /// 사용자가 멈춘 작업을 되살리면 그 뜻이 사라진다.
    /// </summary>
    [Fact]
    public void CanceledJob_IsRefused()
    {
        // 파츠 하나는 네 장이 다 됐는데 다른 파츠가 도는 중에 사용자가 멈춘 작업 —
        // 3D 를 만들 재료는 있지만 만들면 안 되는 상황이다
        var job = Canceled();

        Assert.Equal(JobStatus.Canceled, job.Status);
        Assert.Contains(job.Parts, part => Ready(job, part.Id));

        Assert.False(job.AddMeshProduction(MeshProvider, MeshModel, Later));
        Assert.Equal(JobStatus.Canceled, job.Status);
    }

    /// <summary>
    /// 실패한 작업도 대상이다 (D-08).
    ///
    /// 이미지가 일부만 실패했다면 4장이 갖춰진 파츠가 남아 있고, 그 파츠는 3D 를 만들 수
    /// 있다. 작업이 실패했다는 사실이 남은 재료를 못 쓰게 하지 않는다.
    /// </summary>
    [Fact]
    public void PartiallySucceededJob_CanStillGetMeshes()
    {
        var job = Finished(
            false,
            ("가로등", Directions),
            ("벤치", [ViewDirection.Front]));

        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
        Assert.True(job.AddMeshProduction(MeshProvider, MeshModel, Later));

        // 4장이 갖춰진 파츠만 만든다
        Assert.Single(Reconstructs(job));
    }

    /// <summary>거절될 때는 아무것도 바꾸지 않는다 — 반쪽 상태를 남기면 다음 시도가 꼬인다.</summary>
    [Fact]
    public void RefusalChangesNothing()
    {
        var job = Finished(false, ("벤치", [ViewDirection.Front]));
        var before = job.Status;

        job.AddMeshProduction(MeshProvider, MeshModel, Later);

        Assert.Equal(before, job.Status);
        Assert.Null(job.MeshProviderConfigId);
        Assert.Null(job.MeshModel);
    }

    // ─── 멱등 (§4.2) ───

    [Fact]
    public void SecondCall_AddsNothing()
    {
        var job = Finished("가로등", "벤치");
        job.AddMeshProduction(MeshProvider, MeshModel, Later);

        // 두 번째는 "이미 3D 를 골랐다" 로 걸린다 (D-06)
        Assert.False(job.AddMeshProduction(MeshProvider, MeshModel, Later));
        Assert.Equal(2, Reconstructs(job).Length);
    }

    // ─── 상태 재조정 (§4.3) ───

    [Fact]
    public void EveryMeshSucceeds_JobReturnsToSucceeded()
    {
        var job = Finished("가로등");
        job.AddMeshProduction(MeshProvider, MeshModel, Later);

        FinishMeshes(job, succeed: true);
        job.ReconcileFromTasks(Later);

        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    /// <summary>
    /// 강등을 허용한다 (Plan D-04).
    ///
    /// 3D 를 요청했는데 못 만들었으면 부분 성공이 사실이다.
    /// </summary>
    [Fact]
    public void MeshFails_JobIsDemotedToPartiallySucceeded()
    {
        var job = Finished("가로등");
        job.AddMeshProduction(MeshProvider, MeshModel, Later);

        FinishMeshes(job, succeed: false);
        job.ReconcileFromTasks(Later);

        Assert.Equal(JobStatus.PartiallySucceeded, job.Status);
    }

    // ─── 설정 ───

    /// <summary>
    /// 이미지가 끝난 작업.
    ///
    /// <paramref name="parts"/> 는 파츠 이름과 **성공한 방향**의 짝이다. 실제 경로를 그대로
    /// 밟는다 — 파츠를 먼저 다 정하고 팬아웃한 뒤 방향별로 성패를 가른다.
    /// </summary>
    private static PipelineJob Finished(
        bool meshSelected = false,
        params (string Name, ViewDirection[] Succeeded)[] parts)
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now,
            Guid.NewGuid(), "gemini-image",
            meshSelected ? MeshProvider : null,
            meshSelected ? MeshModel : null);

        if (parts.Length > 0)
        {
            job.ApplyParts([.. parts.Select(p => p.Name)]);
        }

        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        var wanted = parts.ToDictionary(
            p => job.Parts.Single(part => part.Name == p.Name).Id,
            p => p.Succeeded);

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate).ToArray())
        {
            task.Claim(Now, Lease);

            if (wanted[task.PartId!.Value].Contains(task.ViewDirection!.Value))
            {
                job.AttachGeneratedImage(
                    task.PartId!.Value, task.Id,
                    $"generated/{task.PartId}-{task.ViewDirection}.png", "image/png", 2048, Now);
                task.Succeed(Now);
            }
            else
            {
                task.Fail("GENERATION_EMPTY_RESPONSE", Now);
            }
        }

        job.ReconcileFromTasks(Now);
        return job;
    }

    /// <summary>
    /// 파츠 하나는 네 장이 다 됐고 다른 파츠가 도는 중에 취소된 작업.
    ///
    /// **재료는 있는데 만들면 안 되는 상황**이라야 거절 규칙이 실제로 검증된다. 재료가
    /// 없으면 "만들 파츠 없음" 으로도 거절되어 무엇이 막았는지 알 수 없다.
    /// </summary>
    private static PipelineJob Canceled()
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini-image");

        job.ApplyParts(["가로등", "벤치"]);

        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, Lease);
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        var done = job.Parts.Single(part => part.Name == "가로등").Id;

        foreach (var task in job.Tasks.Where(t => t.Kind == TaskKind.Generate && t.PartId == done).ToArray())
        {
            task.Claim(Now, Lease);
            job.AttachGeneratedImage(
                done, task.Id, $"generated/{task.ViewDirection}.png", "image/png", 2048, Now);
            task.Succeed(Now);
        }

        // 벤치는 아직 도는 중이다 — 그래서 취소가 성립한다
        job.Cancel(Now);
        return job;
    }

    /// <summary>네 방향 최신 이미지가 다 있는가 — 도메인의 판정과 같은 규칙.</summary>
    private static bool Ready(PipelineJob job, Guid partId)
        => job.GeneratedImages
            .Where(image => image.PartId == partId)
            .Select(image => image.ViewDirection)
            .Distinct()
            .Count() == 4;

    /// <summary>네 방향이 다 성공한 파츠들.</summary>
    private static PipelineJob Finished(params string[] partNames)
        => Finished(false, [.. partNames.Select(name => (name, Directions))]);

    private static void FinishMeshes(PipelineJob job, bool succeed)
    {
        foreach (var task in Reconstructs(job))
        {
            task.Claim(Later, Lease);

            if (succeed)
            {
                task.Succeed(Later);
            }
            else
            {
                task.Fail("MESH_TASK_FAILED", Later);
            }
        }
    }

    private static readonly ViewDirection[] Directions =
        [ViewDirection.Front, ViewDirection.Right, ViewDirection.Back, ViewDirection.Left];

    private static PipelineTask[] Reconstructs(PipelineJob job)
        => [.. job.Tasks.Where(task => task.Kind == TaskKind.Reconstruct)];

    /// <summary>이미지 공정의 상태·시도 수를 찍어 둔다. 하나라도 달라지면 드러난다.</summary>
    private static (Guid Id, TaskStatus Status, int Attempts)[] Snapshot(PipelineJob job)
        => [.. job.Tasks
            .Where(task => task.Kind != TaskKind.Reconstruct)
            .OrderBy(task => task.Ordinal)
            .Select(task => (task.Id, task.Status, task.AttemptCount))];
}
