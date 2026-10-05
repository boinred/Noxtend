using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// OpenAI 모델 목록 — <c>GET /v1/models</c>.
///
/// Design Ref: §3.2
///
/// **OpenAI 는 capability 를 노출하지 않는다.** 응답은 <c>{id, object, created, owned_by}</c>
/// 뿐이라 Anthropic 처럼 "이미지를 읽는가" 를 물어볼 수 없다. 그래서 계열 접두사
/// 허용목록으로 거른다 — 이 목록은 **손으로 관리되며 새 모델 출시보다 늦다.**
///
/// 허용목록(부정 목록이 아니라)을 쓰는 이유: 모르는 모델이 목록에 올라 실행 후 실패하는 것보다
/// 목록에서 빠지는 쪽이 낫다. 새 계열이 나오면 여기 한 줄을 추가한다.
/// </summary>
internal static class OpenAiModels
{
    private const string Endpoint = "https://api.openai.com/v1/models";

    /// <summary>
    /// 이미지 입력과 <c>json_schema</c> strict 를 모두 지원하는 계열.
    /// 추출은 둘 다 요구한다 (<see cref="OpenAiProvider"/> 가 strict schema 를 쓴다).
    /// </summary>
    private static readonly string[] AllowedFamilies =
    [
        "gpt-5",
        "gpt-4.1",
        "gpt-4o",
        "o3",
        "o4-",
    ];

    /// <summary>
    /// 허용 계열 안에서도 빠지는 변종들. 같은 <c>gpt-4o</c> 접두사를 쓰지만
    /// 음성·검색 전용이라 이미지나 구조화 출력이 없다.
    /// </summary>
    private static readonly string[] ExcludedMarkers =
    [
        "audio",
        "realtime",
        "transcribe",
        "tts",
        "search-preview",
        "moderation",
    ];

    public static async Task<IReadOnlyList<ProviderModel>> ListAsync(
        HttpClient http,
        string apiKey,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        try
        {
            using var response = await http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                // 본문을 읽지 않는다 — 오류 본문에 키가 반사될 수 있다 (§4.2 #13)
                throw new ProviderCallFailedException(
                    $"모델 목록을 가져올 수 없습니다: HTTP {(int)response.StatusCode}");
            }

            var payload = await response.Content.ReadFromJsonAsync<ModelListPayload>(ct);

            return (payload?.Data ?? [])
                .Where(m => SupportsExtraction(m.Id))
                // 최신순. 첫 항목이 기본 선택이 되므로 순서가 곧 기본값이다
                .OrderByDescending(m => m.Created)
                .Select(m => new ProviderModel(m.Id, m.Id))
                .ToList();
        }
        catch (ProviderCallFailedException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ProviderCallFailedException(
                $"모델 목록을 가져올 수 없습니다: {ex.GetType().Name}", ex);
        }
    }

    private static bool SupportsExtraction(string id)
        => AllowedFamilies.Any(f => id.StartsWith(f, StringComparison.OrdinalIgnoreCase))
           && !ExcludedMarkers.Any(m => id.Contains(m, StringComparison.OrdinalIgnoreCase));

    private sealed record ModelListPayload(
        [property: JsonPropertyName("data")] List<ModelEntry>? Data);

    private sealed record ModelEntry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("created")] long Created);
}
