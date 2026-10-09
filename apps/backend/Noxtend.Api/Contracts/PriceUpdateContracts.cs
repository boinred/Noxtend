using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Noxtend.Domain.Common;
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

[AttributeUsage(AttributeTargets.Method)]
public sealed class PriceUpdateInputAttribute : ActionFilterAttribute
{
    public PriceUpdateInputAttribute()
    {
        // ApiController 기본 model-state 검사보다 앞선 오류 봉투
        Order = -2001;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
            context.Result = ApiResults.Failure<object>(ErrorCode.PriceUpdateInvalid, "단가 업데이트 입력이 유효하지 않습니다");
    }
}
