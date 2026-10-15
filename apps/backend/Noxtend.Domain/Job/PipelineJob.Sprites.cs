using Noxtend.Domain.Common;
using Noxtend.Domain.Sprites;

namespace Noxtend.Domain.Job;

public sealed partial class PipelineJob
{
    public ProductionMode ProductionMode { get; private set; } = ProductionMode.ThreeD;
    public SpritePipelineState? Sprites { get; private set; }

    public static Result<PipelineJob> CreateSprites(
        Guid sourceImageId, Guid imageProviderConfigId, string imageModel,
        SpriteSettings settings, SpriteCanvas sourceCanvas, SpriteCanvas generationCanvas, DateTimeOffset now)
    {
        var validation = SpriteRules.ValidateSettings(settings, sourceCanvas);
        if (!validation.IsSuccess)
        {
            return Result<PipelineJob>.Fail(validation.ErrorCode!, validation.ErrorMessage!);
        }

        if (generationCanvas is null || generationCanvas.Width <= 0 || generationCanvas.Height <= 0)
        {
            return Result<PipelineJob>.Fail(ErrorCode.SpriteSettingsInvalid, "생성 캔버스는 양수 크기여야 합니다");
        }

        var job = Create(AssetCategory.Background, sourceImageId, now,
            imageProviderConfigId, imageModel, requiresReview: true);
        job.ProductionMode = ProductionMode.TwoD;
        job.Sprites = new SpritePipelineState(settings, sourceCanvas, generationCanvas);
        return Result<PipelineJob>.Ok(job);
    }

    public static Result<PipelineJob> CreateSpriteDraft(
        Guid imageProviderConfigId, string imageModel, SpriteSettings settings,
        SpriteCanvas provisionalSource, SpriteCanvas generationCanvas, SpriteSourceDraft draft, DateTimeOffset now)
    {
        var validation = SpriteRules.ValidateSettings(settings, provisionalSource);
        if (!validation.IsSuccess)
            return Result<PipelineJob>.Fail(validation.ErrorCode!, validation.ErrorMessage!);
        if (generationCanvas is null || generationCanvas.Width <= 0 || generationCanvas.Height <= 0)
            return Result<PipelineJob>.Fail(ErrorCode.SpriteSettingsInvalid, "생성 캔버스는 양수 크기여야 합니다");
        if (draft is null || !SpriteSourceDraft.ValidDescription(draft.Description)
            || (draft.Prompt is not null && !SpriteSourceDraft.ValidPrompt(draft.Prompt))
            || draft.TextProviderConfigId == Guid.Empty || string.IsNullOrWhiteSpace(draft.TextModel)
            || draft.Candidates is not { Count: 0 })
            return Result<PipelineJob>.Fail(ErrorCode.SpriteSettingsInvalid, "설명은 1~1000자, 텍스트 모델이 필요합니다");

        var job = new PipelineJob(Guid.NewGuid(), AssetCategory.Background, null, now,
            imageProviderConfigId, imageModel, null, null, null, null, requiresReview: true)
        {
            ProductionMode = ProductionMode.TwoD,
        };
        job.Sprites = new SpritePipelineState(settings, provisionalSource, generationCanvas, draft with
        {
            Description = draft.Description.Trim(), Prompt = draft.Prompt?.Trim(), TextModel = draft.TextModel.Trim(),
        });
        return Result<PipelineJob>.Ok(job);
    }

    public Result<PipelineTask> PlanSourceEnhance(string description, Guid requestId)
    {
        var guard = CheckSourceCommand();
        if (!guard.IsSuccess) return SpriteFailure<PipelineTask>(guard);
        if (!SpriteSourceDraft.ValidDescription(description))
            return Result<PipelineTask>.Fail(ErrorCode.SpriteSettingsInvalid, "설명은 1~1000자여야 합니다");
        var draft = Sprites!.SourceDraft!;
        Sprites.ReplaceSourceDraft(draft with { Description = description.Trim() });
        return Result<PipelineTask>.Ok(PlanSourceTask(TaskKind.EnhanceSpriteSource,
            draft.TextProviderConfigId, draft.TextModel, new(description.Trim(), null), requestId));
    }

    public Result<bool> SaveSourcePrompt(string prompt)
    {
        var guard = CheckSourceCommand();
        if (!guard.IsSuccess) return guard;
        if (!SpriteSourceDraft.ValidPrompt(prompt))
            return Result<bool>.Fail(ErrorCode.SpriteSettingsInvalid, "프롬프트는 1~4000자여야 합니다");
        var draft = Sprites!.SourceDraft!;
        if (draft.Prompt == prompt.Trim()) return Result<bool>.Ok(false);
        Sprites.ReplaceSourceDraft(draft with { Prompt = prompt.Trim() });
        Sprites.Touch();
        return Result<bool>.Ok(true);
    }

