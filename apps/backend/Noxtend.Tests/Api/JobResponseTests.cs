using Noxtend.Api.Contracts;
using Noxtend.Application.Job;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Job;
using Noxtend.Tests.Domain;

namespace Noxtend.Tests.Api;

public sealed class JobResponseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 합성(반전) 이미지가 더 최신이어도 갤러리 응답은 실제 이미지를 보여준다 (spec 20260917).
    ///
    /// 합성 이미지는 "이 방향의 현재 이미지"가 아니다 — 이번 3D 요청에만 쓰이는 파생물이다.
    /// </summary>
    [Fact]
    public void SyntheticImage_DoesNotSupersedeTheRealImageInTheGallery()
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "image-model");
        job.ApplyParts(["등대"]);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, TimeSpan.FromMinutes(1));
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        var frontTask = job.Tasks.Single(t => t.Kind == TaskKind.Generate && t.ViewDirection == ViewDirection.Front);
        job.AttachGeneratedImage(frontTask.PartId!.Value, frontTask.Id, "front.png", "image/png", 100, Now);

        job.PlanSelectedViews([ViewDirection.Right]);
        var rightTask = job.Tasks.Single(t => t.Kind == TaskKind.Generate && t.ViewDirection == ViewDirection.Right);
        job.AttachGeneratedImage(rightTask.PartId!.Value, rightTask.Id, "right-real.png", "image/png", 100, Now);
        var realRightImageId = job.GeneratedImages.Single(i => i.BlobKey == "right-real.png").Id;

        var later = Now.AddMinutes(1);
        job.AttachGeneratedImage(
            rightTask.PartId!.Value, rightTask.Id, "right-synthetic.png", "image/png", 100, later, isSynthetic: true);

        var response = JobResponse.From(new JobDetails(job, []));
        var rightImage = Assert.Single(response.Parts)
            .GeneratedImages.Single(image => image.ViewDirection == "right");

        Assert.Equal(realRightImageId, rightImage.Id);
    }

    [Fact]
    public void ExposesFourDirectionImagesInStableOrder()
    {
        // 파츠 생성 결과 API의 Meshy 입력 순서 계약
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "image-model");
        job.ApplyParts(["등대"]);
        var decompose = job.PlanTask(TaskKind.Decompose, 0);
        decompose.Claim(Now, TimeSpan.FromMinutes(1));
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();
        job.PlanSelectedViews([ViewDirection.Right, ViewDirection.Back, ViewDirection.Left]);

        foreach (var task in job.Tasks.Where(task => task.Kind == TaskKind.Generate))
        {
            job.AttachGeneratedImage(
                task.PartId!.Value, task.Id, $"{task.ViewDirection}.png", "image/png", 100, Now);
        }

        var response = JobResponse.From(new JobDetails(job, []));
        var part = Assert.Single(response.Parts);

        Assert.Equal(
            ["front", "right", "back", "left"],
            part.GeneratedImages.Select(image => image.ViewDirection));
        Assert.Equal(
            ["front", "right", "back", "left"],
            response.Tasks.Where(task => task.Kind == "generate").Select(task => task.ViewDirection));
    }

    [Fact]
    public void ExposesSelectedTextAndImageModels()
    {
        // 진행 화면 표기와 재시도 프리필의 입력 — 저장돼 있어도 응답에 없으면 화면이 모른다
        var textProvider = Guid.NewGuid();
        var imageProvider = Guid.NewGuid();
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, imageProvider, "gemini-3-image");
        job.PlanTask(TaskKind.Analyze, 0, null, textProvider, "gpt-5.6-luna");

        var response = JobResponse.From(new JobDetails(job, []));

        Assert.Equal(textProvider, response.Models.Text!.ProviderConfigId);
        Assert.Equal("gpt-5.6-luna", response.Models.Text.Model);
        Assert.Equal(imageProvider, response.Models.Image!.ProviderConfigId);
        Assert.Equal("gemini-3-image", response.Models.Image.Model);
    }

    [Fact]
    public void ExposesGenderAndPartHints()
    {
        // 캐릭터 "다시 시도" 이어받기(character-mesh-ui §FR-08)의 입력 — 저장은 됐는데
        // 응답에 없으면 화면이 재현할 수 없다
        var hints = PartHintCodec.Serialize([new PartHint("상의", 1, null), new PartHint("장갑", 2, "손목형")]);
        var job = PipelineJob.Create(
            AssetCategory.Character, Guid.NewGuid(), Now, gender: Gender.Female, partHints: hints);

        var response = JobResponse.From(new JobDetails(job, []));

        Assert.Equal("female", response.Gender);
        Assert.Equal(
            [("상의", 1, null), ("장갑", 2, "손목형")],
            response.PartHints.Select(hint => (hint.Type, hint.Count, hint.Variant)));
    }

    [Fact]
    public void OmitsGenderAndPartHintsForNonCharacterJobs()
    {
        // 배경·오브젝트는 성별·힌트를 보낸 적이 없다 — null/빈 배열로 조용히 채워져야 한다
        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);

        var response = JobResponse.From(new JobDetails(job, []));

        Assert.Null(response.Gender);
        Assert.Empty(response.PartHints);
    }

    [Fact]
    public void OmitsModelsThatWereNeverSelected()
    {
        // 이미지 생성 없이 접수된 옛 작업 — 없는 것을 빈 문자열로 뭉개면 화면이 빈 배지를 그린다
        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);
        job.PlanTask(TaskKind.Analyze, 0);

        var response = JobResponse.From(new JobDetails(job, []));

        Assert.Null(response.Models.Text);
        Assert.Null(response.Models.Image);
    }

    /// <summary>
    /// B-11 — API 가 배치를 배열로 낸다 (§7 · FR-06).
    ///
    /// 분해 전 파츠는 **빈 배열**이다. `null` 을 쓰지 않는 이유는 "아직 없다" 와 "0개다" 가
    /// 여기서 같은 뜻이고, 화면이 null 검사를 하지 않아도 되기 때문이다.
    /// </summary>
    [Fact]
    public void PartResponse_ExposesPlacementsAsAnArray()
    {
        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);
        job.ApplyScene(TestScene.Default);
        job.ApplyParts(["가로수", "부두"]);

        var beforeDecompose = JobResponse.From(new JobDetails(job, []));
        Assert.All(beforeDecompose.Parts, part => Assert.Empty(part.Placements));

        job.ApplyPartDetails([
            new PartDetail("가로수", "식생", "가로수", [
                new Bounds(0.31, 0.62, 0.04, 0.09),
                new Bounds(0.48, 0.58, 0.03, 0.07),
            ], 1, []),
            new PartDetail("부두", "구조물", "부두", [new Bounds(0.1, 0.1, 0.2, 0.2)], 2, []),
        ]);

        var response = JobResponse.From(new JobDetails(job, []));
        var tree = response.Parts.Single(p => p.Name == "가로수");

        Assert.Equal([0.31, 0.48], tree.Placements.Select(p => p.X));
    }

    /// <summary>구조화 높이는 legacy 설명과 함께 API에서 손실 없이 전달된다.</summary>
    [Fact]
    public void SceneResponse_ExposesNumericScaleHeight()
    {
        var response = SceneResponse.From(TestScene.Default);

        Assert.Equal("부두 기둥", response.Scale.Object);
        Assert.Equal("높이 3m", response.Scale.RealWorldSize);
        Assert.Equal(3, response.Scale.HeightMeters);
    }
}
