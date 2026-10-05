using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Models;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// Anthropic 모델 목록 — 공식 SDK 의 <c>Models.List()</c>.
///
/// Design Ref: §3.2
///
/// **capability 로 걸러낸다.** 추출 공정은 이미지를 읽고 구조화 출력을 요구하므로
/// (<see cref="AnthropicProvider"/> 가 <c>OutputConfig</c> 를 쓴다) 둘 중 하나라도
/// 없는 모델은 고를 수 있어도 실행 후에야 실패한다 — 사용자는 이유를 알 방법이 없다.
/// Anthropic 이 <c>capabilities</c> 를 노출하므로 목록 단계에서 막을 수 있다.
/// </summary>
internal static class AnthropicModels
{
    /// <summary>목록 조회는 짧다. 추출과 달리 20분 타임아웃이 필요 없다.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>한 페이지에 다 담기게 크게 잡는다 — 모델 수는 수십 개 규모다.</summary>
    private const int PageLimit = 200;

    public static async Task<IReadOnlyList<ProviderModel>> ListAsync(
        string apiKey,
        CancellationToken ct)
    {
        var client = new AnthropicClient(new ClientOptions
        {
            ApiKey = apiKey,
            Timeout = Timeout,
        });

        try
        {
            var models = new List<ProviderModel>();
            var page = await client.Models.List(new ModelListParams { Limit = PageLimit }, ct);

            // 페이지를 끝까지 따라간다. 지금은 한 페이지로 끝나지만 모델이 늘면 조용히 잘린다
            while (true)
            {
                foreach (var model in page.Items)
                {
                    if (SupportsExtraction(model))
                    {
                        models.Add(new ProviderModel(model.ID, model.DisplayName));
                    }
                }

                if (!page.HasNext())
                {
                    break;
                }

                page = await page.Next(ct);
            }

            return models;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 원문 메시지를 그대로 올리지 않는다 — 키가 섞일 수 있다 (§4.2 #13)
            throw new ProviderCallFailedException(
                $"모델 목록을 가져올 수 없습니다: {ex.GetType().Name}", ex);
        }
    }

    /// <summary>
    /// 추출 공정이 요구하는 두 조건.
    ///
    /// capability 가 <c>null</c> 이면 "없음" 으로 본다. 새 모델이 이 필드를 채우지 않아
    /// 목록에서 빠지는 쪽이, 못 쓰는 모델이 목록에 올라 실행 후 실패하는 쪽보다 낫다.
    /// </summary>
    private static bool SupportsExtraction(ModelInfo model)
        => model.Capabilities?.ImageInput?.Supported == true
           && model.Capabilities?.StructuredOutputs?.Supported == true;
}
