using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;

namespace Noxtend.Infrastructure.Image;

/// <summary>
/// 이미지 생성 모델 목록.
///
/// Design Ref: §4.2 #7 · Plan D-4
///
/// **공급자 API 를 부르지 않고 손으로 관리한다.** 텍스트 모델은 `GET /v1/models` 로
/// 셀 수 있지만 이미지 생성 모델은 그 목록 안에서 구분되지 않고 — OpenAI 는 capability 를
/// 노출하지 않으며 Google 은 생성 모델과 이해 모델이 같은 계열 이름을 쓴다 — 접두사로
/// 거르면 텍스트 모델이 섞여 들어와 실행 후에야 실패한다.
///
/// **허용목록이라 새 모델 출시보다 늦다.** 모르는 모델이 목록에 올라 사용자가 고르고
/// 비싼 실패를 겪는 것보다, 목록에서 빠져 한 줄 추가를 기다리는 쪽이 낫다.
/// 같은 판단을 <see cref="Noxtend.Infrastructure.Llm.OpenAiModels"/> 도 한다.
/// </summary>
internal static class ImageModels
{
    private const string OpenAiModelsEndpoint = "https://api.openai.com/v1/models";
    private const string GoogleModelsEndpoint =
        "https://generativelanguage.googleapis.com/v1beta/models";

    private static readonly IReadOnlyList<ProviderModel> OpenAi =
    [
        new("gpt-image-2", "GPT Image 2"),
    ];

    private static readonly IReadOnlyList<ProviderModel> Google =
    [
        new("gemini-3.1-flash-image", "Gemini 3.1 Flash Image"),
        new("gemini-3.1-flash-lite-image", "Gemini 3.1 Flash Lite Image"),
        new("gemini-3-pro-image", "Gemini 3 Pro Image"),
        new("gemini-2.5-flash-image", "Gemini 2.5 Flash Image"),
    ];

    /// <summary>
    /// <c>Llm:UseFake</c> 일 때의 목록.
    ///
    /// **비어 있으면 안 된다.** 스튜디오가 이미지 모델을 못 고르면 접수가 막히므로
    /// (§4.2 #1) Fake 모드의 E2E 가 시작 화면에서 멈춘다.
    /// </summary>
    private static readonly IReadOnlyList<ProviderModel> Fake =
    [
        new("fake-image-a", "Fake Image A"),
        new("fake-image-b", "Fake Image B"),
    ];

    public static IReadOnlyList<ProviderModel> FakeModels => Fake;

    public static IReadOnlyList<ProviderModel> For(ProviderKind kind) => kind switch
    {
        ProviderKind.OpenAI => OpenAi,
        ProviderKind.Google => Google,

        // Anthropic 은 이미지 생성 모델이 없다. 예외가 아니라 빈 목록인 이유는
        // "키가 틀렸다" 가 아니라 "이 공급자로는 그릴 수 없다" 이기 때문이다
        _ => [],
    };

    public static async Task<IReadOnlyList<ProviderModel>> ListOpenAiAsync(
        HttpClient http,
        string apiKey,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, OpenAiModelsEndpoint);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Secret-safe provider error
                throw new ProviderCallFailedException(
                    $"모델 목록을 가져올 수 없습니다: HTTP {(int)response.StatusCode}");
            }

            var payload = await response.Content.ReadFromJsonAsync<OpenAiModelListPayload>(ct);
            var available = (payload?.Data ?? [])
                .Select(model => model.Id)
                .ToHashSet(StringComparer.Ordinal);

            // Reviewed catalog intersected with models available to this credential
            return OpenAi.Where(model => available.Contains(model.Id)).ToList();
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

    public static async Task<IReadOnlyList<ProviderModel>> ListGoogleAsync(
        HttpClient http,
        string apiKey,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GoogleModelsEndpoint);
        request.Headers.Add("x-goog-api-key", apiKey);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Secret-safe provider error
                throw new ProviderCallFailedException(
                    $"모델 목록을 가져올 수 없습니다: HTTP {(int)response.StatusCode}");
            }

            var payload = await response.Content.ReadFromJsonAsync<GoogleModelListPayload>(ct);
            var available = (payload?.Models ?? [])
                .Select(model => model.Name.StartsWith("models/", StringComparison.Ordinal)
                    ? model.Name["models/".Length..]
                    : model.Name)
                .ToHashSet(StringComparer.Ordinal);

            // Reviewed catalog intersected with models available to this credential
            return Google.Where(model => available.Contains(model.Id)).ToList();
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

    private sealed record GoogleModelListPayload(
        [property: JsonPropertyName("models")] List<GoogleModelEntry>? Models);

    private sealed record GoogleModelEntry(
        [property: JsonPropertyName("name")] string Name);

    private sealed record OpenAiModelListPayload(
        [property: JsonPropertyName("data")] List<OpenAiModelEntry>? Data);

    private sealed record OpenAiModelEntry(
        [property: JsonPropertyName("id")] string Id);
}
