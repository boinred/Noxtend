using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Noxtend.Tests.Infrastructure;

namespace Noxtend.Tests.Api;

[Collection(SqlServerCollection.Name)]
public sealed class ModelPriceUpdateAcceptanceTests(SqlServerFixture sql)
{
    private ModelPriceUpdateAcceptanceHost Host(string scenario = "normal", string? provider = null)
        => new(sql.FreshDatabase(nameof(ModelPriceUpdateAcceptanceTests)))
        {
            Scenario = scenario,
            ScenarioProvider = provider,
        };

    [Fact]
    public void RecordedFixturesMatchSha256Manifest()
    {
        var root = ModelPriceUpdateAcceptanceHost.FixtureRoot;
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")))!;
        foreach (var source in manifest["sources"]!.AsArray())
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, source!["file"]!.GetValue<string>()));
            Assert.Equal(source["sha256"]!.GetValue<string>(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
    }

    [Fact]
    public async Task ExistingCrudUsesDisposableSqlDatabase()
    {
        await using var host = Host();
        using var client = host.CreateClient();
        await ClearPrices(client);
        var price = await Seed(client, "acceptance-crud", "openai");
        Assert.Equal("acceptance-crud", price["model"]!.GetValue<string>());
        Assert.Single(await Prices(client));
        Assert.Empty(host.OutboundPaths);
        using var outbound = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
        host.Scenario = "union-normal";
        var listA = await SyntheticModels(outbound, "acceptance-key-union-a", false, HttpStatusCode.OK);
        var listB = await SyntheticModels(outbound, "acceptance-key-union-b", false, HttpStatusCode.OK);
        Assert.Equal("claude-sonnet-4-6", listA["data"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("claude-haiku-4-5", listB["data"]![0]!["id"]!.GetValue<string>());
        Assert.False(listA["has_more"]!.GetValue<bool>());
        Assert.False(listB["has_more"]!.GetValue<bool>());
        host.Scenario = "union-models-denied";
        await SyntheticModels(outbound, "acceptance-key-union-a", false, HttpStatusCode.OK);
        await SyntheticModels(outbound, "acceptance-key-union-b", false, HttpStatusCode.Unauthorized);
        host.Scenario = "union-models-partial";
        await SyntheticModels(outbound, "acceptance-key-union-a", false, HttpStatusCode.OK);
        var partial = await SyntheticModels(outbound, "acceptance-key-union-b", false, HttpStatusCode.OK);
        Assert.True(partial["has_more"]!.GetValue<bool>());
        await SyntheticModels(outbound, "acceptance-key-union-b", true, HttpStatusCode.Unauthorized);
    }

    private static async Task<JsonNode> SyntheticModels(HttpClient outbound, string key, bool nextPage, HttpStatusCode status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models" + (nextPage ? "?after_id=claude-haiku-4-5" : ""));
        request.Headers.Add("x-api-key", key);
        using var response = await outbound.SendAsync(request);
        Assert.Equal(status, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    [Theory]
    [InlineData("openai", "gpt-5.3-codex", 1.75, 14.0)]
    [InlineData("anthropic", "claude-sonnet-4-6", 3.0, 15.0)]
    [InlineData("google", "gemini-2.5-flash", 0.30, 2.50)]
    [InlineData("tripo", "P1-20260311", null, null)]
    [InlineData("meshy", "meshy-6", null, null)]
    public async Task OfficialRawSourcesProduceExactCandidates(string provider, string model, double? input, double? output)
    {
        await using var host = Host();
        using var client = host.CreateClient();
        await ClearPrices(client);
        var config = await Provider(client, provider);
        var before = (await Prices(client)).ToJsonString();
        var preview = await Preview(client, [config]);
        var candidate = Candidate(preview, provider, model);
        Assert.Equal(before, (await Prices(client)).ToJsonString());
        Assert.NotEmpty(host.OutboundPaths);
        Assert.NotEmpty(candidate["evidence"]!.AsArray());
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ModelPriceUpdateAcceptanceHost.FixtureRoot, "manifest.json")))!;
        var sourceFile = provider == "tripo" ? "tripo-p1.html" : $"{provider}-pricing.html";
        var hash = manifest["sources"]!.AsArray().Single(item => item!["file"]!.GetValue<string>() == sourceFile)!["sha256"]!.GetValue<string>();
        Assert.Contains(candidate["evidence"]!.AsArray(), evidence => evidence!["sha256"]!.GetValue<string>() == hash);
        foreach (var evidence in candidate["evidence"]!.AsArray())
        {
            Assert.True(Uri.TryCreate(evidence!["url"]!.GetValue<string>(), UriKind.Absolute, out _));
            Assert.Equal(64, evidence["sha256"]!.GetValue<string>().Length);
            Assert.NotNull(evidence["collectedAt"]);
        }
        if (input.HasValue)
        {
            Assert.Equal((decimal)input.Value, candidate["terms"]!["inputPerMillion"]!.GetValue<decimal>());
            Assert.Equal((decimal)output!.Value, candidate["terms"]!["outputPerMillion"]!.GetValue<decimal>());
            Assert.Contains("standard", candidate["conditions"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("batch", candidate["conditions"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        }
        if (provider is "google" or "meshy") Assert.NotNull(candidate["blockedReason"]);
        if (provider == "meshy")
        {
            Assert.Equal("mesh", candidate["area"]!.GetValue<string>());
            Assert.Equal("multi-image-to-3d", candidate["operation"]!.GetValue<string>());
            Assert.Contains("texture_image_resolution=2048", candidate["conditions"]!.GetValue<string>());
            Assert.Contains("geometry_resolution=standard", candidate["conditions"]!.GetValue<string>());
            Assert.Contains(candidate["evidence"]!.AsArray(), item => item!["creditsPerTask"]?.GetValue<decimal>() == 30m && item["usdPerCredit"] is null);
            Assert.All(candidate["evidence"]!.AsArray(), item => Assert.Null(item!["usdPerCredit"]));
            Assert.Contains("계정별 환산 지원 필요", candidate["blockedReason"]!.GetValue<string>());
            Assert.Null(candidate["terms"]?["perImage"]);
            await Apply(client, preview, candidate, HttpStatusCode.BadRequest, "PriceUpdateInvalid");
            Assert.Empty(await Prices(client));
        }
        if (provider == "google")
        {
            var image = Candidate(preview, "google", "gemini-2.5-flash-image");
            Assert.Equal("image", image["area"]!.GetValue<string>());
            Assert.NotNull(image["blockedReason"]);
            if (image["terms"]?["perImage"] is { } outputPrice)
                Assert.Equal(0.039m, outputPrice.GetValue<decimal>());
            Assert.Contains("standard", image["conditions"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
            var evidenceText = string.Join("; ", image["evidence"]!.AsArray().Select(e => e!["conditions"]!.GetValue<string>()));
            foreach (var token in new[] { "input=0.30 USD/1M text/image tokens", "output=0.039 USD/image", "size<=1024x1024" })
                Assert.Contains(token, evidenceText);
            await Apply(client, preview, image, HttpStatusCode.BadRequest, "PriceUpdateInvalid");
            Assert.Empty(await Prices(client));
        }
        if (provider == "tripo")
        {
            Assert.Equal("mesh", candidate["area"]!.GetValue<string>());
            Assert.Equal("multiview-to-3d", candidate["operation"]!.GetValue<string>());
            Assert.Contains("texture_quality=standard", candidate["conditions"]!.GetValue<string>());
            Assert.Null(candidate["blockedReason"]);
            Assert.Equal(0.5m, candidate["terms"]!["perImage"]!.GetValue<decimal>());
            Assert.Contains(candidate["evidence"]!.AsArray(), e => e!["creditsPerTask"]?.GetValue<decimal>() == 50m && e["usdPerCredit"]?.GetValue<decimal>() == 0.01m);
            await Apply(client, preview, candidate, HttpStatusCode.OK);
            var saved = Assert.Single(await Prices(client))!;
            Assert.Equal("P1-20260311", saved["model"]!.GetValue<string>());
            Assert.Equal("tripo", saved["provider"]!.GetValue<string>());
            Assert.Equal(0.5m, saved["perImage"]!.GetValue<decimal>());
            Assert.False(saved["allowHistoricalFallback"]!.GetValue<bool>());
        }
        if (provider == "openai")
        {
            var unknown = Candidate(preview, "openai", "fixture-unpriced-openai");
            Assert.Equal("priceUnknown", unknown["changeKind"]!.GetValue<string>());
            Assert.NotNull(unknown["blockedReason"]);
            Assert.NotNull(Candidate(preview, "openai", "gpt-image-2.5-sunburst")["blockedReason"]);
        }
        Assert.DoesNotContain("acceptance-key", preview.ToJsonString());
    }

    [Fact]
    public async Task SelectionIsAtomicAndSuccessfulReplaySurvivesExpiry()
    {
        await using var host = Host();
        using var client = host.CreateClient();
        await ClearPrices(client);
        await Seed(client, "gpt-5.3-codex", "openai");
        var preview = await Preview(client, [await Provider(client, "openai")]);
        var candidate = Candidate(preview, "openai", "gpt-5.3-codex");
        Assert.Equal("priceChanged", candidate["changeKind"]!.GetValue<string>());
        Assert.NotNull(candidate["currentTerms"]);
        Assert.Null(candidate["blockedReason"]);
        var request = new { requestId = Guid.NewGuid(), candidateIds = new[] { candidate["id"]!.GetValue<string>() }, effectiveFrom = (string?)null };
        var receipt = await Send(client, HttpMethod.Post, ApplyUrl(preview), request, HttpStatusCode.OK);
        var prices = await Prices(client);
        Assert.Equal(2, prices.Count);
        var added = prices.Single(row => row!["inputPerMillion"]!.GetValue<decimal>() == 1.75m)!;
        Assert.Equal("openai", added["provider"]!.GetValue<string>());
        Assert.False(added["allowHistoricalFallback"]!.GetValue<bool>());
        Assert.NotNull(added["sourceEvidenceJson"]);
        var at = DateTimeOffset.Parse(receipt["appliedAt"]!.GetValue<string>());
        Assert.True(DateTimeOffset.Parse(added["effectiveFrom"]!.GetValue<string>()) >= at);
        host.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(receipt.ToJsonString(), (await Send(client, HttpMethod.Post, ApplyUrl(preview), request, HttpStatusCode.OK)).ToJsonString());
        Assert.Equal(2, (await Prices(client)).Count);
        await Send(client, HttpMethod.Post, ApplyUrl(preview), new { request.requestId, candidateIds = Array.Empty<string>(), effectiveFrom = (string?)null }, HttpStatusCode.Conflict, "PriceUpdateRequestConflict");
    }

    [Fact]
    public async Task UnknownOrBlockedSelectionRejectsWholeBundle()
    {
        await using var host = Host();
        using var client = host.CreateClient();
        await ClearPrices(client);
        var preview = await Preview(client, [await Provider(client, "openai")]);
        var good = Candidate(preview, "openai", "gpt-5.3-codex")["id"]!.GetValue<string>();
        var blocked = Candidate(preview, "openai", "fixture-unpriced-openai")["id"]!.GetValue<string>();
        await Send(client, HttpMethod.Post, ApplyUrl(preview), new { requestId = Guid.NewGuid(), candidateIds = new[] { good, blocked }, effectiveFrom = (string?)null }, HttpStatusCode.BadRequest, "PriceUpdateInvalid");
        await Send(client, HttpMethod.Post, ApplyUrl(preview), new { requestId = Guid.NewGuid(), candidateIds = new[] { good, good }, effectiveFrom = (string?)null }, HttpStatusCode.BadRequest, "PriceUpdateInvalid");
        await Send(client, HttpMethod.Post, ApplyUrl(preview), new { requestId = Guid.NewGuid(), candidateIds = new[] { good, Guid.NewGuid().ToString() }, effectiveFrom = (string?)null }, HttpStatusCode.BadRequest, "PriceUpdateInvalid");
        Assert.Empty(await Prices(client));
    }

    [Fact]
    public async Task ConcurrentAppliesStoreOnlyOneAtomicResult()
    {
        await using var host = Host();
        using var client = host.CreateClient();
        await ClearPrices(client);
        var preview = await Preview(client, [await Provider(client, "openai")]);
        var candidateId = Candidate(preview, "openai", "gpt-5.3-codex")["id"]!.GetValue<string>();
        var calls = Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(ApplyUrl(preview), new
        {
            requestId = Guid.NewGuid(), candidateIds = new[] { candidateId }, effectiveFrom = (string?)null,
        }));
        var responses = await Task.WhenAll(calls);
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Single(await Prices(client));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task CurrentPriceChangedAfterPreviewReturnsConflictWithoutWrites()
    {
        await using var host = Host();
        using var client = host.CreateClient();
        await ClearPrices(client);
        var initial = await Seed(client, "gpt-5.3-codex", "openai");
        var preview = await Preview(client, [await Provider(client, "openai")]);
        await Send(client, HttpMethod.Put, $"/api/prices/{initial["id"]}", PriceInput("gpt-5.3-codex", "openai", 0.9m), HttpStatusCode.OK);
        await Apply(client, preview, Candidate(preview, "openai", "gpt-5.3-codex"), HttpStatusCode.Conflict, "PriceUpdateConflict");
        Assert.Single(await Prices(client));
    }

    [Fact]
    public async Task ExpiredUnappliedPreviewIsRejected()
    {
        await using var host = Host();
        using var client = host.CreateClient();
        var preview = await Preview(client, [await Provider(client, "openai")]);
        Assert.Equal(TimeSpan.FromMinutes(30), DateTimeOffset.Parse(preview["expiresAt"]!.GetValue<string>()) - DateTimeOffset.Parse(preview["createdAt"]!.GetValue<string>()));
        var before = (await Prices(client)).ToJsonString();
        host.Clock.Advance(TimeSpan.FromMinutes(31));
        await Apply(client, preview, Candidate(preview, "openai", "gpt-5.3-codex"), HttpStatusCode.Conflict, "PriceUpdateExpired");
        Assert.Equal(before, (await Prices(client)).ToJsonString());
    }

    [Fact]
    public async Task ConfigDuplicatesMergeCandidatesAndPaginatedCatalogsAreComplete()
    {
        await using var host = Host();
        using var client = host.CreateClient();
        await ClearPrices(client);
        var a = await Provider(client, "anthropic");
        var b = await Provider(client, "anthropic");
        var preview = await Preview(client, [a, a, b]);
        Assert.Equal(2, preview["providers"]!.AsArray().Count);
        var candidate = Candidate(preview, "anthropic", "claude-sonnet-4-6");
        Assert.Equal(2, candidate["providerConfigIds"]!.AsArray().Count);
        var groups = preview["candidates"]!.AsArray().GroupBy(c => $"{c!["provider"]}|{c["model"]}|{c["area"]}|{c["operation"]}|{c["conditions"]}");
        Assert.All(groups, group => Assert.Single(group));
        Assert.NotNull(Candidate(preview, "anthropic", "claude-haiku-4-5"));
        Assert.All(preview["providers"]!.AsArray(), p => Assert.Equal("complete", p!["modelListStatus"]!.GetValue<string>()));
        await Apply(client, preview, candidate, HttpStatusCode.OK);
        Assert.Single(await Prices(client));
    }

    [Theory]
    [InlineData("normal", true)]
    [InlineData("models-partial", false)]
    [InlineData("models-denied", false)]
    public async Task MissingCatalogDecisionRequiresCompleteUnion(string scenario, bool expectedMissing)
    {
        await using var host = Host("union-" + scenario, "anthropic");
        using var client = host.CreateClient();
        await ClearPrices(client);
        await Seed(client, "claude-haiku-4-5", "anthropic");
        await Seed(client, "acceptance-not-in-catalog", "anthropic");
        var a = await Provider(client, "anthropic", "acceptance-key-union-a");
        var b = await Provider(client, "anthropic", "acceptance-key-union-b");
        var existing = (await Prices(client)).ToJsonString();
        var configs = await Send(client, HttpMethod.Get, "/api/providers", null, HttpStatusCode.OK);
        var preview = await Preview(client, [a, b]);
        Assert.Equal(existing, (await Prices(client)).ToJsonString());
        Assert.Equal(configs.ToJsonString(), (await Send(client, HttpMethod.Get, "/api/providers", null, HttpStatusCode.OK)).ToJsonString());
        Assert.Equal(new[] { a, b }.Order(), preview["providers"]!.AsArray().Select(c => c!["providerConfigId"]!.GetValue<string>()).Order());
        var observedA = Assert.Single(preview["providers"]!.AsArray(), c => c!["providerConfigId"]!.GetValue<string>() == a)!;
        var observedB = Assert.Single(preview["providers"]!.AsArray(), c => c!["providerConfigId"]!.GetValue<string>() == b)!;
        Assert.Equal("complete", observedA["modelListStatus"]!.GetValue<string>());
        Assert.Equal(scenario == "normal" ? "complete" : scenario == "models-partial" ? "partial" : "failed", observedB["modelListStatus"]!.GetValue<string>());
        var sonnet = Candidate(preview, "anthropic", "claude-sonnet-4-6");
        Assert.Equal(new[] { a }, sonnet["providerConfigIds"]!.AsArray().Select(id => id!.GetValue<string>()));
        Assert.Contains(observedA["candidateIds"]!.AsArray(), id => id!.GetValue<string>() == sonnet["id"]!.GetValue<string>());
        Assert.DoesNotContain(preview["candidates"]!.AsArray(), c => c!["model"]!.GetValue<string>() == "claude-haiku-4-5" && c["changeKind"]!.GetValue<string>() == "notInCatalog");
        if (scenario == "normal")
        {
            var haiku = Candidate(preview, "anthropic", "claude-haiku-4-5");
            Assert.Equal(new[] { b }, haiku["providerConfigIds"]!.AsArray().Select(id => id!.GetValue<string>()));
            Assert.Contains(observedB["candidateIds"]!.AsArray(), id => id!.GetValue<string>() == haiku["id"]!.GetValue<string>());
        }
        var missing = preview["candidates"]!.AsArray().Where(c => c!["model"]!.GetValue<string>() == "acceptance-not-in-catalog").ToArray();
        if (expectedMissing)
        {
            var candidate = Assert.Single(missing)!;
            Assert.Equal("notInCatalog", candidate["changeKind"]!.GetValue<string>());
            Assert.NotNull(candidate["blockedReason"]);
        }
        else Assert.DoesNotContain(missing, c => c!["changeKind"]!.GetValue<string>() == "notInCatalog");
        Assert.DoesNotContain("acceptance-key", preview.ToJsonString());
    }

    [Fact]
    public async Task ProviderFailureKeepsSiblingSuccessAndSeparatesModelAndPriceErrors()
    {
        await using var host = Host("models-denied", "anthropic");
        using var client = host.CreateClient();
        var preview = await Preview(client, [await Provider(client, "openai"), await Provider(client, "anthropic")]);
        var failed = preview["providers"]!.AsArray().Single(p => p!["provider"]!.GetValue<string>() == "anthropic")!;
        Assert.NotEqual("success", failed["status"]!.GetValue<string>());
        Assert.Equal("failed", failed["modelListStatus"]!.GetValue<string>());
        Assert.NotNull(failed["modelError"]);
        Assert.Null(failed["priceError"]);
        Assert.NotNull(Candidate(preview, "openai", "gpt-5.3-codex"));
    }

    [Theory]
    [InlineData("openai", "structure-changed")]
    [InlineData("anthropic", "structure-changed")]
    [InlineData("google", "structure-changed")]
    [InlineData("tripo", "structure-changed")]
    [InlineData("meshy", "structure-changed")]
    [InlineData("openai", "unit-missing")]
    [InlineData("anthropic", "unit-missing")]
    [InlineData("google", "unit-missing")]
    [InlineData("tripo", "unit-missing")]
    [InlineData("meshy", "unit-missing")]
    public async Task UnreadableOfficialPricesNeverBecomeApplicable(string provider, string scenario)
    {
        await using var host = Host(scenario, provider);
        using var client = host.CreateClient();
        var before = (await Prices(client)).ToJsonString();
        var preview = await Preview(client, [await Provider(client, provider)]);
        Assert.All(preview["candidates"]!.AsArray(), c => Assert.NotNull(c!["blockedReason"]));
        Assert.NotEqual("success", preview["providers"]![0]!["status"]!.GetValue<string>());
        Assert.Equal(before, (await Prices(client)).ToJsonString());
    }

    [Fact]
    public async Task FutureOfficialPricesCannotBeAppliedEarlyAndDoNotFallBackHistorically()
    {
        await using var host = Host("future", "anthropic");
        using var client = host.CreateClient();
        await ClearPrices(client);
        var preview = await Preview(client, [await Provider(client, "anthropic")]);
        var candidate = Candidate(preview, "anthropic", "claude-sonnet-4-6");
        Assert.Equal("2099-01-01T00:00:00+00:00", DateTimeOffset.Parse(candidate["terms"]!["officialEffectiveFrom"]!.GetValue<string>()).ToString("yyyy-MM-ddTHH:mm:sszzz"));
        await Send(client, HttpMethod.Post, ApplyUrl(preview), new { requestId = Guid.NewGuid(), candidateIds = new[] { candidate["id"]!.GetValue<string>() }, effectiveFrom = "2026-10-10T00:00:00Z" }, HttpStatusCode.BadRequest, "PriceUpdateInvalid");
        Assert.Empty(await Prices(client));
        await Apply(client, preview, candidate, HttpStatusCode.OK);
        var price = Assert.Single(await Prices(client))!;
        Assert.True(DateTimeOffset.Parse(price["effectiveFrom"]!.GetValue<string>()) >= new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.False(price["allowHistoricalFallback"]!.GetValue<bool>());
    }

    [Fact]
    public async Task EmptyUnknownAndDisabledConfigsAreInvalidAndUnknownPreviewIs404()
    {
        await using var host = Host();
        using var client = host.CreateClient();
        await Send(client, HttpMethod.Post, "/api/prices/update-previews", new { providerConfigIds = Array.Empty<string>() }, HttpStatusCode.BadRequest, "PriceUpdateInvalid");
        await Send(client, HttpMethod.Post, "/api/prices/update-previews", new { providerConfigIds = new[] { Guid.NewGuid().ToString() } }, HttpStatusCode.BadRequest, "PriceUpdateInvalid");
        var manyConfigs = new List<string>();
        for (var i = 0; i < 26; i++) manyConfigs.Add(await Provider(client, "openai"));
        await Send(client, HttpMethod.Post, "/api/prices/update-previews", new { providerConfigIds = manyConfigs }, HttpStatusCode.BadRequest, "PriceUpdateInvalid");
        var disabled = manyConfigs[0];
        await Send(client, HttpMethod.Put, $"/api/providers/{disabled}", new { kind = "openai", displayName = "Disabled acceptance", isEnabled = false }, HttpStatusCode.OK);
        await Send(client, HttpMethod.Post, "/api/prices/update-previews", new { providerConfigIds = new[] { disabled } }, HttpStatusCode.BadRequest, "PriceUpdateInvalid");
        await Send(client, HttpMethod.Post, $"/api/prices/update-previews/{Guid.NewGuid()}/apply", new { requestId = Guid.NewGuid(), candidateIds = new[] { Guid.NewGuid() }, effectiveFrom = (string?)null }, HttpStatusCode.NotFound, "PriceUpdateNotFound");
    }

    private static JsonNode Candidate(JsonNode preview, string provider, string model)
    {
        var matches = preview["candidates"]!.AsArray().Where(c => c!["provider"]!.GetValue<string>() == provider && c["model"]!.GetValue<string>() == model).ToArray();
        Assert.NotEmpty(matches);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ModelPriceUpdateAcceptanceHost.FixtureRoot, "manifest.json")))!;
        var expected = manifest["expectedCandidates"]!.AsArray().FirstOrDefault(c => c!["provider"]!.GetValue<string>() == provider && c["model"]!.GetValue<string>() == model);
        if (provider == "google" && model == "gemini-2.5-flash-image")
            return Assert.Single(matches, c => c!["area"]!.GetValue<string>() == "image" &&
                c["conditions"]!.GetValue<string>().Contains("standard", StringComparison.OrdinalIgnoreCase))!;
        if (expected?["requiredConditionTokens"] is { } conditionTokens)
            return Assert.Single(matches, c => c!["area"]!.GetValue<string>() == expected["area"]!.GetValue<string>() &&
                c["operation"]!.GetValue<string>() == expected["operation"]!.GetValue<string>() &&
                conditionTokens.AsArray().All(token => c["conditions"]!.GetValue<string>().Contains(token!.GetValue<string>())))!;
        if (expected?["inputPerMillion"] is { } input)
            return Assert.Single(matches, c => c!["terms"]?["inputPerMillion"]?.GetValue<decimal>() == input.GetValue<decimal>() &&
                c["terms"]?["outputPerMillion"]?.GetValue<decimal>() == expected["outputPerMillion"]!.GetValue<decimal>() &&
                c["conditions"]!.GetValue<string>().Contains("standard", StringComparison.OrdinalIgnoreCase))!;
        if (expected?["perImage"] is { } perImage && provider != "tripo")
            return Assert.Single(matches, c => c!["terms"]?["perImage"]?.GetValue<decimal>() == perImage.GetValue<decimal>() &&
                c["conditions"]!.GetValue<string>().Contains("standard", StringComparison.OrdinalIgnoreCase))!;
        return matches.FirstOrDefault(c => c!["blockedReason"] is null) ?? matches[0]!;
    }
    private static string ApplyUrl(JsonNode preview) => $"/api/prices/update-previews/{preview["id"]}/apply";
    private static Task<JsonNode> Apply(HttpClient client, JsonNode preview, JsonNode candidate, HttpStatusCode status, string? code = null)
        => Send(client, HttpMethod.Post, ApplyUrl(preview), new { requestId = Guid.NewGuid(), candidateIds = new[] { candidate["id"]!.GetValue<string>() }, effectiveFrom = (string?)null }, status, code);
    private static Task<JsonNode> Preview(HttpClient client, string[] ids)
        => Send(client, HttpMethod.Post, "/api/prices/update-previews", new { providerConfigIds = ids }, HttpStatusCode.OK);
    private static async Task<string> Provider(HttpClient client, string kind, string? testKey = null)
        => (await Send(client, HttpMethod.Post, "/api/providers", new { kind, displayName = "Acceptance " + kind, apiKey = testKey ?? "acceptance-key-" + Guid.NewGuid().ToString("N") }, HttpStatusCode.Created))["id"]!.GetValue<string>();
    private static object PriceInput(string model, string provider, decimal input = 0.01m)
        => new { model, provider, inputPerMillion = input, outputPerMillion = 0.02m, longContextFrom = (int?)null, longInputPerMillion = (decimal?)null, longOutputPerMillion = (decimal?)null, perImage = (decimal?)null, effectiveFrom = "2020-01-01T00:00:00Z", note = "Acceptance manual baseline, not a source fixture" };
    private static Task<JsonNode> Seed(HttpClient client, string model, string provider)
        => Send(client, HttpMethod.Post, "/api/prices", PriceInput(model, provider), HttpStatusCode.Created);
    private static async Task<JsonArray> Prices(HttpClient client)
        => (await Send(client, HttpMethod.Get, "/api/prices", null, HttpStatusCode.OK)).AsArray();
    private static async Task ClearPrices(HttpClient client)
    {
        foreach (var price in await Prices(client))
        {
            using var response = await client.DeleteAsync($"/api/prices/{price!["id"]}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
    private static async Task<JsonNode> Send(HttpClient client, HttpMethod method, string url, object? input, HttpStatusCode expected, string? code = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (input is not null) request.Content = JsonContent.Create(input);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{method} {url}: expected {(int)expected}, actual {(int)response.StatusCode}; {body[..Math.Min(body.Length, 300)]}");
        var envelope = JsonNode.Parse(body)!;
        if ((int)expected < 400)
        {
            Assert.Null(envelope["error"]);
            return envelope["data"]!;
        }
        if (code is not null) Assert.Equal(code, envelope["error"]!["code"]!.GetValue<string>());
        return envelope["error"]!;
    }
}
