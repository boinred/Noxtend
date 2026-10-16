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

    public static async Task<ProviderCallFailedException> OpenAiFailureAsync(
        HttpResponseMessage response, string apiKey, CancellationToken ct)
    {
        var status = ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var transient = IsTransient(response.StatusCode);
        try
        {
            // 외부 오류 본문 읽기 상한과 원문 미보존
            const int maxBytes = 16 * 1024;
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var bytes = new byte[maxBytes + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), ct);
                if (read == 0) break;
                count += read;
            }
            if (count > maxBytes) return new(status, isTransient: transient);
            using var body = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 16 });
            if (body.RootElement.ValueKind != JsonValueKind.Object
                || !body.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                return new(status, isTransient: transient);
            string? Value(string name) => error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            var code = Value("code");
            var type = Value("type");
            if (response.StatusCode == HttpStatusCode.TooManyRequests
                && (type == "insufficient_quota" || code is "insufficient_quota" or "credit_balance_exhausted"
                    or "organization_spend_limit_exceeded" or "project_spend_limit_exceeded" or "organization_usage_limit_exceeded"))
                transient = false;
            var diagnostic = status;
            // 자유 텍스트 대신 알려진 스키마 거절 원인만 보존
            var message = Value("message");
            if (message is not null && message.Contains("Invalid schema for response_format", StringComparison.Ordinal)
                && message.Contains("'oneOf' is not permitted", StringComparison.Ordinal))
            {
                diagnostic += "; schema: oneOf is not permitted";
                var contextStart = message.IndexOf("In context=(", StringComparison.Ordinal);
                var contextEnd = contextStart < 0 ? -1 : message.IndexOf("), 'oneOf' is not permitted", contextStart, StringComparison.Ordinal);
                if (contextEnd >= 0)
                {
                    var context = message[(contextStart + "In context=(".Length)..contextEnd];
                    foreach (var field in context.Split(',').Select(token => token.Trim().Trim('\'')).Distinct())
                        if (field is "grid" or "assets" or "sourceBounds" or "cellWidth" or "cellHeight"
                            && (string.IsNullOrEmpty(apiKey) || !field.Contains(apiKey, StringComparison.OrdinalIgnoreCase)))
                            diagnostic += $"; field={field}";
                }
            }
            foreach (var (name, value) in new[] { ("code", code), ("type", type), ("param", Value("param")) })
                if (value is not null && Safe(value, apiKey) && diagnostic.Length + name.Length + value.Length + 3 <= 256)
                    diagnostic += $"; {name}={value}";
            if (diagnostic == status) diagnostic += "; details omitted";
            return new(diagnostic, isTransient: transient);
        }
        catch (Exception ex) when (ex is JsonException or IOException or HttpRequestException)
        {
            // 진단 파싱 실패 시 기존 HTTP 실패 유지
            return new(status, isTransient: transient);
        }
    }

    private static bool Safe(string value, string apiKey)
        => (string.IsNullOrEmpty(apiKey) || !value.Contains(apiKey, StringComparison.OrdinalIgnoreCase))
            && value is "invalid_json_schema" or "invalid_request_error" or "invalid_api_key" or "authentication_error"
                or "permission_denied" or "insufficient_quota" or "credit_balance_exhausted"
                or "organization_spend_limit_exceeded" or "project_spend_limit_exceeded" or "organization_usage_limit_exceeded"
                or "rate_limit_exceeded" or "rate_limit_error" or "slow_down" or "server_error" or "api_error"
                or "invalid_value" or "unsupported_value" or "unsupported_parameter" or "missing_required_parameter"
                or "context_length_exceeded" or "content_policy_violation" or "model_not_found"
                or "response_format" or "response_format.json_schema" or "response_format.json_schema.schema"
                or "model" or "prompt" or "size" or "quality" or "n" or "image" or "background" or "output_format";

    public static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException)
        {
            // 헤더 수신 이후 본문 전송 실패의 재시도·원문 미노출 유지
            throw new ProviderCallFailedException(nameof(HttpRequestException), isTransient: true);
        }
    }

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        try
        {
            return await http.SendAsync(request, completion, ct);
        }
        catch (HttpRequestException ex)
        {
            // 외부 공급자 일시적 전송 실패 정규화
            throw new ProviderCallFailedException(
                nameof(HttpRequestException), ex, isTransient: true);
        }
    }
}
