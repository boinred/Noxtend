using System.Net.Http.Headers;
using System.Text.Json;
using Noxtend.Application.Common;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Mesh;

/// <summary>
/// Tripo 결과 스트림을 연다.
///
/// Design Ref: §4.2 · §7.5 · Plan D-08 · NFR-07
///
/// **URL 이 밖으로 나가지 않는다.** 공급자가 주는 model URL 은 성공 후 5분이면 만료된다.
/// 그것을 반환값이나 DB 에 두면 이미 죽은 값을 들고 있게 되므로, 이 호출 안에서 작업을
/// 다시 조회해 갓 나온 URL 로 스트림을 열고 그 수명만 호출자에게 넘긴다.
///
/// **Tripo 는 FBX 를 내지 않는다** (Plan D-05) — 여기서 나오는 것은 GLB 와 미리보기뿐이다.
/// </summary>
internal static class TripoResultDownload
{
    public static async Task<IMeshResultDownload> OpenAsync(
        HttpClient http,
        string apiKey,
        string providerTaskId,
        MeshGenerationOptions options,
        CancellationToken ct)
    {
        // 작업을 다시 조회한다 — 앞서 본 URL 은 이미 만료됐을 수 있다
        var (modelUrl, previewUrl) = await ReadResultUrlsAsync(http, apiKey, providerTaskId, ct);

        var modelResponse = await MeshResultHttp.OpenAsync(http, modelUrl, options, ct);

        var parts = new List<MeshResultPart>
        {
            new(MeshArtifactKind.Glb, await modelResponse.Content.ReadAsStreamAsync(ct),
                modelResponse.Content.Headers.ContentType?.MediaType),
        };

        var responses = new List<HttpResponseMessage> { modelResponse };

        if (previewUrl is not null)
        {
            try
            {
                var previewResponse = await MeshResultHttp.OpenAsync(http, previewUrl, options, ct);

                parts.Add(new MeshResultPart(
                    MeshArtifactKind.Preview,
                    await previewResponse.Content.ReadAsStreamAsync(ct),
                    previewResponse.Content.Headers.ContentType?.MediaType));

                responses.Add(previewResponse);
            }
            catch (MeshProviderException)
            {
                // **미리보기는 없어도 된다.** 이것 때문에 GLB 를 버리면 가장 비싼 결과가
                // 부수적인 이미지 하나로 날아간다
            }
        }

        return new MeshResultDownload(parts, responses);
    }

    private static async Task<(string Model, string? Preview)> ReadResultUrlsAsync(
        HttpClient http, string apiKey, string providerTaskId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"tasks/{providerTaskId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new MeshProviderException(
                "MESH_RESULT_EXPIRED",
                $"결과 조회에 실패했습니다. HTTP {(int)response.StatusCode}",
                canRetry: false);
        }

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var output = body.RootElement.GetProperty("data").GetProperty("output");

        var model = output.TryGetProperty("model_url", out var m) ? m.GetString() : null;

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new MeshProviderException(
                "MESH_RESULT_INVALID", "결과에 모델 링크가 없습니다", canRetry: false);
        }

        return (
            model,
            output.TryGetProperty("rendered_image_url", out var p) ? p.GetString() : null);
    }
}
