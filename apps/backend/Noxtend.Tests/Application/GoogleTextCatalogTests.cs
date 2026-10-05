using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Persistence.InMemory;

namespace Noxtend.Tests.Application;

/// <summary>
/// Gemini 텍스트 모델 목록·공급자 등록 — gemini-text-provider (2026-08-28).
///
/// <see cref="ImageModelCatalogTests"/>와 같은 이유로 실 키 검증까지 확인한다 —
/// 허용목록만 돌려주면 쓰레기 키로도 연결 확인이 성공한다.
/// </summary>
public sealed class GoogleTextCatalogTests
{
    [Fact]
    public void GoogleSupportsTextAnalysis_AlongsideImageGeneration()
    {
        Assert.Equal(
            [ProviderCapability.TextAnalysis, ProviderCapability.ImageGeneration],
            ProviderCapabilities.For(ProviderKind.Google));
    }

    [Fact]
    public async Task GoogleTextModels_RejectsInvalidCredential()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (catalog, providerId) = await CreateCatalogAsync(handler);

        var exception = await Assert.ThrowsAsync<ProviderCallFailedException>(
            () => catalog.ListAsync(providerId, CancellationToken.None));

        Assert.Contains("HTTP 401", exception.Message);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models", handler.RequestUri);
    }

    [Fact]
    public async Task GoogleTextModels_ReturnsOnlyCurrentAllowedModelsAvailableToTheKey_ExcludingImageVariants()
    {
        const string payload = """
            {
              "models": [
                { "name": "models/gemini-3-pro" },
                { "name": "models/gemini-2.5-flash" },
                { "name": "models/gemini-3-pro-image" },
                { "name": "models/gemini-embedding-001" },
                { "name": "models/gemini-1.5-pro" }
              ]
            }
            """;

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        });
        var (catalog, providerId) = await CreateCatalogAsync(handler);

        var models = await catalog.ListAsync(providerId, CancellationToken.None);

        // gemini-3-pro-image(이미지 전용)·gemini-embedding-001(임베딩)·
        // gemini-1.5-pro(허용 계열 밖)는 빠지고 텍스트 계열만 남는다
        Assert.Equal(["gemini-3-pro", "gemini-2.5-flash"], models.Select(model => model.Id));
        Assert.Equal("test-google-key", handler.ApiKey);
    }

    [Fact]
    public async Task LlmProviderFactory_CreatesGoogleProvider_InsteadOfThrowing()
    {
        var configs = new InMemoryProviderConfigRepository();
        var config = ProviderConfig.Create(
            "텍스트 공급자", ProviderKind.Google, "test-google-key", "-key", DateTimeOffset.UtcNow);
        await configs.AddAsync(config, CancellationToken.None);

        var factory = new LlmProviderFactory(
            new ProviderCredentialResolver(configs, new IdentityProtector()),
            new StubHttpClientFactory(new HttpClient(new StubHandler(
                _ => new HttpResponseMessage(HttpStatusCode.OK)))),
            useFake: false,
            decorate: provider => provider);

        var provider = await factory.CreateAsync(config.Id, "gemini-3-pro", CancellationToken.None);

        Assert.IsType<GoogleProvider>(provider);
    }

    private static async Task<(ModelCatalog Catalog, Guid ProviderId)> CreateCatalogAsync(StubHandler handler)
    {
        var configs = new InMemoryProviderConfigRepository();
        var config = ProviderConfig.Create(
            "텍스트 공급자", ProviderKind.Google, "test-google-key", "-key", DateTimeOffset.UtcNow);
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            ApiKey = request.Headers.TryGetValues("x-goog-api-key", out var values)
                ? values.Single()
                : null;
            return Task.FromResult(respond(request));
        }
    }
}
