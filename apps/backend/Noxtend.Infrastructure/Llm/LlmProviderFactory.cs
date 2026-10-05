using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// 설정 id + 모델 → 공급자 어댑터.
///
/// Design Ref: §2.2 키의 수명 · §3.2
///
/// **이 클래스가 보안 경계다.** 복호화·어댑터 생성이 전부 여기서 끝나므로
/// Application 은 id 만 넘기고 평문 키를 본 적이 없다. 인증이 없는 사이클이라
/// 노출 경로를 규율이 아니라 구조로 막는다 (§1.1).
///
/// 모델은 설정에서 읽지 않고 **인자로 받는다** — 공정이 모델을 소유하므로
/// 같은 키로 여러 모델을 부를 수 있다. 모델 id 는 비밀이 아니라 계층을 건너도 된다.
///
/// <c>Llm:UseFake</c> 가 참이면 Fake 를 돌려준다 — E2E 가 비용·지연·비결정성을
/// 동시에 갖지 않게 하는 스위치다 (§10.3). 배포에서는 항상 거짓이다.
/// </summary>
internal sealed class LlmProviderFactory(
    ProviderCredentialResolver credentials,
    IHttpClientFactory httpClients,
    bool useFake,
    Func<ILlmProvider, ILlmProvider> decorate) : ILlmProviderFactory
{
    /// <summary>공급자 호출은 10분 이상 걸릴 수 있다 (§2.2). 기본 100초로는 정상 호출이 끊긴다.</summary>
    /// <summary>
    /// 텍스트 호출용. 이미지와 나눠 두는 이유는 **기다릴 만한 시간이 다르기 때문**이다
    /// (queue-reclaim §관련 결함 B).
    ///
    /// 하나로 쓰던 때는 이미지 기준 20분이 텍스트에도 걸렸다. 텍스트 실측은 9~51초인데,
    /// 공급자가 늦게 답하면 워커 한 대가 최대 20분 묶인다 — 텍스트 단계는 워커가 하나라
    /// (<c>TaskWorkerRegistration</c>) 그동안 그 단계 전체가 멈춘다. 실제로 겪었다.
    /// </summary>
    public const string HttpClientName = "llm-text";

    /// <summary>이미지 생성용. 한 장에 수십 초가 정상이라 넉넉히 잡는다.</summary>
    public const string ImageHttpClientName = "llm-image";

    public async Task<ILlmProvider> CreateAsync(
        Guid providerConfigId,
        string model,
        CancellationToken ct)
    {
        // Fake 도 감싼다 — 내역 기록은 Fake 모드에서도 검증 대상이다
        if (useFake)
        {
            return decorate(FakeLlmProvider.Succeeding());
        }

        var credential = await credentials.ResolveAsync(providerConfigId, ct);
        var http = httpClients.CreateClient(HttpClientName);

        ILlmProvider adapter = credential.Kind switch
        {
            ProviderKind.OpenAI => new OpenAiProvider(http, credential.ApiKey, model),
            ProviderKind.Anthropic => new AnthropicProvider(credential.ApiKey, model),
            ProviderKind.Google => new GoogleProvider(http, credential.ApiKey, model),
            _ => throw new ProviderCallFailedException(
                $"지원하지 않는 공급자입니다: {credential.Kind}"),
        };

        return decorate(adapter);
    }
}
