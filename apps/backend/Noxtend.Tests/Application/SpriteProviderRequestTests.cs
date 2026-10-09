using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Api.Contracts;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Domain.Sprites;
using Noxtend.Infrastructure.Image;
using SkiaSharp;

namespace Noxtend.Tests.Application;

public sealed class SpriteProviderRequestTests
{
    private const string Model = "gpt-image-2.5-sunburst";
    private static readonly ImageCallContext Context = new(
        Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Model);

    [Fact]
    public void SpriteCatalog_ContainsExplicitlySupportedModel()
    {
        var supported = Assert.Single(ImageModels.For(ProviderKind.OpenAI), model => model.Id == Model);
        Assert.True(supported.Sprite!.SupportsTransparency);
        Assert.Equal([
            new SpriteCanvas(1024, 1024), new(1536, 1024), new(1024, 1536), new(1536, 768),
            new(768, 1536), new(1536, 864), new(864, 1536),
        ], supported.Sprite.Sizes);
        Assert.Null(ImageModels.For(ProviderKind.OpenAI).First(model => model.Id == "gpt-image-2").Sprite);
        Assert.All(ImageModels.For(ProviderKind.Google), model => Assert.Null(model.Sprite));
        Assert.All(ImageModels.FakeModels, model => Assert.Equal(supported.Sprite, model.Sprite));
    }

