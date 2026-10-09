using Noxtend.Infrastructure.Prices;
using Noxtend.Tests.Api;

namespace Noxtend.Tests.Infrastructure;

public sealed class OfficialTextPriceSourceTests
{
    [Theory]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("google")]
    public void Every_standard_match_is_checked_independent_of_order(string provider)
    {
        foreach (var rates in new[] { new[] { "1.75", "9.25" }, new[] { "9.25", "1.75" }, new[] { "1.75", "1.75" } })
        {
            var html = string.Concat(rates.Select(rate => StandardTable(provider, rate)));
            var candidate = Assert.Single(OfficialTextPriceParser.Parse(provider, System.Text.Encoding.UTF8.GetBytes(html),
                "https://official.example/pricing", DateTimeOffset.UtcNow, [provider == "anthropic" ? "claude-synthetic" : "synthetic-model"]));
            if (rates[0] != rates[1])
            {
                Assert.Null(candidate.Terms);
                Assert.NotNull(candidate.BlockedReason);
                Assert.Equal(2, candidate.Evidence.Count);
                Assert.Contains(candidate.Evidence, e => e.Conditions.Contains("1.75", StringComparison.Ordinal));
                Assert.Contains(candidate.Evidence, e => e.Conditions.Contains("9.25", StringComparison.Ordinal));
            }
            else
            {
                Assert.Equal(1.75m, candidate.Terms!.InputPerMillion);
                Assert.Null(candidate.BlockedReason);
            }
        }
    }

    private static string StandardTable(string provider, string rate) => provider switch
    {
        "openai" => $"""
            <h2>Flagship models</h2><div><span class="pricing-switcher-meta">Prices per 1M tokens</span></div>
            <div data-content-switcher-root><div data-content-switcher-pane data-value="standard">
            <table><thead><tr><th>Model</th><th>Input</th><th>Output</th></tr></thead>
            <tbody><tr><td>synthetic-model</td><td>${rate}</td><td>$14</td></tr></tbody></table></div></div>
            """,
        "anthropic" => $"""
            <h2>Model pricing</h2><table><thead><tr><th>Model</th><th colspan="2">Base tokens</th><th colspan="3">Prompt caching</th></tr>
            <tr><th>Name</th><th>Input</th><th>Output</th><th>5m writes</th><th>1h writes</th><th>Hits and refreshes</th></tr></thead>
            <tbody><tr><td><a href="/docs/en/models/synthetic/overview">Claude synthetic</a></td><td>${rate} / MTok</td><td>$14 / MTok</td><td>$2</td><td>$3</td><td>$4</td></tr></tbody></table>
            """,
        _ => $"""
            <h2 id="synthetic-model">Synthetic model</h2><section><h3>Standard</h3>
            <table><thead><tr><th>Price</th><th>Paid Tier, per 1M tokens in USD</th></tr></thead>
            <tbody><tr><td>Input price</td><td>${rate}</td></tr><tr><td>Output price</td><td>$14</td></tr></tbody></table></section>
            """,
    };

    [Theory]
    [InlineData("openai")]
    [InlineData("anthropic")]
    public void Conflicting_rows_in_the_same_standard_table_keep_both_evidence(string provider)
    {
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(StandardTable(provider, "1.75"));
        var body = document.QuerySelector("tbody")!;
        var row = (AngleSharp.Dom.IElement)body.FirstElementChild!.Clone(true);
        row.Children[1].TextContent = provider == "anthropic" ? "$9.25 / MTok" : "$9.25";
        body.AppendChild(row);
        var candidate = Assert.Single(OfficialTextPriceParser.Parse(provider, System.Text.Encoding.UTF8.GetBytes(document.DocumentElement!.OuterHtml),
            "https://official.example/pricing", DateTimeOffset.UtcNow, [provider == "anthropic" ? "claude-synthetic" : "synthetic-model"]));
        Assert.Null(candidate.Terms);
        Assert.NotNull(candidate.BlockedReason);
        Assert.Equal(2, candidate.Evidence.Count);
    }

    [Theory]
    [InlineData("Input price")]
    [InlineData("Output price")]
    public void Google_duplicate_price_labels_cannot_overwrite_an_earlier_rate(string label)
    {
        var html = StandardTable("google", "1.75").Replace("</tbody>", $"<tr><td>{label}</td><td>$9.25</td></tr></tbody>");
        var candidate = Assert.Single(OfficialTextPriceParser.Parse("google", System.Text.Encoding.UTF8.GetBytes(html),
            "https://official.example/pricing", DateTimeOffset.UtcNow, ["synthetic-model"]));
        Assert.Null(candidate.Terms);
        Assert.NotNull(candidate.BlockedReason);
        Assert.Contains("9.25", Assert.Single(candidate.Evidence).Conditions);
    }

    [Fact]
    public void Different_actual_modality_conditions_remain_separate_candidates()
    {
        var simple = StandardTable("google", "1.75");
        var audio = simple.Replace("$1.75</td>", "$1.75 (text / image / video)<br>$1.75 (audio)</td>");
        var candidates = OfficialTextPriceParser.Parse("google", System.Text.Encoding.UTF8.GetBytes(simple + audio),
            "https://official.example/pricing", DateTimeOffset.UtcNow, ["synthetic-model"]);
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.NotNull(candidate.Terms));
        Assert.Equal(2, candidates.Select(candidate => candidate.Conditions).Distinct().Count());
    }

    [Fact]
    public void Conflicting_official_dates_block_prices_and_keep_date_evidence()
    {
        var html = StandardTable("openai", "1.75") + """
            <p>All prices on this page become effective from <time datetime="2099-01-01T00:00:00Z">2099-01-01</time></p>
            <p>All prices on this page become effective from <time datetime="2099-02-01T00:00:00Z">2099-02-01</time></p>
            """;
        var candidate = Assert.Single(OfficialTextPriceParser.Parse("openai", System.Text.Encoding.UTF8.GetBytes(html),
            "https://official.example/pricing", DateTimeOffset.UtcNow, ["synthetic-model"]));
        Assert.Null(candidate.Terms);
        Assert.NotNull(candidate.BlockedReason);
        Assert.Contains(candidate.Evidence, e => e.Conditions.Contains("2099-01-01", StringComparison.Ordinal));
        Assert.Contains(candidate.Evidence, e => e.Conditions.Contains("2099-02-01", StringComparison.Ordinal));
    }

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
