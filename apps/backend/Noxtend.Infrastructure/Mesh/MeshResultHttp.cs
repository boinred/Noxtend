using System.Net;
using Noxtend.Application.Common;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Mesh;

/// <summary>
/// 공급자가 준 결과 링크를 안전하게 여는 자리.
///
/// Design Ref: §4.2 · §5.6 · §13.2
///
/// **공급자마다 다시 쓰지 않는다.** 여기 있는 것은 SSRF 방어이지 Tripo 규약이 아니다.
/// 어댑터마다 베껴 두면 둘째 공급자에서 한 겹이 빠지고, 그 빠진 겹은 공급자가 우리
/// 네트워크 안쪽을 가리키는 URL 을 줄 때까지 드러나지 않는다.
/// </summary>
internal static class MeshResultHttp
{
    private const int MaxRedirects = 3;

    /// <summary>
    /// **리다이렉트마다 다시 검사한다.** 첫 URL 만 보면 허용된 host 가 사설 주소로
    /// 넘겨주는 것을 막지 못한다.
    /// </summary>
    public static async Task<HttpResponseMessage> OpenAsync(
        HttpClient http, string url, MeshGenerationOptions options, CancellationToken ct)
    {
        var target = Validated(url, options);

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);

            // 결과 CDN 은 인증을 요구하지 않는다. 키를 붙이면 우리 자격이 남의 host 로 간다
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Moved
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();

                if (location is null || hop == MaxRedirects)
                {
                    throw new MeshProviderException(
                        "MESH_RESULT_EXPIRED", "결과 링크를 따라갈 수 없습니다", canRetry: false);
                }

                target = Validated(
                    location.IsAbsoluteUri ? location.ToString() : new Uri(target, location).ToString(),
                    options);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                response.Dispose();

                // 서명이 만료됐다. 자동으로 새 유료 작업을 만들지 않는다 (Plan D-07)
                throw new MeshProviderException(
                    "MESH_RESULT_EXPIRED", "결과 링크가 만료되었습니다", canRetry: false);
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                response.Dispose();

                throw new MeshProviderException(
                    "MESH_PROVIDER_UNAVAILABLE",
                    $"결과를 내려받지 못했습니다. HTTP {(int)status}",
                    canRetry: true);
            }

            return response;
        }

        throw new MeshProviderException(
            "MESH_RESULT_EXPIRED", "결과 링크를 따라갈 수 없습니다", canRetry: false);
    }

    /// <summary>HTTPS 와 허용 host 만. 사설·루프백·링크로컬은 거절한다.</summary>
    private static Uri Validated(string url, MeshGenerationOptions options)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new MeshProviderException(
                "MESH_RESULT_INVALID", "결과 링크가 HTTPS 가 아닙니다", canRetry: false);
        }

        if (IPAddress.TryParse(uri.Host, out var address) && IsPrivate(address))
        {
            throw new MeshProviderException(
                "MESH_RESULT_INVALID", "결과 링크가 내부 주소를 가리킵니다", canRetry: false);
        }

        var allowed = options.AllowedResultHosts.Any(host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase));

        return allowed
            ? uri
            : throw new MeshProviderException(
                "MESH_RESULT_INVALID", "허용되지 않은 결과 host 입니다", canRetry: false);
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();

        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
               && (bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168)
                   || (bytes[0] == 169 && bytes[1] == 254));
    }
}

/// <summary>
/// 열린 결과 스트림 묶음.
///
/// Design Ref: §4.2 · D-08
///
/// **응답 객체를 함께 붙들고 있는 이유**는 스트림을 다 쓰기 전에 응답을 닫으면 내려받기가
/// 끊기기 때문이다.
/// </summary>
internal sealed class MeshResultDownload(
    IReadOnlyList<MeshResultPart> parts,
    IReadOnlyList<HttpResponseMessage> responses) : IMeshResultDownload
{
    public IReadOnlyList<MeshResultPart> Parts => parts;

    public async ValueTask DisposeAsync()
    {
        foreach (var part in parts)
        {
            await part.Content.DisposeAsync();
        }

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}
