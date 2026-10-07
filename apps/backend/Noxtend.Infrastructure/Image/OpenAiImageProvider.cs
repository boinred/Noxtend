using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;

namespace Noxtend.Infrastructure.Image;

/// <summary>
/// OpenAI 이미지 생성 어댑터.
///
/// Design Ref: §3.2 · C-4
///
/// **평문 키는 이 객체 안에서만 산다.** Factory 가 복호화해 넘기고, 헤더에 실린 뒤
/// 어디에도 기록되지 않는다. 예외 메시지에도 넣지 않는다.
///
/// **단계를 모르고 저장소도 모른다** (G-1 · G-2). 바이트를 돌려줄 뿐이고 Blob 에 넣는
/// 것은 Application 이 한다.
///
/// 참조 원본이 있으면 편집 엔드포인트를, 없으면 생성 엔드포인트를 쓴다 — 같은 모델이라도
/// 경로가 다르다.
/// </summary>
public sealed partial class OpenAiImageProvider(HttpClient http, string apiKey, string model) : IImageProvider
{
    /// <summary>
    /// 서버 고정 품질 (§2.3 A-8 · 사이클 #9).
    ///
    /// **안 보내면 공급자 기본값에 맡겨진다.** 1024x1024 장당 단가가 low $0.006 ·
    /// medium $0.053 · high $0.211 로 35배까지 달라지는데 단가표에는 한 값만 심으므로,
    /// 요청이 품질을 말하지 않으면 심어 둔 값과 실제 청구가 조용히 어긋난다.
    ///
    /// 여기를 바꾸면 `SeedModelPrices.MissingImagePrices` 의 `gpt-image-2` 값도 같이
    /// 바뀌어야 한다.
    /// </summary>
    private const string Quality = "medium";

