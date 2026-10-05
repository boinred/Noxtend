using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;

namespace Noxtend.Tests.Application;

/// <summary>
/// 접수 · 취소 · 조회의 계약. Design Ref: §4.2 #5~#8
///
/// 오류 코드가 화면과의 계약이므로 (§4.0) 분기마다 코드를 고정한다.
/// </summary>
public sealed class JobLifecycleTests
{
    // §4.2 #5 — JOB_UPLOAD_NOT_FOUND
    [Fact]
    public async Task Start_RejectsUnknownUpload()
    {
        var fixture = new PipelineFixture();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, Guid.NewGuid(), providerId, StubModelCatalog.DefaultModel, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.JobUploadNotFound, result.ErrorCode);
        Assert.Empty(fixture.Queue.Enqueued);
    }

    // §4.2 #5 — JOB_PROVIDER_NOT_FOUND
    [Fact]
    public async Task Start_RejectsUnknownProvider()
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, uploadId, Guid.NewGuid(), StubModelCatalog.DefaultModel, CancellationToken.None);

        Assert.Equal(ErrorCode.JobProviderNotFound, result.ErrorCode);
    }

    // §4.2 #5 — JOB_PROVIDER_DISABLED
    [Fact]
    public async Task Start_RejectsDisabledProvider()
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync(enabled: false);

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, uploadId, providerId, StubModelCatalog.DefaultModel, CancellationToken.None);

        Assert.Equal(ErrorCode.JobProviderDisabled, result.ErrorCode);
    }

    // 사이클 #5: 공정이 하나 → 셋. 단정이 깨진 것이 맞고, 그것이 §2.3 검증이다
    [Fact]
    public async Task Start_PlansThreeStagesInOrder()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();

        var ordered = job.Tasks.OrderBy(t => t.Ordinal).ToList();

        Assert.Equal([TaskKind.Analyze, TaskKind.Extract, TaskKind.Decompose],
            ordered.Select(t => t.Kind));
        Assert.Equal([0, 1, 2], ordered.Select(t => t.Ordinal));

        // 공급자·모델은 공정에 붙는다 — 단계마다 다르게 쓸 수 있어야 한다 (§2.3)
        Assert.All(job.Tasks, t => Assert.NotNull(t.ProviderConfigId));
        Assert.All(job.Tasks, t => Assert.NotNull(t.Model));
    }

    // 활성 프롬프트가 없으면 워커까지 보내지 않는다 (§6)
    [Fact]
    public async Task Start_RejectsWhenAStageHasNoActivePrompt()
    {
        var fixture = new PipelineFixture();
        fixture.Prompts.Remove(LlmOperationKind.Decompose);

        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, uploadId, providerId,
            StubModelCatalog.DefaultModel, CancellationToken.None);

        Assert.Equal(ErrorCode.PromptNotActive, result.ErrorCode);
        Assert.Empty(fixture.Queue.Enqueued);
    }

    // §4.2 #8 — 409 JOB_ALREADY_TERMINAL
    [Fact]
    public async Task Cancel_RejectsJobThatAlreadyFinished()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        await fixture.RunAllStagesAsync(job);

        var result = await fixture.Cancel.HandleAsync(job.Id, CancellationToken.None);

        Assert.Equal(ErrorCode.JobAlreadyTerminal, result.ErrorCode);
        Assert.Equal("succeeded", result.ErrorMessage);
    }

    [Fact]
    public async Task Cancel_ReturnsImmediatelyWithoutWaitingForTheWorker()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        job.Tasks[0].Claim(fixture.Clock.Now, fixture.Options.Lease);

        var result = await fixture.Cancel.HandleAsync(job.Id, CancellationToken.None);

        // 사용자가 취소를 눌렀는데 응답이 10분 뒤에 오면 그것은 취소가 아니다 (§4.2 #8)
        Assert.True(result.IsSuccess);
        Assert.Equal(JobStatus.Canceled, job.Status);
    }

    [Fact]
    public async Task Get_ReportsNotFoundForUnknownId()
    {
        var fixture = new PipelineFixture();

        var result = await fixture.Get.HandleAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ErrorCode.JobNotFound, result.ErrorCode);
    }

    // §4.2 #7 — 홈 두 섹션
    [Fact]
    public async Task List_SeparatesActiveFromTerminal()
    {
        var fixture = new PipelineFixture();
        var finished = await fixture.StartJobAsync();
        await fixture.RunAllStagesAsync(finished);

        var running = await fixture.StartJobAsync();

        var active = await fixture.List.HandleAsync(
            JobListFilter.Active, null, 10, CancellationToken.None);
        var terminal = await fixture.List.HandleAsync(
            JobListFilter.Terminal, null, 10, CancellationToken.None);

        Assert.Equal([running.Id], active.Items.Select(j => j.Id));
        Assert.Equal([finished.Id], terminal.Items.Select(j => j.Id));
    }

    [Fact]
    public async Task List_FiltersByCategory()
    {
        var fixture = new PipelineFixture();
        var background = await fixture.StartJobAsync(category: AssetCategory.Background);
        await fixture.StartJobAsync(category: AssetCategory.Character);

        var result = await fixture.List.HandleAsync(
            JobListFilter.Active, AssetCategory.Background, 10, CancellationToken.None);

        // 스튜디오 3종 대비 — 카테고리는 조회 조건이지 백본의 분기가 아니다 (§2.4)
        Assert.Equal([background.Id], result.Items.Select(j => j.Id));
    }

    [Fact]
    public async Task Upload_StoresBlobKeyGeneratedByTheServer()
    {
        var fixture = new PipelineFixture();
        using var content = new MemoryStream([1, 2, 3, 4]);

        var result = await fixture.Upload.HandleAsync(
            content, "../../etc/passwd", "image/png", 4, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // 원본 파일명이 경로로 쓰이면 경로 조작이 열린다 (§7)
        Assert.DoesNotContain("..", result.Value!.BlobKey);
        Assert.DoesNotContain("passwd", result.Value.BlobKey);
        Assert.Equal("../../etc/passwd", result.Value.OriginalName);
    }

    [Fact]
    public async Task Upload_RejectsFileThatBreaksTheRules()
    {
        var fixture = new PipelineFixture();
        using var content = new MemoryStream([1]);

        var result = await fixture.Upload.HandleAsync(
            content, "doc.pdf", "application/pdf", 1, CancellationToken.None);

        Assert.Equal(ErrorCode.UploadUnsupportedType, result.ErrorCode);
    }
}
