using System.Net;
using System.Text.Json;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure;

internal static class ProviderHttp
{
    /// <summary>
    /// 다시 걸어볼 만한 실패인가.
    ///
    /// 5xx 는 공급자 쪽 사정이고 429 는 "지금 말고 나중에" 다 — 둘 다 다시 걸면 된다.
    /// 4xx 나머지(401 인증·402 결제·400 요청 오류)는 몇 번을 걸어도 같다.
    ///
    /// **공급자 어댑터 여섯 곳이 각자 같은 한 줄을 들고 있었다.** 판정이 따로 돌면 어떤
    /// 공급자는 재시도하고 어떤 공급자는 안 하는 상태가 조용히 생긴다. HTTP 를 보내는
    /// 자리가 여기 하나이므로 판정도 여기 둔다.
    /// </summary>
    public static bool IsTransient(HttpStatusCode status)
        => (int)status >= 500 || status == HttpStatusCode.TooManyRequests;

    public static async Task<bool> IsOpenAiTransientAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return IsTransient(response.StatusCode);
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (body.RootElement.ValueKind == JsonValueKind.Object
                && body.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var type = error.TryGetProperty("type", out var typeValue) && typeValue.ValueKind == JsonValueKind.String
                    ? typeValue.GetString() : null;
                var code = error.TryGetProperty("code", out var codeValue) && codeValue.ValueKind == JsonValueKind.String
                    ? codeValue.GetString() : null;
                if (type == "insufficient_quota" || code is "insufficient_quota" or "credit_balance_exhausted"
                    or "organization_spend_limit_exceeded" or "project_spend_limit_exceeded" or "organization_usage_limit_exceeded")
                    return false;
            }
        }
        catch (JsonException) { }
        return true;
    }

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            // 외부 공급자 일시적 전송 실패 정규화
            throw new ProviderCallFailedException(
                nameof(HttpRequestException), ex, isTransient: true);
        }
    }
}
