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
        Assert.StartsWith(status.ToString(CultureInfo.InvariantCulture), error.Message);
        Assert.DoesNotContain("secret-key", error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ErrorDiagnostics_ReachRecorderWithoutResponseSecrets(bool image)
    {
        const string key = "fake_direct_key";
        var payload = """
            {"error":{"code":"invalid_json_schema","type":"invalid_request_error","param":"response_format","message":"Invalid schema for response_format 'analyze': In context=('properties', 'grid'), 'oneOf' is not permitted. fake_direct_key sk-proj-fake org-fake proj-fake Bearer fake a@example.test https://example.test/?key=fake data:image/png;base64,AAAA request-secret"}}
            """;
        using var http = new HttpClient(new ErrorHandler(400, payload));
        var recorder = new CapturingRecorder();
        ProviderCallFailedException error;
        if (image)
        {
            var provider = new Noxtend.Infrastructure.Image.RecordingImageProvider(
                new Noxtend.Infrastructure.Image.OpenAiImageProvider(http, key, "gpt-image-2"), recorder,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Noxtend.Infrastructure.Image.RecordingImageProvider>.Instance);
            error = await Assert.ThrowsAsync<ProviderCallFailedException>(() => provider.GenerateAsync(
                new(new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "gpt-image-2"), "prompt", [], "1024x1024"), default));
        }
        else
        {
            var provider = new RecordingLlmProvider(new OpenAiProvider(http, key, "gpt-test"), recorder,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RecordingLlmProvider>.Instance);
            error = await Assert.ThrowsAsync<ProviderCallFailedException>(() => provider.CompleteAsync(Request(), default));
        }
        Assert.Contains("oneOf", error.Message);
        Assert.Contains("grid", error.Message);
        Assert.Contains("code=invalid_json_schema", error.Message);
        Assert.Contains("type=invalid_request_error", error.Message);
        Assert.Contains("param=response_format", error.Message);
        Assert.Equal(error.Message, recorder.Entry!.FailureReason);
        Assert.Null(recorder.Entry.ResponsePayload);
        Assert.True(error.Message.Length <= 256);
        foreach (var secret in new[] { key, "sk-proj", "org-fake", "proj-fake", "Bearer", "example.test", "AAAA", "request-secret" })
            Assert.DoesNotContain(secret, error.ToString());
    }

    [Theory]
    [InlineData("sk-proj-fake", "invalid_request_error")]
    [InlineData("fake_direct_key", "invalid_request_error")]
    [InlineData("org_fake", "proj_fake")]
    [InlineData("Bearer fake", "Authorization")]
    [InlineData("a@example.test", "https://example.test/?key=fake")]
    [InlineData("data:image/png;base64,AAAA", "request content")]
    [InlineData("invalid\ncode", "invalid\u0001type")]
    public async Task ErrorFields_RejectSecretsAndUnsafeTokens(string code, string type)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { error = new { code, type, param = code, message = "private request" } }))
        };
        var error = await Noxtend.Infrastructure.ProviderHttp.OpenAiFailureAsync(response, "fake_direct_key", default);
        Assert.Equal(type == "invalid_request_error" ? "400; type=invalid_request_error" : "400; details omitted", error.Message);
        Assert.False(error.IsTransient);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>secret</html>")]
    [InlineData("{broken secret")]
    [InlineData("null")]
    [InlineData("{\"error\":42}")]
    [InlineData("{\"error\":{\"message\":\"private request\",\"code\":42,\"param\":{}}}")]
    public async Task MalformedErrors_KeepStatusFallback(string payload)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(payload) };
        var error = await Noxtend.Infrastructure.ProviderHttp.OpenAiFailureAsync(response, "key", default);
        Assert.StartsWith("500", error.Message);
        Assert.True(error.IsTransient);
    }

    [Fact]
    public async Task ErrorBody_ReadIsBounded_AndCancellationIsPreserved()
    {
        var stream = new CountingStream(new byte[100_000]);
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StreamContent(stream) };
        var error = await Noxtend.Infrastructure.ProviderHttp.OpenAiFailureAsync(response, "key", default);
        Assert.Equal("400", error.Message);
        Assert.Equal(16 * 1024 + 1, stream.BytesRead);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var canceledResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StreamContent(new CountingStream(new byte[10]))
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Noxtend.Infrastructure.ProviderHttp.OpenAiFailureAsync(canceledResponse, "key", canceled.Token));
    }

    [Fact]
    public async Task ErrorBody_ReadFailureDoesNotReplaceHttpFailure()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StreamContent(new CountingStream([], fail: true))
        };
        var error = await Noxtend.Infrastructure.ProviderHttp.OpenAiFailureAsync(response, "key", default);
        Assert.Equal("502", error.Message);
        Assert.True(error.IsTransient);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledAdapterCall_DoesNotRecordFailure(bool image)
    {
        using var http = new HttpClient(new ErrorHandler(400, "private response"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var recorder = new CapturingRecorder();
        if (image)
        {
            var provider = new Noxtend.Infrastructure.Image.RecordingImageProvider(
                new Noxtend.Infrastructure.Image.OpenAiImageProvider(http, "key", "gpt-image-2"), recorder,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Noxtend.Infrastructure.Image.RecordingImageProvider>.Instance);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GenerateAsync(
                new(new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "gpt-image-2"), "prompt", [], "1024x1024"), canceled.Token));
        }
        else
        {
            var provider = new RecordingLlmProvider(new OpenAiProvider(http, "key", "gpt-test"), recorder,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RecordingLlmProvider>.Instance);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CompleteAsync(Request(), canceled.Token));
        }
        Assert.Null(recorder.Entry);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task HttpTimeoutAndCallerCancellation_CoverBodyReadAfterHeaders(bool image, bool success, bool callerCancellation)
    {
        var handler = new SlowBodyHandler(success);
        using var http = new HttpClient(handler) { Timeout = callerCancellation ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(100) };
        using var caller = new CancellationTokenSource();
        if (callerCancellation) caller.CancelAfter(TimeSpan.FromMilliseconds(100));
        if (image)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new Noxtend.Infrastructure.Image.OpenAiImageProvider(http, "key", "gpt-image-2").GenerateAsync(
                    new(new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "gpt-image-2"), "prompt", [], "1024x1024"), caller.Token).WaitAsync(TimeSpan.FromSeconds(5)));
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new OpenAiProvider(http, "key", "gpt-test").CompleteAsync(Request(), caller.Token).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(handler.Stream.ReadStarted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulHeaders_BodyTransportFailureRemainsTransient(bool image)
    {
        using var http = new HttpClient(new FailedBodyHandler());
        ProviderCallFailedException error;
        if (image)
            error = await Assert.ThrowsAsync<ProviderCallFailedException>(() =>
                new Noxtend.Infrastructure.Image.OpenAiImageProvider(http, "key", "gpt-image-2").GenerateAsync(
                    new(new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "gpt-image-2"), "prompt", [], "1024x1024"), default));
        else
            error = await Assert.ThrowsAsync<ProviderCallFailedException>(() =>
                new OpenAiProvider(http, "key", "gpt-test").CompleteAsync(Request(), default));
        Assert.True(error.IsTransient);
        Assert.Equal(nameof(HttpRequestException), error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("fake-body-secret", error.ToString());
    }

    [Fact]
    public async Task SchemaFieldDiagnostic_UsesContextInsteadOfRequestEcho()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""
                {"error":{"code":"invalid_json_schema","message":"Invalid schema for response_format 'analyze': In context=('properties', 'assets'), 'oneOf' is not permitted. Request echo: 'grid' 'sourceBounds' 'cellWidth'"}}
                """)
        };
        var error = await Noxtend.Infrastructure.ProviderHttp.OpenAiFailureAsync(response, "fake-key", default);
        Assert.Contains("field=assets", error.Message);
        Assert.DoesNotContain("field=grid", error.Message);
        Assert.DoesNotContain("field=sourceBounds", error.Message);
        Assert.DoesNotContain("field=cellWidth", error.Message);
    }

    [Fact]
    public async Task RepeatedSchemaContextFields_DoNotExceedDiagnosticLimit()
    {
        var context = string.Join(", ", Enumerable.Repeat("'grid'", 500));
        var message = $"Invalid schema for response_format 'analyze': In context=({context}), 'oneOf' is not permitted.";
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { error = new { message } }))
        };
        var error = await Noxtend.Infrastructure.ProviderHttp.OpenAiFailureAsync(response, "fake-key", default);
        Assert.Contains("field=grid", error.Message);
        Assert.True(error.Message.Length <= 256);
    }

    private sealed class FailedBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailedBodyContent() });
    }

    private sealed class FailedBodyContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => Task.FromException(new HttpRequestException("fake-body-secret"));
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class SlowBodyHandler(bool success) : HttpMessageHandler
    {
        public SlowBodyStream Stream { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(success ? HttpStatusCode.OK : HttpStatusCode.BadRequest) { Content = new StreamContent(Stream) });
    }

    private sealed class SlowBodyStream : MemoryStream
    {
        public bool ReadStarted { get; private set; }
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken ct)
        {
            ReadStarted = true;
            return Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ReadStarted = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 0;
        }
    }

    private sealed class CountingStream(byte[] bytes, bool fail = false) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (fail) throw new IOException("fake-secret");
            var count = await base.ReadAsync(buffer, ct);
            BytesRead += count;
            return count;
        }
    }

    private sealed class CapturingRecorder : ILlmCallRecorder
    {
        public LlmCallEntry? Entry { get; private set; }
        public Task RecordAsync(LlmCallEntry entry, CancellationToken ct)
        {
            Entry = entry;
            return Task.CompletedTask;
        }
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
