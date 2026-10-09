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
        if (provider == "google") Assert.NotNull(candidate.BlockedReason);
        else Assert.Null(candidate.BlockedReason);
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
    public void Different_input_modality_rates_are_blocked_without_losing_display_terms()
    {
        var candidate = Assert.Single(Parse("google", "gemini-2.5-flash"));
        Assert.NotNull(candidate.BlockedReason);
        Assert.Equal(0.30m, candidate.Terms!.InputPerMillion);
        Assert.Contains("audio", Assert.Single(candidate.Evidence).Conditions);
        Assert.DoesNotContain("excludes audio", candidate.Conditions);
    }

    [Theory]
    [InlineData("$0.4 (text / image / video)<br>$0.4 (audio)", false)]
    [InlineData("$0.4 (text / image / video)<br>$0.8 (audio)", true)]
    [InlineData("$0.4", false)]
    public void Input_modality_policy_depends_on_rates_not_model_name(string input, bool blocked)
    {
        var html = $"""
            <h2 id="synthetic-google-model">Synthetic input dimensions</h2><section><h3>Standard</h3>
            <table><thead><tr><th>Price</th><th>Paid Tier, per 1M tokens in USD</th></tr></thead>
            <tbody><tr><td>Input price</td><td>{input}</td></tr>
            <tr><td>Output price</td><td>$2</td></tr></tbody></table></section>
            """;
        var candidate = Assert.Single(OfficialTextPriceParser.Parse("google", System.Text.Encoding.UTF8.GetBytes(html),
            "https://official.example/pricing", DateTimeOffset.UtcNow, ["synthetic-google-model"]));
        Assert.Equal(blocked, candidate.BlockedReason is not null);
        Assert.Equal(0.4m, candidate.Terms!.InputPerMillion);
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

    [Theory]
    [InlineData("leaf-swapped")]
    [InlineData("group-swapped")]
    [InlineData("wrong-span")]
    public void Anthropic_changed_header_structure_is_blocked(string mutation)
    {
        var groups = mutation == "group-swapped"
            ? "<th>Model</th><th colspan=\"3\">Prompt caching</th><th colspan=\"2\">Base tokens</th>"
            : $"<th>Model</th><th colspan=\"{(mutation == "wrong-span" ? 3 : 2)}\">Base tokens</th><th colspan=\"3\">Prompt caching</th>";
        var leaves = mutation == "leaf-swapped"
            ? "<th>Name</th><th>Output</th><th>Input</th>"
            : "<th>Name</th><th>Input</th><th>Output</th>";
        var prices = mutation == "leaf-swapped"
            ? "<td>$15 / MTok</td><td>$3 / MTok</td>"
            : "<td>$3 / MTok</td><td>$15 / MTok</td>";
        var html = $"""
            <h2>Model pricing</h2><table><thead><tr>{groups}</tr>
            <tr>{leaves}<th>5m writes</th><th>1h writes</th><th>Hits and refreshes</th></tr></thead>
            <tbody><tr><td><a href="/docs/en/models/sonnet-4-6/overview">Claude Sonnet 4.6</a></td>
            {prices}<td>$3.75 / MTok</td><td>$6 / MTok</td><td>$0.30 / MTok</td></tr></tbody></table>
            """;
        var candidate = Assert.Single(OfficialTextPriceParser.Parse("anthropic", System.Text.Encoding.UTF8.GetBytes(html),
            "https://official.example/pricing", DateTimeOffset.UtcNow, ["claude-sonnet-4-6"]));
        Assert.Null(candidate.Terms);
        Assert.NotNull(candidate.BlockedReason);
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
