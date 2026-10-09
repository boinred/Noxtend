namespace Noxtend.Tuning.Domain.Ports;

public interface IOfficialModelPriceSource
{
    Task<OfficialPriceCollection> CollectAsync(Guid providerConfigId, CancellationToken ct);
}

public sealed record OfficialPriceTerms(
    decimal InputPerMillion, decimal OutputPerMillion, int? LongContextFrom = null,
    decimal? LongInputPerMillion = null, decimal? LongOutputPerMillion = null,
    decimal? PerImage = null, DateTimeOffset? OfficialEffectiveFrom = null);

public sealed record OfficialPriceEvidence(
    string Url, DateTimeOffset CollectedAt, string Sha256, string Conditions,
    decimal? CreditsPerTask = null, decimal? UsdPerCredit = null);

public sealed record OfficialPriceCandidate(
    string Model, string Area, string Operation, string Conditions,
    OfficialPriceTerms? Terms, IReadOnlyList<OfficialPriceEvidence> Evidence, string? BlockedReason);

public sealed record OfficialPriceCollection(
    Guid ProviderConfigId, string Provider, string Status, string ModelListStatus,
    DateTimeOffset CollectedAt, IReadOnlyList<string> ModelIds,
    IReadOnlyList<OfficialPriceCandidate> Candidates, string? ModelError, string? PriceError);
