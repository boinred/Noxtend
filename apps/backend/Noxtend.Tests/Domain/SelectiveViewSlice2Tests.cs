using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Infrastructure.Mesh;

namespace Noxtend.Tests.Domain;

/// <summary>
/// 선택적 비정면 생성 및 3D 모델 제출 유연화 (Slice 2) 도메인/인프라 단위 테스트.
/// </summary>
public sealed class SelectiveViewSlice2Tests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MeshInputSet_WithFrontAndOneNonFront_Succeeds()
    {
        var frontId = Guid.NewGuid();
        var backId = Guid.NewGuid();

        var inputSet = new MeshInputSet(frontId, backImageId: backId);

        Assert.Equal(frontId, inputSet.FrontImageId);
        Assert.Equal(backId, inputSet.BackImageId);
        Assert.Null(inputSet.RightImageId);
        Assert.Null(inputSet.LeftImageId);
        Assert.Equal(2, inputSet.Pairs().Count());
    }

    [Fact]
    public void MeshInputSet_WithoutFront_ThrowsArgumentException()
    {
        var rightId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new MeshInputSet(Guid.Empty, rightImageId: rightId));
    }

    [Fact]
    public void MeshInputSet_WithOnlyFront_Succeeds()
    {
        // 공급자별 최소 장수는 도메인이 아니라 각 어댑터가 검증한다 (spec 20260917 설계 결정)
        var frontId = Guid.NewGuid();

        var inputSet = new MeshInputSet(frontId);

        Assert.Equal(frontId, inputSet.FrontImageId);
        Assert.Single(inputSet.Pairs());
    }

    [Fact]
    public void MeshInputSet_WithEmptyNonFrontIdOrDuplicateFrontId_ThrowsArgumentException()
    {
        var frontId = Guid.NewGuid();

        // Guid.Empty non-front ID
        Assert.Throws<ArgumentException>(() => new MeshInputSet(frontId, rightImageId: Guid.Empty));

        // Duplicate frontId passed as non-front ID
        Assert.Throws<ArgumentException>(() => new MeshInputSet(frontId, backImageId: frontId));
    }

    [Fact]
    public void ReturnToDescriptionsFromGeneration_CancelsInFlightTasksAndObsoletesPartImages()
    {
        var job = PipelineJob.Create(AssetCategory.Character, Guid.NewGuid(), Now, imageProviderConfigId: Guid.NewGuid(), imageModel: "dall-e-3", requiresReview: true);
        job.ApplyParts(["상의"]);
        var part = job.Parts.Single();

        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, TimeSpan.FromMinutes(2));
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        job.ApproveReview(Now);
        job.EditReviewDescription(part.Id, "멋진 검은 상의");
        job.ConfirmDescriptions(Now);

        var frontTask = job.Tasks.First(t => t.Kind == TaskKind.Generate);
        frontTask.Claim(Now, TimeSpan.FromMinutes(2));
        frontTask.Succeed(Now);

        job.AttachGeneratedImage(part.Id, frontTask.Id, "blob-key-front", "image/png", 1024, Now);

        // 추가 비정면 생성 공정 스케줄링 (진행 중 상태)
        var selectedTasks = job.PlanSelectedViews([ViewDirection.Right]);
        var rightTask = Assert.Single(selectedTasks);
        rightTask.Claim(Now, TimeSpan.FromMinutes(2));

        job.ReturnToDescriptionsFromGeneration(part.Id, Now);

        Assert.True(job.GeneratedImages.First().IsObsoleted);
        Assert.Equal(ReviewPhase.Descriptions, job.ReviewPhase);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Equal(2, job.ReviewRevision);
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Canceled, rightTask.Status);
        Assert.Contains(part.Name, job.DescriptionsStale);

        // 복귀 후 뒤늦게 도착한 생성물도 IsObsoleted 처리됨
        job.AttachGeneratedImage(part.Id, rightTask.Id, "blob-key-right-late", "image/png", 1024, Now);
        Assert.True(job.GeneratedImages.Last().IsObsoleted);
    }
}
