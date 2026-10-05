using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Persistence.InMemory;

namespace Noxtend.Tests.Application;

/// <summary>Google image model discovery must validate the credential, not only return constants.</summary>
public sealed class ImageModelCatalogTests
{
    [Fact]
    public async Task OpenAiImageModels_RejectsInvalidCredential()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (catalog, providerId) = await CreateCatalogAsync(
            handler, ProviderKind.OpenAI, "test-openai-key");

        var exception = await Assert.ThrowsAsync<ProviderCallFailedException>(
            () => catalog.ListImageModelsAsync(providerId, CancellationToken.None));

        Assert.Contains("HTTP 401", exception.Message);
        Assert.Equal("https://api.openai.com/v1/models", handler.RequestUri);
        Assert.Equal("Bearer test-openai-key", handler.Authorization);
    }

    [Fact]
    public async Task OpenAiImageModels_ReturnsOnlyCurrentAllowedModelsAvailableToTheKey()
    {
        const string payload = """
            {
              "data": [
                { "id": "gpt-image-2" },
                { "id": "gpt-image-1.5" },
                { "id": "gpt-5.6-sol" }
              ]
            }
            """;

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        });
        var (catalog, providerId) = await CreateCatalogAsync(
            handler, ProviderKind.OpenAI, "test-openai-key");

        var models = await catalog.ListImageModelsAsync(providerId, CancellationToken.None);

        Assert.Equal(["gpt-image-2"], models.Select(model => model.Id));
    }

    [Fact]
    public async Task GoogleImageModels_RejectsInvalidCredential()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (catalog, providerId) = await CreateGoogleCatalogAsync(handler);

        var exception = await Assert.ThrowsAsync<ProviderCallFailedException>(
            () => catalog.ListImageModelsAsync(providerId, CancellationToken.None));

        Assert.Contains("HTTP 401", exception.Message);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models", handler.RequestUri);
    }

    [Fact]
    public async Task GoogleImageModels_ReturnsOnlyCurrentAllowedModelsAvailableToTheKey()
    {
        const string payload = """
            {
              "models": [
                { "name": "models/gemini-3.1-flash-image", "displayName": "Gemini 3.1 Flash Image" },
                { "name": "models/gemini-3-pro-image", "displayName": "Gemini 3 Pro Image" },
                { "name": "models/gemini-text-only", "displayName": "Gemini Text" }
              ]
            }
            """;

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        });
        var (catalog, providerId) = await CreateGoogleCatalogAsync(handler);

        var models = await catalog.ListImageModelsAsync(providerId, CancellationToken.None);

        Assert.Equal(
            ["gemini-3.1-flash-image", "gemini-3-pro-image"],
            models.Select(model => model.Id));
        Assert.Equal("test-google-key", handler.ApiKey);
    }

    [Fact]
    public void StaticImageModelCatalog_UsesCurrentStableModelIds()
    {
        Assert.Equal(
            ["gpt-image-2"],
            Noxtend.Infrastructure.Image.ImageModels.For(ProviderKind.OpenAI)
                .Select(model => model.Id));

        Assert.Equal(
            [
                "gemini-3.1-flash-image",
                "gemini-3.1-flash-lite-image",
                "gemini-3-pro-image",
                "gemini-2.5-flash-image",
            ],
            Noxtend.Infrastructure.Image.ImageModels.For(ProviderKind.Google)
                .Select(model => model.Id));
    }

    private static async Task<(ModelCatalog Catalog, Guid ProviderId)> CreateGoogleCatalogAsync(
        StubHandler handler)
        => await CreateCatalogAsync(handler, ProviderKind.Google, "test-google-key");

    private static async Task<(ModelCatalog Catalog, Guid ProviderId)> CreateCatalogAsync(
        StubHandler handler,
        ProviderKind kind,
        string apiKey)
    {
        var configs = new InMemoryProviderConfigRepository();
        var config = ProviderConfig.Create(
            "이미지 공급자", kind, apiKey, "-key", DateTimeOffset.UtcNow);
        await configs.AddAsync(config, CancellationToken.None);

        return (
            new ModelCatalog(
                new ProviderCredentialResolver(configs, new IdentityProtector()),
                new StubHttpClientFactory(new HttpClient(handler)),
                new MemoryCache(new MemoryCacheOptions()),
                useFake: false),
            config.Id);
    }

    private sealed class IdentityProtector : ISecretProtector
    {
        public string Protect(string plain) => plain;
        public string Unprotect(string cipher) => cipher;
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            ApiKey = request.Headers.TryGetValues("x-goog-api-key", out var values)
                ? values.Single()
                : null;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(respond(request));
        }
    }
}
