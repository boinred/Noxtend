using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Infrastructure.Prices;

internal static class OfficialMeshPriceParser
{
    internal const string TripoConditions = "Paid Standard API; texture=true; pbr=true; texture_quality=standard; face_limit=5000; texture_alignment=original_image";
    internal const string MeshyConditions = "Paid Standard API; should_texture=true; enable_pbr=true; texture_image_resolution=2048; geometry_resolution=standard; topology=triangle";
    internal const string UnknownReason = "정확한 모델·작업·옵션·credit 단위 확인 필요";

    internal static IDocument Document(byte[] body)
    {
        var document = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(body));
        foreach (var element in document.QuerySelectorAll("script, style")) element.Remove();
        return document;
    }

    internal static IReadOnlyList<string> TripoModels(byte[] catalog, byte[] detail)
    {
        var models = Document(catalog);
        if (Text(models.QuerySelector("h1")) != "Models" || models.QuerySelector("a[href='/en/models/p1'] h2") is null)
            throw new FormatException("Tripo 모델 목록 구조 확인 필요");
        var document = Document(detail);
        if (Text(document.QuerySelector("h1")) != "Tripo P1") throw new FormatException("Tripo 모델 ID 확인 필요");
        var ids = SectionTables(document, "Snapshots").SelectMany(t => t.QuerySelectorAll("td"))
            .Concat(document.QuerySelector("h1")?.ParentElement?.QuerySelectorAll("span").ToArray() ?? [])
            .Select(Text).Where(id => Regex.IsMatch(id, @"^P1-\d{8}$")).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) throw new FormatException("Tripo 고정 모델 ID 확인 필요");
        return ids;
    }

    internal static JsonElement MeshyProperties(JsonElement root)
    {
        var reference = root.GetProperty("paths").GetProperty("/v1/multi-image-to-3d").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
        const string prefix = "#/components/schemas/";
        if (reference is null || !reference.StartsWith(prefix, StringComparison.Ordinal)) throw new JsonException();
        return root.GetProperty("components").GetProperty("schemas").GetProperty(reference[prefix.Length..]).GetProperty("properties");
    }

    internal static IReadOnlyList<string> MeshyModels(byte[] api)
    {
        using var document = JsonDocument.Parse(api);
        var ids = MeshyProperties(document.RootElement).GetProperty("ai_model").GetProperty("enum").EnumerateArray()
            .Select(x => x.GetString()).ToArray();
        if (ids.Length == 0 || ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsControl))) throw new JsonException();
        return ids.Select(id => id!).Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static OfficialPriceCandidate Tripo(string model, byte[] detail, byte[] pricing, DateTimeOffset at)
    {
        var document = Document(detail);
        var publicPricing = Document(pricing);
        var evidence = new List<OfficialPriceEvidence>();
        var row = SectionTables(document, "Pricing")
            .Where(t => Headers(t).SequenceEqual(["Task Type", "No Texture", "Standard Texture", "Detailed Texture"]))
            .SelectMany(t => t.QuerySelectorAll("tbody tr")).SingleOrDefault(r => Text(r.Children.FirstOrDefault()) == "Multiview to 3D");
        var exactModel = Text(document.QuerySelector("h1")?.ParentElement?.QuerySelector("span"));
        decimal? credits = exactModel == model && row is not null && row.Children.Length == 4 && Number(Text(row.Children[2]), out var amount) ? amount : null;
        var hasApiPricing = Text(publicPricing.QuerySelector("h1")) == "API Pricing"
            && publicPricing.QuerySelectorAll("p").Any(p => Text(p).StartsWith("Simple, transparent pay-as-you-go pricing.", StringComparison.Ordinal));
        var conversion = publicPricing.QuerySelectorAll("div, span, p").Select(Text)
            .Where(p => hasApiPricing && Regex.IsMatch(p, @"^1 credit = \$(\d+(?:\.\d+)?) USD$")).Distinct(StringComparer.Ordinal).ToArray();
        var rates = conversion.SelectMany(p => Regex.Matches(p, @"1 credit = \$(\d+(?:\.\d+)?) USD"))
            .Select(m => decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().ToArray();
        decimal? usd = rates.Length == 1 ? rates[0] : null;
        evidence.Add(Evidence(detail, "https://developers.tripo3d.ai/en/models/p1", at,
            row is null ? UnknownReason : "Tripo P1; Pricing; Task Type / No Texture / Standard Texture / Detailed Texture; " +
                string.Join(" / ", row.Children.Select(Text)) + "; USD/credit conversion from https://developers.tripo3d.ai/en/pricing: " + string.Join("; ", conversion),
            usd is null ? null : credits, usd));
        evidence.Add(Evidence(pricing, "https://developers.tripo3d.ai/en/pricing", at, conversion.Length == 0 ? "API Pricing; USD/credit 확인 필요" : string.Join("; ", conversion), usd: usd));
        var detailDate = OfficialTextPriceParser.EffectiveDate(document);
        var globalDate = OfficialTextPriceParser.EffectiveDate(publicPricing);
        var conflictingDate = detailDate is not null && globalDate is not null && detailDate != globalDate;
        var valid = credits is not null && usd is not null && !conflictingDate;
        return new(model, "mesh", "multiview-to-3d", TripoConditions,
            valid ? new(0, 0, PerImage: credits * usd, OfficialEffectiveFrom: detailDate ?? globalDate) : null,
            evidence, valid ? null : conflictingDate ? "공식 시행일 상충 확인 필요" : UnknownReason);
    }

    internal static OfficialPriceCandidate Meshy(string model, byte[] api, byte[] endpoint, byte[] pricing, DateTimeOffset at)
    {
        var document = Document(pricing);
        using var schema = JsonDocument.Parse(api);
        var properties = MeshyProperties(schema.RootElement);
        var endpointDocument = Document(endpoint);
        var textureOption = endpointDocument.QuerySelectorAll("dl").SingleOrDefault(dl => Text(dl.QuerySelector("dd")) == "texture_resolution" && dl.QuerySelectorAll("dd").Any(dd => Text(dd) == "default 2k"));
        var geometryOption = endpointDocument.QuerySelectorAll("dl").SingleOrDefault(dl => Text(dl.QuerySelector("dd")) == "geometry_resolution" && dl.QuerySelectorAll("dd").Any(dd => Text(dd) == "default standard"));
        var textureEnums = properties.GetProperty("texture_resolution").GetProperty("enum").EnumerateArray().Select(v => v.GetString()).ToArray();
        var conditionsSupported = textureOption is not null && geometryOption is not null
            && Text(textureOption).Contains("2048×2048", StringComparison.Ordinal)
            && properties.GetProperty("geometry_resolution").GetProperty("default").GetString() == "standard"
            && textureEnums.Contains("2k");
        var evidence = new List<OfficialPriceEvidence>
        {
            Evidence(api, "https://docs.meshy.ai/openapi.json", at, "Multi-Image to 3D request schema; geometry_resolution default=" +
                properties.GetProperty("geometry_resolution").GetProperty("default").GetString() + "; texture_resolution enum=" + string.Join("/", textureEnums)),
            Evidence(endpoint, "https://docs.meshy.ai/en/api/multi-image-to-3d", at, Text(textureOption) + "; " + Text(geometryOption)),
        };
        var creditValues = new List<decimal>();
        var effective = OfficialTextPriceParser.EffectiveDate(document);
        var pricingModel = model;
        if (model == "meshy-7" && document.QuerySelectorAll("p").Any(p => Text(p).StartsWith("Legacy models: meshy-7 uses meshy-7.1 pricing;", StringComparison.Ordinal)))
            pricingModel = "meshy-7.1";
        var hasUnit = document.QuerySelectorAll("p").Any(p => Text(p).StartsWith("Prices are listed in credits per request unless otherwise noted.", StringComparison.Ordinal));
        foreach (var table in SectionTables(document, "3D Generation"))
        {
            var headers = table.QuerySelectorAll("thead th").ToArray();
            if (headers.Length < 3 || Text(headers[0]) != "API" || Text(headers[1]) != "Configuration") continue;
            // latest 별칭의 버전 추측 방지
            var column = Array.FindIndex(headers, h => Text(h.QuerySelector("code")) == pricingModel);
            if (column < 2 || model == "latest") continue;
            var operation = "";
            foreach (var row in table.QuerySelectorAll("tbody tr"))
            {
                var cells = row.Children.ToArray();
                if (cells.Length != headers.Length) continue;
                if (Text(cells[0]).Length > 0) operation = cells[0].QuerySelector("a")?.GetAttribute("href") ?? "";
                if (operation == "/en/api/multi-image-to-3d" && Text(cells[1]) == "Mesh with 2K textures" &&
                    Number(Text(cells[column]), out var amount))
                {
                    creditValues.Add(amount);
                    evidence.Add(Evidence(pricing, "https://docs.meshy.ai/en/api/pricing", at,
                        "3D Generation; " + (hasUnit ? "credits per request" : "unit unconfirmed") + "; Multi-Image to 3D; Mesh with 2K textures; " + model + "; pricing model=" + pricingModel + "; " + string.Join(" / ", cells.Select(Text)), hasUnit ? amount : null));
                }
            }
        }
        if (evidence.Count == 2) evidence.Add(Evidence(pricing, "https://docs.meshy.ai/en/api/pricing", at, UnknownReason));
        var verified = hasUnit && conditionsSupported && creditValues.Distinct().Count() == 1;
        return new(model, "mesh", "multi-image-to-3d", MeshyConditions,
            verified ? new(0, 0, OfficialEffectiveFrom: effective) : null, evidence,
            model == "latest" ? "latest 고정 모델 버전 확인 필요" : verified ? "계정별 환산 지원 필요" : UnknownReason);
    }

    private static IEnumerable<IElement> SectionTables(IDocument document, string title)
    {
        var section = "";
        foreach (var element in document.QuerySelectorAll("h2, h3, table"))
        {
            if (element.LocalName is "h2" or "h3") section = Text(element);
            else if (section == title) yield return element;
        }
    }

    private static string[] Headers(IElement table) => table.QuerySelectorAll("thead th").Select(Text).ToArray();
    private static bool Number(string value, out decimal amount)
        => decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) && amount > 0;
    private static string Text(IElement? element) => Regex.Replace(element?.TextContent ?? "", @"\s+", " ").Trim();
    private static OfficialPriceEvidence Evidence(byte[] body, string url, DateTimeOffset at, string conditions, decimal? credits = null, decimal? usd = null)
        => new(url, at, Convert.ToHexStringLower(SHA256.HashData(body)), conditions, credits, usd);
}