    public Result<PipelineTask> PlanSourceGeneration(string prompt, Guid requestId)
    {
        var guard = CheckSourceCommand();
        if (!guard.IsSuccess) return SpriteFailure<PipelineTask>(guard);
        if (!SpriteSourceDraft.ValidPrompt(prompt))
            return Result<PipelineTask>.Fail(ErrorCode.SpriteSettingsInvalid, "프롬프트는 1~4000자여야 합니다");
        var draft = Sprites!.SourceDraft!;
        if (draft.Candidates.Count >= SpriteSourceDraft.MaxCandidates)
            return Result<PipelineTask>.Fail(ErrorCode.SpriteSourceLimit, "원본 후보는 작업당 8장까지입니다");
        // 화면의 미저장 편집을 저장과 생성 입력에 함께 고정
        Sprites.ReplaceSourceDraft(draft with { Prompt = prompt.Trim() });
        return Result<PipelineTask>.Ok(PlanSourceTask(TaskKind.GenerateSpriteSource,
            ImageProviderConfigId!.Value, ImageModel!, new(null, prompt.Trim()), requestId));
    }

    public bool TryApplySourcePrompt(Guid taskId, string prompt)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is not { Kind: TaskKind.EnhanceSpriteSource, Status: TaskStatus.Running or TaskStatus.Succeeded }
            || !IsCurrentTask(task) || !SpriteSourceDraft.ValidPrompt(prompt)) return false;
        Sprites!.ReplaceSourceDraft(Sprites.SourceDraft! with { Prompt = prompt.Trim() });
        Sprites.Touch();
        return true;
    }

    public bool TryAddSourceCandidate(Guid taskId, SpriteSourceCandidate candidate)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is not { Kind: TaskKind.GenerateSpriteSource } || candidate is null
            || !CanAttachSpriteResult(task, candidate.CreatedAt) || !IsCurrentTask(task)
            || candidate.Id == Guid.Empty || string.IsNullOrWhiteSpace(candidate.BlobKey)
            || string.IsNullOrWhiteSpace(candidate.ContentType) || candidate.Width <= 0 || candidate.Height <= 0
            || candidate.Prompt != task.SourceInput?.Prompt) return false;
        var draft = Sprites!.SourceDraft!;
        if (draft.Candidates.Count >= SpriteSourceDraft.MaxCandidates
            || draft.Candidates.Any(c => c.Id == candidate.Id)) return false;
        Sprites.ReplaceSourceDraft(draft with { Candidates = [.. draft.Candidates, candidate] });
        Sprites.Touch();
        return true;
    }

    public Result<PipelineTask> SelectSource(Guid storedImageId, SpriteCanvas actualSource,
        SpriteCanvas generationCanvas, Guid requestId)
    {
        var guard = CheckSourceCommand();
        if (!guard.IsSuccess) return SpriteFailure<PipelineTask>(guard);
        if (storedImageId == Guid.Empty)
            return Result<PipelineTask>.Fail(ErrorCode.JobUploadNotFound, "확정할 원본 이미지가 필요합니다");
        var validation = SpriteRules.ValidateSettings(Sprites!.Settings, actualSource);
        if (!validation.IsSuccess) return SpriteFailure<PipelineTask>(validation);
        if (generationCanvas is null || generationCanvas.Width <= 0 || generationCanvas.Height <= 0)
            return Result<PipelineTask>.Fail(ErrorCode.SpriteSettingsInvalid, "생성 캔버스는 양수 크기여야 합니다");
        SourceImageId = storedImageId;
        Sprites.ReplaceCanvases(actualSource, generationCanvas);
        Sprites.SetPhase(SpritePhase.Analyzing);
        var draft = Sprites.SourceDraft!;
        return Result<PipelineTask>.Ok(PlanSourceTask(TaskKind.AnalyzeSprites,
            draft.TextProviderConfigId, draft.TextModel, null, requestId));
    }
    public SpriteAssetCanvas SpriteAssetCanvasOf(SpriteAsset asset)
        => asset.Canvas ?? new(Sprites!.OutputCanvas, Sprites.GenerationCanvas, Sprites.Transform, 1);

    public bool SpritePlanNeedsGenerationSizes(IReadOnlyList<SpriteAssetPlan> plans, SpriteGrid? grid)
    {
        var state = Sprites!;
        var cellWidth = SpriteRules.CellWidth(grid, plans, state.Settings);
        return plans.Any(plan => SpriteRules.RoleOf(state.Settings, plan) is SpriteAssetRole.Object or SpriteAssetRole.Backdrop
            && !state.Assets.Any(asset => asset.Id == plan.Id
                && SpriteAssetCanvasOf(asset).Output == SpriteRules.AssetOutput(state.Settings, state.SourceCanvas, cellWidth, plan).Value.Canvas));
    }

    public Result<bool> ReplaceSpritePlan(IReadOnlyList<SpriteAssetPlan> plans, int expectedRevision)
        => ReplaceSpritePlan(plans, null, [], expectedRevision);

    public Result<bool> ReplaceSpritePlan(IReadOnlyList<SpriteAssetPlan> plans, SpriteGrid? grid,
        IReadOnlyList<SpriteCanvas> generationSizes, int expectedRevision)
    {
        var guard = CheckSpriteCommand(expectedRevision);
        if (!guard.IsSuccess) return guard;
        var state = Sprites!;
        if (_tasks.Any(t => t.Kind == TaskKind.AnalyzeSprites && !t.IsTerminal && IsCurrentTask(t)))
            return Result<bool>.Fail(ErrorCode.SpriteBusy, "분석 중에는 계획을 편집할 수 없습니다");
        var validation = SpriteRules.Validate(state.Settings, state.SourceCanvas, plans, grid);
        if (!validation.IsSuccess) return validation;
        plans = plans.Select(p => SpriteRules.NormalizeRole(state.Settings, p)).ToArray();
        if (SpritePlanNeedsGenerationSizes(plans, grid) && (generationSizes.Count == 0
            || generationSizes.Any(size => size.Width <= 0 || size.Height <= 0)))
            return Result<bool>.Fail(ErrorCode.JobImageModelUnavailable, "확인된 생성 크기가 필요합니다");
        var cellWidth = SpriteRules.CellWidth(grid, plans, state.Settings);
        var canvases = plans.ToDictionary(p => p.Id, p =>
        {
            var output = SpriteRules.AssetOutput(state.Settings, state.SourceCanvas, cellWidth, p).Value;
            var existing = state.Assets.FirstOrDefault(a => a.Id == p.Id);
            return SpriteRules.RoleOf(state.Settings, p) is SpriteAssetRole.Object or SpriteAssetRole.Backdrop
                && existing is not null && SpriteAssetCanvasOf(existing).Output == output.Canvas
                ? SpriteAssetCanvasOf(existing) with { Scale = output.Scale }
                : SpriteRules.AssetCanvas(state.Settings, state.SourceCanvas, state.GenerationCanvas, cellWidth, p, generationSizes).Value!;
        });
        var affected = state.Assets.Where(a => !plans.Any(p => p.Id == a.Id)
            || plans.Any(p => p.Id == a.Id && (GenerationChanged(a.Plan, p)
                || SpriteAssetCanvasOf(a).Output != canvases[p.Id].Output))).ToArray();
        if (affected.Any(a => SpriteAssetBusy(a.Id)))
            return Result<bool>.Fail(ErrorCode.SpriteBusy, "실행 중인 대상의 생성 입력은 바꿀 수 없습니다");
        var gridChanged = state.Grid != grid;
        if (!gridChanged && state.Assets.Count == plans.Count && state.Assets.All(a => plans.Contains(a.Plan)
            && SpriteAssetCanvasOf(a) == canvases[a.Id])) return Result<bool>.Ok(false);
        var changedIds = plans.Where(p => !state.Assets.Any(a => a.Plan == p && SpriteAssetCanvasOf(a) == canvases[p.Id]))
            .Select(p => p.Id).ToArray();
        var membershipChanged = state.Assets.Count != plans.Count || state.Assets.Any(a => !plans.Any(p => p.Id == a.Id));
        foreach (var asset in affected) CancelPendingSpriteTasks(asset.Id);
        var assets = plans.OrderBy(p => p.Order).Select(plan =>
        {
            var asset = state.Assets.FirstOrDefault(a => a.Id == plan.Id);
            var canvas = canvases[plan.Id];
            var anchor = SpriteRules.Anchor(SpriteRules.RoleOf(state.Settings, plan), canvas.Output);
            if (asset is null) asset = new SpriteAsset(plan, anchor);
            else if (asset.Plan != plan || affected.Contains(asset)) asset.UpdatePlan(plan, affected.Contains(asset));
            asset.UpdateCanvas(canvas, anchor);
            return asset;
        }).ToArray();
        var generationChanged = affected.Length > 0 || plans.Any(p => !state.Assets.Any(a => a.Id == p.Id));
        state.ReplaceAssets(assets);
        state.ReplaceGrid(grid);
        state.Touch(membershipChanged || gridChanged ? null : changedIds);
        if (generationChanged) state.SetPhase(SpritePhase.PlanReview);
        UpdateSpriteState();
        return Result<bool>.Ok(true);
    }

    public Result<IReadOnlyList<SpriteFrameInput>> ApproveSpritePlan(int expectedRevision)
    {
        var guard = CheckSpriteCommand(expectedRevision);
        if (!guard.IsSuccess) return SpriteFailure<IReadOnlyList<SpriteFrameInput>>(guard);
        if (Sprites!.Phase != SpritePhase.PlanReview || Sprites.Assets.Count == 0)
            return Result<IReadOnlyList<SpriteFrameInput>>.Fail(ErrorCode.SpriteNotReady, "계획 검수가 필요합니다");
        var inputs = Sprites.Assets.Where(a => a.Frames[0].CurrentImageId is null
                && !_tasks.Any(t => t.Id == a.Frames[0].CurrentTaskId && !t.IsTerminal))
            .Select(a => SpriteInput(a, 0)).ToArray();
        Sprites.Touch(inputs.Select(i => i.AssetId).ToArray());
        OpenSpriteReview();
        var active = inputs.Length > 0 || _tasks.Any(t => !t.IsTerminal && IsCurrentTask(t));
        Sprites.SetPhase(active ? SpritePhase.BaseGeneration : SpritePhase.BaseReview);
        if (active) Status = JobStatus.Running;
        return Result<IReadOnlyList<SpriteFrameInput>>.Ok(Array.AsReadOnly(inputs));
    }

    public Result<IReadOnlyList<SpriteFrameInput>> ApproveSpriteBases(IReadOnlyList<Guid> assetIds, int expectedRevision)
    {
        var guard = CheckSpriteCommand(expectedRevision);
        if (!guard.IsSuccess) return SpriteFailure<IReadOnlyList<SpriteFrameInput>>(guard);
        var selected = SelectSpriteAssets(assetIds);
        if (!selected.IsSuccess)
            return Result<IReadOnlyList<SpriteFrameInput>>.Fail(selected.ErrorCode!, selected.ErrorMessage!);
        if (selected.Value!.Any(a => SpriteAssetBusy(a.Plan.Id)))
            return Result<IReadOnlyList<SpriteFrameInput>>.Fail(ErrorCode.SpriteBusy, "대상이 생성 중입니다");
        if (selected.Value!.Any(a => a.Frames[0].CurrentImageId is null))
            return Result<IReadOnlyList<SpriteFrameInput>>.Fail(ErrorCode.SpriteNotReady, "기준 이미지가 필요합니다");
        var inputs = new List<SpriteFrameInput>();
        var changed = new List<Guid>();
        foreach (var asset in selected.Value!)
        {
            if (asset.ApprovedBaseImageId == asset.Frames[0].CurrentImageId) continue;
            asset.ApproveBase(asset.Frames[0].CurrentImageId!.Value);
            changed.Add(asset.Plan.Id);
            if (!asset.Plan.Loop) asset.Approve(SpriteApproval(asset));
            else inputs.AddRange(asset.Frames.Skip(1).Select(f => SpriteInput(asset, f.Index)));
        }
        if (changed.Count > 0)
        {
            Sprites!.Touch(changed);
            UpdateSpriteState(inputs.Count > 0);
        }
        return Result<IReadOnlyList<SpriteFrameInput>>.Ok(inputs.AsReadOnly());
    }

    public Result<bool> ApproveSpriteAsset(Guid assetId, int expectedRevision)
    {
        var guard = CheckSpriteCommand(expectedRevision);
        if (!guard.IsSuccess) return guard;
        var asset = Sprites!.Assets.FirstOrDefault(a => a.Plan.Id == assetId);
        if (asset is null) return Result<bool>.Fail(ErrorCode.SpriteAssetNotFound, "대상이 없습니다");
        if (SpriteAssetBusy(assetId)) return Result<bool>.Fail(ErrorCode.SpriteBusy, "대상이 생성 중입니다");
        if (asset.ApprovedBaseImageId != asset.Frames[0].CurrentImageId || asset.ApprovedBaseImageId is null
            || asset.Frames.Any(f => f.CurrentImageId is null))
            return Result<bool>.Fail(ErrorCode.SpriteNotReady, "승인된 기준과 모든 프레임이 필요합니다");
        if (asset.Approval is not null) return Result<bool>.Ok(false);
        asset.Approve(SpriteApproval(asset));
        Sprites.Touch([assetId]);
        UpdateSpriteState();
        return Result<bool>.Ok(true);
    }

    public Result<SpriteFrameInput> RegenerateSpriteFrame(Guid assetId, int index, int expectedRevision)
    {
        var guard = CheckSpriteCommand(expectedRevision);
        if (!guard.IsSuccess) return SpriteFailure<SpriteFrameInput>(guard);
        var asset = Sprites!.Assets.FirstOrDefault(a => a.Plan.Id == assetId);
        if (asset is null) return Result<SpriteFrameInput>.Fail(ErrorCode.SpriteAssetNotFound, "대상이 없습니다");
        if (index < 0 || index >= asset.Frames.Count)
            return Result<SpriteFrameInput>.Fail(ErrorCode.SpriteFrameInvalid, "프레임 범위를 벗어났습니다");
        if (_tasks.Any(t => t.Status == TaskStatus.Running && t.SpriteInput?.AssetId == assetId
            && IsCurrentTask(t) && (index == 0 || t.SpriteInput.FrameIndex == index)))
            return Result<SpriteFrameInput>.Fail(ErrorCode.SpriteBusy, "해당 슬롯이 생성 중입니다");
        if (index > 0 && (asset.ApprovedBaseImageId is null
            || asset.ApprovedBaseImageId != asset.Frames[0].CurrentImageId))
            return Result<SpriteFrameInput>.Fail(ErrorCode.SpriteNotReady, "승인된 기준 이미지가 필요합니다");
        var reopening = IsTerminal;
        CancelPendingSpriteTasks(assetId, index == 0 ? null : index);
        asset.InvalidateFrame(index);
        Sprites.Touch([assetId]);
        OpenSpriteReview();
        Status = reopening ? JobStatus.Pending : JobStatus.Running;
        Sprites.SetPhase(index == 0 ? SpritePhase.BaseGeneration : SpritePhase.FrameGeneration);
        return Result<SpriteFrameInput>.Ok(SpriteInput(asset, index));
    }

    public IReadOnlyList<PipelineTask> PlanSpriteFrames(IReadOnlyList<SpriteFrameInput> inputs, Guid requestId)
    {
        if (requestId == Guid.Empty || !CheckSpriteMutation(Sprites?.ReviewRevision ?? 0).IsSuccess
            || inputs.Select(i => (i.AssetId, i.FrameIndex)).Distinct().Count() != inputs.Count
            || inputs.Any(input => !Sprites!.Assets.Any(asset => asset.Plan.Id == input.AssetId
                && MatchesSpriteInput(asset, input) && asset.Frames[input.FrameIndex].CurrentImageId is null
                && !_tasks.Any(t => t.Id == asset.Frames[input.FrameIndex].CurrentTaskId && !t.IsTerminal))))
            throw new InvalidOperationException("현재 비어 있는 슬롯의 고정 입력과 요청 ID가 필요합니다");
        var tasks = new List<PipelineTask>();
        foreach (var input in inputs)
        {
            var task = PlanTask(TaskKind.GenerateSprite, _tasks.Count,
                providerConfigId: ImageProviderConfigId, model: ImageModel);
            BindSpriteFrame(task.Id, input);
            task.BindRequest(requestId);
            tasks.Add(task);
        }
        return tasks.AsReadOnly();
    }

    public void BindSpriteFrame(Guid taskId, SpriteFrameInput input)
    {
        var guard = CheckSpriteMutation(Sprites?.ReviewRevision ?? 0);
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        var asset = Sprites?.Assets.FirstOrDefault(a => a.Plan.Id == input.AssetId);
        if (!guard.IsSuccess || task is null || task.Status != TaskStatus.Pending
            || task.SpriteInput is not null || task.SpriteExportInput is not null || asset is null
            || !MatchesSpriteInput(asset, input) || asset.Frames[input.FrameIndex].CurrentImageId is not null)
            throw new InvalidOperationException("현재 대상의 대기 공정과 고정 입력이 필요합니다");
        var frame = asset.Frames[input.FrameIndex];
        if (frame.CurrentTaskId is { } current && _tasks.Any(t => t.Id == current && !t.IsTerminal))
            throw new InvalidOperationException("슬롯에 진행 중인 공정이 있습니다");
        task.BindSpriteInput(input);
        frame.Bind(taskId);
    }

    // 결과 반영은 전역 검수 revision 대신 대상별 고정 입력 기준
    public bool IsCurrentTask(PipelineTask task)
    {
        if (task.JobId != Id || !_tasks.Any(t => t.Id == task.Id)) return false;
        if (ProductionMode != ProductionMode.TwoD) return task.SpriteInput is null && task.SpriteExportInput is null;
        if (Sprites is null || Status == JobStatus.Canceled) return false;
        if (IsSourceTask(task))
            return Sprites.Phase == SpritePhase.SourceDraft
                && task.Id == _tasks.Where(IsSourceTask).MaxBy(t => t.Ordinal)?.Id;
        if (task.SpriteInput is { } input)
        {
            var asset = Sprites.Assets.FirstOrDefault(a => a.Plan.Id == input.AssetId);
            return asset is not null && MatchesSpriteInput(asset, input)
                && asset.Frames[input.FrameIndex].CurrentTaskId == task.Id;
        }
        if (task.SpriteExportInput is { } export)
            return (export.SchemaVersion == 1 || export.Grid == EffectiveSpriteExportGrid())
                && SpriteExportAssetsCurrent(export);
        return task.Kind != TaskKind.AnalyzeSprites
            || (Sprites.Phase == SpritePhase.Analyzing && Sprites.Assets.Count == 0
                && task.Id == _tasks.Where(t => t.Kind == TaskKind.AnalyzeSprites).MaxBy(t => t.Ordinal)?.Id);
    }

    public bool CanPreserveSpriteExportTask(PipelineTask task)
        => ProductionMode == ProductionMode.TwoD && Sprites is not null && Status != JobStatus.Canceled
            && task.JobId == Id && _tasks.Any(t => t.Id == task.Id) && task.Kind == TaskKind.PackSprites
            && task.Status == TaskStatus.Running
            && task.SpriteExportInput is { SchemaVersion: 2 } input
            && input.Grid != EffectiveSpriteExportGrid() && SpriteExportAssetsCurrent(input);

    private bool SpriteExportAssetsCurrent(SpriteExportInput input)
        => input.Assets.Select(a => a.Id).Concat(input.ExcludedAssetIds).ToHashSet()
                .SetEquals(Sprites!.Assets.Select(a => a.Plan.Id))
            && input.Assets.All(a => Sprites.Assets.Any(current => current.Plan.Id == a.Id
                && current.Approval is not null && SameApprovedAsset(current.Approval.Snapshot, a)));

    public bool TryAttachSpriteImage(SpriteImage image)
    {
        if (ProductionMode != ProductionMode.TwoD || Sprites is null || Status == JobStatus.Canceled) return false;
        var task = _tasks.FirstOrDefault(t => t.Id == image.TaskId);
        if (task?.SpriteInput is not { } input || !CanAttachSpriteResult(task, image.CreatedAt)
            || !IsCurrentTask(task) || image.AssetId != input.AssetId || image.FrameIndex != input.FrameIndex
            || image.PlanRevision != input.PlanRevision || image.BaseImageId != input.BaseImageId
            || image.Width != input.Canvas.Width || image.Height != input.Canvas.Height
            || image.ContentType != "image/png" || string.IsNullOrWhiteSpace(image.BlobKey)) return false;
        var asset = Sprites.Assets.Single(a => a.Plan.Id == input.AssetId);
        var frame = asset.Frames[input.FrameIndex];
        if (frame.CurrentImageId is not null || Sprites.Images.Any(i => i.Id == image.Id)) return false;
        if (input.FrameIndex == 0) asset.InvalidateFrame(0);
        frame.Bind(task.Id);
        frame.Attach(image.Id);
        Sprites.AddImage(image);
        Sprites.Touch([asset.Plan.Id]);
        return true;
    }

    public Result<SpriteExportInput> CaptureSpriteExport(IReadOnlyList<Guid> assetIds, int expectedRevision)
    {
        var guard = CheckSpriteCommand(expectedRevision);
        if (!guard.IsSuccess) return SpriteFailure<SpriteExportInput>(guard);
        var selected = SelectSpriteAssets(assetIds);
        if (!selected.IsSuccess) return Result<SpriteExportInput>.Fail(selected.ErrorCode!, selected.ErrorMessage!);
        if (selected.Value!.Any(a => a.Approval is null || a.Approval.PlanRevision != a.PlanRevision))
            return Result<SpriteExportInput>.Fail(ErrorCode.SpriteNotReady, "내보낼 대상의 최종 승인이 필요합니다");
        if (_tasks.Any(t => t.SpriteExportInput is not null && !t.IsTerminal && IsCurrentTask(t)))
            return Result<SpriteExportInput>.Fail(ErrorCode.SpriteBusy, "내보내기가 진행 중입니다");
        var reopening = IsTerminal;
        Sprites!.Touch(assetIds.ToArray());
        OpenSpriteReview();
        Sprites.SetPhase(SpritePhase.Packaging);
        Status = reopening ? JobStatus.Pending : JobStatus.Running;
        return Result<SpriteExportInput>.Ok(new(2, Guid.NewGuid(), Sprites.ReviewRevision,
            Sprites.Settings.View, Sprites.Settings.OutputKind, Sprites.SourceCanvas, Sprites.OutputCanvas,
            Array.AsReadOnly(selected.Value!.OrderBy(a => a.Plan.Order).Select(a => a.Approval!.Snapshot).ToArray()),
            Array.AsReadOnly(Sprites.Assets.Where(a => !assetIds.Contains(a.Plan.Id)).Select(a => a.Plan.Id).ToArray()),
            EffectiveSpriteExportGrid(), Sprites.Settings.OutputKind == SpriteOutputKind.Tiles ? Sprites.Settings.TileWidth : null,
            Array.AsReadOnly(selected.Value!.OrderBy(a => a.Plan.Order).Select(a => new SpriteExportAsset(a.Id,
                SpriteRules.RoleOf(Sprites.Settings, a.Plan), SpriteAssetCanvasOf(a).Output,
                SpriteAssetCanvasOf(a).Scale, a.Plan.SourceBounds)).ToArray())));
    }

    private SpriteGrid? EffectiveSpriteExportGrid()
        => Sprites!.Settings.OutputKind == SpriteOutputKind.Layers ? null
            : new(SpriteRules.CellWidth(Sprites.Grid, Sprites.Assets.Select(a => a.Plan).ToArray(), Sprites.Settings)!.Value);

    public bool TryAttachSpriteExport(SpriteExport export)
    {
        if (ProductionMode != ProductionMode.TwoD || Sprites is null || Status == JobStatus.Canceled) return false;
        var task = _tasks.FirstOrDefault(t => t.Id == export.TaskId);
        if (task?.SpriteExportInput is not { } input || !CanAttachSpriteResult(task, export.CreatedAt)
            || !SameExportInput(input, export.Input) || export.Id != input.ExportId || export.Manifest.JobId != Id
            || export.Manifest.ProductionMode != ProductionMode.TwoD || export.Manifest.SchemaVersion != input.SchemaVersion || !SameExportInput(export.Manifest.Input, input)
            || string.IsNullOrWhiteSpace(export.BlobKey) || Sprites.Exports.Any(e => e.Id == export.Id)) return false;
        export.SetCurrent(IsCurrentTask(task));
        Sprites.AddExport(export);
        return export.IsCurrent;
    }

    public void AcceptSpriteRequest(SpriteAcceptedRequest request)
    {
        if (ProductionMode != ProductionMode.TwoD || Sprites is null || request.JobId != Id || request.Receipt.JobId != Id
            || Sprites.Requests.Any(r => r.RequestId == request.RequestId))
            throw new InvalidOperationException("작업에 속한 고유 요청이 필요합니다");
        Sprites.AddRequest(request);
    }

    private Result<bool> CheckSpriteMutation(int expectedRevision)
    {
        if (ProductionMode != ProductionMode.TwoD || Sprites is null)
            return Result<bool>.Fail(ErrorCode.SpriteWrongMode, "2D 배경 작업이 필요합니다");
        if (Status == JobStatus.Canceled)
            return Result<bool>.Fail(ErrorCode.JobAlreadyTerminal, "취소된 작업은 다시 열 수 없습니다");
        return expectedRevision == Sprites.ReviewRevision ? Result<bool>.Ok(true)
            : Result<bool>.Fail(ErrorCode.SpriteRevisionConflict, "검수 revision이 변경되었습니다");
    }
    private Result<bool> CheckSpriteCommand(int expectedRevision)
    {
        var guard = CheckSpriteMutation(expectedRevision);
        return guard.IsSuccess && Sprites!.Phase == SpritePhase.SourceDraft
            ? Result<bool>.Fail(ErrorCode.SpriteNotReady, "원본 확정 후 진행할 수 있습니다") : guard;
    }
    // 요청 revision 비교는 접수 handler 책임
    private Result<bool> CheckSourceCommand()
    {
        var guard = CheckSpriteMutation(Sprites?.ReviewRevision ?? 0);
        if (!guard.IsSuccess) return guard;
        if (Sprites!.Phase != SpritePhase.SourceDraft || Sprites.SourceDraft is null)
            return Result<bool>.Fail(ErrorCode.SpriteNotReady, "원본 준비 단계가 아닙니다");
        return _tasks.Any(t => IsSourceTask(t) && !t.IsTerminal)
            ? Result<bool>.Fail(ErrorCode.SpriteBusy, "원본 준비 공정이 진행 중입니다") : guard;
    }
    private PipelineTask PlanSourceTask(TaskKind kind, Guid providerConfigId, string model,
        SpriteSourceInput? input, Guid requestId)
    {
        if (requestId == Guid.Empty) throw new InvalidOperationException("요청 ID가 필요합니다");
        var task = PlanTask(kind, _tasks.Count, providerConfigId: providerConfigId, model: model);
        if (input is not null) task.BindSourceInput(input);
        task.BindRequest(requestId);
        Sprites!.Touch();
        OpenSpriteReview();
        Status = JobStatus.Running;
        return task;
    }
    private static bool IsSourceTask(PipelineTask task)
        => task.Kind is TaskKind.EnhanceSpriteSource or TaskKind.GenerateSpriteSource;
    private static Result<T> SpriteFailure<T>(Result<bool> result)
        => Result<T>.Fail(result.ErrorCode!, result.ErrorMessage!);
    private bool GenerationChanged(SpriteAssetPlan before, SpriteAssetPlan after)
        => SpriteRules.RoleOf(Sprites!.Settings, before) != SpriteRules.RoleOf(Sprites.Settings, after)
            || before.SourceBounds != after.SourceBounds || before.RequiresTransparency != after.RequiresTransparency
            || before.Loop != after.Loop || (before.Loop && before.FrameCount != after.FrameCount)
            || before.MotionNotes != after.MotionNotes;
    private bool SpriteAssetBusy(Guid id)
        => _tasks.Any(t => t.SpriteInput?.AssetId == id && t.Status == TaskStatus.Running && IsCurrentTask(t));
    private void CancelPendingSpriteTasks(Guid assetId, int? index = null)
    {
        foreach (var task in _tasks.Where(t => t.SpriteInput?.AssetId == assetId && t.Status == TaskStatus.Pending
            && (index is null || t.SpriteInput.FrameIndex == index))) task.Cancel(DateTimeOffset.UtcNow);
    }
    private SpriteFrameInput SpriteInput(SpriteAsset asset, int index)
        => new(asset.Plan.Id, asset.Plan, index, asset.PlanRevision, index == 0 ? null : asset.ApprovedBaseImageId,
            SpriteAssetCanvasOf(asset).Generation, SpriteAssetCanvasOf(asset).Output, SpriteAssetCanvasOf(asset).Transform);
    private bool MatchesSpriteInput(SpriteAsset asset, SpriteFrameInput input)
        => input.AssetId == input.Plan.Id && input.PlanRevision == asset.PlanRevision
            && !GenerationChanged(asset.Plan, input.Plan) && input.FrameIndex >= 0 && input.FrameIndex < asset.Frames.Count
            && input.Canvas == SpriteAssetCanvasOf(asset).Output && input.GenerationCanvas == SpriteAssetCanvasOf(asset).Generation
            && input.Transform == SpriteAssetCanvasOf(asset).Transform && (input.FrameIndex == 0 ? input.BaseImageId is null
                : input.BaseImageId is not null && input.BaseImageId == asset.ApprovedBaseImageId
                    && input.BaseImageId == asset.Frames[0].CurrentImageId);
    private SpriteAssetApproval SpriteApproval(SpriteAsset asset)
        => new(asset.PlanRevision, new(asset.Plan.Id, asset.Plan.Name, asset.Plan.Order, asset.Plan.Fps,
            asset.Plan.Loop, asset.Anchor, Sprites!.Settings.Repeat,
            Sprites.Settings.View == SpriteView.Isometric && SpriteRules.RoleOf(Sprites.Settings, asset.Plan) == SpriteAssetRole.Tile
                ? SpriteTileLayout.Diamond : SpriteTileLayout.Square,
            asset.Frames[0].CurrentImageId!.Value,
            Array.AsReadOnly(asset.Frames.OrderBy(f => f.Index).Select(f => f.CurrentImageId!.Value).ToArray())));
    private Result<IReadOnlyList<SpriteAsset>> SelectSpriteAssets(IReadOnlyList<Guid> ids)
    {
        if (ids is null || ids.Count == 0 || ids.Distinct().Count() != ids.Count)
            return Result<IReadOnlyList<SpriteAsset>>.Fail(ErrorCode.SpritePlanInvalid, "고유한 제작 대상이 필요합니다");
        var assets = Sprites!.Assets.Where(a => ids.Contains(a.Plan.Id)).ToArray();
        return assets.Length == ids.Count ? Result<IReadOnlyList<SpriteAsset>>.Ok(Array.AsReadOnly(assets))
            : Result<IReadOnlyList<SpriteAsset>>.Fail(ErrorCode.SpriteAssetNotFound, "대상이 없습니다");
    }
    private static bool SameApprovedAsset(SpriteApprovedAsset left, SpriteApprovedAsset right)
        => left.Id == right.Id && left.Name == right.Name && left.Order == right.Order && left.Fps == right.Fps
            && left.Loop == right.Loop && left.Anchor == right.Anchor && left.Repeat == right.Repeat
            && left.Layout == right.Layout && left.BaseImageId == right.BaseImageId
            && left.ImageIds.SequenceEqual(right.ImageIds);
    private static bool SameExportInput(SpriteExportInput left, SpriteExportInput right)
        => left.SchemaVersion == right.SchemaVersion && left.ExportId == right.ExportId
            && left.ReviewRevision == right.ReviewRevision && left.View == right.View && left.OutputKind == right.OutputKind
            && left.SourceCanvas == right.SourceCanvas && left.OutputCanvas == right.OutputCanvas
            && left.Grid == right.Grid && left.TileWidth == right.TileWidth
            && (left.AssetDetails is null ? right.AssetDetails is null
                : right.AssetDetails is not null && left.AssetDetails.SequenceEqual(right.AssetDetails))
            && left.ExcludedAssetIds.SequenceEqual(right.ExcludedAssetIds) && left.Assets.Count == right.Assets.Count
            && left.Assets.Zip(right.Assets).All(pair => SameApprovedAsset(pair.First, pair.Second));
    private static bool CanAttachSpriteResult(PipelineTask task, DateTimeOffset now)
        => task.Status == TaskStatus.Succeeded || (task.Status == TaskStatus.Running
            && task.LeaseExpiresAt is { } expires && expires > now);
    private void OpenSpriteReview()
    {
        Status = JobStatus.PendingReview;
        CompletedAt = null;
        FailureReason = null;
    }
    private void UpdateSpriteState(bool hasNewFrames = false)
    {
        OpenSpriteReview();
        var state = Sprites!;
        var active = _tasks.Where(t => !t.IsTerminal && IsCurrentTask(t)).ToArray();
        if (active.Length > 0 || hasNewFrames) Status = JobStatus.Running;
        // 원본 준비·분석 재시도 대기 중 단계 보존
        if (state.Phase is SpritePhase.PlanReview or SpritePhase.SourceDraft or SpritePhase.Analyzing) return;
        state.SetPhase(active.Any(t => t.SpriteExportInput is not null) ? SpritePhase.Packaging
            : active.Any(t => t.SpriteInput?.FrameIndex == 0) ? SpritePhase.BaseGeneration
            : hasNewFrames || active.Any(t => t.SpriteInput is not null) ? SpritePhase.FrameGeneration
            : state.Assets.All(a => a.Approval is not null) ? SpritePhase.ExportReady
            : state.Assets.Any(a => a.ApprovedBaseImageId is null) ? SpritePhase.BaseReview : SpritePhase.FrameReview);
    }
    private void ReconcileSpriteTasks(DateTimeOffset now)
    {
        if (Status == JobStatus.Canceled || IsTerminal) return;
        // 원본 준비 공정 실패는 작업 실패가 아닌 재시도 대기
        if (Sprites!.Phase == SpritePhase.SourceDraft)
        {
            UpdateSpriteState();
            return;
        }
        var tasks = _tasks.Where(IsCurrentTask).ToArray();
        if (tasks.Any(t => !t.IsTerminal))
        {
            UpdateSpriteState();
            return;
        }
        var exportTask = _tasks.Where(t => t.SpriteExportInput is not null).MaxBy(t => t.Ordinal);
        var export = Sprites!.Exports.LastOrDefault(e => e.IsCurrent && e.TaskId == exportTask?.Id);
        if (export is not null && export.Id != Sprites.CompletedExportId
            && exportTask?.Status == TaskStatus.Succeeded && IsCurrentTask(exportTask))
        {
            Status = export.Input.ExcludedAssetIds.Count == 0 ? JobStatus.Succeeded : JobStatus.PartiallySucceeded;
            CompletedAt = now;
            Sprites.CompleteExport(export.Id);
            return;
        }
        if (exportTask is { Status: TaskStatus.Failed } && IsCurrentTask(exportTask)
            && exportTask.SpriteExportInput!.ExportId != Sprites.CompletedExportId
            && Sprites.Images.Count > 0)
        {
            Status = JobStatus.PartiallySucceeded;
            CompletedAt = now;
            FailureReason = exportTask.FailureReason;
            Sprites.ConsumeFailedExport(exportTask.SpriteExportInput!.ExportId);
            return;
        }
        if (Sprites.Assets.Count == 0 || Sprites.Assets.All(a => a.Frames.All(f => f.CurrentImageId is null)))
        {
            var failure = tasks.FirstOrDefault(t => t.Status == TaskStatus.Failed);
            if (failure is not null) Fail(failure.FailureReason ?? ErrorCode.SpriteNotReady, now);
            return;
        }
        UpdateSpriteState();
    }
}
