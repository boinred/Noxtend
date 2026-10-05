using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Image;

namespace Noxtend.Tests.Application;

/// <summary>
/// 이미지 어댑터가 공급자의 **실제 청구 근거**를 받아 적는가.
///
/// Plan D-8 은 "이미지는 장당 과금" 을 전제했지만 두 공급자 다 토큰으로 매긴다.
/// 장당 정액은 "1024px · 참조 없음" 한 조합의 환산값인데, **D-5 때문에 참조 원본을
/// 항상 보내므로** 입력 토큰이 더 붙는다 — 정액만으로는 하한밖에 안 나온다.
///
/// 실측을 안 받아 오면 그 차액이 영원히 안 보이므로 여기서 고정한다.
/// </summary>
public sealed class ImageProviderUsageTests
{
    private static readonly ImageCallContext Context = new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "m");

    private static ImageRequest Request(ImageContent? reference = null)
        => new(
            Context, "단독 파츠를 그려라",
            reference is { } content
                ? [new ReferenceImage(content, ReferenceRole.Original)]
                : [],
            "1024x1024");

    [Fact]
    public async Task Google_RecordsInputAndOutputTokens()
    {
        // Interactions API의 `total_input_tokens`에 참조 원본 토큰이 포함된다
        const string payload = """
            {
              "steps": [
                { "type": "model_output", "content": [
                  { "type": "image", "mime_type": "image/png", "data": "iVBORw0KGgo=" }
                ] }
              ],
              "usage": {
                "total_input_tokens": 1240,
                "total_output_tokens": 1120,
                "total_tokens": 2360
              }
            }
            """;

        var provider = new GoogleImageProvider(
            Client(payload), "key", "gemini-3.1-flash-lite-image");

        var result = await provider.GenerateAsync(
            Request(new ImageContent([1, 2, 3], "image/png")), CancellationToken.None);

        Assert.Equal(1240, result.InputTokens);
        Assert.Equal(1120, result.OutputTokens);
        Assert.Equal(1, result.ImageCount);
    }

    [Fact]
    public async Task OpenAi_RecordsInputAndOutputTokens()
    {
        const string payload = """
            {
              "data": [{ "b64_json": "iVBORw0KGgo=" }],
              "usage": { "input_tokens": 1580, "output_tokens": 1120, "total_tokens": 2700 }
            }
            """;

        var provider = new OpenAiImageProvider(Client(payload), "key", "gpt-image-2");

        var result = await provider.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(1580, result.InputTokens);
        Assert.Equal(1120, result.OutputTokens);
    }

    // generation-rate-limiting §3① — 사전 예방을 위해 응답 헤더의 남은 횟수·초기화까지
    // 남은 시간을 ImageResult 에 실어 보낸다(InputTokens/OutputTokens 와 같은 방식).
    // 저장·판단은 어댑터가 아니라 호출한 쪽(RunGenerationTaskHandler)이 한다
    [Fact]
    public async Task OpenAi_RecordsRateLimitHeaders()
    {
        const string payload = """{ "data": [{ "b64_json": "iVBORw0KGgo=" }] }""";
        var headers = new Dictionary<string, string>
        {
            ["x-ratelimit-remaining-requests"] = "3",
            ["x-ratelimit-reset-requests"] = "1m30s",
        };

        var provider = new OpenAiImageProvider(Client(payload, headers), "key", "gpt-image-2");

        var result = await provider.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(3, result.RateLimitRemainingRequests);
        Assert.Equal(TimeSpan.FromSeconds(90), result.RateLimitResetAfter);
    }

    // 헤더가 없는 응답도 있을 수 있다(구버전 프록시 등) — null로 두고 호출자가
    // "정보 없음"으로 처리하게 한다. 0으로 채우면 항상 대기하게 돼 더 위험하다
    [Fact]
    public async Task MissingRateLimitHeaders_LeavesFieldsNull()
    {
        const string payload = """{ "data": [{ "b64_json": "iVBORw0KGgo=" }] }""";

        var provider = new OpenAiImageProvider(Client(payload), "key", "gpt-image-2");

        var result = await provider.GenerateAsync(Request(), CancellationToken.None);

        Assert.Null(result.RateLimitRemainingRequests);
        Assert.Null(result.RateLimitResetAfter);
        Assert.Null(result.RateLimitRemainingTokens);
        Assert.Null(result.RateLimitResetTokensAfter);
    }

    // TPM(토큰) 헤더도 RPM(요청) 헤더와 같은 자리에서, 같은 이름 규칙으로 온다
    [Fact]
    public async Task OpenAi_RecordsTokenRateLimitHeaders()
    {
        const string payload = """{ "data": [{ "b64_json": "iVBORw0KGgo=" }] }""";
        var headers = new Dictionary<string, string>
        {
            ["x-ratelimit-remaining-tokens"] = "12000",
            ["x-ratelimit-reset-tokens"] = "1m30s",
        };

        var provider = new OpenAiImageProvider(Client(payload, headers), "key", "gpt-image-2");

        var result = await provider.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(12000, result.RateLimitRemainingTokens);
        Assert.Equal(TimeSpan.FromSeconds(90), result.RateLimitResetTokensAfter);
    }

    // 독립 리뷰 지적 — double.Parse 가 컬처에 의존하면 "1.44s" 가 de-DE 에서 144 로 읽힌다
    [Fact]
    public async Task ResetHeaderParsing_IsNotCultureDependent()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            const string payload = """{ "data": [{ "b64_json": "iVBORw0KGgo=" }] }""";
            var headers = new Dictionary<string, string> { ["x-ratelimit-reset-requests"] = "1.44s" };
            var provider = new OpenAiImageProvider(Client(payload, headers), "key", "gpt-image-2");

            var result = await provider.GenerateAsync(Request(), CancellationToken.None);

            Assert.Equal(TimeSpan.FromSeconds(1.44), result.RateLimitResetAfter);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // OpenAI 는 토큰 축 초기화를 ms 단위로, 일부 한도는 h 단위로 돌려준다
    [Theory]
    [InlineData("6ms", 6)]
    [InlineData("1h0m0s", 3_600_000)]
    public async Task ResetHeaderParsing_HandlesMillisecondsAndHours(string raw, double expectedMs)
    {
        const string payload = """{ "data": [{ "b64_json": "iVBORw0KGgo=" }] }""";
        var headers = new Dictionary<string, string> { ["x-ratelimit-reset-tokens"] = raw };
        var provider = new OpenAiImageProvider(Client(payload, headers), "key", "gpt-image-2");

        var result = await provider.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), result.RateLimitResetTokensAfter);
    }

    [Fact]
    public async Task MissingUsage_LeavesTokensNull_SoThePerImageRowStillApplies()
    {
        // usage 를 안 주는 응답이 있을 수 있다. 그때 0 으로 채우면 "공짜" 가 되므로
        // null 로 두고 단가표의 장당 값으로 떨어지게 한다
        const string payload = """
            { "data": [{ "b64_json": "iVBORw0KGgo=" }] }
            """;

        var provider = new OpenAiImageProvider(Client(payload), "key", "gpt-image-2");

        var result = await provider.GenerateAsync(Request(), CancellationToken.None);

        Assert.Null(result.InputTokens);
        Assert.Null(result.OutputTokens);
    }

    [Fact]
    public async Task Google_SendsEveryReferenceAsAnInteractionImageInput()
    {
        // Interactions API에서도 참조 순서는 프롬프트의 reference 번호와 일치해야 한다
        const string payload = """
            {
              "steps": [
                { "type": "model_output", "content": [
                  { "type": "image", "mime_type": "image/png", "data": "iVBORw0KGgo=" }
                ] }
              ]
            }
            """;

        var handler = new CapturingStubHandler(payload);
        var provider = new GoogleImageProvider(new HttpClient(handler), "key", "gemini-3.1-flash-lite-image");

        var request = new ImageRequest(
            Context, "단독 파츠를 그려라",
            [
                new ReferenceImage(new ImageContent([1], "image/png"), ReferenceRole.FrontView),
                new ReferenceImage(new ImageContent([2], "image/png"), ReferenceRole.Original),
            ],
            "1024x1024");

        await provider.GenerateAsync(request, CancellationToken.None);

        using var document = JsonDocument.Parse(handler.Body!);
        var root = document.RootElement;
        var input = root.GetProperty("input");

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/interactions", handler.RequestUri);
        Assert.Equal("gemini-3.1-flash-lite-image", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("image", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("1:1", root.GetProperty("response_format").GetProperty("aspect_ratio").GetString());
        Assert.Equal("1K", root.GetProperty("response_format").GetProperty("image_size").GetString());
        Assert.Equal(3, input.GetArrayLength());
        Assert.Equal("text", input[0].GetProperty("type").GetString());
        Assert.Equal("단독 파츠를 그려라", input[0].GetProperty("text").GetString());

        // 개수만이 아니라 순서도 확인한다 — input[1]이 FrontView([1]), input[2]가
        // Original([2])이어야 프롬프트의 "reference 1 = 정면" 문구와 실제 전송이 맞는다
        Assert.Equal(
            Convert.ToBase64String([1]),
            input[1].GetProperty("data").GetString());
        Assert.Equal("image/png", input[1].GetProperty("mime_type").GetString());
        Assert.Equal(
            Convert.ToBase64String([2]),
            input[2].GetProperty("data").GetString());
    }

    [Fact]
    public async Task OpenAiTransportFailure_BecomesTransientProviderFailure()
    {
        // 이미지 생성 외부 전송 실패 경계
        var transportError = new HttpRequestException("TLS connection failed");
        var provider = new OpenAiImageProvider(
            new HttpClient(new ThrowingHandler(transportError)),
            "key",
            "gpt-image-2");

        var exception = await Assert.ThrowsAsync<ProviderCallFailedException>(
            () => provider.GenerateAsync(Request(), CancellationToken.None));

        Assert.True(exception.IsTransient);
        Assert.Same(transportError, exception.InnerException);
    }

    private static HttpClient Client(string payload, IReadOnlyDictionary<string, string>? headers = null)
        => new(new StubHandler(payload, headers));

    private sealed class StubHandler(string payload, IReadOnlyDictionary<string, string>? headers = null)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };

            foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class CapturingStubHandler(string payload) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

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
