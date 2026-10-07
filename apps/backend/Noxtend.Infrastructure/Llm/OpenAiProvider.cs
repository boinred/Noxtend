using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// OpenAI Chat Completions 어댑터.
///
/// Design Ref: §3.2 · §4.2 #13
///
/// **평문 키는 이 객체 안에서만 산다.** Factory 가 복호화해 넘기고, 헤더에 실린 뒤
/// 어디에도 기록되지 않는다. 예외 메시지에도 넣지 않는다 (§2.2).
///
/// **단계를 모른다** (§3.3 G-1). Anthropic 어댑터와 같은 이유다.
/// </summary>
public sealed partial class OpenAiProvider(HttpClient http, string apiKey, string model) : ILlmProvider
{
    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = request.System },
                new { role = "user", content = BuildUserContent(request) },
            },
            // 구조화 출력. 스키마를 걸어야 파싱 실패가 예외가 아니라 예외적인 일이 된다
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    // 단계 이름을 실어 공급자 대시보드에서 구분되게 한다
                    name = request.Context.Kind.ToString().ToLowerInvariant(),
                    strict = true,
                    schema = JsonDocument.Parse(request.JsonSchema).RootElement,
                },
            },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = JsonContent.Create(payload),
        };
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

        var content = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (content is null)
        {
            throw new ProviderBadResponseException("응답에 텍스트가 없습니다");
        }

        // usage 는 없을 수 있다 — 스트리밍이나 일부 모델에서 생략된다
        int? Usage(string name) =>
            root.TryGetProperty("usage", out var usage) && usage.TryGetProperty(name, out var value)
                ? value.GetInt32()
                : null;

        return new LlmResult(
            content,
            Usage("prompt_tokens"),
            Usage("completion_tokens"),
            RateLimitRemaining(response.Headers, "x-ratelimit-remaining-requests"),
            RateLimitResetAfter(response.Headers, "x-ratelimit-reset-requests"),
            RateLimitRemaining(response.Headers, "x-ratelimit-remaining-tokens"),
            RateLimitResetAfter(response.Headers, "x-ratelimit-reset-tokens"));
    }

    // text-generation-rate-limiting §구현변경-3/후속(TPM) — OpenAiImageProvider 와 이름·
    // 포맷이 같다(둘 다 OpenAI REST 관례). RPM(requests)·TPM(tokens) 헤더가 이름만 다르고
    // 모양은 같아 같은 파서를 헤더 이름만 바꿔 재사용한다. 값을 저장·판단하지 않고 그대로
    // 실어 보내기만 한다
    private static int? RateLimitRemaining(HttpResponseHeaders headers, string headerName)
        => headers.TryGetValues(headerName, out var values)
            && int.TryParse(values.FirstOrDefault(), out var n)
                ? n
                : null;

    /// <summary>
    /// "6ms"·"6s"·"1m30s"·"1h0m0s" 형식의 OpenAI 초기화 헤더를 상대 시간으로 바꾼다.
    /// 형식이 예상과 다르면 null — 대기 여부는 이 값을 쓰는 쪽이 "정보 없음"으로 판단한다.
    /// `OpenAiImageProvider`의 같은 이름 메서드와 파싱 규칙이 동일하다(의도적 중복 —
    /// 두 곳뿐이라 공유 헬퍼로 안 뽑는다).
    ///
    /// 독립 리뷰 지적 두 건을 여기서 고친다 — ① `double.Parse`에 컬처를 안 주면
    /// 소수점 콤마 로케일(예: de-DE)에서 "1.44s"가 144초로 읽힌다(문화권 의존
    /// 버그). ② TPM 헤더는 "6ms"처럼 ms 단위로, 일부 한도는 "1h0m0s"처럼 h 단위로
    /// 오는데 옛 정규식은 m·s만 인식해 null로 떨어뜨렸다.
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

    /// <summary>
    /// 이미지는 0..4장 (§7.2). 각 장 바로 앞에 label text 를 넣는다 —
    /// 유사도 평가처럼 여러 장을 보낼 때 모델이 어느 장이 원본인지 알아야 한다.
    /// </summary>
    private static object[] BuildUserContent(LlmRequest request)
    {
        var blocks = new List<object> { new { type = "text", text = request.User } };

        foreach (var image in request.Images)
        {
            blocks.Add(new { type = "text", text = $"[image: {image.Name}]" });
            blocks.Add(new
            {
                type = "image_url",
                image_url = new
                {
                    url = $"data:{image.Content.ContentType};base64,{Convert.ToBase64String(image.Content.Bytes)}",
                },
            });
        }

        return [.. blocks];
    }
}
