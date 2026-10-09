using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Noxtend.Infrastructure.Prices;
using Noxtend.Tests.Api;

namespace Noxtend.Tests.Infrastructure;

public sealed class OfficialModelCollectionTests
{
    [Theory]
    [InlineData("openai", 1, 3)]
    [InlineData("anthropic", 2, 2)]
    [InlineData("google", 2, 2)]
    public async Task Actual_source_reads_all_pages_and_bypasses_cache(string provider, int pages, int count)
    {
        var requests = new List<Uri>();
        var modelPages = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            Assert.True(request.Headers.CacheControl!.NoCache);
            Assert.True(request.Headers.CacheControl.NoStore);
            if (request.RequestUri!.AbsolutePath.EndsWith("/models"))
            {
                modelPages++;
                AssertCredential(request, provider);
                var file = provider == "openai" ? "openai-models.json" : $"{provider}-models-page-{modelPages}.json";
                return Task.FromResult(Response(File.ReadAllBytes(Fixture(file))));
            }
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("x-api-key"));
            Assert.False(request.Headers.Contains("x-goog-api-key"));
            return Task.FromResult(Response(File.ReadAllBytes(Fixture($"{provider}-pricing.html"))));
        }));
        var config = Guid.NewGuid();
        var result = await OfficialModelPriceSource.CollectTextAsync(new(http), config, provider, "test-secret", TimeProvider.System, default);
        Assert.Equal(config, result.ProviderConfigId);
        Assert.Equal("complete", result.ModelListStatus);
        Assert.Null(result.ModelError);
        Assert.Equal(pages, modelPages);
        Assert.Equal(count, result.ModelIds.Count);
        Assert.Equal(count, result.Candidates.Count);
        Assert.DoesNotContain("test-secret", JsonSerializer.Serialize(result));
        foreach (var candidate in result.Candidates)
        {
            var expected = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Fixture($"{provider}-pricing.html"))));
            Assert.Equal(expected, Assert.Single(candidate.Evidence).Sha256);
        }
        if (pages > 1) Assert.Contains(requests, u => u.Query.Contains(provider == "anthropic" ? "after_id=" : "pageToken="));
    }

    [Theory]
    [InlineData(false, "failed", 0)]
    [InlineData(true, "partial", 1)]
    public async Task Model_failure_retains_only_observed_models_and_redacts_error(bool secondPage, string status, int count)
    {
        var page = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/models")) return Task.FromResult(Response(File.ReadAllBytes(Fixture("anthropic-pricing.html"))));
            page++;
            if (secondPage && page == 1) return Task.FromResult(Response(File.ReadAllBytes(Fixture("anthropic-models-page-1.json"))));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("test-secret malicious reflected content") });
        }));
        var result = await Collect(http);
        Assert.Equal(status, result.ModelListStatus);
        Assert.Equal(count, result.ModelIds.Count);
        Assert.NotNull(result.ModelError);
        Assert.DoesNotContain("test-secret", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("empty", "complete", 0)]
    [InlineData("malformed", "failed", 0)]
    [InlineData("repeat", "partial", 1)]
    [InlineData("limit", "partial", 20)]
    public async Task Empty_malformed_repeated_and_over_limit_are_distinct(string scenario, string status, int pages)
    {
        var page = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/models")) return Task.FromResult(Response(File.ReadAllBytes(Fixture("anthropic-pricing.html"))));
            page++;
            var json = scenario switch
            {
                "empty" => "{\"data\":[],\"has_more\":false}",
                "malformed" => "{\"data\":null,\"has_more\":false}",
                _ => JsonSerializer.Serialize(new { data = new[] { new { id = "claude-sonnet-4-6" } }, has_more = true, last_id = scenario == "repeat" ? "same" : "token-" + page }),
            };
            return Task.FromResult(Response(Encoding.UTF8.GetBytes(json)));
        }));
        var result = await Collect(http);
        Assert.Equal(status, result.ModelListStatus);
        if (scenario == "empty") { Assert.Empty(result.ModelIds); Assert.Null(result.ModelError); Assert.Equal("success", result.Status); }
        else Assert.NotNull(result.ModelError);
        Assert.Equal(scenario == "repeat" ? 2 : scenario == "empty" || scenario == "malformed" ? 1 : pages, page);
    }

    [Fact]
    public async Task Price_failure_keeps_candidates_blocked_and_separate_from_list_failure()
    {
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/models")
            ? Response(File.ReadAllBytes(Fixture("anthropic-models-union-a.json")))
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var result = await Collect(http);
        Assert.Equal("complete", result.ModelListStatus);
        Assert.Equal("partial", result.Status);
        Assert.Null(result.ModelError);
        Assert.NotNull(result.PriceError);
        Assert.Null(Assert.Single(result.Candidates).Terms);
        Assert.NotNull(Assert.Single(result.Candidates).BlockedReason);
    }

    [Theory]
    [InlineData("https://untrusted.example/v1/models")]
    [InlineData("http://api.anthropic.com/v1/models")]
    [InlineData("https://api.anthropic.com:444/v1/models")]
    [InlineData("https://test-secret@api.anthropic.com/v1/models")]
    [InlineData("https://api.anthropic.com/v1/other")]
    public async Task Untrusted_paths_are_rejected_before_send(string url)
    {
        using var http = new HttpClient(new Handler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected send")));
        await Assert.ThrowsAsync<OfficialPriceSourceException>(() => new OfficialPriceHttp(http).GetAsync(new Uri(url), "anthropic", "test-secret", default));
    }

    [Theory]
    [InlineData("https://platform.claude.com/docs/en/about-claude/pricing", "test-secret")]
    [InlineData("https://api.openai.com/v1/models", "test-secret")]
    [InlineData("https://api.anthropic.com/v1/models", "test-secret\r\nInjected: value")]
    public async Task Credentials_cannot_cross_provider_or_public_document_boundary(string url, string key)
    {
        using var http = new HttpClient(new Handler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected send")));
        var error = await Assert.ThrowsAsync<OfficialPriceSourceException>(() => new OfficialPriceHttp(http).GetAsync(new Uri(url), "anthropic", key, default));
        Assert.DoesNotContain("test-secret", error.Message);
    }

    [Fact]
    public async Task Request_timeout_cancels_slow_header_response()
    {
        using var http = new HttpClient(new Handler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Response([]);
        }));
        var started = System.Diagnostics.Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<OfficialPriceSourceException>(() => new OfficialPriceHttp(http).GetAsync(new("https://api.anthropic.com/v1/models"), "anthropic", "test-secret", default));
        Assert.Contains("시간 초과", error.Message);
        Assert.InRange(started.Elapsed.TotalSeconds, 14, 25);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    public async Task Redirects_are_failures(int status)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.Location = new Uri("https://untrusted.example/");
            return Task.FromResult(response);
        }));
        await Assert.ThrowsAsync<OfficialPriceSourceException>(() => new OfficialPriceHttp(http).GetAsync(new("https://api.anthropic.com/v1/models"), "anthropic", "test-secret", default));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Both_declared_and_streamed_oversize_are_rejected(bool declared)
    {
        using var http = new HttpClient(new Handler((_, _) =>
        {
            var content = declared ? (HttpContent)new ByteArrayContent(new byte[OfficialPriceHttp.MaxBytes + 1])
                : new StreamContent(new NonSeekableStream(new byte[OfficialPriceHttp.MaxBytes + 1]));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        var error = await Assert.ThrowsAsync<OfficialPriceSourceException>(() => new OfficialPriceHttp(http).GetAsync(new("https://api.anthropic.com/v1/models"), "anthropic", "test-secret", default));
        Assert.Contains("크기 상한", error.Message);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_internal_timeout_is_sanitized()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        using var http = new HttpClient(new Handler((_, ct) => { ct.ThrowIfCancellationRequested(); throw new OperationCanceledException("test-secret"); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Collect(http, canceled.Token));
        var result = await Collect(http);
        Assert.Equal("failed", result.ModelListStatus);
        Assert.Contains("시간 초과", result.ModelError);
        Assert.DoesNotContain("test-secret", JsonSerializer.Serialize(result));
    }

    private static Task<Noxtend.Tuning.Domain.Ports.OfficialPriceCollection> Collect(HttpClient http, CancellationToken ct = default)
        => OfficialModelPriceSource.CollectTextAsync(new(http), Guid.NewGuid(), "anthropic", "test-secret", TimeProvider.System, ct);
    private static string Fixture(string file) => Path.Combine(ModelPriceUpdateAcceptanceHost.FixtureRoot, file);
    private static HttpResponseMessage Response(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    private static void AssertCredential(HttpRequestMessage request, string provider)
    {
        var key = provider == "openai" ? request.Headers.Authorization!.Parameter
            : request.Headers.GetValues(provider == "anthropic" ? "x-api-key" : "x-goog-api-key").Single();
        Assert.Equal("test-secret", key);
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
