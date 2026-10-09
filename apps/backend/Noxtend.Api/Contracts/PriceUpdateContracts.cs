using Noxtend.Tuning.Application.Prices;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Api.Contracts;

public sealed record PriceUpdatePreviewResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    IReadOnlyList<PriceUpdateProviderResult> Providers, IReadOnlyList<PriceUpdateCandidateResponse> Candidates)
{
    public static PriceUpdatePreviewResponse From(PriceUpdatePreview preview)
        => new(preview.Id, preview.CreatedAt, preview.ExpiresAt, preview.Providers,
            preview.Candidates.Select(c => new PriceUpdateCandidateResponse(c.Id, c.Provider, c.Model, c.Area,
                c.Operation, c.Conditions, c.ProviderConfigIds, c.CurrentTerms, c.Terms, c.Evidence,
                c.ChangeKind, c.ExecutionSupport, c.BlockedReason)).ToArray());
}

public sealed record PriceUpdateCandidateResponse(Guid Id, string Provider, string Model, string Area, string Operation,
    string Conditions, IReadOnlyList<Guid> ProviderConfigIds, OfficialPriceTerms? CurrentTerms, OfficialPriceTerms? Terms,
    IReadOnlyList<OfficialPriceEvidence> Evidence, string ChangeKind, string ExecutionSupport, string? BlockedReason);
