using System.Text.Json;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Infrastructure.Prices;

internal static class OfficialMeshPriceSource
{
    internal static async Task<OfficialPriceCollection> CollectAsync(
        OfficialPriceHttp http, Guid configId, string provider, TimeProvider clock, CancellationToken ct)
    {
        using var total = CancellationTokenSource.CreateLinkedTokenSource(ct);
        total.CancelAfter(TimeSpan.FromSeconds(120));
        var at = clock.GetUtcNow();
        IReadOnlyList<string> models = [];
        byte[]? catalog = null, detail = null;
        var listStatus = "failed";
        string? modelError = null, priceError = null;
        try
        {
            if (provider == "tripo")
            {
                catalog = await http.GetAsync(new("https://developers.tripo3d.ai/en/models"), null, null, total.Token);
                detail = await http.GetAsync(new("https://developers.tripo3d.ai/en/models/p1"), null, null, total.Token);
                models = OfficialMeshPriceParser.TripoModels(catalog, detail);
                // 공개 계열 목록의 다른 모델은 정확한 snapshot ID 미확인
                listStatus = "partial";
                modelError = "공개 목록의 다른 계열 고정 모델 ID 확인 필요";
            }
            else
            {
                catalog = await http.GetAsync(new("https://docs.meshy.ai/openapi.json"), null, null, total.Token);
                models = OfficialMeshPriceParser.MeshyModels(catalog);
                listStatus = "complete";
            }
        }
        catch (Exception ex) when (ex is OfficialPriceSourceException or FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            modelError = ex is OfficialPriceSourceException ? ex.Message : "공식 3D 모델 목록 구조 확인 필요";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            modelError = "전체 수집 시간 초과";
        }
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<OfficialPriceCandidate> candidates = [];
        try
        {
            var priceUrl = provider == "tripo" ? "https://developers.tripo3d.ai/en/pricing" : "https://docs.meshy.ai/en/api/pricing";
            var pricing = await http.GetAsync(new(priceUrl), null, null, total.Token);
            if (models.Count > 0)
            {
                if (provider == "tripo")
                    candidates = models.Select(model =>
                    {
                        total.Token.ThrowIfCancellationRequested();
                        return OfficialMeshPriceParser.Tripo(model, detail!, pricing, clock.GetUtcNow());
                    }).ToArray();
                else
                {
                    var endpoint = await http.GetAsync(new("https://docs.meshy.ai/en/api/multi-image-to-3d"), null, null, total.Token);
                    candidates = models.Select(model =>
                    {
                        total.Token.ThrowIfCancellationRequested();
                        return OfficialMeshPriceParser.Meshy(model, catalog!, endpoint, pricing, clock.GetUtcNow());
                    }).ToArray();
                }
            }
            total.Token.ThrowIfCancellationRequested();
            if (candidates.Any(c => c.BlockedReason == OfficialMeshPriceParser.UnknownReason))
                priceError = "일부 모델의 공식 작업·옵션 요금 확인 필요";
        }
        catch (Exception ex) when (ex is OfficialPriceSourceException or FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            priceError = ex is OfficialPriceSourceException ? ex.Message : "공식 3D 요금 자료 확인 필요";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            priceError = "전체 수집 시간 초과";
        }
        ct.ThrowIfCancellationRequested();
        if (priceError is not null && candidates.Count == 0)
            candidates = models.Select(model => new OfficialPriceCandidate(model, "mesh",
                provider == "tripo" ? "multiview-to-3d" : "multi-image-to-3d",
                provider == "tripo" ? OfficialMeshPriceParser.TripoConditions : OfficialMeshPriceParser.MeshyConditions,
                null, [], priceError)).ToArray();
        var status = listStatus == "failed" ? "failed" : listStatus == "partial" || priceError is not null ? "partial" : "success";
        return new(configId, provider, status, listStatus, at, models, candidates, modelError, priceError);
    }
}
