using System.Globalization;
using System.Net;
using System.Text;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Application;

public sealed class OpenAiProviderTests
{
    public static IEnumerable<object[]> ErrorResponses()
    {
        var permanent = new[] { "insufficient_quota", "credit_balance_exhausted", "organization_spend_limit_exceeded",
            "project_spend_limit_exceeded", "organization_usage_limit_exceeded" };
        foreach (var image in new[] { false, true })
        {
            foreach (var code in permanent)
                yield return [image, 429, "{\"error\":{\"code\":\"" + code + "\",\"message\":\"secret-key\"}}", false];
            yield return [image, 429, "{\"error\":{\"type\":\"insufficient_quota\",\"code\":null}}", false];
            foreach (var payload in new[] { "{\"error\":{\"code\":\"rate_limit_exceeded\"}}", "{\"error\":{\"code\":\"slow_down\"}}",
                "not json secret-key", "{}", "null", "{\"error\":{\"code\":42}}", "{\"error\":\"secret-key\"}" })
                yield return [image, 429, payload, true];
            yield return [image, 401, "secret-key", false];
            yield return [image, 500, "secret-key", true];
        }
    }

    [Theory]
    [MemberData(nameof(ErrorResponses))]
    public async Task BillingQuota_IsTerminal_WhileRateLimitsKeepStatusFallback(bool image, int status, string payload, bool transient)
    {
        using var http = new HttpClient(new ErrorHandler(status, payload));
        ProviderCallFailedException error;
        if (image)
        {
            var provider = new Noxtend.Infrastructure.Image.OpenAiImageProvider(http, "key", "gpt-image-2");
            error = await Assert.ThrowsAsync<ProviderCallFailedException>(() => provider.GenerateAsync(
                new(new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "gpt-image-2"), "prompt", [], "1024x1024"), default));
        }
        else
            error = await Assert.ThrowsAsync<ProviderCallFailedException>(() => new OpenAiProvider(http, "key", "gpt-test").CompleteAsync(Request(), default));
        Assert.Equal(transient, error.IsTransient);
        Assert.Equal(status.ToString(CultureInfo.InvariantCulture), error.Message);
        Assert.DoesNotContain("secret-key", error.ToString());
    }

    private sealed class ErrorHandler(int status, string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(payload) });
    }

    // text-generation-rate-limiting §구현변경-3 — OpenAiImageProvider 와 같은 헤더 이름·포맷
    [Fact]
    public async Task RecordsRateLimitHeaders_FromSuccessResponse()
    {
        const string payload = """
            { "choices": [{ "message": { "content": "{}" } }] }
            """;
        var headers = new Dictionary<string, string>
        {
            ["x-ratelimit-remaining-requests"] = "3",
            ["x-ratelimit-reset-requests"] = "1m30s",
        };

        var provider = new OpenAiProvider(Client(payload, headers), "test-api-key", "gpt-test");

        var result = await provider.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal(3, result.RateLimitRemainingRequests);
        Assert.Equal(TimeSpan.FromSeconds(90), result.RateLimitResetAfter);
    }

    // 헤더가 없는 응답도 있다 — null 로 두고 호출자가 "정보 없음"으로 처리하게 한다
    [Fact]
    public async Task MissingRateLimitHeaders_LeavesFieldsNull()
    {
        const string payload = """
            { "choices": [{ "message": { "content": "{}" } }] }
            """;

        var provider = new OpenAiProvider(Client(payload), "test-api-key", "gpt-test");

        var result = await provider.CompleteAsync(Request(), CancellationToken.None);

        Assert.Null(result.RateLimitRemainingRequests);
        Assert.Null(result.RateLimitResetAfter);
        Assert.Null(result.RateLimitRemainingTokens);
        Assert.Null(result.RateLimitResetTokensAfter);
    }

    // TPM(토큰) 헤더도 RPM(요청) 헤더와 같은 자리에서, 같은 이름 규칙으로 온다
    [Fact]
    public async Task RecordsTokenRateLimitHeaders_FromSuccessResponse()
    {
        const string payload = """
            { "choices": [{ "message": { "content": "{}" } }] }
            """;
        var headers = new Dictionary<string, string>
        {
            ["x-ratelimit-remaining-tokens"] = "8500",
            ["x-ratelimit-reset-tokens"] = "6s",
        };

        var provider = new OpenAiProvider(Client(payload, headers), "test-api-key", "gpt-test");

        var result = await provider.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal(8500, result.RateLimitRemainingTokens);
        Assert.Equal(TimeSpan.FromSeconds(6), result.RateLimitResetTokensAfter);
    }

    // 독립 리뷰 지적 — double.Parse 가 컬처에 의존하면 "1.44s" 가 de-DE 에서는
    // 소수점이 아니라 천단위 구분자로 읽혀 144 로 둔갑한다. 144초 대기는 실제
    // 헤더 의도(1.44초)의 100배다
    [Fact]
    public async Task ResetHeaderParsing_IsNotCultureDependent()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            const string payload = """
                { "choices": [{ "message": { "content": "{}" } }] }
                """;
            var headers = new Dictionary<string, string> { ["x-ratelimit-reset-requests"] = "1.44s" };
            var provider = new OpenAiProvider(Client(payload, headers), "test-api-key", "gpt-test");

            var result = await provider.CompleteAsync(Request(), CancellationToken.None);

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
        const string payload = """
            { "choices": [{ "message": { "content": "{}" } }] }
            """;
        var headers = new Dictionary<string, string> { ["x-ratelimit-reset-tokens"] = raw };
        var provider = new OpenAiProvider(Client(payload, headers), "test-api-key", "gpt-test");

        var result = await provider.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), result.RateLimitResetTokensAfter);
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

    [Fact]
    public async Task TransportFailure_BecomesTransientProviderFailure()
    {
        // 외부 전송 실패 경계
        var transportError = new HttpRequestException(
            "TLS connection failed",
            new IOException("bad record mac"));
        var provider = new OpenAiProvider(
            new HttpClient(new ThrowingHandler(transportError)),
            "test-api-key",
            "gpt-test");

        var exception = await Assert.ThrowsAsync<ProviderCallFailedException>(
            () => provider.CompleteAsync(Request(), CancellationToken.None));

        Assert.True(exception.IsTransient);
        Assert.Same(transportError, exception.InnerException);
        Assert.Equal(nameof(HttpRequestException), exception.Message);
    }

    /// <summary>
    /// 유사도 평가는 reference·render 두 장을 순서대로 보낸다 (§7.2).
    /// 각 이미지 바로 앞에 label text 를 넣는다 — 모델이 어느 장이 원본인지 알아야 한다.
    /// </summary>
    [Fact]
    public async Task MultipleImages_AreSentInOrderWithALabelBeforeEach()
    {
        const string payload = """
            { "choices": [{ "message": { "content": "{}" } }] }
            """;
        var capture = new CapturingHandler(payload);
        var provider = new OpenAiProvider(new HttpClient(capture), "test-api-key", "gpt-test");

        var request = Request() with
        {
            Images =
            [
                new LlmImage("reference", new ImageContent([1, 2], "image/png")),
                new LlmImage("render", new ImageContent([3, 4], "image/png")),
            ],
        };
        await provider.CompleteAsync(request, CancellationToken.None);

        using var sent = System.Text.Json.JsonDocument.Parse(capture.Body!);
        var content = sent.RootElement.GetProperty("messages")[1].GetProperty("content");

        // user 텍스트 → [label, image] × 2, 순서 보존
        Assert.Equal(5, content.GetArrayLength());
        Assert.Equal("[image: reference]", content[1].GetProperty("text").GetString());
        Assert.Equal("image_url", content[2].GetProperty("type").GetString());
        Assert.Equal("[image: render]", content[3].GetProperty("text").GetString());
        Assert.Equal("image_url", content[4].GetProperty("type").GetString());
    }

    private sealed class CapturingHandler(string payload) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static LlmRequest Request()
        => new(
            LlmCallContext.ForTask(
                Guid.NewGuid(),
                Guid.NewGuid(),
                TaskKind.Analyze,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "gpt-test"),
            "system",
            "user",
            [],
            "{\"type\":\"object\"}");

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }
}
