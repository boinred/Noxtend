using Noxtend.Tuning.Application.Prices;
using Noxtend.Tuning.Domain.Call;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Tests.Application;

public sealed class PriceCandidateComparisonTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
    private static readonly OfficialPriceTerms Terms = new(3, 15);

    [Theory]
    [InlineData("1.1234567", true)]
    [InlineData("1000000000000", true)]
    [InlineData("0.0000001", true)]
    [InlineData("999999999999.999999", false)]
    [InlineData("0.000001", false)]
    [InlineData("1.1234560", false)]
    [InlineData("0", false)]
    public void Collected_rates_require_exact_sql_representation_in_every_field(string value, bool blocked)
    {
        var rate = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        foreach (var terms in new[]
        {
            Terms with { InputPerMillion = rate }, Terms with { OutputPerMillion = rate },
            Terms with { LongContextFrom = 100, LongInputPerMillion = rate, LongOutputPerMillion = 20 },
            Terms with { LongContextFrom = 100, LongInputPerMillion = 4, LongOutputPerMillion = rate },
            Terms with { PerImage = rate },
        })
        {
            var source = Collection("known");
            source = source with { Candidates = [source.Candidates[0] with { Terms = terms }] };
            var candidate = Assert.Single(Compare([source], [Price("known", 2)]).Candidates);
            Assert.Equal(blocked, candidate.BlockedReason is not null);
            Assert.Equal(terms, candidate.Terms);
            Assert.Equal(2m, candidate.CurrentTerms!.InputPerMillion);
        }
    }

    [Fact]
    public void Identical_observations_merge_configs_and_evidence_once()
    {
        var first = Collection("claude-sonnet-4-6");
        var second = Collection("claude-sonnet-4-6");
        var result = Compare([first, second], []);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("newModel", candidate.ChangeKind);
        Assert.Equal(2, candidate.ProviderConfigIds.Count);
        Assert.Single(candidate.Evidence);
        Assert.All(result.Providers, p => Assert.Equal(candidate.Id, Assert.Single(p.CandidateIds)));
        Assert.Equal("unverified", candidate.ExecutionSupport);
        Assert.Null(candidate.CurrentTerms);
    }

    [Theory]
    [InlineData("terms")]
    [InlineData("date")]
    [InlineData("credits")]
    public void Conflicting_terms_effective_dates_or_credits_are_blocked(string dimension)
    {
        var first = Collection("claude-sonnet-4-6");
        var candidate = first.Candidates[0];
        var other = dimension switch
        {
            "terms" => candidate with { Terms = Terms with { InputPerMillion = 4 } },
            "date" => candidate with { Terms = Terms with { OfficialEffectiveFrom = At.AddDays(1) } },
            _ => candidate with { Evidence = [candidate.Evidence[0] with { CreditsPerTask = 50 }] },
        };
        var result = Compare([first, Collection("claude-sonnet-4-6") with { Candidates = [other] }], []);
        Assert.NotNull(Assert.Single(result.Candidates).BlockedReason);
        Assert.Equal("priceUnknown", result.Candidates[0].ChangeKind);
    }

    [Fact]
    public void Different_conditions_cannot_write_the_same_model_storage_key()
    {
        var first = Collection("claude-sonnet-4-6");
        var other = first.Candidates[0] with { Conditions = "Paid Standard; alternate size" };
        var result = Compare([first with { Candidates = [first.Candidates[0], other] }], []);
        Assert.Equal(2, result.Candidates.Count);
        Assert.All(result.Candidates, c => Assert.NotNull(c.BlockedReason));
    }

    [Theory]
    [InlineData(3, "unchanged")]
    [InlineData(2, "priceChanged")]
    public void Comparison_preserves_current_terms_and_classifies_prices(decimal input, string kind)
    {
        var price = Price("claude-sonnet-4-6", input);
        var result = Compare([Collection(price.Model)], [price]);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(kind, candidate.ChangeKind);
        Assert.Equal(input, candidate.CurrentTerms!.InputPerMillion);
    }

    [Theory]
    [InlineData("complete", true)]
    [InlineData("partial", false)]
    [InlineData("failed", false)]
    public void Missing_requires_every_config_complete_and_uses_union(string secondStatus, bool missingExpected)
    {
        var a = Collection("claude-sonnet-4-6");
        var b = Collection("claude-haiku-4-5") with { ModelListStatus = secondStatus, Status = secondStatus == "failed" ? "failed" : "success" };
        var prices = new[] { Price("claude-haiku-4-5", 3), Price("truly-absent", 3), Price("unclassified", 3, null) };
        var result = Compare([a, b], prices);
        Assert.DoesNotContain(result.Candidates, c => c.Model == "claude-haiku-4-5" && c.ChangeKind == "notInCatalog");
        Assert.Equal(missingExpected, result.Candidates.Any(c => c.Model == "truly-absent" && c.ChangeKind == "notInCatalog"));
        Assert.DoesNotContain(result.Candidates, c => c.Model == "unclassified");
        Assert.All(result.Candidates.Where(c => c.ChangeKind == "notInCatalog"), c =>
        {
            Assert.Null(c.Terms);
            Assert.NotNull(c.BlockedReason);
            Assert.Empty(c.ProviderConfigIds);
        });
        Assert.Equal(3, prices.Length);
    }

    [Fact]
    public void Alias_current_lookup_and_fingerprint_include_future_and_historical_related_rows()
    {
        var basePrice = Price("claude-sonnet-4-6", 3);
        var future = ModelPrice.CreateCollected("claude-sonnet-4-6-20990101", 4, 20, null, null, null,
            At.AddYears(20), "future", null, "anthropic", "[]");
        var collection = Collection("claude-sonnet-4-6-20990101");
        var candidate = Assert.Single(Compare([collection], [basePrice, future]).Candidates, c => c.Model == collection.ModelIds[0]);
        Assert.Equal(3, candidate.CurrentTerms!.InputPerMillion);
        future.Update(5, 20, null, null, null, future.EffectiveFrom, "future");
        var after = Assert.Single(Compare([collection], [basePrice, future]).Candidates, c => c.Model == collection.ModelIds[0]);
        Assert.NotEqual(candidate.BaselineFingerprint, after.BaselineFingerprint);
    }

    [Fact]
    public void Unknown_price_retains_current_value_and_does_not_claim_execution_support()
    {
        var source = Collection("unknown");
        source = source with { Candidates = [source.Candidates[0] with { Terms = null, BlockedReason = "확인 필요" }] };
        var candidate = Assert.Single(Compare([source], [Price("unknown", 3)]).Candidates);
        Assert.Equal("priceUnknown", candidate.ChangeKind);
        Assert.NotNull(candidate.CurrentTerms);
        Assert.Equal("unverified", candidate.ExecutionSupport);
    }

    [Fact]
    public void Execution_support_is_exact_and_independent_of_applicability()
    {
        var source = Collection("known");
        source = source with { Candidates = [source.Candidates[0] with { BlockedReason = "계정별 환산 지원 필요" }] };
        var candidate = Assert.Single(PriceCandidateComparison.Compare([source], [], At, [("anthropic", "known", "text")]).Candidates);
        Assert.Equal("supported", candidate.ExecutionSupport);
        Assert.NotNull(candidate.BlockedReason);
    }

    [Fact]
    public void Official_effective_date_is_part_of_comparison_even_when_numbers_are_equal()
    {
        var source = Collection("known");
        source = source with { Candidates = [source.Candidates[0] with { Terms = Terms with { OfficialEffectiveFrom = At.AddYears(20) } }] };
        Assert.Equal("priceChanged", Assert.Single(Compare([source], [Price("known", 3)]).Candidates).ChangeKind);
    }

    [Fact]
    public void Equivalent_condition_order_is_canonicalized()
    {
        var source = Collection("known");
        var reordered = source.Candidates[0] with { Conditions = "USD per 1M tokens; Paid   Standard" };
        var other = source with { ProviderConfigId = Guid.NewGuid(), Candidates = [reordered] };
        Assert.Single(Compare([source, other], []).Candidates);
    }

    [Fact]
    public void Collected_official_date_round_trips_for_unchanged_comparison()
    {
        var source = Collection("known");
        source = source with { Candidates = [source.Candidates[0] with { Terms = Terms with { OfficialEffectiveFrom = At.AddDays(-1) } }] };
        var price = ModelPrice.CreateCollected("known", 3, 15, null, null, null, At.AddDays(-1), "existing", null,
            "anthropic", "{\"officialEffectiveFrom\":\"2026-10-08T00:00:00Z\",\"evidence\":[]}");
        var candidate = Assert.Single(Compare([source], [price]).Candidates);
        Assert.Equal("unchanged", candidate.ChangeKind);
        Assert.Equal(At.AddDays(-1), candidate.CurrentTerms!.OfficialEffectiveFrom);
    }

    [Fact]
    public void Existing_other_provider_storage_key_is_blocked_and_fingerprinted()
    {
        var price = Price("known", 3, "openai");
        var source = Collection("known");
        var candidate = Assert.Single(Compare([source], [price]).Candidates);
        Assert.NotNull(candidate.BlockedReason);
        Assert.Null(candidate.CurrentTerms);
        price.Update(4, 15, null, null, null, price.EffectiveFrom, "updated");
        Assert.NotEqual(candidate.BaselineFingerprint, PriceCandidateComparison.Fingerprint("anthropic", "known", [price]));
    }

    private static PriceComparisonResult Compare(IReadOnlyList<OfficialPriceCollection> sources, IReadOnlyList<ModelPrice> prices)
        => PriceCandidateComparison.Compare(sources, prices, At, []);

    private static OfficialPriceCollection Collection(string model)
        => new(Guid.NewGuid(), "anthropic", "success", "complete", At, [model],
            [new(model, "text", "text", "Paid Standard; USD per 1M tokens", Terms,
                [new("https://official.example/pricing", At, new string('a', 64), "Base tokens")], null)], null, null);

    private static ModelPrice Price(string model, decimal input, string? provider = "anthropic")
        => ModelPrice.Create(model, input, 15, null, null, null, At.AddDays(-1), "existing", provider: provider);
}
