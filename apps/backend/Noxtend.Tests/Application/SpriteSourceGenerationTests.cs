using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Api.Contracts;
using Noxtend.Api.Controllers;
using Noxtend.Application.Generation;
using Noxtend.Application.Sprites;
using Noxtend.Application.Uploads;
using Noxtend.Domain.Common;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;
using Noxtend.Infrastructure.Image;

namespace Noxtend.Tests.Application;

public sealed class SpriteSourceGenerationTests
{
    [Fact]
    public async Task Success_StoresUploadWithTrimmedPromptAndSourceContext()
    {
        var image = new CapturingProvider(FakeImageProvider.Succeeding());
        var f = new PipelineFixture(imageProvider: image);
        var command = (await Prepare(f)) with { Prompt = "  항구 {{literal}}  ", ImageModel = " " + StubModelCatalog.DefaultImageModel + " " };
        var result = await f.GenerateSpriteSource.HandleAsync(command, default);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Same(result.Value, await f.Images.GetAsync(result.Value!.Id, default));
        Assert.Equal("sprite-prompt", result.Value.OriginalName);
        await using var blob = await f.Blobs.OpenReadAsync(result.Value.BlobKey, default);
        Assert.True(blob.Length > 0);
        var request = Assert.Single(image.Requests);
        Assert.Empty(request.Reference);
        Assert.Equal("1536x1024", request.Size);
        Assert.Equal(ImageBackground.Opaque, request.Background);
        Assert.Equal("2D 기준 장면\n\n항구 {{literal}}", request.Prompt);
        Assert.Equal(command.RequestId, request.Context.SourceGenerationId);
        Assert.Null(request.Context.JobId);
        Assert.Null(request.Context.TaskId);
        Assert.Null(request.Context.PartId);
        Assert.Equal(StubModelCatalog.DefaultImageModel, request.Context.Model);
        Assert.Contains((LlmOperationKind.GenerateSpriteSource, Noxtend.Domain.Job.AssetCategory.Background), f.Prompts.Queries);
    }

    [Fact]
    public void PickSize_PrefersLandscapeOrSquareThenLargestArea()
    {
        Assert.Equal(new(1024, 1024), GenerateSpriteSourceHandler.PickSize([new(1024, 1536), new(1024, 1024)]));
        Assert.Equal(new(1024, 1536), GenerateSpriteSourceHandler.PickSize([new(1024, 1536)]));
        Assert.Equal(new(1792, 1024), GenerateSpriteSourceHandler.PickSize([new(1024, 1024), new(1536, 1024), new(1792, 1024)]));
    }

    [Theory]
    [InlineData("nullPrompt")]
    [InlineData("emptyPrompt")]
    [InlineData("spacePrompt")]
    [InlineData("longPrompt")]
    [InlineData("emptyId")]
    [InlineData("nullModel")]
    [InlineData("emptyModel")]
    [InlineData("spaceModel")]
    public async Task InvalidSettings_DoNotCallProviderOrUpload(string input)
    {
        var provider = new CapturingProvider(FakeImageProvider.Succeeding());
        var f = new PipelineFixture(imageProvider: provider);
        var command = await Prepare(f);
        command = input switch
        {
            "nullPrompt" => command with { Prompt = null! },
            "emptyPrompt" => command with { Prompt = "" },
            "spacePrompt" => command with { Prompt = "   " },
            "longPrompt" => command with { Prompt = new('x', 1001) },
            "emptyId" => command with { RequestId = Guid.Empty },
            "nullModel" => command with { ImageModel = null! },
            "emptyModel" => command with { ImageModel = "" },
            _ => command with { ImageModel = "   " },
        };
        var result = await f.GenerateSpriteSource.HandleAsync(command, default);
        Assert.Equal(ErrorCode.SpriteSettingsInvalid, result.ErrorCode);
        Assert.Equal("유효한 요청 ID, 1~1000자 장면 설명과 이미지 모델이 필요합니다", result.ErrorMessage);
        Assert.Empty(provider.Requests);
        Assert.Equal(0, f.Blobs.Count);
    }

    [Fact]
    public async Task Exactly1000TrimmedCharacters_Succeed()
    {
        var f = new PipelineFixture();
        Assert.True((await f.GenerateSpriteSource.HandleAsync((await Prepare(f)) with { Prompt = " " + new string('x', 1000) + " " }, default)).IsSuccess);
    }

