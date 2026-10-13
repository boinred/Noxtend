using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Sprites;

namespace Noxtend.Tests.Domain;

public sealed class SpriteSourceDraftTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
    private static readonly SpriteSettings Settings = new(SpriteView.SideView, SpriteOutputKind.Layers);
    private static readonly Guid TextProvider = Guid.NewGuid();

    [Fact]
    public void CreateDraft_StartsSourceDraftWithoutSourceAndRunsEnhance()
    {
        var job = PipelineJob.CreateSpriteDraft(Guid.NewGuid(), "image-model", Settings, new(1024, 1024),
            new(1024, 1024), Draft("  80년대 서울  "), Now).Value!;
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites!.Phase);
        Assert.Null(job.SourceImageId);
        Assert.Equal("80년대 서울", job.Sprites.SourceDraft!.Description);
        var requestId = Guid.NewGuid();

        var task = job.PlanSourceEnhance("80년대 서울", requestId).Value!;

        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(TaskKind.EnhanceSpriteSource, task.Kind);
        Assert.Equal(TextProvider, task.ProviderConfigId);
        Assert.Equal("text-model", task.Model);
        Assert.Equal(new SpriteSourceInput("80년대 서울", null), task.SourceInput);
        Assert.Equal(requestId, task.RequestId);
        Assert.True(job.IsCurrentTask(task));
        Assert.True(job.IsReadyToRun(task, Now));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void Description_IsTrimmedThenLimitedTo1000(int length, bool valid)
    {
        var description = " " + new string('d', length) + " ";
        var created = PipelineJob.CreateSpriteDraft(Guid.NewGuid(), "image-model", Settings, new(1024, 1024),
            new(1024, 1024), Draft(description), Now);
        Assert.Equal(valid, created.IsSuccess);
        if (!valid) Assert.Equal(ErrorCode.SpriteSettingsInvalid, created.ErrorCode);
        var job = Idle();
        var revision = Revision(job);
        Assert.Equal(valid, job.PlanSourceEnhance(description, Guid.NewGuid()).IsSuccess);
        Assert.Equal(valid ? revision + 1 : revision, Revision(job));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(4000, true)]
    [InlineData(4001, false)]
    public void Prompt_IsTrimmedThenLimitedTo4000(int length, bool valid)
    {
        var prompt = " " + new string('p', length) + " ";
        var saving = Idle();
        var saved = saving.SaveSourcePrompt(prompt);
        Assert.Equal(valid, saved.IsSuccess);
        if (valid) Assert.Equal(prompt.Trim(), saving.Sprites!.SourceDraft!.Prompt);
        else Assert.Equal(ErrorCode.SpriteSettingsInvalid, saved.ErrorCode);
        var generating = Idle();
        var planned = generating.PlanSourceGeneration(prompt, Guid.NewGuid());
        Assert.Equal(valid, planned.IsSuccess);
        if (valid) Assert.Equal(prompt.Trim(), planned.Value!.SourceInput!.Prompt);
    }

    [Fact]
    public void ActiveSourceTask_RejectsSourceCommandsAsBusy()
    {
        var job = Running();
        var revision = Revision(job);
        Assert.Equal(ErrorCode.SpriteBusy, job.SaveSourcePrompt("프롬프트").ErrorCode);
        Assert.Equal(ErrorCode.SpriteBusy, job.PlanSourceGeneration("프롬프트", Guid.NewGuid()).ErrorCode);
        Assert.Equal(ErrorCode.SpriteBusy, job.PlanSourceEnhance("설명", Guid.NewGuid()).ErrorCode);
        Assert.Equal(ErrorCode.SpriteBusy,
            job.SelectSource(Guid.NewGuid(), new(1024, 1024), new(1024, 1024), Guid.NewGuid()).ErrorCode);
        Assert.Equal(revision, Revision(job));
        Assert.Single(job.Tasks);
    }

    [Fact]
    public void EightCandidates_RejectGenerationWithSourceLimit()
    {
        var job = Idle();
        for (var i = 0; i < SpriteSourceDraft.MaxCandidates; i++)
        {
            var task = job.PlanSourceGeneration($"프롬프트 {i}", Guid.NewGuid()).Value!;
            task.Claim(Now, TimeSpan.FromMinutes(2));
            Assert.True(job.TryAddSourceCandidate(task.Id, Candidate(task)));
            task.Succeed(Now);
            job.ReconcileFromTasks(Now);
        }
        var revision = Revision(job);
        Assert.Equal(ErrorCode.SpriteSourceLimit, job.PlanSourceGeneration("한 장 더", Guid.NewGuid()).ErrorCode);
        Assert.Equal(revision, Revision(job));
        Assert.Equal(SpriteSourceDraft.MaxCandidates, job.Sprites!.SourceDraft!.Candidates.Count);
    }

    [Fact]
    public void JobOutsideSourceDraft_RejectsSourceCommands()
    {
        var job = PipelineJob.CreateSprites(Guid.NewGuid(), Guid.NewGuid(), "image-model", Settings,
            new(1024, 1024), new(1024, 1024), Now).Value!;
        Assert.Equal(ErrorCode.SpriteNotReady, job.SaveSourcePrompt("프롬프트").ErrorCode);
        Assert.Equal(ErrorCode.SpriteNotReady, job.PlanSourceGeneration("프롬프트", Guid.NewGuid()).ErrorCode);
        Assert.Equal(ErrorCode.SpriteNotReady, job.PlanSourceEnhance("설명", Guid.NewGuid()).ErrorCode);
        Assert.Equal(ErrorCode.SpriteNotReady,
            job.SelectSource(Guid.NewGuid(), new(1024, 1024), new(1024, 1024), Guid.NewGuid()).ErrorCode);
        var legacy = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);
        Assert.Equal(ErrorCode.SpriteWrongMode, legacy.SaveSourcePrompt("프롬프트").ErrorCode);
    }

    [Fact]
    public void SourceDraft_RejectsExistingSpriteCommands()
    {
        var job = Idle();
        var revision = Revision(job);
        var id = Guid.NewGuid();
        Assert.Equal(ErrorCode.SpriteNotReady,
            job.ReplaceSpritePlan([new(id, "배경", 0, new(0, 0, 1, 1), false)], revision).ErrorCode);
        Assert.Equal(ErrorCode.SpriteNotReady, job.ApproveSpritePlan(revision).ErrorCode);
        Assert.Equal(ErrorCode.SpriteNotReady, job.ApproveSpriteBases([id], revision).ErrorCode);
        Assert.Equal(ErrorCode.SpriteNotReady, job.RegenerateSpriteFrame(id, 0, revision).ErrorCode);
        Assert.Equal(ErrorCode.SpriteNotReady, job.ApproveSpriteAsset(id, revision).ErrorCode);
        Assert.Equal(ErrorCode.SpriteNotReady, job.CaptureSpriteExport([id], revision).ErrorCode);
        Assert.Equal(revision, Revision(job));
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites!.Phase);
    }

    [Fact]
    public void EnhanceSuccess_AppliesPromptAndWaitsForReview()
    {
        var job = Running();
        var task = job.Tasks.Single();
        task.Claim(Now, TimeSpan.FromMinutes(2));
        var revision = Revision(job);
        Assert.True(job.TryApplySourcePrompt(task.Id, "  상세 프롬프트  "));
        task.Succeed(Now);
        job.ReconcileFromTasks(Now);
        Assert.Equal("상세 프롬프트", job.Sprites!.SourceDraft!.Prompt);
        Assert.Equal(revision + 1, Revision(job));
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites.Phase);
    }

    [Fact]
    public void NonCurrentResult_IsDiscardedAndJobKeepsRunningNewTask()
    {
        var job = Running();
        var first = job.Tasks.Single();
        first.Claim(Now, TimeSpan.FromMinutes(2));
        first.Succeed(Now);
        job.ReconcileFromTasks(Now);
        var second = job.PlanSourceGeneration("프롬프트", Guid.NewGuid()).Value!;
        Assert.False(job.IsCurrentTask(first));
        Assert.True(job.IsCurrentTask(second));
        var revision = Revision(job);
        Assert.False(job.TryApplySourcePrompt(first.Id, "늦게 도착한 프롬프트"));
        Assert.Equal("프롬프트", job.Sprites!.SourceDraft!.Prompt);
        Assert.Equal(revision, Revision(job));
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.Running, job.Status);
        second.Claim(Now, TimeSpan.FromMinutes(2));
        second.Fail(ErrorCode.ProviderCallFailed, Now);
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.PendingReview, job.Status);
    }

    [Theory]
    [InlineData(TaskKind.EnhanceSpriteSource, ErrorCode.ProviderCallFailed)]
    [InlineData(TaskKind.GenerateSpriteSource, ErrorCode.ProviderCallFailed)]
    [InlineData(TaskKind.EnhanceSpriteSource, "재시도 한도를 초과했습니다")]
    [InlineData(TaskKind.GenerateSpriteSource, "재시도 한도를 초과했습니다")]
    public void FailedSourceTask_KeepsJobOpenForReview(TaskKind kind, string reason)
    {
        var job = Idle();
        var task = kind == TaskKind.EnhanceSpriteSource
            ? job.PlanSourceEnhance("설명", Guid.NewGuid()).Value!
            : job.PlanSourceGeneration("프롬프트", Guid.NewGuid()).Value!;
        task.Claim(Now, TimeSpan.FromMinutes(2));
        task.Fail(reason, Now);
        job.ReconcileFromTasks(Now);
        Assert.Equal(JobStatus.PendingReview, job.Status);
        Assert.Null(job.FailureReason);
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites!.Phase);
        Assert.True(job.IsCurrentTask(task));
    }

    [Fact]
    public void ManualRetry_RestoresCurrentSourceTaskOnly()
    {
        var job = Idle();
        var enhance = job.PlanSourceEnhance("설명", Guid.NewGuid()).Value!;
        enhance.Claim(Now, TimeSpan.FromMinutes(2));
        enhance.Fail(ErrorCode.ProviderCallFailed, Now);
        job.ReconcileFromTasks(Now);
        Assert.True(job.RetryOutputTask(enhance.Id));
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Pending, enhance.Status);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites!.Phase);

        enhance.Claim(Now, TimeSpan.FromMinutes(2));
        enhance.Fail(ErrorCode.ProviderCallFailed, Now);
        job.ReconcileFromTasks(Now);
        var generate = job.PlanSourceGeneration("프롬프트", Guid.NewGuid()).Value!;
        Assert.False(job.RetryOutputTask(enhance.Id));
        generate.Claim(Now, TimeSpan.FromMinutes(2));
        generate.Fail(ErrorCode.GenerationEmptyResponse, Now);
        job.ReconcileFromTasks(Now);
        Assert.False(job.RetryOutputTask(enhance.Id));
        Assert.True(job.RetryOutputTask(generate.Id));
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Pending, generate.Status);
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites.Phase);
    }

    [Fact]
    public void Candidate_RequiresCurrentGenerationAndMatchingPrompt()
    {
        var job = Idle();
        var task = job.PlanSourceGeneration("프롬프트", Guid.NewGuid()).Value!;
        task.Claim(Now, TimeSpan.FromMinutes(2));
        var revision = Revision(job);
        Assert.False(job.TryAddSourceCandidate(task.Id, Candidate(task) with { Prompt = "다른 프롬프트" }));
        Assert.False(job.TryApplySourcePrompt(task.Id, "보완 결과"));
        Assert.Equal(revision, Revision(job));
        var candidate = Candidate(task);
        Assert.True(job.TryAddSourceCandidate(task.Id, candidate));
        Assert.Equal(revision + 1, Revision(job));
        Assert.False(job.TryAddSourceCandidate(task.Id, candidate));
        Assert.Equal(candidate, Assert.Single(job.Sprites!.SourceDraft!.Candidates));
    }

    [Fact]
    public void SelectSource_RecomputesCanvasesAndStartsAnalysis()
    {
        var job = Idle();
        var generate = job.PlanSourceGeneration("프롬프트", Guid.NewGuid()).Value!;
        generate.Claim(Now, TimeSpan.FromMinutes(2));
        Assert.True(job.TryAddSourceCandidate(generate.Id, Candidate(generate)));
        generate.Succeed(Now);
        job.ReconcileFromTasks(Now);
        var revision = Revision(job);
        var storedImageId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var actual = new SpriteCanvas(1536, 1024);

        var analyze = job.SelectSource(storedImageId, actual, new(1536, 1024), requestId).Value!;

        Assert.Equal(storedImageId, job.SourceImageId);
        Assert.Equal(actual, job.Sprites!.SourceCanvas);
        Assert.Equal(new SpriteCanvas(1536, 1024), job.Sprites.GenerationCanvas);
        Assert.Equal(SpriteRules.OutputCanvas(Settings, actual), job.Sprites.OutputCanvas);
        Assert.Equal(SpriteRules.Transform(new(1536, 1024), job.Sprites.OutputCanvas), job.Sprites.Transform);
        Assert.Equal(SpritePhase.Analyzing, job.Sprites.Phase);
        Assert.Equal(TaskKind.AnalyzeSprites, analyze.Kind);
        Assert.Equal(TextProvider, analyze.ProviderConfigId);
        Assert.Equal("text-model", analyze.Model);
        Assert.Equal(requestId, analyze.RequestId);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(revision + 1, Revision(job));
        Assert.True(job.IsCurrentTask(analyze));
        Assert.False(job.IsCurrentTask(generate));
        Assert.Single(job.Sprites.SourceDraft!.Candidates);
        Assert.Equal(ErrorCode.SpriteNotReady, job.SaveSourcePrompt("프롬프트").ErrorCode);
    }

    [Fact]
    public void NullSource_ExistsOnlyInSourceDraft()
    {
        var uploaded = PipelineJob.CreateSprites(Guid.NewGuid(), Guid.NewGuid(), "image-model", Settings,
            new(1024, 1024), new(1024, 1024), Now).Value!;
        Assert.NotNull(uploaded.SourceImageId);
        var job = Idle();
        var rejected = job.SelectSource(Guid.Empty, new(1024, 1024), new(1024, 1024), Guid.NewGuid());
        Assert.False(rejected.IsSuccess);
        Assert.Null(job.SourceImageId);
        Assert.Equal(SpritePhase.SourceDraft, job.Sprites!.Phase);
    }

    [Fact]
    public void SourceCommands_IncreaseReviewRevision()
    {
        var job = Idle();
        var revision = Revision(job);
        Assert.True(job.SaveSourcePrompt("첫 프롬프트").Value);
        Assert.Equal(++revision, Revision(job));
        Assert.False(job.SaveSourcePrompt(" 첫 프롬프트 ").Value);
        Assert.Equal(revision, Revision(job));
        var enhance = job.PlanSourceEnhance("바뀐 설명", Guid.NewGuid()).Value!;
        Assert.Equal(++revision, Revision(job));
        Assert.Equal("바뀐 설명", job.Sprites!.SourceDraft!.Description);
        enhance.Claim(Now, TimeSpan.FromMinutes(2));
        Assert.True(job.TryApplySourcePrompt(enhance.Id, "보완 프롬프트"));
        Assert.Equal(++revision, Revision(job));
        enhance.Succeed(Now);
        job.ReconcileFromTasks(Now);
        var generate = job.PlanSourceGeneration("편집한 프롬프트", Guid.NewGuid()).Value!;
        Assert.Equal(++revision, Revision(job));
        Assert.Equal("편집한 프롬프트", job.Sprites.SourceDraft.Prompt);
    }

    private static SpriteSourceDraft Draft(string description)
        => new(description, null, TextProvider, "text-model", []);

    private static PipelineJob Idle()
        => PipelineJob.CreateSpriteDraft(Guid.NewGuid(), "image-model", Settings, new(1024, 1024),
            new(1024, 1024), Draft("80년대 서울"), Now).Value!;

    private static PipelineJob Running()
    {
        var job = Idle();
        Assert.True(job.PlanSourceEnhance("80년대 서울", Guid.NewGuid()).IsSuccess);
        return job;
    }

    private static SpriteSourceCandidate Candidate(PipelineTask task)
        => new(Guid.NewGuid(), $"sprites/sources/{Guid.NewGuid():N}.png", "image/png", 1024, 1024,
            task.SourceInput!.Prompt!, Now);

    private static int Revision(PipelineJob job) => job.Sprites!.ReviewRevision;
}
