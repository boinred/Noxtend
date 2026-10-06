using Noxtend.Application.Scene;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Scene;
using Noxtend.Infrastructure.Persistence.InMemory;
using Noxtend.Tests.Domain;

namespace Noxtend.Tests.Application;

/// <summary>
/// 조립 명세 조회. Design Ref: scene-assembly §3.4 · §4
///
/// **낡음 판정이 이 핸들러의 존재 이유다.** 유도 자체는 Composer 검사가 본다 — 여기서
/// 보는 것은 "언제 다시 유도하는가" 와 "무엇이 빠졌다고 말하는가" 다.
/// </summary>
public sealed class SceneLayoutHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);

    /// <summary>B-06 — GLB 없는 파츠는 빠지고, 빠졌다고 말한다 (FR-08).</summary>
    [Fact]
    public async Task Handle_ExcludesPartsWithoutMeshes_AndNamesThem()
    {
        var (handler, jobs) = Build();
        var job = SeedJob(withMeshOnFirstPart: true);
        await jobs.AddAsync(job, CancellationToken.None);

        var result = await handler.HandleAsync(job.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var view = result.Value!;

        // 첫 파츠(배치 2개)만 조립된다
        Assert.Equal(2, view.Instances.Count);
        Assert.All(view.Instances, i => Assert.Equal(job.Parts[0].Id, i.PartId));
        Assert.Equal(["뒤쪽 성벽"], view.MissingPartNames);
    }

    /// <summary>B-07 — 완성 GLB 가 0개면 빈 명세다. 오류가 아니다 (§4.2).</summary>
    [Fact]
    public async Task Handle_ReturnsEmptyLayout_WhenNoMeshIsReady()
    {
        var (handler, jobs) = Build();
        var job = SeedJob(withMeshOnFirstPart: false);
        await jobs.AddAsync(job, CancellationToken.None);

        var result = await handler.HandleAsync(job.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Instances);
        Assert.Equal(2, result.Value.MissingPartNames.Count);
    }

    /// <summary>
    /// B-08 — **GLB 가 늘면 명세가 낡은 것이다** (§3.4). 저장된 것을 그대로 주면
    /// 새로 완성된 파츠가 화면에 영영 안 나타난다.
    /// </summary>
    [Fact]
    public async Task Handle_Recomposes_WhenTheMeshCountChanged()
    {
        var (handler, jobs) = Build();
        var job = SeedJob(withMeshOnFirstPart: true);
        await jobs.AddAsync(job, CancellationToken.None);

        // 첫 조회 — 파츠 하나만 조립
        var first = await handler.HandleAsync(job.Id, CancellationToken.None);
        Assert.Equal(2, first.Value!.Instances.Count);

        // 둘째 파츠의 3D 가 도착한다
        AttachMesh(job, job.Parts[1].Id);

        var second = await handler.HandleAsync(job.Id, CancellationToken.None);

        Assert.Equal(3, second.Value!.Instances.Count);
        Assert.Empty(second.Value.MissingPartNames);
    }

    /// <summary>
    /// SC-07 — **유도 규칙이 바뀌면 저장된 레이아웃도 낡는다**
    /// (background-scale-calibration #18 §4.6 · D-06).
    ///
    /// 서명이 mesh 조합만 담던 시절에는 공식을 고쳐도 캐시가 남아 수정이 화면에
    /// 보이지 않았다 — 카메라 사이클에서 실측한 결함이다. 옛 판으로 저장된 활성
    /// 레이아웃은 다음 조회에서 Superseded 되고 새 revision 이 나와야 한다.
    /// </summary>
    [Fact]
    public async Task Handle_RecomposesWhenTheCompositionVersionMoved()
    {
        var (handler, jobs, layouts) = BuildWithLayouts();
        var job = SeedJob(withMeshOnFirstPart: true);
        await jobs.AddAsync(job, CancellationToken.None);

        // 옛 판으로 합성된 활성 레이아웃 — 판이 서명에 섞여 있으므로 지금 서명과 다르다
        var stale = SceneLayout.ComposeActive(
            job.Id, revision: 1, [], "STALE-COMPOSITION-VERSION", sourceMeshCount: 1,
            SceneStaging.ComposeCamera(null, []), SceneStaging.ComposeLight(null), Now);
        await layouts.AddAsync(stale, CancellationToken.None);

        var view = await handler.HandleAsync(job.Id, CancellationToken.None);

        Assert.True(view.Value!.Revision > stale.Revision, "새 revision 이 나와야 합니다");
        Assert.NotEqual(stale.Id, view.Value.Id);
        Assert.Equal(SceneLayoutState.Superseded, stale.State);

        // 새로 합성된 것이므로 유도 판단이 함께 붙는다 (FR-07)
        Assert.NotNull(view.Value.Composition);
        Assert.Equal(
            view.Value.Composition!.GroundedCount + view.Value.Composition.ElevatedCount,
            view.Value.Instances.Count);
    }

    /// <summary>
    /// **재분해가 레이아웃을 무효화한다** (background-surface-parts #20).
    ///
    /// 재분해는 파츠를 이름으로 제자리 갱신하므로 `PartId` 도 GLB 도 그대로다. 서명이
    /// mesh 조합만 담으면 표면 표시와 배치 좌표가 통째로 바뀌어도 낡은 레이아웃이 계속
    /// 쓰인다 — **유료 재분해를 하고도 화면이 그대로인** 결함이다. 표시와 배치가 합성의
    /// 입력인 이상 서명에 들어가야 한다.
    /// </summary>
    [Fact]
    public async Task Handle_RecomposesWhenDecomposeChangedTheSurfaceMark()
    {
        var (handler, jobs, layouts) = BuildWithLayouts();
        var job = SeedJob(withMeshOnFirstPart: true);
        await jobs.AddAsync(job, CancellationToken.None);

        // 먼저 한 번 조회해 현재 입력으로 레이아웃을 저장시킨다
        var before = await handler.HandleAsync(job.Id, CancellationToken.None);
        var stored = Assert.Single(await layouts.ListByJobAsync(job.Id, CancellationToken.None));
        Assert.Equal(SceneLayoutState.Active, stored.State);

        var partIdsBefore = job.Parts.Select(part => part.Id).ToArray();
        var meshIdsBefore = job.GeneratedMeshes.Select(mesh => mesh.Id).ToArray();

        // 재분해 — GLB 는 그대로이고 표면 표시만 붙는다
        job.ApplyPartDetails(
        [
            new PartDetail("앞쪽 난간", "구조물", "난간", [
                new Bounds(0.05, 0.6, 0.2, 0.3), new Bounds(0.75, 0.6, 0.2, 0.3)], 0, [],
                PartSurface.Ground),
            new PartDetail("뒤쪽 성벽", "구조물", "성벽", [new Bounds(0.3, 0.2, 0.4, 0.4)], 1, []),
        ]);

        // **이 검사가 의미를 가지려면 mesh 조합이 그대로여야 한다.** 재분해는 이름으로
        // 제자리 갱신하므로 PartId 도 GLB 도 그대로다 — 서명이 mesh 만 담던 시절에는
        // 그래서 아무것도 바뀌지 않았다
        Assert.Equal(partIdsBefore, job.Parts.Select(part => part.Id));
        Assert.Equal(meshIdsBefore, job.GeneratedMeshes.Select(mesh => mesh.Id));

        var after = await handler.HandleAsync(job.Id, CancellationToken.None);

        Assert.True(after.Value!.Revision > before.Value!.Revision, "새 revision 이 나와야 합니다");
        Assert.NotEqual(before.Value.SourceMeshSignature, after.Value.SourceMeshSignature);
    }

    /// <summary>
    /// **배치 좌표만 바뀌어도 다시 합성한다.**
    ///
    /// 재분해가 표시 없이 좌표만 고치는 경우가 더 흔하다 — 그것도 합성 입력이다.
    /// </summary>
    [Fact]
    public async Task Handle_RecomposesWhenDecomposeMovedThePlacements()
    {
        var (handler, jobs, _) = BuildWithLayouts();
        var job = SeedJob(withMeshOnFirstPart: true);
        await jobs.AddAsync(job, CancellationToken.None);

        var before = await handler.HandleAsync(job.Id, CancellationToken.None);

        job.ApplyPartDetails(
        [
            new PartDetail("앞쪽 난간", "구조물", "난간", [
                new Bounds(0.05, 0.6, 0.2, 0.3), new Bounds(0.75, 0.6, 0.2, 0.31)], 0, []),
            new PartDetail("뒤쪽 성벽", "구조물", "성벽", [new Bounds(0.3, 0.2, 0.4, 0.4)], 1, []),
        ]);

        var after = await handler.HandleAsync(job.Id, CancellationToken.None);

        Assert.NotEqual(before.Value!.SourceMeshSignature, after.Value!.SourceMeshSignature);
    }

    /// <summary>
    /// **서명이 가리키는 GLB 와 화면이 그리는 GLB 가 같다** (#20).
    ///
    /// "파츠별 최신 GLB" 규칙을 두 곳에서 각자 쓰면 `CreatedAt` 동률에서 갈린다 —
    /// 한쪽은 내림차순 첫째를, 다른 쪽은 오름차순 마지막을 고른다. 그러면 서명은 A 를
    /// 가리키는데 화면은 B 를 그리고, 서명이 안정적이라 **스스로 낫지 않는다.**
    /// 고정 시계를 쓰는 테스트에서는 이 동률이 쉽게 만들어진다.
    /// </summary>
    [Fact]
    public async Task Handle_UsesOneRuleForTheLatestMeshEvenOnTies()
    {
        var (handler, jobs) = Build();
        var job = SeedJob(withMeshOnFirstPart: true);

        // 같은 시각에 재시도가 하나 더 붙는다 — CreatedAt 동률
        AttachMesh(job, job.Parts[0].Id);
        await jobs.AddAsync(job, CancellationToken.None);

        var view = await handler.HandleAsync(job.Id, CancellationToken.None);

        // 화면이 지목한 GLB 가 도메인이 "최신" 이라 부르는 그것이어야 한다.
        // 배치가 둘이라 인스턴스도 둘이지만 GLB 는 하나여야 한다
        var drawn = Assert.Single(view.Value!.Instances.Select(i => i.MeshId).Distinct());
        Assert.Equal(job.LatestMeshFor(job.Parts[0].Id)!.Id, drawn);
        Assert.Equal(Assert.Single(job.LayoutSignatureInputs()).MeshId, drawn);
    }

    /// <summary>같은 판이면 다시 합성하지 않는다 — 조회마다 revision 이 늘면 이력이 무의미해진다.</summary>
    [Fact]
    public async Task Handle_ReusesTheStoredLayoutOnTheSameVersion()
    {
        var (handler, jobs) = Build();
        var job = SeedJob(withMeshOnFirstPart: true);
        await jobs.AddAsync(job, CancellationToken.None);

        var first = await handler.HandleAsync(job.Id, CancellationToken.None);
        var second = await handler.HandleAsync(job.Id, CancellationToken.None);

        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Equal(first.Value.Revision, second.Value.Revision);
    }

    [Fact]
    public async Task Handle_FailsForUnknownJob()
    {
        var (handler, _) = Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.JobNotFound, result.ErrorCode);
    }

    /// <summary>기준 파츠의 GLB가 없어도 그 2D placement로 완성 GLB들을 보정한다.</summary>
    [Fact]
    public async Task Handle_CalibratesFromAnAnchorWithoutAMesh()
    {
        var (handler, jobs) = Build();
        var job = SeedJob(withMeshOnFirstPart: true, CalibratedScene("뒤쪽 성벽", 4));
        await jobs.AddAsync(job, CancellationToken.None);

        var result = await handler.HandleAsync(job.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var expected = SceneLayoutComposer.Compose(
            [new SceneComposeInput(job.Parts[0].Id, job.Parts[0].DepthOrder, job.Parts[0].Placements)],
            job.Scene!.Camera,
            new SceneScaleCalibration(4, job.Parts[1].Placements));
        Assert.Equal(expected.Select(instance => instance.Scale), result.Value!.Instances.Select(instance => instance.Scale));
        Assert.Equal(["뒤쪽 성벽"], result.Value.MissingPartNames);
    }

    /// <summary>구조화 높이가 있는데 exact anchor가 사라졌으면 legacy scale로 숨기지 않는다.</summary>
    [Fact]
    public async Task Handle_FailsWhenTheStructuredScaleAnchorIsMissing()
    {
        var (handler, jobs) = Build();
        var job = SeedJob(withMeshOnFirstPart: true, CalibratedScene("없는 기준 파츠", 4));
        await jobs.AddAsync(job, CancellationToken.None);

        var result = await handler.HandleAsync(job.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.SceneIncomplete, result.ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoDLayoutAndRestore_RejectBeforePersistence(bool restore)
    {
        var (handler, jobs, layouts) = BuildWithLayouts();
        var job = PipelineJob.CreateSprites(Guid.NewGuid(), Guid.NewGuid(), "image",
            new(Noxtend.Domain.Sprites.SpriteView.SideView, Noxtend.Domain.Sprites.SpriteOutputKind.Layers),
            new(24, 16), new(1536, 1024), Now).Value!;
        await jobs.AddAsync(job, default);
        if (!restore)
        {
            var get = await handler.HandleAsync(job.Id, default);
            Assert.Equal(ErrorCode.SpriteWrongMode, get.ErrorCode);
            Assert.Empty(await layouts.ListByJobAsync(job.Id, default));
            return;
        }

        var source = SceneLayout.ComposeActive(job.Id, 1, [], SceneMeshSignature.Compute(job.LayoutSignatureInputs()), 0,
            SceneStaging.ComposeCamera(null, []), SceneStaging.ComposeLight(null), Now);
        await layouts.AddAsync(source, default);
        var restored = await new RestoreSceneRevisionHandler(jobs, layouts, new FixedClock(Now))
            .HandleAsync(job.Id, source.Id, default);
        Assert.Equal(ErrorCode.SpriteWrongMode, restored.ErrorCode);
        Assert.Single(await layouts.ListByJobAsync(job.Id, default));
        Assert.Equal(SceneLayoutState.Active, source.State);
    }

    // ─── 설정 ───

    private static (GetSceneLayoutHandler Handler, InMemoryJobRepository Jobs) Build()
        => BuildWithLayouts() is var (handler, jobs, _) ? (handler, jobs) : default;

    private static (GetSceneLayoutHandler Handler, InMemoryJobRepository Jobs,
        InMemorySceneLayoutRepository Layouts) BuildWithLayouts()
    {
        var jobs = new InMemoryJobRepository();
        var layouts = new InMemorySceneLayoutRepository();

        return (new GetSceneLayoutHandler(jobs, layouts, new FixedClock(Now)), jobs, layouts);
    }

    /// <summary>파츠 둘 — 앞쪽(배치 2개)·뒤쪽(배치 1개). 장면 명세에 지평선 0.4.</summary>
    private static PipelineJob SeedJob(bool withMeshOnFirstPart, SceneSpec? scene = null)
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini");
        if (scene is not null)
        {
            job.ApplyScene(scene);
        }

        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, TimeSpan.FromMinutes(1));
        decompose.Succeed(Now);

        job.ApplyParts(["앞쪽 난간", "뒤쪽 성벽"]);
        job.ApplyPartDetails(
        [
            new PartDetail("앞쪽 난간", "구조물", "난간", [
                new Bounds(0.05, 0.6, 0.2, 0.3), new Bounds(0.75, 0.6, 0.2, 0.3)], 0, []),
            new PartDetail("뒤쪽 성벽", "구조물", "성벽", [new Bounds(0.3, 0.2, 0.4, 0.4)], 1, []),
        ]);
        job.PlanReadyFollowUpTasks();

        if (withMeshOnFirstPart)
        {
            AttachMesh(job, job.Parts[0].Id);
        }

        return job;
    }

    private static SceneSpec CalibratedScene(string anchor, double heightMeters)
        => TestScene.Default with
        {
            Scale = new ScaleReference(anchor, $"높이 {heightMeters}m", heightMeters),
        };

    private static void AttachMesh(PipelineJob job, Guid partId)
        => job.AttachGeneratedMesh(
            partId, Guid.NewGuid(), Guid.NewGuid(),
            [new MeshArtifactDescriptor(MeshArtifactKind.Glb, "meshes/part.glb", "model/gltf-binary", 1024)],
            credits: null, Now);
}
