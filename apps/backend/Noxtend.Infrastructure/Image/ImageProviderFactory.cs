using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Infrastructure.Image;

/// <summary>
/// 설정 id + 모델 → 이미지 어댑터.
///
/// Design Ref: §3.2 · NFR-05
///
/// **<see cref="LlmProviderFactory"/> 와 같은 보안 경계다.** 복호화·어댑터 생성이 전부
/// 여기서 끝나므로 Application 은 id 만 넘기고 평문 키를 본 적이 없다. 자격증명 해석은
/// <see cref="ProviderCredentialResolver"/> 를 공유한다 — 사용 중지 확인이나 복호화 실패
/// 메시지 같은 규칙이 두 갈래로 어긋나지 않는다.
///
/// <c>Llm:UseFake</c> 가 참이면 Fake 를 돌려준다 — 텍스트와 같은 스위치를 쓴다.
/// 스위치를 나누면 "텍스트는 가짜인데 이미지는 진짜" 라는 조합이 생기고, 그것은
/// E2E 에서 아무도 의도하지 않은 비용이 나가는 조합이다.
/// </summary>
internal sealed class ImageProviderFactory(
    ProviderCredentialResolver credentials,
    IHttpClientFactory httpClients,
    bool useFake,
    Func<IImageProvider, IImageProvider> decorate) : IImageProviderFactory
{
    public async Task<IImageProvider> CreateAsync(
        Guid providerConfigId,
        string model,
        CancellationToken ct)
    {
        // Fake 도 감싼다 — 내역 기록은 Fake 모드에서도 검증 대상이다
        if (useFake)
        {
            return decorate(FakeImageProvider.Succeeding());
        }

        var credential = await credentials.ResolveAsync(providerConfigId, ct);
        var http = httpClients.CreateClient(LlmProviderFactory.ImageHttpClientName);

        IImageProvider adapter = credential.Kind switch
        {
            ProviderKind.OpenAI => new OpenAiImageProvider(http, credential.ApiKey, model),
            ProviderKind.Google => new GoogleImageProvider(http, credential.ApiKey, model),

            // Anthropic 은 이미지를 그리지 않는다. 접수 시점에 막히지만(§4.2 #1)
            // 설정이 나중에 바뀔 수 있으므로 여기서도 확정 실패다
            _ => throw new ProviderCallFailedException(
                $"이미지 생성을 지원하지 않는 공급자입니다: {credential.Kind}"),
        };

        return decorate(adapter);
    }
}
