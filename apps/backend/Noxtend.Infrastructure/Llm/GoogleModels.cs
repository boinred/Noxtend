using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// Google Gemini 텍스트 모델 목록 — <c>GET /v1beta/models</c>.
///
/// Design Ref: gemini-text-provider (2026-08-28)
///
/// <see cref="OpenAiModels"/>와 같은 판단이다 — Gemini도 계열 이름만으로는 이미지
/// 전용·임베딩·TTS 변종이 섞여 있어, 텍스트+구조화 출력을 지원하는 계열을 허용목록으로
/// 손으로 관리한다. 이미지 생성 모델 목록(<see cref="Image.ImageModels"/>)과 겹치지
/// 않도록 <c>-image</c> 계열은 제외한다.
/// </summary>
internal static class GoogleModels
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models";

    private static readonly string[] AllowedFamilies =
    [
        "gemini-3",
        "gemini-2.5",
    ];

    /// <summary>이미지 생성·임베딩·TTS 전용 변종 — 텍스트+구조화 출력 대상이 아니다.</summary>
    private static readonly string[] ExcludedMarkers =
    [
        "image",
        "embedding",
        "tts",
        "live",
        "native-audio",
    ];

    public static async Task<IReadOnlyList<ProviderModel>> ListAsync(
        HttpClient http,
        string apiKey,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Add("x-goog-api-key", apiKey);

        try
        {
            using var response = await http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderCallFailedException(
                    $"모델 목록을 가져올 수 없습니다: HTTP {(int)response.StatusCode}");
            }

            var payload = await response.Content.ReadFromJsonAsync<ModelListPayload>(ct);

            return (payload?.Models ?? [])
                .Select(m => m.Name.StartsWith("models/", StringComparison.Ordinal)
                    ? m.Name["models/".Length..]
                    : m.Name)
                .Where(SupportsExtraction)
                .Select(id => new ProviderModel(id, id))
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
        [property: JsonPropertyName("models")] List<ModelEntry>? Models);

    private sealed record ModelEntry(
        [property: JsonPropertyName("name")] string Name);
}
