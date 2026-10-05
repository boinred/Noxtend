using System.Net.Http.Headers;
using System.Text.Json;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;

namespace Noxtend.Infrastructure.Mesh;

/// <summary>
/// 3D 생성 모델 목록.
///
/// Design Ref: §10.1 · Plan D-04
///
/// **고정 스냅숏이다.** Tripo 는 모델 목록 endpoint 를 주지 않고, 준다 해도 우리는 검증한
/// 모델만 쓴다. `P1-20260311` 같은 날짜 붙은 이름을 고정하는 것이 품질 drift 를 막는
/// 유일한 장치다 — `P1` 만 쓰면 공급자가 뒤에서 바꿔도 우리는 모른다.
///
/// **그래도 키는 확인한다.** 목록이 고정이라 그냥 돌려줄 수도 있지만, 그러면 키가 틀린 것을
/// 접수 후 첫 유료 호출에서야 알게 된다. 잔액 조회는 credit 을 쓰지 않으면서 키가 살아
/// 있는지 확인하는 유일한 경로다.
/// </summary>
internal static class MeshModels
{
    private const string BalanceEndpoint = "https://openapi.tripo3d.ai/v3/account/balance";

    private static readonly IReadOnlyList<ProviderModel> Tripo =
    [
        new("P1-20260311", "Tripo P1 (Low Poly)"),
    ];

    /// <summary>
    /// Meshy 도 고정한다 (meshy-provider Plan 1.3).
    ///
    /// <c>latest</c> 를 쓰면 공급자가 뒤에서 모델을 바꿔도 우리는 모른다 — Tripo 를
    /// 날짜로 고정한 것과 같은 이유다.
    /// </summary>
    private static readonly IReadOnlyList<ProviderModel> Meshy =
    [
        new("meshy-7", "Meshy 7 (Multi-Image)"),
    ];

    private const string MeshyBalanceEndpoint = "https://api.meshy.ai/openapi/v1/balance";

    /// <summary><c>Llm:UseFake</c> 일 때의 목록. 비어 있으면 Fake E2E 가 접수에서 막힌다.</summary>
    private static readonly IReadOnlyList<ProviderModel> Fake =
    [
        new("fake-mesh-a", "Fake Mesh A"),
    ];

    public static IReadOnlyList<ProviderModel> FakeModels => Fake;

    public static IReadOnlyList<ProviderModel> For(ProviderKind kind) => kind switch
    {
        ProviderKind.Tripo => Tripo,
        ProviderKind.Meshy => Meshy,

        // 3D 를 못 만드는 공급자다. 예외가 아니라 빈 목록인 이유는 "키가 틀렸다" 가 아니라
        // "이 공급자로는 못 만든다" 이기 때문이다
        _ => [],
    };

    /// <summary>
    /// 잔액을 조회해 키가 살아 있는지 확인하고 고정 목록을 돌려준다.
    ///
    /// **생성 endpoint 를 부르지 않는다** (NFR-08). 목록을 여는 것만으로 credit 이 나가면
    /// 관리자 화면을 여는 것이 유료 행위가 된다.
    /// </summary>
    public static Task<IReadOnlyList<ProviderModel>> ListTripoAsync(
        HttpClient http,
        string apiKey,
        CancellationToken ct)
        => ListAsync(http, apiKey, BalanceEndpoint, "Tripo", Tripo, ct);

    /// <summary>
    /// Meshy 도 같은 규약이다 — 잔액을 조회해 키만 확인하고 고정 목록을 돌려준다.
    /// </summary>
    public static Task<IReadOnlyList<ProviderModel>> ListMeshyAsync(
        HttpClient http,
        string apiKey,
        CancellationToken ct)
        => ListAsync(http, apiKey, MeshyBalanceEndpoint, "Meshy", Meshy, ct);

    /// <summary>
    /// 남은 크레딧을 읽는다.
    ///
    /// **응답 모양이 공급자마다 다르다** — Meshy 는 `{"balance": N}`, Tripo 는
    /// `{"data":{"balance": N}}` 다. 어휘 차이를 여기서 흡수한다.
    ///
    /// 읽을 수 없으면 <c>null</c> 이다. **0 으로 뭉개면 안 된다** — 0 은 "다 썼다" 이고
    /// null 은 "모른다" 다. 잔액을 못 읽었다고 작업을 막을 이유는 없다.
    /// </summary>
    public static async Task<int?> ReadBalanceAsync(
        HttpClient http, string apiKey, ProviderKind kind, CancellationToken ct)
    {
        var endpoint = kind switch
        {
            ProviderKind.Tripo => BalanceEndpoint,
            ProviderKind.Meshy => MeshyBalanceEndpoint,
            _ => null,
        };

        if (endpoint is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = body.RootElement;

            // Tripo 는 봉투 안에 담는다
            if (kind == ProviderKind.Tripo
                && root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object)
            {
                root = data;
            }

            return root.TryGetProperty("balance", out var balance)
                   && balance.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            // 본문이 JSON 이 아닌 것은 실패가 아니다 — 잔액을 모를 뿐이다
            return null;
        }
    }

    private static async Task<IReadOnlyList<ProviderModel>> ListAsync(
        HttpClient http,
        string apiKey,
        string balanceEndpoint,
        string providerName,
        IReadOnlyList<ProviderModel> snapshot,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, balanceEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            // 원문 본문은 넣지 않는다 — 공급자 오류에 키가 되비쳐 오는 경우가 있다 (§13.1)
            throw new ProviderCallFailedException(
                $"{providerName} 계정 조회에 실패했습니다. HTTP {(int)response.StatusCode}");
        }

        return snapshot;
    }
}
