using System.Text.Json;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Infrastructure;

public sealed class AnthropicProviderTests
{
    [Fact]
    public void BuildRequest_SendsSchemaWithoutRejectedKeywords()
    {
        var provider = new AnthropicProvider("test-key", "test-model");
        var request = new LlmRequest(
            LlmCallContext.ForTask(Guid.NewGuid(), Guid.NewGuid(), TaskKind.Analyze,
                Guid.NewGuid(), Guid.NewGuid(), "test-model"),
            "시스템", "사용자", [],
            """{"type":"object","required":["n"],"properties":{"n":{"type":"array","maxItems":3,"items":{"type":"integer","maximum":9}}}}""");

        var schema = provider.BuildRequest(request).OutputConfig!.Format!.Schema;
        var json = JsonSerializer.Serialize(schema);

        Assert.DoesNotContain("maxItems", json);
        Assert.DoesNotContain("maximum", json);
        Assert.Equal("object", schema["type"].GetString());
        Assert.Equal("n", schema["required"][0].GetString());
    }
}
