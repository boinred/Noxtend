using System.Net;
using System.Text.Json;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Application;

public sealed class OpenAiSpriteSchemaTests
{
    [Fact]
    public async Task AnalyzeSpritesV4IsSentAsStrictSchemaWithAnyOf()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var prompt = SeedPrompts.AnalyzeSpritesV4();
        var context = LlmCallContext.ForTask(Guid.NewGuid(), Guid.NewGuid(), TaskKind.AnalyzeSprites,
            Guid.NewGuid(), Guid.NewGuid(), "gpt-test");
        await new OpenAiProvider(http, "fake-key", "gpt-test").CompleteAsync(
            new LlmRequest(context, prompt.System, prompt.User, [], prompt.Schema), default);
        using var sent = JsonDocument.Parse(handler.Body!);
        var format = sent.RootElement.GetProperty("response_format").GetProperty("json_schema");
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.Equal("analyzesprites", format.GetProperty("name").GetString());
        Assert.Equal(prompt.Schema, format.GetProperty("schema").GetRawText());
        Assert.Equal(2, format.GetProperty("schema").GetProperty("properties").GetProperty("grid").GetProperty("anyOf").GetArrayLength());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}") };
        }
    }
}