    [Theory]
    [InlineData(false, ImageBackground.Transparent, "transparent")]
    [InlineData(true, ImageBackground.Transparent, "transparent")]
    [InlineData(false, ImageBackground.Opaque, "opaque")]
    [InlineData(true, ImageBackground.Opaque, "opaque")]
    public async Task SpriteRequest_SendsExplicitPngBackgroundAndSize(
        bool edit, ImageBackground background, string expectedBackground)
    {
        var handler = new CapturingHandler();
        var provider = new OpenAiImageProvider(new HttpClient(handler), "secret-key", Model);
        await provider.GenerateAsync(Request(edit, background), CancellationToken.None);

        Assert.Equal(edit ? "/v1/images/edits" : "/v1/images/generations", handler.Path);
        Assert.Equal(expectedBackground, handler.Fields["background"]);
        Assert.Equal("png", handler.Fields["output_format"]);
        Assert.Equal("1536x768", handler.Fields["size"]);
        Assert.Equal("medium", handler.Fields["quality"]);
        Assert.Equal(Model, handler.Fields["model"]);
        Assert.False(handler.Fields.ContainsKey("response_format"));
        if (edit)
        {
            Assert.Equal(["AQ==", "Ag=="], handler.Images);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyRequest_KeepsExistingQualityAndBodyDefaults(bool edit)
    {
        var handler = new CapturingHandler();
        var provider = new OpenAiImageProvider(new HttpClient(handler), "secret-key", "gpt-image-2");
        await provider.GenerateAsync(Request(edit, null) with { Size = "1024x1024" }, CancellationToken.None);

        Assert.Equal("medium", handler.Fields["quality"]);
        Assert.Equal("1024x1024", handler.Fields["size"]);
        Assert.False(handler.Fields.ContainsKey("background"));
        Assert.Equal("png", handler.Fields["output_format"]);
        Assert.False(handler.Fields.ContainsKey("response_format"));
        Assert.Equal(6, handler.Fields.Count);
        Assert.Equal("1", handler.Fields["n"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SunburstLegacyRequest_UsesPngWithoutUnsupportedResponseFormat(bool edit)
    {
        var handler = new CapturingHandler();
        var provider = new OpenAiImageProvider(new HttpClient(handler), "secret-key", Model);
        await provider.GenerateAsync(Request(edit, null) with { Size = "1024x1024" }, CancellationToken.None);
        Assert.Equal("png", handler.Fields["output_format"]);
        Assert.False(handler.Fields.ContainsKey("response_format"));
        Assert.False(handler.Fields.ContainsKey("background"));
        Assert.Equal("medium", handler.Fields["quality"]);
        Assert.Equal("1024x1024", handler.Fields["size"]);
        Assert.Equal("1", handler.Fields["n"]);
        Assert.Equal(Model, handler.Fields["model"]);
    }

    [Theory]
    [InlineData(false, "gpt-image-2", "1536x768", ImageBackground.Transparent)]
    [InlineData(false, "gpt-image-2.5-sunburst", "512x512", ImageBackground.Opaque)]
    [InlineData(false, "gpt-image-2.5-sunburst", "1536x768", (ImageBackground)99)]
    [InlineData(true, "gemini-3.1-flash-image", "1024x1024", ImageBackground.Transparent)]
    [InlineData(true, "gemini-3.1-flash-image", "1536x768", ImageBackground.Opaque)]
    public async Task UnknownTransparency_RejectsBeforeImageProviderCall(
        bool google, string model, string size, ImageBackground background)
    {
        var handler = new CapturingHandler();
        IImageProvider provider = google
            ? new GoogleImageProvider(new HttpClient(handler), "secret-key", model)
            : new OpenAiImageProvider(new HttpClient(handler), "secret-key", model);
        var exception = await Assert.ThrowsAsync<ProviderCallFailedException>(
            () => provider.GenerateAsync(Request(false, background) with { Size = size }, CancellationToken.None));
        Assert.False(exception.IsTransient);
        Assert.Equal(0, handler.CallCount);
        Assert.DoesNotContain("secret-key", exception.Message);
    }

    [Fact]
    public async Task Recording_LegacyContextKeepsGenerateOperation()
    {
        var partId = Guid.NewGuid();
        var request = Request(false, null) with { Context = Context with { PartId = partId } };
        var recorder = new CapturingRecorder();
        var provider = new RecordingImageProvider(
            FakeImageProvider.Returning([3, 4, 5]), recorder, NullLogger<RecordingImageProvider>.Instance);
        await provider.GenerateAsync(request, CancellationToken.None);
        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(LlmOperationKind.Generate, entry.Context.Kind);
        Assert.Equal(TaskKind.Generate, request.Context.Kind);
        Assert.Equal(partId, request.Context.PartId);
        AssertContext(entry);
    }

    [Fact]
    public async Task Recording_UsesExplicitContextKind()
    {
        var recorder = new CapturingRecorder();
        var provider = new RecordingImageProvider(
            FakeImageProvider.Succeeding(), recorder, NullLogger<RecordingImageProvider>.Instance);
        await provider.GenerateAsync(Request(false, null) with
        {
            Context = Context with { Kind = TaskKind.Analyze },
        }, CancellationToken.None);
        Assert.Equal(LlmOperationKind.Analyze, Assert.Single(recorder.Entries).Context.Kind);
    }

    [Fact]
    public async Task Recording_SpriteMetadataUsesTaskCorrelationAndContainsNoImageBytes()
    {
        var recorder = new CapturingRecorder();
        var provider = new RecordingImageProvider(
            FakeImageProvider.Returning([3, 4, 5]), recorder, NullLogger<RecordingImageProvider>.Instance);
        await provider.GenerateAsync(Request(true, ImageBackground.Transparent), CancellationToken.None);
        var entry = Assert.Single(recorder.Entries);
        AssertContext(entry);
        Assert.Null(Context.PartId);
        Assert.Contains("[size] 1536x768", entry.RequestPayload);
        Assert.Contains("[background] Transparent", entry.RequestPayload);
        Assert.Contains("[output_format] png", entry.RequestPayload);
        Assert.Contains("2장", entry.RequestPayload);
        Assert.DoesNotContain("AQ==", entry.RequestPayload);
        Assert.DoesNotContain("Ag==", entry.RequestPayload);
        Assert.DoesNotContain("AwQF", entry.ResponsePayload!);
        Assert.DoesNotContain("secret-key", entry.RequestPayload);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recording_SourceGenerationKeepsJoblessCorrelationOnSuccessAndFailure(bool fails)
    {
        var sourceGenerationId = Guid.NewGuid();
        var request = Request(false, ImageBackground.Opaque) with
        {
            Context = ImageCallContext.ForSourceGeneration(
                sourceGenerationId, Context.PromptVersionId, Context.ProviderConfigId, Model),
        };
        var recorder = new CapturingRecorder();
        var provider = new RecordingImageProvider(
            fails ? FakeImageProvider.Failing(new ProviderCallFailedException("failure", isTransient: false))
                : FakeImageProvider.Succeeding(), recorder, NullLogger<RecordingImageProvider>.Instance);

        if (fails)
            await Assert.ThrowsAsync<ProviderCallFailedException>(() => provider.GenerateAsync(request, CancellationToken.None));
        else
        {
            var result = await provider.GenerateAsync(request, CancellationToken.None);
            using var bitmap = SKBitmap.Decode(result.Bytes);
            Assert.NotNull(bitmap);
            Assert.Equal(1536, bitmap.Width);
            Assert.Equal(768, bitmap.Height);
        }

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(LlmOperationKind.GenerateSpriteSource, entry.Context.Kind);
        Assert.Equal(sourceGenerationId, entry.Context.SourceGenerationId);
        Assert.Null(entry.Context.JobId);
        Assert.Null(entry.Context.TaskId);
        Assert.Null(entry.Context.SimilarityEvaluationId);
        Assert.Equal(Context.PromptVersionId, entry.Context.PromptVersionId);
        Assert.Equal(Context.ProviderConfigId, entry.Context.ProviderConfigId);
        Assert.Equal(Model, entry.Context.Model);
        Assert.Equal(!fails, entry.Succeeded);
        Assert.Equal(fails ? null : 1, entry.OutputImages);
        Assert.Equal(fails ? null : 1_120, entry.InputTokens);
        Assert.Equal(fails ? null : 1_120, entry.OutputTokens);
    }

    [Theory]
    [InlineData(256, 128, 1536, 768)]
    [InlineData(128, 256, 768, 1536)]
    [InlineData(640, 360, 1536, 864)]
    [InlineData(360, 640, 864, 1536)]
    [InlineData(200, 200, 1024, 1024)]
    public void GenerationCanvas_SelectsClosestSupportedAspect(int w, int h, int expectedW, int expectedH)
    {
        var capability = ImageModels.For(ProviderKind.OpenAI).Single(model => model.Id == Model).Sprite!;
        Assert.Equal(new(expectedW, expectedH), SpriteRules.GenerationCanvas(new(w, h), capability.Sizes));
    }

    [Fact]
    public void GenerationCanvas_RejectsUnconfirmedOrInvalidSizes()
    {
        Assert.Throws<ArgumentException>(() => SpriteRules.GenerationCanvas(new(128, 128), []));
        Assert.Throws<ArgumentException>(() => SpriteRules.GenerationCanvas(new(128, 128), [new(0, 1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteRules.GenerationCanvas(new(0, 128), [new(1024, 1024)]));
    }

    [Fact]
    public void ProviderWireModel_PreservesUnknownAndConfirmedSpriteCapabilities()
    {
        var model = ImageModels.For(ProviderKind.OpenAI).Single(model => model.Id == Model);
        var response = ProviderModelResponse.From(model);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var sprite = document.RootElement.GetProperty("sprite");
        Assert.True(sprite.GetProperty("supportsTransparency").GetBoolean());
        Assert.Equal(7, sprite.GetProperty("sizes").GetArrayLength());
        Assert.Null(ProviderModelResponse.From(new("legacy", "Legacy")).Sprite);
    }

    private static void AssertContext(LlmCallEntry entry)
    {
        Assert.Equal(Context.JobId, entry.Context.JobId);
        Assert.Equal(Context.TaskId, entry.Context.TaskId);
        Assert.Null(entry.Context.SourceGenerationId);
        Assert.Equal(Context.PromptVersionId, entry.Context.PromptVersionId);
        Assert.Equal(Context.ProviderConfigId, entry.Context.ProviderConfigId);
        Assert.Equal(Model, entry.Context.Model);
    }

    private static ImageRequest Request(bool edit, ImageBackground? background)
        => new(Context, "sprite prompt", edit ? [
            new(new ImageContent([1], "image/png"), ReferenceRole.Original),
            new(new ImageContent([2], "image/png"), ReferenceRole.SpriteBase),
        ] : [], "1536x768", background);

    private sealed class CapturingRecorder : ILlmCallRecorder
    {
        public List<LlmCallEntry> Entries { get; } = [];
        public Task RecordAsync(LlmCallEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Fields { get; } = [];
        public List<string> Images { get; } = [];
        public string? Path { get; private set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            Path = request.RequestUri!.AbsolutePath;
            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                {
                    var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                    if (name == "image[]")
                        Images.Add(Convert.ToBase64String(await part.ReadAsByteArrayAsync(ct)));
                    else
                        Fields.Add(name, await part.ReadAsStringAsync(ct));
                }
            }
            else
            {
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                foreach (var property in document.RootElement.EnumerateObject())
                    Fields.Add(property.Name, property.Value.ToString());
            }
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "data": [{ "b64_json": "iVBORw0KGgo=" }] }""", Encoding.UTF8, "application/json"),
            };
        }
    }
}