    [Theory]
    [InlineData(false, ErrorCode.JobImageProviderDisabled)]
    [InlineData(true, ErrorCode.JobImageProviderNotFound)]
    public async Task UnavailableProvider_DoesNotGenerate(bool missing, string error)
    {
        var provider = new CapturingProvider(FakeImageProvider.Succeeding());
        var f = new PipelineFixture(imageProvider: provider);
        var command = await Prepare(f, enabled: false);
        if (missing) command = command with { ImageProviderConfigId = Guid.NewGuid() };
        Assert.Equal(error, (await f.GenerateSpriteSource.HandleAsync(command, default)).ErrorCode);
        Assert.Empty(provider.Requests);
        Assert.Equal(0, f.Blobs.Count);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("noSprite")]
    [InlineData("opaque")]
    [InlineData("nullSizes")]
    [InlineData("emptySizes")]
    [InlineData("nullSize")]
    [InlineData("zeroWidth")]
    [InlineData("zeroHeight")]
    [InlineData("negativeWidth")]
    [InlineData("negativeHeight")]
    public async Task UnconfirmedModelCapabilities_DoNotGenerate(string input)
    {
        var provider = new CapturingProvider(FakeImageProvider.Succeeding());
        var f = new PipelineFixture(imageProvider: provider);
        var command = await Prepare(f);
        SpriteImageCapabilities? capability = input switch
        {
            "noSprite" => null,
            "opaque" => new(false, [new(1024, 1024)]),
            "nullSizes" => new(true, null!),
            "emptySizes" => new(true, []),
            "nullSize" => new(true, [null!]),
            "zeroWidth" => new(true, [new(0, 1024)]),
            "zeroHeight" => new(true, [new(1024, 0)]),
            "negativeWidth" => new(true, [new(-1, 1024)]),
            "negativeHeight" => new(true, [new(1024, -1)]),
            _ => new(true, [new(1024, 1024)]),
        };
        f.Catalog.ImageModels.Clear();
        if (input != "missing") f.Catalog.ImageModels.Add(new(command.ImageModel, "sprite", capability));
        Assert.Equal(ErrorCode.JobImageModelUnavailable, (await f.GenerateSpriteSource.HandleAsync(command, default)).ErrorCode);
        Assert.Empty(provider.Requests);
        Assert.Equal(0, f.Blobs.Count);
    }

    [Fact]
    public async Task MissingPrompt_DoesNotGenerate()
    {
        var provider = new CapturingProvider(FakeImageProvider.Succeeding());
        var f = new PipelineFixture(imageProvider: provider);
        var command = await Prepare(f);
        f.Prompts.Remove(LlmOperationKind.GenerateSpriteSource);
        Assert.Equal(ErrorCode.PromptNotActive, (await f.GenerateSpriteSource.HandleAsync(command, default)).ErrorCode);
        Assert.Empty(provider.Requests);
        Assert.Equal(0, f.Blobs.Count);
    }

