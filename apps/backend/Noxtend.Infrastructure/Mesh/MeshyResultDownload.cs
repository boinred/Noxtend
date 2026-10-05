using System.Net.Http.Headers;
using System.Text.Json;
using Noxtend.Application.Common;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Mesh;

/// <summary>
/// Meshy 결과 스트림을 연다.
///
/// Design Ref: §4.2 · §5.6 · Plan D-07
///
/// **URL 을 저장하지 않는다.** Meshy 의 서명 URL 만료 시간은 문서에 없다 — 모른다는 것이
/// 저장하지 않을 이유로 충분하다. 이 호출 안에서 작업을 다시 조회해 갓 나온 URL 로
/// 스트림을 열고, 그 수명만 호출자에게 넘긴다.
///
/// **GLB 와 FBX 를 함께 받는다** (Plan D-05). 크레딧은 생성 단위라 형식을 더 받아도
/// 추가 비용이 없다.
/// </summary>
internal static class MeshyResultDownload
{
    public static async Task<IMeshResultDownload> OpenAsync(
        HttpClient http,
        string apiKey,
        string providerTaskId,
        MeshGenerationOptions options,
        CancellationToken ct)
    {
        var urls = await ReadResultUrlsAsync(http, apiKey, providerTaskId, ct);

        var glbResponse = await MeshResultHttp.OpenAsync(http, urls.Glb, options, ct);

        var parts = new List<MeshResultPart>
        {
            new(MeshArtifactKind.Glb, await glbResponse.Content.ReadAsStreamAsync(ct),
                glbResponse.Content.Headers.ContentType?.MediaType),
        };

        var responses = new List<HttpResponseMessage> { glbResponse };

        // **GLB 를 지킨다.** 나머지 둘은 없어도 되고, 하나 때문에 가장 비싼 결과를
        // 버리지 않는다 (§11.3 O-03·O-04)
        await AddOptionalAsync(MeshArtifactKind.Fbx, urls.Fbx);
        await AddOptionalAsync(MeshArtifactKind.Preview, urls.Thumbnail);

        return new MeshResultDownload(parts, responses);

        async Task AddOptionalAsync(MeshArtifactKind kind, string? url)
        {
            if (url is null)
            {
                return;
            }

            try
            {
                var response = await MeshResultHttp.OpenAsync(http, url, options, ct);

                parts.Add(new MeshResultPart(
                    kind,
                    await response.Content.ReadAsStreamAsync(ct),
                    response.Content.Headers.ContentType?.MediaType));

                responses.Add(response);
            }
            catch (MeshProviderException)
            {
                // 없는 것으로 둔다 — 화면이 그 형식의 버튼을 감춘다
            }
        }
    }

    private static async Task<(string Glb, string? Fbx, string? Thumbnail)> ReadResultUrlsAsync(
        HttpClient http, string apiKey, string providerTaskId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"multi-image-to-3d/{providerTaskId}");

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
        var root = body.RootElement;

        var models = root.ValueKind == JsonValueKind.Object
                     && root.TryGetProperty("model_urls", out var urls)
                     && urls.ValueKind == JsonValueKind.Object
            ? urls
            : throw new MeshProviderException(
                "MESH_RESULT_INVALID", "결과에 모델 링크가 없습니다", canRetry: false);

        var glb = Text(models, "glb")
                  ?? throw new MeshProviderException(
                      "MESH_RESULT_INVALID", "결과에 GLB 링크가 없습니다", canRetry: false);

        return (glb, Text(models, "fbx"), Text(root, "thumbnail_url"));
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
           && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
