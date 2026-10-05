using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Image;

/// <summary>
/// Google Gemini 이미지 생성 어댑터.
///
/// Design Ref: §3.2 · Plan D-4 · R-7
///
/// **왜 둘째 공급자가 필요한가.** 이미지 생성은 공급자 간 화풍 차가 텍스트보다 훨씬 커서
/// 파츠들이 서로 다른 화풍으로 나오면 조립이 불가능하다 (R-1). 그 판정 기준이 아직
/// 사람 눈밖에 없으므로, 골든 세트로 나란히 놓고 고르려면 둘이 동시에 있어야 한다.
///
/// **R-7 이 여기서 확인된다** — 사이클 #4 설계는 "공급자 추가는 어댑터 하나가 늘 뿐"
/// 이라고 주장했다. 자격증명·모델 목록·단가가 함께 움직이는지가 이번에 드러난다.
///
/// 최신 Interactions API 응답의 이미지가 <c>steps[].content[]</c> 안에 텍스트 파트와
/// 섞여 오므로, 타입을 확인해 이미지 블록만 추출한다.
/// </summary>
public sealed class GoogleImageProvider(HttpClient http, string apiKey, string model) : IImageProvider
{
    public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct)
    {
        var input = new List<object> { new { type = "text", text = request.Prompt } };

        // 프롬프트 reference 번호와 동일한 참조 이미지 순서
        foreach (var reference in request.Reference)
        {
            input.Add(new
            {
                type = "image",
                mime_type = reference.Content.ContentType,
                data = Convert.ToBase64String(reference.Content.Bytes),
            });
        }

        // Interactions API 이미지 출력 계약과 무상태 실행 설정
        var payload = new
        {
            model,
            input = input.ToArray(),
            response_format = new
            {
                type = "image",
                aspect_ratio = "1:1",
                image_size = "1K",
            },
            store = false,
        };

        // 프록시·접근 로그의 API 키 노출 방지
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            "https://generativelanguage.googleapis.com/v1beta/interactions")
        {
            Content = JsonContent.Create(payload),
        };
        message.Headers.Add("x-goog-api-key", apiKey);

        using var response = await ProviderHttp.SendAsync(http, message, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderCallFailedException(
                $"{(int)response.StatusCode}",
                isTransient: ProviderHttp.IsTransient(response.StatusCode));
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        return ExtractImage(document.RootElement);
    }

    /// <summary>
    /// 응답에서 첫 이미지 파트를 꺼낸다.
    ///
    /// 모델이 이미지와 함께 설명 텍스트를 내므로 콘텐츠 블록을 훑어 <c>type=image</c>인
    /// 것을 찾아야 한다 — 첫 블록이 이미지라고 가정하면 텍스트가 앞에 오는 날 깨진다.
    /// </summary>
    private static ImageResult ExtractImage(JsonElement root)
    {
        // 참조 이미지 토큰을 포함한 Interactions API 청구 근거
        var usage = root.TryGetProperty("usage", out var meta) ? meta : (JsonElement?)null;
        int? Tokens(string name) =>
            usage is { } m && m.TryGetProperty(name, out var value) && value.TryGetInt32(out var n)
                ? n
                : null;

        if (!root.TryGetProperty("steps", out var steps) || steps.GetArrayLength() == 0)
        {
            throw new ProviderBadResponseException("응답에 실행 단계가 없습니다");
        }

        var images = 0;
        JsonElement? first = null;

        foreach (var step in steps.EnumerateArray())
        {
            if (!step.TryGetProperty("type", out var stepType)
                || stepType.GetString() != "model_output"
                || !step.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var block in content.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var blockType)
                    || blockType.GetString() != "image")
                {
                    continue;
                }

                images++;
                first ??= block;
            }
        }

        if (first is not { } data
            || !data.TryGetProperty("data", out var encoded)
            || encoded.GetString() is not { Length: > 0 } base64)
        {
            throw new ProviderBadResponseException("응답에 이미지 바이트가 없습니다");
        }

        var contentType = data.TryGetProperty("mime_type", out var mime)
            ? mime.GetString() ?? "image/png"
            : "image/png";

        return new ImageResult(
            Convert.FromBase64String(base64),
            contentType,
            images,
            Tokens("total_input_tokens"),
            Tokens("total_output_tokens"));
    }

}