    [Theory]
    [InlineData("success", 201, true, 1)]
    [InlineData("failure", 502, false, null)]
    [InlineData("badResponse", 502, false, null)]
    [InlineData("two", 502, true, 2)]
    [InlineData("gif", 502, true, 1)]
    [InlineData("empty", 502, true, 1)]
    [InlineData("large", 502, true, 1)]
    public async Task ApiOutcome_PreservesProviderSuccessAndUsage(string input, int status, bool succeeded, int? count)
    {
        var recorder = new Recorder();
        var inner = input switch
        {
            "failure" => FakeImageProvider.Failing(new ProviderCallFailedException("quota", isTransient: false)),
            "badResponse" => FakeImageProvider.Failing(new ProviderBadResponseException("bad response")),
            "two" => FakeImageProvider.Returning([137, 80, 78, 71], imageCount: 2),
            "gif" => FakeImageProvider.Returning([1, 2, 3], "image/gif"),
            "empty" => FakeImageProvider.Returning([]),
            "large" => FakeImageProvider.Returning(new byte[12 * 1024 * 1024 + 1]),
            _ => FakeImageProvider.Succeeding(),
        };
        var provider = new RecordingImageProvider(inner, recorder, NullLogger<RecordingImageProvider>.Instance);
        var f = new PipelineFixture(imageProvider: provider);
        var command = await Prepare(f);
        var response = Assert.IsType<ObjectResult>(await new UploadsController(f.Upload, f.Images, f.Blobs, f.GenerateSpriteSource)
            .GenerateAsync(new(command.RequestId, command.Prompt, command.ImageProviderConfigId, command.ImageModel), default));
        Assert.Equal(status, response.StatusCode);
        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(command.RequestId, entry.Context.SourceGenerationId);
        Assert.Equal(LlmOperationKind.GenerateSpriteSource, entry.Context.Kind);
        Assert.Equal(succeeded, entry.Succeeded);
        Assert.Equal(count, entry.OutputImages);
        Assert.Equal(succeeded ? 1120 : (int?)null, entry.InputTokens);
        Assert.Equal(succeeded ? 1120 : (int?)null, entry.OutputTokens);
        Assert.False(recorder.Tokens.Single().CanBeCanceled);
        if (status != 201)
        {
            Assert.Equal(0, f.Blobs.Count);
            var error = Assert.IsType<ApiResponse<UploadResponse>>(response.Value).Error!;
            Assert.Equal(input == "failure" ? ErrorCode.ProviderCallFailed : ErrorCode.ProviderBadResponse, error.Code);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FactoryErrors_AreMappedBeforeUpload(bool badResponse)
    {
        var f = new PipelineFixture();
        var command = await Prepare(f);
        var handler = Handler(f, factory: new FailingFactory(badResponse));
        Assert.Equal(badResponse ? ErrorCode.ProviderBadResponse : ErrorCode.ProviderCallFailed,
            (await handler.HandleAsync(command, default)).ErrorCode);
        Assert.Equal(0, f.Blobs.Count);
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("rate")]
    [InlineData("provider")]
    [InlineData("save")]
    public async Task Deadline_CoversEachAsyncStageAndMapsTo502(string stage)
    {
        var time = new ManualTime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recorder = new Recorder();
        var image = new CapturingProvider(stage == "provider" ? FakeImageProvider.Slow(TimeSpan.FromHours(1)) : FakeImageProvider.Succeeding(),
            stage == "provider" ? entered : null);
        var recorded = new RecordingImageProvider(image, recorder, NullLogger<RecordingImageProvider>.Instance);
        var f = new PipelineFixture(imageProvider: recorded);
        var command = await Prepare(f);
        var catalog = stage == "catalog" ? new SlowCatalog(f.Catalog, entered) : null;
        var limiter = stage == "rate" ? new SlowLimiter(entered, f.Clock.Now) : null;
        var blobs = stage == "save" ? new SlowBlobs(entered) : null;
        var handler = Handler(f, time, catalog, limiter, blobs);
        var api = new UploadsController(f.Upload, f.Images, f.Blobs, handler);
        var pending = api.GenerateAsync(new(command.RequestId, command.Prompt, command.ImageProviderConfigId, command.ImageModel), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(179));
        Assert.False(pending.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        var response = Assert.IsType<ObjectResult>(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(502, response.StatusCode);
        var error = Assert.IsType<ApiResponse<UploadResponse>>(response.Value).Error!;
        Assert.Equal(ErrorCode.ProviderCallFailed, error.Code);
        Assert.Equal("기준 이미지 생성 제한 시간(180초)을 초과했습니다. 다시 시도해 주세요", error.Message);
        Assert.Equal(0, f.Blobs.Count);
        Assert.All(recorder.Entries, entry => Assert.True(entry.Succeeded));
        Assert.Equal(stage == "save" ? 1 : 0, recorder.Entries.Count);
        Assert.All(image.Tokens, token => Assert.True(token.CanBeCanceled && token.IsCancellationRequested == (stage == "provider" || stage == "save")));
        if (catalog is not null) Assert.True(catalog.Token.IsCancellationRequested);
        if (limiter is not null) Assert.True(limiter.Token.IsCancellationRequested);
        if (blobs is not null) Assert.True(blobs.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task ClientCancellation_PropagatesWithoutRecordingUnknownUsage()
    {
        var time = new ManualTime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recorder = new Recorder();
        var image = new CapturingProvider(FakeImageProvider.Slow(TimeSpan.FromHours(1)), entered);
        var f = new PipelineFixture(imageProvider: new RecordingImageProvider(image, recorder, NullLogger<RecordingImageProvider>.Instance));
        var command = await Prepare(f);
        using var request = new CancellationTokenSource();
        var pending = Handler(f, time).HandleAsync(command, request.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        request.Cancel();
        time.Advance(TimeSpan.FromSeconds(180));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(recorder.Entries);
        Assert.Equal(0, f.Blobs.Count);
    }

    [Fact]
    public async Task CancellationDuringStorage_PreservesAlreadyWrittenBlob()
    {
        var f = new PipelineFixture();
        var command = await Prepare(f);
        var time = new ManualTime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blobs = new SlowBlobs(entered, f.Blobs);
        var pending = Handler(f, time, blobs: blobs).HandleAsync(command, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, f.Blobs.Count);
        time.Advance(TimeSpan.FromSeconds(180));
        Assert.Equal(ErrorCode.ProviderCallFailed, (await pending).ErrorCode);
        Assert.Equal(1, f.Blobs.Count);
    }

    [Fact]
    public async Task ResponseAfterDeadline_KeepsActualProviderRecordWithoutUpload()
    {
        var time = new ManualTime();
        var recorder = new Recorder();
        var image = new CapturingProvider(FakeImageProvider.Succeeding(), expire: () => time.Advance(TimeSpan.FromSeconds(180)));
        var f = new PipelineFixture(imageProvider: new RecordingImageProvider(image, recorder, NullLogger<RecordingImageProvider>.Instance));
        var command = await Prepare(f);
        Assert.Equal(ErrorCode.ProviderCallFailed, (await Handler(f, time).HandleAsync(command, default)).ErrorCode);
        var entry = Assert.Single(recorder.Entries);
        Assert.True(entry.Succeeded);
        Assert.Equal((1120, 1120, 1), (entry.InputTokens, entry.OutputTokens, entry.OutputImages));
        Assert.Equal(0, f.Blobs.Count);
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("factory")]
    [InlineData("provider")]
    public async Task NoncooperativeReturn_IsCheckedBeforeNextStage(string stage)
    {
        var time = new ManualTime();
        var image = new CapturingProvider(FakeImageProvider.Succeeding(), expire: stage == "provider" ? () => time.Advance(TimeSpan.FromSeconds(180)) : null);
        var f = new PipelineFixture(imageProvider: image);
        var command = await Prepare(f);
        var handler = Handler(f, time,
            catalog: stage == "catalog" ? new SlowCatalog(f.Catalog, null, () => time.Advance(TimeSpan.FromSeconds(180))) : null,
            factory: stage == "factory" ? new ExpiringFactory(image, time) : null);
        Assert.Equal(ErrorCode.ProviderCallFailed, (await handler.HandleAsync(command, default)).ErrorCode);
        Assert.Equal(stage == "provider" ? 1 : 0, image.Requests.Count);
        Assert.Equal(0, f.Blobs.Count);
    }

    [Fact]
    public async Task RateLimitHeaders_AreRecordedWithActualTokens()
    {
        var f = new PipelineFixture();
        var command = await Prepare(f);
        var handler = Handler(f, factory: new PipelineFixture.StubImageProviderFactory(new HeaderProvider()));
        Assert.True((await handler.HandleAsync(command, default)).IsSuccess);
        var status = await f.RateLimiter.GetStatusAsync(command.ImageProviderConfigId, default);
        Assert.Equal(new RateLimitStatus(4, f.Clock.Now.AddSeconds(15), 9000, f.Clock.Now.AddSeconds(20), 17), status);
    }

    internal static async Task<GenerateSpriteSourceCommand> Prepare(PipelineFixture f, bool enabled = true)
    {
        var provider = await f.SeedProviderAsync(enabled);
        f.Catalog.ImageModels.Clear();
        f.Catalog.ImageModels.Add(new(StubModelCatalog.DefaultImageModel, "sprite", new(true, [new(1024, 1024), new(1536, 1024)])));
        return new(Guid.NewGuid(), "항구", provider, StubModelCatalog.DefaultImageModel);
    }

    private static GenerateSpriteSourceHandler Handler(PipelineFixture f, TimeProvider? time = null,
        IModelCatalog? catalog = null, IRateLimiter? limiter = null, IBlobStorage? blobs = null, IImageProviderFactory? factory = null)
        => new(f.Providers, catalog ?? f.Catalog, f.Prompts, factory ?? new PipelineFixture.StubImageProviderFactory(f.Images_),
            limiter is null ? f.RateLimitGate : new RateLimitGate(limiter, f.Clock),
            blobs is null ? f.Upload : new CreateUploadHandler(blobs, f.Images, f.Clock), time);

    private sealed class CapturingProvider(IImageProvider inner, TaskCompletionSource? entered = null, Action? expire = null) : IImageProvider
    {
        public List<ImageRequest> Requests { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            Tokens.Add(ct);
            entered?.TrySetResult();
            var result = await inner.GenerateAsync(request, ct);
            expire?.Invoke();
            return result;
        }
    }

    private sealed class HeaderProvider : IImageProvider
    {
        public Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct)
            => Task.FromResult(new ImageResult([1], "image/png", 1, 10, 7, 4, TimeSpan.FromSeconds(15), 9000, TimeSpan.FromSeconds(20)));
    }

    private sealed class Recorder : ILlmCallRecorder
    {
        public List<LlmCallEntry> Entries { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Task RecordAsync(LlmCallEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            Tokens.Add(ct);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingFactory(bool badResponse) : IImageProviderFactory
    {
        public Task<IImageProvider> CreateAsync(Guid providerConfigId, string model, CancellationToken ct)
            => throw (badResponse ? new ProviderBadResponseException("bad factory") : new ProviderCallFailedException("quota"));
    }

    private sealed class ExpiringFactory(IImageProvider image, ManualTime time) : IImageProviderFactory
    {
        public Task<IImageProvider> CreateAsync(Guid providerConfigId, string model, CancellationToken ct)
        {
            Assert.True(ct.CanBeCanceled);
            time.Advance(TimeSpan.FromSeconds(180));
            return Task.FromResult(image);
        }
    }

    private sealed class SlowCatalog(IModelCatalog inner, TaskCompletionSource? entered, Action? expire = null) : IModelCatalog
    {
        public CancellationToken Token { get; private set; }
        public async Task<IReadOnlyList<ProviderModel>> ListImageModelsAsync(Guid id, CancellationToken ct)
        {
            Token = ct;
            if (entered is not null)
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
            expire?.Invoke();
            return await inner.ListImageModelsAsync(id, ct);
        }
        public Task<IReadOnlyList<ProviderModel>> ListAsync(Guid id, CancellationToken ct) => inner.ListAsync(id, ct);
        public Task<IReadOnlyList<ProviderModel>> ListMeshModelsAsync(Guid id, CancellationToken ct) => inner.ListMeshModelsAsync(id, ct);
        public Task<int?> GetMeshCreditBalanceAsync(Guid id, CancellationToken ct) => inner.GetMeshCreditBalanceAsync(id, ct);
    }

    private sealed class SlowLimiter(TaskCompletionSource entered, DateTimeOffset now) : IRateLimiter
    {
        public CancellationToken Token { get; private set; }
        public Task<RateLimitStatus?> GetStatusAsync(Guid id, CancellationToken ct)
        {
            Token = ct;
            entered.TrySetResult();
            return Task.FromResult<RateLimitStatus?>(new(0, now.AddHours(1)));
        }
        public Task UpdateAsync(Guid id, RateLimitStatus status, CancellationToken ct) => throw new InvalidOperationException("생성 전 대기");
    }

    private sealed class SlowBlobs(TaskCompletionSource entered, IBlobStorage? backing = null) : IBlobStorage
    {
        public CancellationToken Token { get; private set; }
        public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct)
        {
            Token = ct;
            if (backing is not null) await backing.SaveAsync(content, contentType, ct);
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("취소 없는 저장");
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }

    internal sealed class ManualTime : TimeProvider
    {
        private ManualTimer? timer;
        public void Advance(TimeSpan elapsed) => timer!.Advance(elapsed);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => timer = new ManualTimer(callback, state, dueTime);
        private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan remaining) : ITimer
        {
            private bool disposed;
            public void Advance(TimeSpan elapsed)
            {
                if (disposed) return;
                remaining -= elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    disposed = true;
                    callback(state);
                }
            }
            public bool Change(TimeSpan dueTime, TimeSpan period) { remaining = dueTime; return !disposed; }
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
