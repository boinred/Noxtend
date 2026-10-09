using System.Net;
using Noxtend.Infrastructure.Prices;
using Noxtend.Tests.Api;

namespace Noxtend.Tests.Infrastructure;

public sealed class OfficialMeshPriceSourceTests
{
    [Fact]
    public async Task Tripo_matches_P1_multiview_standard_texture_and_public_credit_conversion()
    {
        var result = await Collect("tripo");
        var candidate = Assert.Single(result.Candidates, c => c.Model == "P1-20260311");
        Assert.Equal("mesh", candidate.Area);
        Assert.Equal("multiview-to-3d", candidate.Operation);
        Assert.Contains("texture_quality=standard", candidate.Conditions);
        Assert.Null(candidate.BlockedReason);
        Assert.Equal(0.5m, candidate.Terms!.PerImage);
        Assert.Contains(candidate.Evidence, e => e.CreditsPerTask == 50 && e.UsdPerCredit == 0.01m);
        Assert.Contains(candidate.Evidence, e => e.UsdPerCredit == 0.01m);
        Assert.All(candidate.Evidence, e => Assert.Matches("^[a-f0-9]{64}$", e.Sha256));
        Assert.Equal("partial", result.ModelListStatus);
    }

    [Fact]
    public async Task Meshy_retains_exact_options_and_credits_without_global_account_conversion()
    {
        var result = await Collect("meshy");
        var candidate = Assert.Single(result.Candidates, c => c.Model == "meshy-6");
        Assert.Equal("multi-image-to-3d", candidate.Operation);
        Assert.Contains("texture_image_resolution=2048", candidate.Conditions);
        Assert.Contains("geometry_resolution=standard", candidate.Conditions);
        Assert.Equal("계정별 환산 지원 필요", candidate.BlockedReason);
        Assert.Null(candidate.Terms?.PerImage);
        Assert.Contains(candidate.Evidence, e => e.CreditsPerTask == 30);
        Assert.All(candidate.Evidence, e => Assert.Null(e.UsdPerCredit));
        Assert.Null(Assert.Single(result.Candidates, c => c.Model == "latest").Terms);
        Assert.Contains(Assert.Single(result.Candidates, c => c.Model == "meshy-7").Evidence, e => e.CreditsPerTask == 30);
    }

    [Theory]
    [InlineData("tripo", "structure-changed")]
    [InlineData("tripo", "unit-missing")]
    [InlineData("meshy", "structure-changed")]
    [InlineData("meshy", "unit-missing")]
    public async Task Damaged_official_sources_never_return_applicable_prices(string provider, string mutation)
    {
        var result = await Collect(provider, mutation);
        Assert.NotNull(result.PriceError);
        Assert.NotEmpty(result.Candidates);
        Assert.All(result.Candidates, c => Assert.NotNull(c.BlockedReason));
        Assert.DoesNotContain(result.Candidates, c => c.Terms?.PerImage is not null);
    }

    [Fact]
    public async Task Explicit_future_effective_date_is_retained_on_Tripo_price()
    {
        var candidate = Assert.Single((await Collect("tripo", "future")).Candidates);
        Assert.Equal(DateTimeOffset.Parse("2099-01-01T00:00:00Z"), candidate.Terms!.OfficialEffectiveFrom);
    }

    [Fact]
    public async Task Collection_cancellation_propagates()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Collect("tripo", ct: canceled.Token));
    }

    [Fact]
    public void Tripo_amounts_are_read_from_supplied_DOM_instead_of_seed_prices()
    {
        var detail = System.Text.Encoding.UTF8.GetString(Read("tripo-p1.html")).Replace(">50</td>", ">75</td>", StringComparison.Ordinal);
        var pricing = System.Text.Encoding.UTF8.GetString(Read("tripo-pricing.html")).Replace("1 credit = $0.01 USD", "1 credit = $0.02 USD", StringComparison.Ordinal);
        var candidate = OfficialMeshPriceParser.Tripo("P1-20260311", System.Text.Encoding.UTF8.GetBytes(detail),
            System.Text.Encoding.UTF8.GetBytes(pricing), DateTimeOffset.UtcNow);
        Assert.Null(candidate.BlockedReason);
        Assert.Equal(1.5m, candidate.Terms!.PerImage);
    }

    [Fact]
    public void Changed_Meshy_default_options_are_blocked_and_future_date_is_preserved()
    {
        var endpoint = System.Text.Encoding.UTF8.GetString(Read("meshy-multi-image.html")).Replace(
            "default<!-- --> <!-- -->2k", "default<!-- --> <!-- -->4k", StringComparison.Ordinal);
        var candidate = OfficialMeshPriceParser.Meshy("meshy-6", Read("meshy-openapi.json"),
            System.Text.Encoding.UTF8.GetBytes(endpoint), Read("meshy-pricing.html"), DateTimeOffset.UtcNow);
        Assert.Equal(OfficialMeshPriceParser.UnknownReason, candidate.BlockedReason);
        Assert.Null(candidate.Terms);
        var future = OfficialMeshPriceParser.Meshy("meshy-6", Read("meshy-openapi.json"),
            Read("meshy-multi-image.html"), Read("meshy-pricing-future.html"), DateTimeOffset.UtcNow);
        Assert.Equal("계정별 환산 지원 필요", future.BlockedReason);
        Assert.Equal(DateTimeOffset.Parse("2099-01-01T00:00:00Z"), future.Terms!.OfficialEffectiveFrom);
        Assert.Null(future.Terms.PerImage);
    }

    [Fact]
    public void Conflicting_Tripo_document_effective_dates_are_blocked()
    {
        var global = System.Text.Encoding.UTF8.GetString(Read("tripo-pricing-future.html"))
            .Replace("2099-01-01T00:00:00Z", "2098-01-01T00:00:00Z", StringComparison.Ordinal);
        var candidate = OfficialMeshPriceParser.Tripo("P1-20260311", Read("tripo-p1-future.html"),
            System.Text.Encoding.UTF8.GetBytes(global), DateTimeOffset.UtcNow);
        Assert.Null(candidate.Terms);
        Assert.Equal("공식 시행일 상충 확인 필요", candidate.BlockedReason);
    }

    private static byte[] Read(string file)
        => File.ReadAllBytes(Path.Combine(ModelPriceUpdateAcceptanceHost.FixtureRoot, file));

    private static Task<Noxtend.Tuning.Domain.Ports.OfficialPriceCollection> Collect(
        string provider, string? mutation = null, CancellationToken ct = default)
    {
        var handler = new Fixtures(mutation);
        var http = new HttpClient(handler);
        return OfficialModelPriceSource.CollectTextAsync(new OfficialPriceHttp(http), Guid.NewGuid(),
            provider, "unused-test-key", TimeProvider.System, ct);
    }

    private sealed class Fixtures(string? mutation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Headers.Authorization);
            var file = request.RequestUri!.AbsolutePath switch
            {
                "/en/models" => "tripo-models.html",
                "/en/models/p1" => "tripo-p1.html",
                "/en/pricing" => "tripo-pricing.html",
                "/openapi.json" => "meshy-openapi.json",
                "/en/api/multi-image-to-3d" => "meshy-multi-image.html",
                "/en/api/pricing" => "meshy-pricing.html",
                _ => throw new InvalidOperationException("Unregistered fixture URL"),
            };
            if (mutation is not null && (file.EndsWith("-pricing.html") || file == "tripo-p1.html"))
                file = file[..^5] + "-" + mutation + ".html";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(File.ReadAllBytes(Path.Combine(ModelPriceUpdateAcceptanceHost.FixtureRoot, file))),
            });
        }
    }
}
