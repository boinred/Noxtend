using Noxtend.Infrastructure.Prices;
using Noxtend.Tests.Api;

namespace Noxtend.Tests.Infrastructure;

public sealed class OfficialTextPriceSourceTests
{
    [Theory]
    [InlineData("openai", "gpt-5.3-codex", 1.75, 14)]
    [InlineData("anthropic", "claude-sonnet-4-6", 3, 15)]
    [InlineData("anthropic", "claude-haiku-4-5", 1, 5)]
    [InlineData("google", "gemini-2.5-flash", 0.30, 2.5)]
    public void Raw_standard_prices_are_parsed(string provider, string model, decimal input, decimal output)
    {
        var candidate = Assert.Single(Parse(provider, model));
        Assert.Null(candidate.BlockedReason);
        Assert.Equal(input, candidate.Terms!.InputPerMillion);
        Assert.Equal(output, candidate.Terms.OutputPerMillion);
        Assert.Contains("Standard", candidate.Conditions);
        Assert.Matches("^[a-f0-9]{64}$", Assert.Single(candidate.Evidence).Sha256);
    }

    [Theory]
    [InlineData("openai", "gpt-5.3-codex")]
    [InlineData("anthropic", "claude-sonnet-4-6")]
    [InlineData("google", "gemini-2.5-flash")]
    public void Damaged_price_documents_are_blocked(string provider, string model)
    {
        foreach (var mutation in new[] { "structure-changed", "unit-missing" })
        {
            var candidate = Assert.Single(Parse(provider, model, mutation));
            Assert.Null(candidate.Terms);
            Assert.NotNull(candidate.BlockedReason);
        }
    }

    [Theory]
    [InlineData("openai", "gpt-5.3-codex")]
    [InlineData("anthropic", "claude-sonnet-4-6")]
    [InlineData("google", "gemini-2.5-flash")]
    public void Only_explicit_price_effective_sentence_sets_date(string provider, string model)
    {
        Assert.Null(Assert.Single(Parse(provider, model)).Terms!.OfficialEffectiveFrom);
        Assert.Equal(DateTimeOffset.Parse("2099-01-01T00:00:00Z"),
            Assert.Single(Parse(provider, model, "future")).Terms!.OfficialEffectiveFrom);
    }

    [Fact]
    public void Mixed_image_pricing_remains_blocked_with_dimensions()
    {
        var candidate = Assert.Single(Parse("google", "gemini-2.5-flash-image"));
        Assert.NotNull(candidate.BlockedReason);
        var conditions = Assert.Single(candidate.Evidence).Conditions;
        Assert.Contains("input=0.30 USD/1M text/image tokens", conditions);
        Assert.Contains("output=0.039 USD/image", conditions);
        Assert.Contains("size<=1024x1024", conditions);
    }

    [Fact]
    public void Openai_image_keeps_modality_prices_without_flattening()
    {
        var candidate = Assert.Single(Parse("openai", "gpt-image-2.5-sunburst"));
        Assert.Equal("image", candidate.Area);
        Assert.Null(candidate.Terms);
        Assert.NotNull(candidate.BlockedReason);
        var evidence = Assert.Single(candidate.Evidence).Conditions;
        Assert.Contains("Image", evidence);
        Assert.Contains("Text input", evidence);
        Assert.Contains("Cached input", evidence);
    }

    [Fact]
    public void Exact_mapping_does_not_price_unknown_or_date_alias()
    {
        foreach (var model in new[] { "fixture-unpriced-openai", "gpt-5.3-codex-20990101" })
        {
            var candidate = Assert.Single(Parse("openai", model));
            Assert.Null(candidate.Terms);
            Assert.NotNull(candidate.BlockedReason);
        }
    }

    private static IReadOnlyList<Noxtend.Tuning.Domain.Ports.OfficialPriceCandidate> Parse(
        string provider, string model, string? mutation = null)
    {
        var file = $"{provider}-pricing{(mutation is null ? "" : "-" + mutation)}.html";
        return OfficialTextPriceParser.Parse(provider,
            File.ReadAllBytes(Path.Combine(ModelPriceUpdateAcceptanceHost.FixtureRoot, file)),
            "https://official.example/pricing", DateTimeOffset.Parse("2026-10-09T00:00:00Z"), [model]);
    }
}
