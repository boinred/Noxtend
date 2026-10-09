using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Noxtend.Tuning.Domain.Call;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Tuning.Application.Prices;

public sealed record PriceUpdateProviderResult(
    Guid ProviderConfigId, string Provider, string Status, string ModelListStatus, DateTimeOffset CollectedAt,
    string? ModelError, string? PriceError, IReadOnlyList<Guid> CandidateIds);

public sealed record PriceUpdateCandidate(
    Guid Id, string Provider, string Model, string Area, string Operation, string Conditions,
    IReadOnlyList<Guid> ProviderConfigIds, OfficialPriceTerms? CurrentTerms, OfficialPriceTerms? Terms,
    IReadOnlyList<OfficialPriceEvidence> Evidence, string ChangeKind, string ExecutionSupport, string? BlockedReason,
    string BaselineFingerprint);

public sealed record PriceComparisonResult(
    IReadOnlyList<PriceUpdateProviderResult> Providers, IReadOnlyList<PriceUpdateCandidate> Candidates);

public static class PriceCandidateComparison
{
    public static PriceComparisonResult Compare(
        IReadOnlyList<OfficialPriceCollection> collections, IReadOnlyList<ModelPrice> prices, DateTimeOffset at,
        IReadOnlyCollection<(string Provider, string Model, string Area)> supportedModels)
    {
        var observations = collections.SelectMany(source => source.Candidates.Select(candidate => (Source: source, Candidate: candidate))).ToArray();
        var candidates = new List<PriceUpdateCandidate>();
        foreach (var group in observations.GroupBy(x => (x.Source.Provider, x.Candidate.Model, x.Candidate.Area,
                     x.Candidate.Operation, Conditions: CanonicalConditions(x.Candidate.Conditions))))
        {
            var first = group.First().Candidate;
            var configIds = group.Select(x => x.Source.ProviderConfigId).Distinct().Order().ToArray();
            var evidence = group.SelectMany(x => x.Candidate.Evidence)
                .GroupBy(e => (e.Url, e.Sha256, e.Conditions, e.CreditsPerTask, e.UsdPerCredit))
                .Select(g => g.MinBy(e => e.CollectedAt)!).ToArray();
            var providerConflict = prices.Any(p => p.Model.Equals(first.Model, StringComparison.OrdinalIgnoreCase) && p.Provider is not null && p.Provider != group.Key.Provider);
            var conflicting = group.Select(x => (x.Candidate.Terms, x.Candidate.BlockedReason,
                    Credits: CreditSignature(x.Candidate.Evidence), Usd: UsdSignature(x.Candidate.Evidence)))
                .Distinct().Count() > 1;
            var related = RelatedPrices(group.Key.Provider, first.Model, prices);
            var current = new ModelPriceBook(related).Lookup(first.Model, at);
            var currentTerms = current is null ? null : Terms(current);
            var terms = conflicting ? null : first.Terms;
            var unknown = terms is null || first.Area == "mesh" && terms.PerImage is null;
            var kind = unknown ? "priceUnknown" : current is null ? "newModel" : currentTerms == terms ? "unchanged" : "priceChanged";
            candidates.Add(new(Guid.NewGuid(), group.Key.Provider, first.Model, first.Area, first.Operation, first.Conditions,
                configIds, currentTerms, terms, evidence, kind,
                supportedModels.Contains((group.Key.Provider, first.Model, first.Area)) ? "supported" : "unverified",
                conflicting ? "설정별 공식 요금·시행일 상충 확인 필요" : providerConflict ? "기존 저장 모델의 공급자 상충 확인 필요" : first.BlockedReason,
                Fingerprint(group.Key.Provider, first.Model, prices)));
        }

        // 기존 (Model, EffectiveFrom) 저장 키의 조건·공급자 충돌 보류
        var collisions = candidates.GroupBy(c => c.Model, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(c => c.Id)).ToHashSet();
        for (var i = 0; i < candidates.Count; i++)
            if (collisions.Contains(candidates[i].Id))
                candidates[i] = candidates[i] with { BlockedReason = "서로 다른 요금 조건이 동일 단가 저장 키를 요구합니다" };

        foreach (var provider in collections.GroupBy(c => c.Provider))
        {
            if (provider.Any(c => c.ModelListStatus != "complete")) continue;
            var union = provider.SelectMany(c => c.ModelIds).ToHashSet(StringComparer.Ordinal);
            var observed = candidates.Where(c => c.Provider == provider.Key).Select(c => c.Model).ToHashSet(StringComparer.Ordinal);
            foreach (var model in prices.Where(p => p.Provider == provider.Key).Select(p => p.Model).Distinct(StringComparer.Ordinal))
            {
                if (union.Contains(model) || observed.Contains(model)) continue;
                var current = new ModelPriceBook(RelatedPrices(provider.Key, model, prices)).Lookup(model, at);
                var area = current?.PerImage is null ? "text" : provider.Key is "tripo" or "meshy" ? "mesh" : "image";
                candidates.Add(new(Guid.NewGuid(), provider.Key, model, area, area, "완전 목록에 정확한 ID 미노출",
                    [], current is null ? null : Terms(current),
                    null, [], "notInCatalog", "unverified", "목록 미노출은 폐기·권한 상실 확정이 아닙니다",
                    Fingerprint(provider.Key, model, prices)));
            }
        }
        var providers = collections.Select(c => new PriceUpdateProviderResult(c.ProviderConfigId, c.Provider, c.Status,
            c.ModelListStatus, c.CollectedAt, c.ModelError, c.PriceError,
            candidates.Where(candidate => candidate.ProviderConfigIds.Contains(c.ProviderConfigId)).Select(candidate => candidate.Id).ToArray())).ToArray();
        return new(providers, candidates);
    }

