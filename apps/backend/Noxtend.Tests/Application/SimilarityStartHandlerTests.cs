using Noxtend.Application.Scene;
using Noxtend.Application.Similarity;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Similarity;
using Noxtend.Infrastructure.Blob;
using Noxtend.Infrastructure.Persistence.InMemory;
using Noxtend.Infrastructure.Queue;

namespace Noxtend.Tests.Application;

/// <summary>
/// 유사도 실행 시작. Design Ref: background-similarity-tuning §10.1 · §11.1
///
/// **모든 유료 호출은 명시적 시작 뒤에만** — 자격 검사가 시작보다 먼저고, 같은
/// Idempotency-Key 의 재접수는 새 run(=새 유료 호출)을 만들지 않아야 한다.
/// </summary>
public sealed class SimilarityStartHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TwoDSimilarity_RejectsWithoutBlobRunEvaluationOrQueue()
    {
        var f = await SimilarityFixture.CreateAsync();
        var job = PipelineJob.CreateSprites(Guid.NewGuid(), Guid.NewGuid(), "image",
            new(Noxtend.Domain.Sprites.SpriteView.SideView, Noxtend.Domain.Sprites.SpriteOutputKind.Layers),
            new(24, 16), new(1536, 1024), Now).Value!;
        job.Succeed(Now);
        await f.Jobs.AddAsync(job, default);
        var layout = SceneLayout.ComposeActive(job.Id, 1, [], SceneMeshSignature.Compute(job.LayoutSignatureInputs()), 0,
            SceneStaging.ComposeCamera(null, []), SceneStaging.ComposeLight(null), Now);
        await f.Layouts.AddAsync(layout, default);
        var before = f.Blobs.Count;
        var result = await f.Start.HandleAsync(f.Request() with { JobId = job.Id, LayoutId = layout.Id }, default);
        Assert.Equal(ErrorCode.SpriteWrongMode, result.ErrorCode);
        Assert.Equal(before, f.Blobs.Count);
        Assert.Empty(await f.Similarity.ListRunsByJobAsync(job.Id, default));
        Assert.Empty(f.Queue.Enqueued);
        Assert.Single(await f.Layouts.ListByJobAsync(job.Id, default));
    }

    // ─── 정상 시작 ───

    [Fact]
    public async Task Start_CreatesTheRunBaselineEvaluationAndEnqueues()
    {
        var fixture = await SimilarityFixture.CreateAsync();

        var result = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var started = result.Value!;
        Assert.Equal(SimilarityRunStatus.Evaluating, started.Run.Status);

        var evaluation = started.BaselineEvaluation;
        Assert.Equal(SimilarityEvaluationStatus.Pending, evaluation.Status);
        Assert.Equal(fixture.ActiveLayout.Id, evaluation.LayoutId);
        Assert.Equal("image/png", evaluation.Render!.ContentType);
        Assert.Equal(64, evaluation.Render.Sha256.Length);

        // 렌더 blob 이 실제로 저장되고, 평가가 큐에 올라간다
        Assert.Single(fixture.Queue.Enqueued, evaluation.Id);
    }

    /// <summary>같은 key + 같은 payload → 기존 run. 유료 호출이 늘지 않는다 (§10.1).</summary>
    [Fact]
    public async Task Start_IsIdempotentForTheSameKeyAndPayload()
    {
        var fixture = await SimilarityFixture.CreateAsync();
        var first = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);

        var second = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.Run.Id, second.Value!.Run.Id);
        Assert.Single(fixture.Queue.Enqueued);
    }

    /// <summary>같은 key 인데 payload 가 다르면 재사용이 아니라 충돌이다 (§10.1).</summary>
    [Fact]
    public async Task Start_RejectsTheSameKeyWithADifferentPayload()
    {
        var fixture = await SimilarityFixture.CreateAsync();
        await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);

        var result = await fixture.Start.HandleAsync(
            fixture.Request() with { MaxIterations = 3 }, CancellationToken.None);

        Assert.Equal(ErrorCode.SimilarityConflict, result.ErrorCode);
    }

    [Fact]
    public async Task Start_RejectsASecondOpenRun()
    {
        var fixture = await SimilarityFixture.CreateAsync();
        await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);

        var result = await fixture.Start.HandleAsync(
            fixture.Request() with { IdempotencyKey = "another-key" }, CancellationToken.None);

        Assert.Equal(ErrorCode.SimilarityConflict, result.ErrorCode);
    }

    // ─── 자격 (§11.1) ───

    [Fact]
    public async Task Start_RefusesAJobThatIsNotASucceededBackground()
    {
        var fixture = await SimilarityFixture.CreateAsync(succeeded: false);

        var result = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(ErrorCode.SimilarityNotReady, result.ErrorCode);
    }

    [Fact]
    public async Task Start_RefusesWhenThePromptSlotIsEmpty()
    {
        var fixture = await SimilarityFixture.CreateAsync(withPrompt: false);

        var result = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(ErrorCode.SimilarityNotReady, result.ErrorCode);
        Assert.Contains("프롬프트", result.ErrorMessage);
    }

    /// <summary>Google 은 이미지 생성 어댑터뿐 — 평가 capability 가 없다 (§7.2).</summary>
    [Fact]
    public async Task Start_RefusesAProviderWithoutTheCapability()
    {
        var fixture = await SimilarityFixture.CreateAsync(providerKind: ProviderKind.Google);

        var result = await fixture.Start.HandleAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(ErrorCode.SimilarityNotReady, result.ErrorCode);
    }

    /// <summary>기준 layout 이 현재 mesh 조합의 것이 아니면 시작 자체가 거짓이다.</summary>
    [Fact]
    public async Task Start_RefusesAStaleLayout()
    {
        var fixture = await SimilarityFixture.CreateAsync();

        var result = await fixture.Start.HandleAsync(
            fixture.Request() with { LayoutId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(ErrorCode.SceneRevisionStale, result.ErrorCode);
    }

    /// <summary>
    /// **저장된 서명과 비교하는 서명이 같은 재료에서 나온다** (#20).
    ///
    /// 서명 재료를 저장 쪽과 비교 쪽이 따로 만들던 시절에는, 조회 핸들러가 분해 결과까지
    /// 담아 저장하고 유사도는 mesh id 만으로 비교해 **두 값이 영원히 어긋났다** — 유사도
    /// 시작이 항상 `SceneRevisionStale` 로 막혔다. fixture 가 양쪽 모두 옛 방식을 쓰는
    /// 바람에 테스트가 그 결함을 통째로 가렸다.
    ///
    /// 그래서 여기서는 fixture 를 믿지 않고 **조회 핸들러가 실제로 저장한** 레이아웃으로
    /// 시작한다. 두 경로가 갈리면 이 검사가 먼저 깨진다.
    /// </summary>
    [Fact]
    public async Task Start_AcceptsTheLayoutTheSceneHandlerStored()
    {
        var fixture = await SimilarityFixture.CreateAsync();

        var stored = await fixture.SceneLayouts.HandleAsync(fixture.JobId, CancellationToken.None);
        Assert.True(stored.IsSuccess, "조회 핸들러가 레이아웃을 저장해야 합니다");

        var result = await fixture.Start.HandleAsync(
            fixture.Request() with { LayoutId = stored.Value!.Id }, CancellationToken.None);

        Assert.NotEqual(ErrorCode.SceneRevisionStale, result.ErrorCode);
    }

    /// <summary>
    /// **상태 조회도 같은 재료로 비교한다** (#20).
    ///
    /// 서명 재료가 갈리면 이 핸들러는 "3D 배경 화면을 한 번 열어 배치를 준비해야 합니다"
    /// 를 영원히 내건다 — 운영자가 3D 탭을 열고 와도 사라지지 않는다. 저장 경로를 실제로
    /// 태운 뒤 그 사유가 **없어야** 함을 본다.
    /// </summary>
    [Fact]
    public async Task Status_IsReadyAfterTheSceneHandlerStoredTheLayout()
    {
        var fixture = await SimilarityFixture.CreateAsync();
        await fixture.SceneLayouts.HandleAsync(fixture.JobId, CancellationToken.None);

        var status = await fixture.Status.HandleAsync(fixture.JobId, CancellationToken.None);

        Assert.DoesNotContain(
            status.Value!.BlockingReasons,
            reason => reason.Contains("배치를 준비", StringComparison.Ordinal));
    }

    /// <summary>
    /// **revision 복원도 같은 재료로 비교한다** (#20).
    ///
    /// 서명이 갈리면 복원이 항상 `SceneRevisionStale` 로 실패한다.
    /// </summary>
    [Fact]
    public async Task Restore_AcceptsARevisionTheSceneHandlerStored()
    {
        var fixture = await SimilarityFixture.CreateAsync();
        var stored = await fixture.SceneLayouts.HandleAsync(fixture.JobId, CancellationToken.None);

        // 두 번째 조회는 같은 서명이라 재합성하지 않는다 — 저장된 그 revision 을 되살린다
        var restored = await fixture.Restore.HandleAsync(
            fixture.JobId, stored.Value!.Id, CancellationToken.None);

        Assert.True(restored.IsSuccess, $"복원이 실패했습니다: {restored.ErrorCode}");
    }

    /// <summary>
    /// **서명을 비교하는 네 경로가 한 값에 합의한다** (#20).
    ///
    /// 조회 핸들러가 저장한 레이아웃을 나머지 셋이 "지금 것" 으로 봐야 한다. 재료를 각자
    /// 만들던 시절에는 조회가 분해 결과까지 담아 저장하고 나머지는 mesh id 만으로 비교해
    /// **세 기능이 동시에 죽었다** — 유사도 상태가 영원히 "배치를 준비해야 합니다" 를 내걸고,
    /// 시작이 항상 `SceneRevisionStale` 이며, 복원이 실패했다.
    ///
    /// 개별 핸들러마다 검사하는 대신 **합의 자체**를 한 번에 잠근다. 어느 하나가 재료를
    /// 다시 자체 계산하는 순간 여기서 깨진다.
    /// </summary>
    [Fact]
    public async Task EverySignatureConsumer_AgreesWithTheStoredLayout()
    {
        var fixture = await SimilarityFixture.CreateAsync();

        var stored = await fixture.SceneLayouts.HandleAsync(fixture.JobId, CancellationToken.None);
        Assert.True(stored.IsSuccess, "조회 핸들러가 레이아웃을 저장해야 합니다");

        // ① 상태 조회 — 준비 사유가 남지 않는다
        var status = await fixture.Status.HandleAsync(fixture.JobId, CancellationToken.None);
        Assert.DoesNotContain(
            status.Value!.BlockingReasons,
            reason => reason.Contains("배치를 준비", StringComparison.Ordinal));

        // ② 유사도 시작 — 낡음으로 막히지 않는다
        var start = await fixture.Start.HandleAsync(
            fixture.Request() with { LayoutId = stored.Value!.Id }, CancellationToken.None);
        Assert.NotEqual(ErrorCode.SceneRevisionStale, start.ErrorCode);

        // ③ revision 복원 — 그 revision 을 현재 것으로 받아들인다
        var restored = await fixture.Restore.HandleAsync(
            fixture.JobId, stored.Value.Id, CancellationToken.None);
        Assert.True(restored.IsSuccess, $"복원이 실패했습니다: {restored.ErrorCode}");
    }

    // ─── 렌더 검증 (§13) ───

    [Fact]
    public async Task Start_RejectsAnOversizedRender()
    {
        var fixture = await SimilarityFixture.CreateAsync();

        var result = await fixture.Start.HandleAsync(
            fixture.Request() with { RenderBytes = new byte[(8 * 1024 * 1024) + 1] },
            CancellationToken.None);

        Assert.Equal(ErrorCode.SimilarityRenderTooLarge, result.ErrorCode);
    }

    [Fact]
    public async Task Start_RejectsANonPngOrWrongSizedRender()
    {
        var fixture = await SimilarityFixture.CreateAsync();

        var jpeg = await fixture.Start.HandleAsync(
            fixture.Request() with { RenderBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4] },
            CancellationToken.None);
        Assert.Equal(ErrorCode.SimilarityRenderInvalid, jpeg.ErrorCode);

        var small = await fixture.Start.HandleAsync(
            fixture.Request() with { RenderBytes = SimilarityFixture.Png(512, 512) },
            CancellationToken.None);
        Assert.Equal(ErrorCode.SimilarityRenderInvalid, small.ErrorCode);
    }

    /// <summary>원본 비율 캡처 — 긴 변이 1024 면 세로·가로형 모두 받는다 (사용자 결정).</summary>
    [Fact]
    public async Task Start_AcceptsAPortraitRenderWithLongSide1024()
    {
        var fixture = await SimilarityFixture.CreateAsync();

        var result = await fixture.Start.HandleAsync(
            fixture.Request() with { RenderBytes = SimilarityFixture.Png(700, 1024) },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}

/// <summary>시작·평가 테스트가 공유하는 조립 — 완료된 배경 작업 + 활성 revision + 스텁들.</summary>
public sealed class SimilarityFixture
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    public required InMemoryJobRepository Jobs { get; init; }
    public required InMemorySceneLayoutRepository Layouts { get; init; }
    public required InMemorySimilarityRepository Similarity { get; init; }
    public required InMemorySimilarityQueue Queue { get; init; }
    public required InMemoryBlobStorage Blobs { get; init; }
    public required StartSimilarityRunHandler Start { get; init; }
    public required PipelineJob Job { get; init; }
    public required SceneLayout ActiveLayout { get; init; }
    public required Guid ProviderConfigId { get; init; }

    /// <summary>
    /// 레이아웃을 **실제로 저장하는** 핸들러.
    ///
    /// 서명이 갈리는 결함은 저장 경로와 비교 경로를 함께 태워야만 드러난다 — fixture 가
    /// 저장까지 흉내 내면 그 갈림이 보이지 않는다 (#20).
    /// </summary>
    public required GetSceneLayoutHandler SceneLayouts { get; init; }

    /// <summary>서명을 저장 경로와 비교하는 나머지 둘 — 같은 갈림에 물린다.</summary>
    public required GetSimilarityStatusHandler Status { get; init; }

    public required RestoreSceneRevisionHandler Restore { get; init; }

    public Guid JobId => Job.Id;

    public StartSimilarityRunRequest Request() => new(
        Job.Id, ProviderConfigId, "gpt-test", MaxIterations: 2,
        ActiveLayout.Id, "key-1", Png(1024, 1024), "image/png");

    public static async Task<SimilarityFixture> CreateAsync(
        bool succeeded = true,
        bool withPrompt = true,
        ProviderKind providerKind = ProviderKind.OpenAI)
    {
        var jobs = new InMemoryJobRepository();
        var layouts = new InMemorySceneLayoutRepository();
        var similarity = new InMemorySimilarityRepository();
        var queue = new InMemorySimilarityQueue();
        var blobs = new InMemoryBlobStorage();
        var providers = new InMemoryProviderConfigRepository();

        var config = ProviderConfig.Create("평가", providerKind, "cipher", "1234", Now);
        await providers.AddAsync(config, CancellationToken.None);

        var job = SeedCompletedBackgroundJob(succeeded);
        await jobs.AddAsync(job, CancellationToken.None);

        var layout = ActiveLayoutFor(job);
        await layouts.AddAsync(layout, CancellationToken.None);

        var handler = new StartSimilarityRunHandler(
            jobs, layouts, similarity, providers,
            new StubSimilarityPromptCatalog(withPrompt), blobs, queue, new FixedClock(Now));

        var sceneLayouts = new GetSceneLayoutHandler(jobs, layouts, new FixedClock(Now));
        var status = new GetSimilarityStatusHandler(
            jobs, layouts, similarity, new StubSimilarityPromptCatalog(withPrompt));
        var restore = new RestoreSceneRevisionHandler(jobs, layouts, new FixedClock(Now));

        return new SimilarityFixture
        {
            Jobs = jobs,
            Layouts = layouts,
            Similarity = similarity,
            Queue = queue,
            Blobs = blobs,
            Start = handler,
            SceneLayouts = sceneLayouts,
            Status = status,
            Restore = restore,
            Job = job,
            ActiveLayout = layout,
            ProviderConfigId = config.Id,
        };
    }

    /// <summary>1024×1024 가 기본 프레임 계약이다 (§8.1) — IHDR 만 읽는 검증에 충분한 최소 PNG.</summary>
    public static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        bytes[11] = 13;   // IHDR length
        "IHDR"u8.ToArray().CopyTo(bytes, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private static PipelineJob SeedCompletedBackgroundJob(bool succeeded)
    {
        var job = PipelineJob.Create(
            AssetCategory.Background, Guid.NewGuid(), Now, Guid.NewGuid(), "gemini");
        job.ApplyScene(Noxtend.Tests.Domain.TestScene.Default);

        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, TimeSpan.FromMinutes(1));
        decompose.Succeed(Now);

        // 크기 기준 물체("부두 기둥")도 파츠로 둔다 — 없으면 조회 핸들러가 SceneIncomplete 로
        // 막혀서 저장 경로를 태울 수 없다. 실제 작업에서도 기준 물체는 파츠다
        job.ApplyParts(["앞쪽 난간", "부두 기둥"]);
        job.ApplyPartDetails(
        [
            new PartDetail("앞쪽 난간", "구조물", "난간", [new Bounds(0.1, 0.6, 0.2, 0.3)], 0, []),
            new PartDetail("부두 기둥", "구조물", "기둥", [new Bounds(0.6, 0.5, 0.05, 0.4)], 1, []),
        ]);
        job.PlanReadyFollowUpTasks();

        foreach (var part in job.Parts)
        {
            job.AttachGeneratedMesh(
                part.Id, Guid.NewGuid(), Guid.NewGuid(),
                [new MeshArtifactDescriptor(
                    MeshArtifactKind.Glb, "meshes/p.glb", "model/gltf-binary", 1024)],
                credits: null, Now);
        }

        if (succeeded)
        {
            job.Succeed(Now);
        }

        return job;
    }

    private static SceneLayout ActiveLayoutFor(PipelineJob job)
    {
        // **핸들러가 비교할 때와 똑같은 재료를 쓴다.** mesh id 만으로 서명을 만들던
        // 시절에는 이 fixture 안에서만 저장·비교가 일치해, 프로덕션에서 둘이 갈린
        // 결함을 통째로 가렸다 (#20)
        var inputs = job.LayoutSignatureInputs();

        return SceneLayout.ComposeActive(
            job.Id, revision: 1,
            [new SceneInstance(job.Parts[0].Id, 0, 0, 0, -3, 0, 1)],
            SceneMeshSignature.Compute(inputs), inputs.Count,
            SceneStaging.ComposeCamera(null, []), SceneStaging.ComposeLight(null), Now);
    }
}

/// <summary>유사도 평가 프롬프트 슬롯 스텁 — 있음/없음만 가른다.</summary>
public sealed class StubSimilarityPromptCatalog(bool hasPrompt) : IPromptCatalog
{
    public static readonly Guid VersionId = Guid.Parse("c0000000-0000-4000-8000-000000000001");

    public Task<PromptSnapshot?> GetActiveAsync(
        LlmOperationKind kind, AssetCategory category, CancellationToken ct)
        => Task.FromResult(
            hasPrompt && kind == LlmOperationKind.SimilarityEvaluate
                ? new PromptSnapshot(VersionId, 1, "원본과 렌더를 비교하라", string.Empty, "{}")
                : null);
}

/// <summary>정규화 통과 스텁 — 여기서 보는 것은 배선이지 픽셀 처리가 아니다.</summary>
public sealed class PassThroughTranscoder : Noxtend.Domain.Ports.IImageTranscoder
{
    public Task<Noxtend.Domain.Sprites.SpriteImageInfo> InspectSpriteAsync(
        Stream image, long maxBytes, long maxPixels, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<Stream> NormalizeSpriteAsync(Stream image, Noxtend.Domain.Sprites.SpriteCanvas canvas,
        Noxtend.Domain.Sprites.SpriteTransform transform, bool requireTransparency,
        Noxtend.Domain.Sprites.SpriteTileLayout layout, CancellationToken ct)
        => throw new NotSupportedException();

    public Task WriteSpriteSheetAsync(Noxtend.Domain.Sprites.SpriteSheetLayout layout,
        Func<Guid, CancellationToken, Task<Stream>> openFrame, Stream output, CancellationToken ct)
        => throw new NotSupportedException();

    public bool IsUploadable(string contentType) => true;

    public Task<(int Width, int Height)> MeasureAsync(Stream image, CancellationToken ct)
        => Task.FromResult((1024, 1024));

    public Task<Stream> ToPngAsync(Stream image, long maxBytes, CancellationToken ct)
        => Task.FromResult(image);

    public Task<Stream> FlipHorizontallyAsync(Stream image, string contentType, CancellationToken ct)
        => Task.FromResult(image);

    public async Task<byte[]> NormalizeToFrameAsync(
        Stream image, int frameWidth, int frameHeight, string backgroundHex, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
