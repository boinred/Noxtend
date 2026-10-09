using System.Net.Http.Headers;

namespace Noxtend.Infrastructure.Prices;

internal sealed class OfficialPriceHttp(HttpClient http)
{
    public const string ClientName = "official-model-prices";
    internal const int MaxBytes = 8 * 1024 * 1024;
    private static readonly HashSet<string> Allowed =
    [
        "api.openai.com/v1/models", "developers.openai.com/api/docs/pricing",
        "api.anthropic.com/v1/models", "platform.claude.com/docs/en/about-claude/pricing",
        "generativelanguage.googleapis.com/v1beta/models", "ai.google.dev/gemini-api/docs/pricing",
        "developers.tripo3d.ai/en/models", "developers.tripo3d.ai/en/models/p1", "developers.tripo3d.ai/en/pricing",
        "docs.meshy.ai/openapi.json", "docs.meshy.ai/en/api/pricing", "docs.meshy.ai/en/api/multi-image-to-3d",
    ];

    public async Task<byte[]> GetAsync(Uri uri, string? provider, string? apiKey, CancellationToken ct)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            !Allowed.Contains(uri.Host + uri.AbsolutePath))
            throw new OfficialPriceSourceException("허용되지 않은 공식 자료 경로입니다");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        if (apiKey is not null)
        {
            var credentialHost = provider switch
            {
                "openai" => "api.openai.com", "anthropic" => "api.anthropic.com",
                "google" => "generativelanguage.googleapis.com", _ => "",
            };
            if (uri.Host != credentialHost || !uri.AbsolutePath.EndsWith("/models", StringComparison.Ordinal) || apiKey.Any(char.IsControl))
                throw new OfficialPriceSourceException("공급자 인증 경계 확인 필요");
            switch (provider)
            {
                case "openai": request.Headers.Authorization = new("Bearer", apiKey); break;
                case "anthropic":
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                    break;
                case "google": request.Headers.Add("x-goog-api-key", apiKey); break;
                default: throw new OfficialPriceSourceException("지원하지 않는 공급자입니다");
            }
        }
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new OfficialPriceSourceException($"공식 자료 조회 실패: HTTP {(int)response.StatusCode}");
            if (response.Content.Headers.ContentLength > MaxBytes)
                throw new OfficialPriceSourceException("공식 자료 응답 크기 상한 초과");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var result = new MemoryStream();
            var buffer = new byte[8192];
            int bytes;
            while ((bytes = await stream.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (result.Length + bytes > MaxBytes)
                    throw new OfficialPriceSourceException("공식 자료 응답 크기 상한 초과");
                result.Write(buffer, 0, bytes);
            }
            return result.ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new OfficialPriceSourceException("공식 자료 조회 시간 초과");
        }
        catch (HttpRequestException)
        {
            throw new OfficialPriceSourceException("공식 자료 네트워크 조회 실패");
        }
        catch (IOException)
        {
            throw new OfficialPriceSourceException("공식 자료 읽기 실패");
        }
    }
}

internal sealed class OfficialPriceSourceException(string message) : Exception(message);
