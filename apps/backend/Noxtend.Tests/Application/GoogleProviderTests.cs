using System.Net;
using System.Text;
using System.Text.Json;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Application;

/// <summary>
/// Gemini 텍스트 어댑터 — gemini-text-provider (2026-08-28).
///
/// <see cref="OpenAiProviderTests"/>와 같은 계약을 검증하되, Gemini 특유의 응답 모양
/// (candidates[].content.parts[].text, 파트 순서 무보장)을 확인한다.
/// </summary>
public sealed class GoogleProviderTests
{
    [Fact]
    public async Task ReturnsFirstTextPart_WhenItIsTheOnlyPart()
    {
        const string payload = """
            {
              "candidates": [
                { "content": { "parts": [ { "text": "{\"parts\":[]}" } ] } }
              ],
              "usageMetadata": { "promptTokenCount": 42, "candidatesTokenCount": 7 }
            }
            """;

        var provider = new GoogleProvider(Client(payload), "test-google-key", "gemini-3-pro");

        var result = await provider.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("{\"parts\":[]}", result.RawJson);
        Assert.Equal(42, result.InputTokens);
        Assert.Equal(7, result.OutputTokens);
    }

    // 안전 분류기 코멘트 등 다른 파트가 텍스트보다 먼저 올 수 있다 — 첫 파트가
    // 텍스트라고 가정하면 이 조합에서만 깨진다 (GoogleImageProvider와 같은 방어)
    [Fact]
    public async Task FindsTextPart_WhenItIsNotFirst()
    {
        const string payload = """
            {
              "candidates": [
                { "content": { "parts": [
                  { "inlineData": { "mimeType": "image/png", "data": "AA==" } },
                  { "text": "{\"parts\":[]}" }
                ] } }
              ]
            }
            """;

        var provider = new GoogleProvider(Client(payload), "test-google-key", "gemini-3-pro");

        var result = await provider.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("{\"parts\":[]}", result.RawJson);
    }

    [Fact]
    public async Task NoCandidates_ThrowsBadResponse()
    {
        const string payload = """{ "candidates": [] }""";

        var provider = new GoogleProvider(Client(payload), "test-google-key", "gemini-3-pro");

        await Assert.ThrowsAsync<ProviderBadResponseException>(
            () => provider.CompleteAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task SendsSystemInstructionSeparatelyFromUserContent()
    {
        var handler = new CapturingStubHandler(
            """{ "candidates": [ { "content": { "parts": [ { "text": "{}" } ] } } ] }""");
        var provider = new GoogleProvider(new HttpClient(handler), "test-google-key", "gemini-3-pro");

        await provider.CompleteAsync(Request(), CancellationToken.None);

        using var document = JsonDocument.Parse(handler.Body!);
        var root = document.RootElement;

        Assert.Equal(
            "system",
            root.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(
            "user",
            root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("test-google-key", handler.ApiKey);
    }

    // 문자열을 그대로 실으면 스키마가 무시될 수 있다 — 파싱해서 구조로 실어야 한다
    [Fact]
    public async Task ParsesJsonSchemaIntoResponseSchema_RatherThanSendingItAsAString()
    {
        var handler = new CapturingStubHandler(
            """{ "candidates": [ { "content": { "parts": [ { "text": "{}" } ] } } ] }""");
        var provider = new GoogleProvider(new HttpClient(handler), "test-google-key", "gemini-3-pro");

        await provider.CompleteAsync(Request(), CancellationToken.None);

        using var document = JsonDocument.Parse(handler.Body!);
        var generationConfig = document.RootElement.GetProperty("generationConfig");

        Assert.Equal("application/json", generationConfig.GetProperty("responseMimeType").GetString());
        Assert.Equal("object", generationConfig.GetProperty("responseSchema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task TransportFailure_BecomesTransientProviderFailure()
    {
        var transportError = new HttpRequestException("TLS connection failed");
        var provider = new GoogleProvider(
            new HttpClient(new ThrowingHandler(transportError)), "test-google-key", "gemini-3-pro");

        var exception = await Assert.ThrowsAsync<ProviderCallFailedException>(
            () => provider.CompleteAsync(Request(), CancellationToken.None));

        Assert.True(exception.IsTransient);
        Assert.Same(transportError, exception.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task ClassifiesFailuresByStatusCode(HttpStatusCode status, bool expectedTransient)
    {
        var provider = new GoogleProvider(
            new HttpClient(new StatusHandler(status)), "test-google-key", "gemini-3-pro");

        var exception = await Assert.ThrowsAsync<ProviderCallFailedException>(
            () => provider.CompleteAsync(Request(), CancellationToken.None));

        Assert.Equal(expectedTransient, exception.IsTransient);
    }

    private static LlmRequest Request()
        => new(
            LlmCallContext.ForTask(
                Guid.NewGuid(),
                Guid.NewGuid(),
                TaskKind.Extract,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "gemini-3-pro"),
            "system",
            "user",
            [],
            "{\"type\":\"object\"}");

    private static HttpClient Client(string payload) => new(new StubHandler(payload));

    private sealed class StubHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class CapturingStubHandler(string payload) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? ApiKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            ApiKey = request.Headers.TryGetValues("x-goog-api-key", out var values)
                ? values.Single()
                : null;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }
}