    public static string Fingerprint(string provider, string model, IReadOnlyList<ModelPrice> prices)
    {
        var rows = prices.Where(p => ModelPriceBook.Matches(model, p.Model) || ModelPriceBook.Matches(p.Model, model)).OrderBy(p => p.Id).Select(p => new
        {
            p.Id, p.Model, p.Provider, p.InputPerMillion, p.OutputPerMillion, p.LongContextFrom,
            p.LongInputPerMillion, p.LongOutputPerMillion, p.PerImage, EffectiveFrom = p.EffectiveFrom.ToUniversalTime(),
            p.AllowHistoricalFallback, p.SourceEvidenceJson, p.Note,
        });
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Provider = provider, Model = model, Rows = rows })));
    }

    private static IReadOnlyList<ModelPrice> RelatedPrices(string provider, string model, IReadOnlyList<ModelPrice> prices)
        => prices.Where(p => (p.Provider is null || p.Provider == provider) &&
            (ModelPriceBook.Matches(model, p.Model) || ModelPriceBook.Matches(p.Model, model))).ToArray();

    private static OfficialPriceTerms Terms(ModelPrice price)
        => new(price.InputPerMillion, price.OutputPerMillion, price.LongContextFrom, price.LongInputPerMillion,
            price.LongOutputPerMillion, price.PerImage, OfficialDate(price.SourceEvidenceJson));

    private static DateTimeOffset? OfficialDate(string? evidenceJson)
    {
        if (evidenceJson is null) return null;
        using var document = JsonDocument.Parse(evidenceJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("officialEffectiveFrom", out var effective) || effective.ValueKind == JsonValueKind.Null)
            return null;
        return effective.GetDateTimeOffset().ToUniversalTime();
    }

    private static string CanonicalConditions(string conditions)
        => string.Join("; ", conditions.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Regex.Replace(s, @"\s+", " ")).Order(StringComparer.Ordinal));

    private static string CreditSignature(IReadOnlyList<OfficialPriceEvidence> evidence)
        => JsonSerializer.Serialize(evidence.Where(e => e.CreditsPerTask is not null).Select(e => e.CreditsPerTask).Distinct().Order());

    private static string UsdSignature(IReadOnlyList<OfficialPriceEvidence> evidence)
        => JsonSerializer.Serialize(evidence.Where(e => e.UsdPerCredit is not null).Select(e => e.UsdPerCredit).Distinct().Order());
}
