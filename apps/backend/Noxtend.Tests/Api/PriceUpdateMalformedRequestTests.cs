using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Noxtend.Tests.Infrastructure;

namespace Noxtend.Tests.Api;

[Collection(SqlServerCollection.Name)]
public sealed class PriceUpdateMalformedRequestTests(SqlServerFixture sql)
{
    [Theory]
    [InlineData("collect", "{")]
    [InlineData("collect", "")]
    [InlineData("apply", "{")]
    [InlineData("apply", "")]
    public async Task MalformedOrEmptyJson_ReturnsPriceUpdateInvalidEnvelope(string operation, string body)
    {
        await using var host = new ModelPriceUpdateAcceptanceHost(sql.FreshDatabase(nameof(PriceUpdateMalformedRequestTests)));
        using var client = host.CreateClient();
        var url = operation == "collect" ? "/api/prices/update-previews" : $"/api/prices/update-previews/{Guid.NewGuid()}/apply";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.True(payload.AsObject().ContainsKey("data"));
        Assert.Null(payload["data"]);
        Assert.Equal("PriceUpdateInvalid", payload["error"]?["code"]?.GetValue<string>());
        Assert.NotEmpty(payload["error"]!["message"]!.GetValue<string>());
        Assert.Empty(host.OutboundPaths);
    }

    [Fact]
    public async Task ExistingCrudMalformedJson_PreservesMvcValidationPolicy()
    {
        await using var host = new ModelPriceUpdateAcceptanceHost(sql.FreshDatabase(nameof(PriceUpdateMalformedRequestTests)));
        using var client = host.CreateClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/prices", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.NotNull(payload["errors"]);
        Assert.Null(payload["error"]);
        Assert.Empty(host.OutboundPaths);
    }
}
