using System.Text.Json;
using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Llm;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Infrastructure.Prices;

internal sealed class OfficialModelPriceSource(
    ProviderCredentialResolver credentials, IHttpClientFactory clients, TimeProvider clock) : IOfficialModelPriceSource
{
    public async Task<OfficialPriceCollection> CollectAsync(Guid providerConfigId, CancellationToken ct)
    {
        var credential = await credentials.ResolveAsync(providerConfigId, ct);
        var provider = credential.Kind switch
        {
            ProviderKind.OpenAI => "openai", ProviderKind.Anthropic => "anthropic", ProviderKind.Google => "google",
            ProviderKind.Tripo => "tripo", ProviderKind.Meshy => "meshy",
            _ => throw new OfficialPriceSourceException("해당 공급자의 공식 수집은 지원하지 않습니다"),
        };
        using var http = clients.CreateClient(OfficialPriceHttp.ClientName);
        return await CollectTextAsync(new OfficialPriceHttp(http), providerConfigId, provider, credential.ApiKey, clock, ct);
    }

    internal static async Task<OfficialPriceCollection> CollectTextAsync(
        OfficialPriceHttp http, Guid configId, string provider, string apiKey, TimeProvider clock, CancellationToken ct)
    {
        if (provider is "tripo" or "meshy")
            return await OfficialMeshPriceSource.CollectAsync(http, configId, provider, clock, ct);
        using var total = CancellationTokenSource.CreateLinkedTokenSource(ct);
        total.CancelAfter(TimeSpan.FromSeconds(120));
        var collectedAt = clock.GetUtcNow();
        var models = new List<string>();
        var listStatus = "failed";
        string? modelError = null, priceError = null;
        var pages = 0;
        try
        {
            string? token = null;
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                if (pages >= 20) throw new OfficialPriceSourceException("모델 목록 페이지 상한 초과");
                var endpoint = provider switch
                {
                    "openai" => "https://api.openai.com/v1/models",
                    "anthropic" => "https://api.anthropic.com/v1/models?limit=100" + (token is null ? "" : "&after_id=" + Uri.EscapeDataString(token)),
                    "google" => "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000" + (token is null ? "" : "&pageToken=" + Uri.EscapeDataString(token)),
                    _ => throw new OfficialPriceSourceException("지원하지 않는 공급자입니다"),
                };
                var body = await http.GetAsync(new Uri(endpoint), provider, apiKey, total.Token);
                using var payload = JsonDocument.Parse(body);
                var root = payload.RootElement;
                var entries = root.GetProperty(provider == "google" ? "models" : "data");
                if (entries.ValueKind != JsonValueKind.Array) throw new JsonException();
                var pageModels = new List<string>();
                foreach (var entry in entries.EnumerateArray())
                {
                    var id = entry.GetProperty(provider == "google" ? "name" : "id").GetString();
                    if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsControl)) throw new JsonException();
                    if (provider == "google")
                    {
                        if (!id.StartsWith("models/", StringComparison.Ordinal)) throw new JsonException();
                        id = id["models/".Length..];
                        if (id.Length == 0) throw new JsonException();
                    }
                    pageModels.Add(id);
                }
                models.AddRange(pageModels);
                pages++;
                token = provider switch
                {
                    "anthropic" => root.GetProperty("has_more").GetBoolean() ? root.GetProperty("last_id").GetString() : null,
                    "google" => root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null,
                    _ => null,
                };
                if (provider == "anthropic" && root.GetProperty("has_more").GetBoolean() && string.IsNullOrWhiteSpace(token)) throw new JsonException();
                if (token is not null && (string.IsNullOrWhiteSpace(token) || token.Length > 2048 || !seenTokens.Add(token)))
                    throw new OfficialPriceSourceException("모델 목록 페이지 토큰 확인 필요");
            } while (token is not null);
            listStatus = "complete";
        }
        catch (Exception ex) when (ex is OfficialPriceSourceException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            listStatus = pages > 0 ? "partial" : "failed";
            modelError = ex is OfficialPriceSourceException ? ex.Message : "모델 목록 자료 구조 확인 필요";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            listStatus = pages > 0 ? "partial" : "failed";
            modelError = "전체 수집 시간 초과";
        }
        ct.ThrowIfCancellationRequested();
        var exactModels = models.Distinct(StringComparer.Ordinal).ToArray();
        var priceUrl = provider switch
        {
            "openai" => "https://developers.openai.com/api/docs/pricing",
            "anthropic" => "https://platform.claude.com/docs/en/about-claude/pricing",
            "google" => "https://ai.google.dev/gemini-api/docs/pricing",
            _ => throw new OfficialPriceSourceException("지원하지 않는 공급자입니다"),
        };
        IReadOnlyList<OfficialPriceCandidate> candidates = [];
        try
        {
            var body = await http.GetAsync(new Uri(priceUrl), null, null, total.Token);
            candidates = OfficialTextPriceParser.Parse(provider, body, priceUrl, clock.GetUtcNow(), exactModels);
            total.Token.ThrowIfCancellationRequested();
            if (candidates.Any(c => c.Terms is null && c.BlockedReason == "정확한 모델·Standard 요금·단위 확인 필요"))
                priceError = "일부 모델의 공식 요금 확인 필요";
        }
        catch (Exception ex) when (ex is OfficialPriceSourceException or FormatException)
        {
            priceError = ex is OfficialPriceSourceException ? ex.Message : "공식 요금 자료 확인 필요";
            candidates = exactModels.Select(model => new OfficialPriceCandidate(model, "text", "text",
                "Paid Standard; 확인 필요", null, [], priceError)).ToList();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            priceError = "전체 수집 시간 초과";
            candidates = exactModels.Select(model => new OfficialPriceCandidate(model, "text", "text",
                "Paid Standard; 확인 필요", null, [], priceError)).ToList();
        }
        ct.ThrowIfCancellationRequested();
        var status = listStatus == "failed" ? "failed" : listStatus == "partial" || priceError is not null ? "partial" : "success";
        return new(configId, provider, status, listStatus, collectedAt, exactModels, candidates, modelError, priceError);
    }
}