    public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct)
    {
        ImageModels.ValidateSpriteRequest(ProviderKind.OpenAI, model, request);

        using var message = request.Reference.Count > 0
            ? BuildEditRequest(request)
            : BuildGenerateRequest(request);

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await ProviderHttp.SendAsync(http, message, ct);

        if (!response.IsSuccessStatusCode)
        {
            // 상태 코드만 흘린다. 본문에는 키 조각이나 조직 식별자가 섞일 수 있다
            throw new ProviderCallFailedException(
                $"{(int)response.StatusCode}",
                isTransient: await ProviderHttp.IsOpenAiTransientAsync(response, ct));
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;
        var data = root.GetProperty("data");

        // 실제 청구 근거. 편집 경로(참조 동반)는 입력 이미지 토큰이 여기 잡힌다 —
        // 고정밀 입력 토큰율이 적용되므로 장당 정액으로는 하한만 나온다 (D-5)
        int? Tokens(string name) =>
            root.TryGetProperty("usage", out var usage)
            && usage.TryGetProperty(name, out var value)
            && value.TryGetInt32(out var n)
                ? n
                : null;

        if (data.GetArrayLength() == 0)
        {
            throw new ProviderBadResponseException("응답에 이미지가 없습니다");
        }

        var base64 = data[0].TryGetProperty("b64_json", out var encoded)
            ? encoded.GetString()
            : null;

        if (string.IsNullOrEmpty(base64))
        {
            throw new ProviderBadResponseException("응답에 이미지 바이트가 없습니다");
        }

        // 장 수는 응답이 말한다 — 과금 단위이므로 1 이라고 가정하지 않는다 (§3.2)
        return new ImageResult(
            Convert.FromBase64String(base64),
            "image/png",
            data.GetArrayLength(),
            Tokens("input_tokens"),
            Tokens("output_tokens"),
            RateLimitRemaining(response.Headers, "x-ratelimit-remaining-requests"),
            RateLimitResetAfter(response.Headers, "x-ratelimit-reset-requests"),
            RateLimitRemaining(response.Headers, "x-ratelimit-remaining-tokens"),
            RateLimitResetAfter(response.Headers, "x-ratelimit-reset-tokens"));
    }

    // generation-rate-limiting §3① / 후속(TPM) — 값을 저장·판단하지 않고 그대로 실어
    // 보내기만 한다 (G-2). RPM(requests)·TPM(tokens) 헤더는 이름만 다르고 모양이 같아
    // 헤더 이름만 바꿔 같은 파서를 재사용한다
    private static int? RateLimitRemaining(HttpResponseHeaders headers, string headerName)
        => headers.TryGetValues(headerName, out var values)
            && int.TryParse(values.FirstOrDefault(), out var n)
                ? n
                : null;

    /// <summary>
    /// "6ms"·"6s"·"1m30s"·"1h0m0s" 형식의 OpenAI 초기화 헤더를 상대 시간으로 바꾼다.
    /// 형식이 예상과 다르면 null — 대기 여부는 이 값을 쓰는 쪽이 "정보 없음"으로 판단한다.
    ///
    /// 독립 리뷰 지적 두 건 — ① `double.Parse`에 컬처를 안 주면 소수점 콤마
    /// 로케일에서 "1.44s"가 144초로 읽힌다. ② TPM 헤더는 ms 단위로, 일부 한도는
    /// h 단위로도 온다.
    /// </summary>
    private static TimeSpan? RateLimitResetAfter(HttpResponseHeaders headers, string headerName)
    {
        if (!headers.TryGetValues(headerName, out var values))
        {
            return null;
        }

        var raw = values.FirstOrDefault();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        var match = ResetDurationPattern().Match(raw);
        if (!match.Success)
        {
            return null;
        }

        double Group(string name) => match.Groups[name].Success
            ? double.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture)
            : 0;

        return TimeSpan.FromHours(Group("h"))
            + TimeSpan.FromMinutes(Group("m"))
            + TimeSpan.FromSeconds(Group("s"))
            + TimeSpan.FromMilliseconds(Group("ms"));
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^(?:(?<h>\d+)h)?(?:(?<m>\d+)m)?(?:(?<s>\d+(?:\.\d+)?)s)?(?:(?<ms>\d+)ms)?$")]
    private static partial System.Text.RegularExpressions.Regex ResetDurationPattern();

    private HttpRequestMessage BuildGenerateRequest(ImageRequest request)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["prompt"] = request.Prompt,
            ["size"] = request.Size,
            ["quality"] = Quality,
            ["n"] = 1,
            ["output_format"] = "png",
        };
        if (request.Background is { } background)
        {
            payload["background"] = background == ImageBackground.Transparent ? "transparent" : "opaque";
        }

        return new(HttpMethod.Post, "https://api.openai.com/v1/images/generations")
        {
            Content = JsonContent.Create(payload),
        };
    }

    /// <summary>
    /// 참조 목록을 동반한 편집 요청 (Plan D-5 · workstream B §5.2).
    ///
    /// 편집 엔드포인트는 JSON 이 아니라 multipart 다 — 이미지가 파일 파트로 간다.
    /// gpt-image 계열은 <c>image[]</c> 필드를 반복해 최대 16장까지 배열로 받는다
    /// (workstream B §0-2, 공식 문서 확인). 순서가 프롬프트의 참조 번호와 맞아야 한다.
    /// </summary>
    private HttpRequestMessage BuildEditRequest(ImageRequest request)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(model), "model" },
            { new StringContent(request.Prompt), "prompt" },
            { new StringContent(request.Size), "size" },
            { new StringContent(Quality), "quality" },
            { new StringContent("1"), "n" },
            { new StringContent("png"), "output_format" },
        };

        if (request.Background is { } background)
        {
            content.Add(new StringContent(background == ImageBackground.Transparent ? "transparent" : "opaque"), "background");
        }

        for (var index = 0; index < request.Reference.Count; index++)
        {
            var reference = request.Reference[index].Content;
            var image = new ByteArrayContent(reference.Bytes);
            image.Headers.ContentType = new MediaTypeHeaderValue(reference.ContentType);
            content.Add(image, "image[]", $"reference-{index}");
        }

        return new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/edits")
        {
            Content = content,
        };
    }

}
