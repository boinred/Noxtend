namespace Noxtend.Domain.Sprites;

public sealed class SpritePipelineState
{
    private readonly List<SpriteAsset> _assets = [];
    private readonly List<SpriteImage> _images = [];
    private readonly List<SpriteExport> _exports = [];
    private readonly List<SpriteAcceptedRequest> _requests = [];

    private SpritePipelineState()
    {
        // EF Core 재구성용
    }

    internal SpritePipelineState(SpriteSettings settings, SpriteCanvas sourceCanvas, SpriteCanvas generationCanvas)
    {
        Settings = settings;
        SourceCanvas = sourceCanvas;
        GenerationCanvas = generationCanvas;
        OutputCanvas = SpriteRules.OutputCanvas(settings, sourceCanvas);
        Transform = SpriteRules.Transform(generationCanvas, OutputCanvas);
        Phase = SpritePhase.Analyzing;
    }

    public SpriteSettings Settings { get; private set; } = null!;
    public SpriteCanvas SourceCanvas { get; private set; } = null!;
    public SpriteCanvas GenerationCanvas { get; private set; } = null!;
    public SpriteCanvas OutputCanvas { get; private set; } = null!;
    public SpriteTransform Transform { get; private set; } = null!;
    public SpritePhase Phase { get; private set; }
    public int ReviewRevision { get; private set; }
    public Guid? CompletedExportId { get; private set; }
    public IReadOnlyList<SpriteAsset> Assets => _assets.AsReadOnly();
    public IReadOnlyList<SpriteImage> Images => _images.AsReadOnly();
    public IReadOnlyList<SpriteExport> Exports => _exports.AsReadOnly();
    public IReadOnlyList<SpriteAcceptedRequest> Requests => _requests.AsReadOnly();
    internal void SetPhase(SpritePhase phase) => Phase = phase;
    internal void CompleteExport(Guid exportId)
    {
        CompletedExportId = exportId;
        Phase = SpritePhase.Completed;
    }
    internal void Touch(IReadOnlyCollection<Guid>? affectedAssetIds = null)
    {
        ReviewRevision++;
        foreach (var export in _exports.Where(e => affectedAssetIds is null
            || e.Input.Assets.Any(a => affectedAssetIds.Contains(a.Id)))) export.SetCurrent(false);
    }
    internal void ReplaceAssets(IEnumerable<SpriteAsset> assets)
    {
        var replacement = assets.ToArray();
        _assets.Clear();
        _assets.AddRange(replacement);
    }
    internal void AddImage(SpriteImage image) => _images.Add(image);
    internal void AddExport(SpriteExport export) => _exports.Add(export);
    internal void AddRequest(SpriteAcceptedRequest request) => _requests.Add(request);
}

public sealed class SpriteAsset
{
    private readonly List<SpriteFrame> _frames = [];
    private SpriteAsset() { }
    internal SpriteAsset(SpriteAssetPlan plan, SpriteAnchor anchor)
    {
        Id = plan.Id;
        Plan = plan;
        Anchor = anchor;
        PlanRevision = 1;
        ResetFrames();
    }
    public Guid Id { get; private set; }
    public SpriteAssetPlan Plan { get; private set; } = null!;
    public int PlanRevision { get; private set; }
    public SpriteAnchor Anchor { get; private set; } = null!;
    public IReadOnlyList<SpriteFrame> Frames => _frames.OrderBy(frame => frame.Index).ToArray();
    public Guid? ApprovedBaseImageId { get; private set; }
    public SpriteAssetApproval? Approval { get; private set; }

    internal void UpdatePlan(SpriteAssetPlan plan, bool generationChanged)
    {
        Plan = plan;
        Approval = null;
        if (generationChanged)
        {
            PlanRevision++;
            ApprovedBaseImageId = null;
            ResetFrames();
        }
    }
    private void ResetFrames()
    {
        var count = Plan.Loop ? Plan.FrameCount : 1;
        // 동일 슬롯의 EF 추적 키 보존
        _frames.RemoveAll(frame => frame.Index >= count);
        foreach (var frame in _frames) frame.Clear();
        for (var index = _frames.Count; index < count; index++)
            _frames.Add(new SpriteFrame(index));
    }
    internal void InvalidateFrame(int index)
    {
        Approval = null;
        if (index == 0)
        {
            ApprovedBaseImageId = null;
            foreach (var frame in _frames) frame.Clear();
        }
        else _frames.Single(frame => frame.Index == index).Clear();
    }
    internal void ApproveBase(Guid imageId) => ApprovedBaseImageId = imageId;
    internal void Approve(SpriteAssetApproval approval) => Approval = approval;
}

public sealed class SpriteFrame
{
    private SpriteFrame() { }
    internal SpriteFrame(int index) => Index = index;
    public int Index { get; private set; }
    public Guid? CurrentTaskId { get; private set; }
    public Guid? CurrentImageId { get; private set; }
    internal void Bind(Guid taskId) { CurrentTaskId = taskId; CurrentImageId = null; }
    internal void Attach(Guid imageId) => CurrentImageId = imageId;
    internal void Clear() { CurrentTaskId = null; CurrentImageId = null; }
}

public sealed class SpriteImage
{
    private SpriteImage() { }
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid AssetId { get; private set; }
    public int FrameIndex { get; private set; }
    public int PlanRevision { get; private set; }
    public Guid? BaseImageId { get; private set; }
    public string BlobKey { get; private set; } = null!;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string ContentType { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public static SpriteImage Create(Guid taskId, SpriteFrameInput input, string blobKey, DateTimeOffset now)
        => new() { Id = Guid.NewGuid(), TaskId = taskId, AssetId = input.AssetId,
            FrameIndex = input.FrameIndex, PlanRevision = input.PlanRevision, BaseImageId = input.BaseImageId,
            BlobKey = blobKey, Width = input.Canvas.Width, Height = input.Canvas.Height,
            ContentType = "image/png", CreatedAt = now };
}

public sealed class SpriteExport
{
    private SpriteExport() { }
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public SpriteExportInput Input { get; private set; } = null!;
    public SpriteManifest Manifest { get; private set; } = null!;
    public string BlobKey { get; private set; } = null!;
    public bool IsCurrent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public static SpriteExport Create(Guid taskId, SpriteExportInput input, SpriteManifest manifest,
        string blobKey, DateTimeOffset now)
        => new() { Id = input.ExportId, TaskId = taskId, Input = input, Manifest = manifest,
            BlobKey = blobKey, CreatedAt = now };
    internal void SetCurrent(bool current) => IsCurrent = current;
}

public sealed class SpriteAcceptedRequest
{
    private SpriteAcceptedRequest() { }
    public Guid RequestId { get; private set; }
    public Guid JobId { get; private set; }
    public SpriteRequestKind Kind { get; private set; }
    public string Fingerprint { get; private set; } = null!;
    public SpriteReceipt Receipt { get; private set; } = null!;
    public static SpriteAcceptedRequest Create(Guid requestId, Guid jobId, SpriteRequestKind kind,
        string fingerprint, SpriteReceipt receipt)
        => new() { RequestId = requestId, JobId = jobId, Kind = kind, Fingerprint = fingerprint,
            Receipt = receipt with { TaskIds = Array.AsReadOnly(receipt.TaskIds.ToArray()) } };
}
