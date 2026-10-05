using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Persistence.InMemory;

namespace Noxtend.Tests.Application;

/// <summary>
/// 3D 공급자와 그 모델 목록.
///
/// Design Ref: §10.1 · Plan FR-15
///
/// **목록 조회가 credential 을 확인해야 한다.** 모델이 고정 목록이라 그냥 돌려줄 수도
/// 있지만, 그러면 키가 틀린 것을 접수 후 첫 유료 호출에서야 알게 된다. 잔액 조회는
/// credit 을 쓰지 않으면서 키가 살아 있는지 확인하는 유일한 경로다.
/// </summary>
public sealed class MeshModelCatalogTests
{
    [Fact]
    public void TripoDoesMeshGeneration_AndNothingElse()
    {
        // 텍스트나 이미지 목록에 섞이면 사용자가 그 단계에 3D 공급자를 고를 수 있다
        Assert.Equal(
            [ProviderCapability.MeshGeneration],
            ProviderCapabilities.For(ProviderKind.Tripo));
    }

    [Fact]
    public void MeshyDoesMeshGeneration_AndNothingElse()
    {
        Assert.Equal(
            [ProviderCapability.MeshGeneration],
            ProviderCapabilities.For(ProviderKind.Meshy));
    }

    /// <summary>
    /// 3D 를 하는 공급자는 정확히 둘이다.
    ///
    /// **텍스트·이미지 공급자가 여기 섞이면** 사용자가 그 단계에 고를 수 있고, 그 오류는
    /// 접수가 아니라 실행 시점에야 드러난다.
    /// </summary>
    [Fact]
    public void OnlyTripoAndMeshyClaimMeshGeneration()
    {
        var kinds = Enum.GetValues<ProviderKind>()
            .Where(kind => ProviderCapabilities.Supports(kind, ProviderCapability.MeshGeneration))
            .Order()
            .ToArray();

        Assert.Equal([ProviderKind.Tripo, ProviderKind.Meshy], kinds);
    }

    /// <summary>
    /// Meshy 모델도 고정 스냅숏이다 (Plan 1.3).
    ///
    /// <c>latest</c> 를 쓰면 공급자가 뒤에서 바꿔도 우리는 모른다.
    /// </summary>
    [Fact]
    public async Task MeshyModels_ReturnThePinnedSnapshot()
    {
        var handler = new BalanceHandler(HttpStatusCode.OK, """{"balance":100}""");
        var (catalog, providerId) = await CatalogAsync(handler, ProviderKind.Meshy, "test-meshy-key");

        var models = await catalog.ListMeshModelsAsync(providerId, CancellationToken.None);

        Assert.Equal("meshy-7", Assert.Single(models).Id);

        // 생성 endpoint 를 부르면 credit 이 나간다 — 잔액 조회여야 한다
        Assert.Equal("https://api.meshy.ai/openapi/v1/balance", handler.RequestUri);
        Assert.Equal("Bearer test-meshy-key", handler.Authorization);
    }

    [Fact]
    public async Task MeshyModels_RejectAnInvalidKey()
    {
        var handler = new BalanceHandler(HttpStatusCode.Unauthorized, "{}");
        var (catalog, providerId) = await CatalogAsync(handler, ProviderKind.Meshy, "wrong");

        await Assert.ThrowsAsync<ProviderCallFailedException>(
            () => catalog.ListMeshModelsAsync(providerId, CancellationToken.None));
    }

    [Fact]
    public async Task MeshModels_RejectAnInvalidKey()
    {
        var handler = new BalanceHandler(HttpStatusCode.Unauthorized, "{}");
        var (catalog, providerId) = await CatalogAsync(handler);

        await Assert.ThrowsAsync<ProviderCallFailedException>(
            () => catalog.ListMeshModelsAsync(providerId, CancellationToken.None));
    }

    [Fact]
    public async Task MeshModels_CheckTheBalanceEndpointWithABearerToken()
    {
        var handler = new BalanceHandler(HttpStatusCode.OK, """{"code":0,"data":{"balance":100}}""");
        var (catalog, providerId) = await CatalogAsync(handler);

        await catalog.ListMeshModelsAsync(providerId, CancellationToken.None);

        // 생성 endpoint 를 부르면 credit 이 나간다 — 잔액 조회여야 한다
        Assert.Equal("https://openapi.tripo3d.ai/v3/account/balance", handler.RequestUri);
        Assert.Equal("Bearer test-tripo-key", handler.Authorization);
    }

    [Fact]
    public async Task MeshModels_ReturnThePinnedSnapshot()
    {
        var handler = new BalanceHandler(HttpStatusCode.OK, """{"code":0,"data":{"balance":100}}""");
        var (catalog, providerId) = await CatalogAsync(handler);

        var models = await catalog.ListMeshModelsAsync(providerId, CancellationToken.None);

        // 모델 스냅숏을 고정하는 것이 품질 drift 를 막는 유일한 장치다 (Plan D-04)
        Assert.Equal("P1-20260311", Assert.Single(models).Id);
    }

    [Fact]
    public async Task NonMeshProvider_HasNoMeshModels()
    {
        var handler = new BalanceHandler(HttpStatusCode.OK, "{}");
        var (catalog, providerId) = await CatalogAsync(handler, ProviderKind.Anthropic, "test-key");

        // 예외가 아니라 빈 목록이다 — "키가 틀렸다" 가 아니라 "이 공급자로는 못 만든다" 다
        Assert.Empty(await catalog.ListMeshModelsAsync(providerId, CancellationToken.None));
    }

    // ─── 설정 ───

    private static async Task<(ModelCatalog Catalog, Guid ProviderId)> CatalogAsync(
        BalanceHandler handler,
        ProviderKind kind = ProviderKind.Tripo,
        string apiKey = "test-tripo-key")
    {
        var configs = new InMemoryProviderConfigRepository();
        var config = ProviderConfig.Create("3D 공급자", kind, apiKey, "-key", DateTimeOffset.UtcNow);
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

    private sealed class BalanceHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
