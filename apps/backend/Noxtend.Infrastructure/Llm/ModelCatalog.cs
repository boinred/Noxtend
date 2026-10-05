using Microsoft.Extensions.Caching.Memory;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// 공급자별 모델 목록 — 캐시 + 공급자 분기.
///
/// Design Ref: §3.2 · §4.2 #14 (신설)
///
/// **캐시가 여기 있는 이유.** 스튜디오는 진입할 때마다 목록을 읽는다. 모델 목록은
/// 몇 주 단위로 바뀌므로 매번 공급자 API 를 부르면 지연만 늘어난다. 어댑터 안이 아니라
/// 이 층에 두면 두 공급자가 같은 정책을 공유한다.
///
/// 캐시 키에 설정 id 를 쓴다 — 키를 바꾸면 같은 id 라도 목록이 달라질 수 있지만,
/// <see cref="CacheDuration"/> 안의 오차는 "연결 확인" 이 다시 채우므로 문제되지 않는다.
/// </summary>
internal sealed class ModelCatalog(
    ProviderCredentialResolver credentials,
    IHttpClientFactory httpClients,
    IMemoryCache cache,
    bool useFake) : IModelCatalog
{
    /// <summary>모델 목록은 자주 바뀌지 않는다. 짧게 잡아 봐야 얻는 것이 없다.</summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<ProviderModel>> ListAsync(
        Guid providerConfigId,
        CancellationToken ct)
    {
        if (useFake)
        {
            return FakeModels;
        }

        var key = $"models:{providerConfigId}";
        if (cache.TryGetValue(key, out IReadOnlyList<ProviderModel>? cached) && cached is not null)
        {
            return cached;
        }

        var credential = await credentials.ResolveAsync(providerConfigId, ct);

        var models = credential.Kind switch
        {
            ProviderKind.Anthropic => await AnthropicModels.ListAsync(credential.ApiKey, ct),
            ProviderKind.OpenAI => await OpenAiModels.ListAsync(
                httpClients.CreateClient(LlmProviderFactory.HttpClientName), credential.ApiKey, ct),
            ProviderKind.Google => await GoogleModels.ListAsync(
                httpClients.CreateClient(LlmProviderFactory.HttpClientName), credential.ApiKey, ct),
            _ => throw new ProviderCallFailedException(
                $"지원하지 않는 공급자입니다: {credential.Kind}"),
        };

        // 빈 목록도 캐시한다. 비어 있는 이유(권한 등)는 10분 안에 바뀌지 않고,
        // 캐시하지 않으면 막힌 화면이 새로 고칠 때마다 공급자를 두드린다
        cache.Set(key, models, CacheDuration);
        return models;
    }

    /// <summary>
    /// 이미지 생성 모델 목록 (사이클 #7).
    /// Google은 실제 키로 원격 목록을 조회하고, 검토된 허용목록과 교차한다.
    /// </summary>
    public async Task<IReadOnlyList<ProviderModel>> ListImageModelsAsync(
        Guid providerConfigId,
        CancellationToken ct)
    {
        if (useFake)
        {
            return Image.ImageModels.FakeModels;
        }

        var key = $"image-models:{providerConfigId}";
        if (cache.TryGetValue(key, out IReadOnlyList<ProviderModel>? cached) && cached is not null)
        {
            return cached;
        }

        // Credential resolution and provider-specific validation
        var credential = await credentials.ResolveAsync(providerConfigId, ct);
        var models = credential.Kind switch
        {
            ProviderKind.OpenAI => await Image.ImageModels.ListOpenAiAsync(
                httpClients.CreateClient(LlmProviderFactory.HttpClientName), credential.ApiKey, ct),
            ProviderKind.Google => await Image.ImageModels.ListGoogleAsync(
                httpClients.CreateClient(LlmProviderFactory.HttpClientName), credential.ApiKey, ct),
            _ => Image.ImageModels.For(credential.Kind),
        };

        cache.Set(key, models, CacheDuration);
        return models;
    }

    /// <summary>
    /// 3D 생성 모델 목록 (사이클 #10 §10.1).
    ///
    /// 목록 자체는 검증된 스냅숏 고정이고, 원격 호출은 **키가 살아 있는지 확인하는 용도**다.
    /// 잔액 조회라 credit 을 쓰지 않는다 (NFR-08).
    /// </summary>
    public async Task<IReadOnlyList<ProviderModel>> ListMeshModelsAsync(
        Guid providerConfigId,
        CancellationToken ct)
    {
        if (useFake)
        {
            return Mesh.MeshModels.FakeModels;
        }

        var key = $"mesh-models:{providerConfigId}";
        if (cache.TryGetValue(key, out IReadOnlyList<ProviderModel>? cached) && cached is not null)
        {
            return cached;
        }

        var credential = await credentials.ResolveAsync(providerConfigId, ct);
        var models = credential.Kind switch
        {
            ProviderKind.Tripo => await Mesh.MeshModels.ListTripoAsync(
                httpClients.CreateClient(LlmProviderFactory.HttpClientName), credential.ApiKey, ct),
            ProviderKind.Meshy => await Mesh.MeshModels.ListMeshyAsync(
                httpClients.CreateClient(LlmProviderFactory.HttpClientName), credential.ApiKey, ct),
            _ => Mesh.MeshModels.For(credential.Kind),
        };

        cache.Set(key, models, CacheDuration);
        return models;
    }

    /// <summary>
    /// 3D 공급자의 남은 크레딧.
    ///
    /// **캐시하지 않는다.** 모델 목록은 고정 스냅숏이라 캐시가 맞지만, 잔액은 쓸 때마다
    /// 줄어드는 값이다. 낡은 숫자를 보여 주면 "충분하다" 고 믿고 시작했다가 중간에 멈춘다.
    /// </summary>
    public async Task<int?> GetMeshCreditBalanceAsync(Guid providerConfigId, CancellationToken ct)
    {
        if (useFake)
        {
            return null;
        }

        var credential = await credentials.ResolveAsync(providerConfigId, ct);

        return await Mesh.MeshModels.ReadBalanceAsync(
            httpClients.CreateClient(LlmProviderFactory.HttpClientName),
            credential.ApiKey,
            credential.Kind,
            ct);
    }

    /// <summary>
    /// <c>Llm:UseFake</c> 일 때의 목록.
    ///
    /// **비어 있으면 안 된다.** 스튜디오가 목록이 비면 막히도록 만들었으므로, Fake 모드에서
    /// 빈 목록을 주면 E2E 전체가 시작 화면에서 멈춘다. Fake 어댑터가 어떤 모델 id 든
    /// 같은 결과를 내므로 값 자체는 임의여도 되지만 개수는 1 이상이어야 한다.
    /// </summary>
    private static readonly IReadOnlyList<ProviderModel> FakeModels =
    [
        new("fake-model-a", "Fake Model A"),
        new("fake-model-b", "Fake Model B"),
    ];
}
