using System.Text.Json;
using Noxtend.Api.Contracts;
using Noxtend.Tuning.Domain.Call;

namespace Noxtend.Tests.Api;

public sealed class ModelPriceMetadataContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LegacyRequestDefaultsAndServerOwnedFieldsArePreserved()
    {
        var request = JsonSerializer.Deserialize<ModelPriceRequest>("""
            {"model":"test","inputPerMillion":1,"outputPerMillion":2,
             "effectiveFrom":"2026-01-01T00:00:00Z","allowHistoricalFallback":false,
             "sourceEvidenceJson":"client evidence"}
            """, Json)!;
        Assert.Null(request.ToInput().Provider);
        var price = ModelPrice.Create("test", 1m, 2m, null, null, null,
            request.EffectiveFrom, "테스트");
        var response = JsonSerializer.SerializeToElement(ModelPriceResponse.From(price), Json);
        Assert.Equal(JsonValueKind.Null, response.GetProperty("provider").ValueKind);
        Assert.True(response.GetProperty("allowHistoricalFallback").GetBoolean());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("sourceEvidenceJson").ValueKind);
    }
}
