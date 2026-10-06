using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Image;

namespace Noxtend.Tests.Application;

/// <summary>
/// OpenAI 이미지 요청이 **품질을 고정해 보내는가**.
///
/// 안 보내면 공급자 기본값에 맡겨지는데, 1024x1024 장당 단가가 low $0.006 · medium $0.053 ·
/// high $0.211 로 35배까지 달라진다. 단가표에는 한 값만 심으므로, 요청이 품질을 말하지 않으면
/// 심어 둔 값과 실제 청구가 어긋나도 아무도 모른다.
///
/// 크기를 서버가 고정하는 것과 같은 이유다 (§2.3 A-8) — 조립 기준이 정해지기 전에는
/// 사용자가 고를 근거가 없고, 근거 없는 선택지는 비용만 흔든다.
/// </summary>
public sealed class OpenAiImageRequestTests
{
    private static readonly ImageCallContext Context = new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "m");

    private const string Payload = """
        { "data": [{ "b64_json": "iVBORw0KGgo=" }] }
        """;

    [Fact]
    public async Task GenerateRequest_FixesQuality()
    {
        var handler = new CapturingHandler(Payload);
        var provider = new OpenAiImageProvider(new HttpClient(handler), "key", "gpt-image-2");

        await provider.GenerateAsync(
            new ImageRequest(Context, "단독 파츠를 그려라", [], "1024x1024"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(handler.Body!);
        Assert.Equal("medium", document.RootElement.GetProperty("quality").GetString());
        Assert.Equal("png", document.RootElement.GetProperty("output_format").GetString());
        Assert.False(document.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task EditRequest_FixesQuality()
    {
        // 참조 원본이 붙는 편집 경로가 파츠 생성의 기본이다 (D-5) — 여기가 빠지면 의미가 없다
        var handler = new CapturingHandler(Payload);
        var provider = new OpenAiImageProvider(new HttpClient(handler), "key", "gpt-image-2");

        await provider.GenerateAsync(
            new ImageRequest(
                Context, "단독 파츠를 그려라",
                [new ReferenceImage(new ImageContent([1, 2, 3], "image/png"), ReferenceRole.Original)],
                "1024x1024"),
            CancellationToken.None);

        Assert.Contains("name=quality", handler.Body, StringComparison.Ordinal);
        Assert.Contains("medium", handler.Body!, StringComparison.Ordinal);
        Assert.Contains("name=output_format", handler.Body!, StringComparison.Ordinal);
        Assert.Contains("png", handler.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("name=response_format", handler.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditRequest_SendsEveryReferenceAsAnImageArrayEntry()
    {
        // workstream B §5.2 — gpt-image 계열은 image[] 를 반복해 여러 장을 받는다(§0-2)
        var handler = new CapturingHandler(Payload);
        var provider = new OpenAiImageProvider(new HttpClient(handler), "key", "gpt-image-2");

        await provider.GenerateAsync(
            new ImageRequest(
                Context, "단독 파츠를 그려라",
                [
                    new ReferenceImage(new ImageContent([1], "image/png"), ReferenceRole.FrontView),
                    new ReferenceImage(new ImageContent([2], "image/png"), ReferenceRole.Original),
                ],
                "1024x1024"),
            CancellationToken.None);

        var occurrences = Regex.Matches(handler.Body!, "name=\"image\\[\\]\"").Count;
        Assert.Equal(2, occurrences);

        // 개수만이 아니라 순서도 확인한다 — filename=reference-{index} 가 참조 목록
        // 순서(FrontView 먼저, Original 다음)와 일치해야 프롬프트의 참조 번호와 맞는다
        var front = handler.Body!.IndexOf("filename=reference-0", StringComparison.Ordinal);
        var original = handler.Body!.IndexOf("filename=reference-1", StringComparison.Ordinal);
        Assert.True(front >= 0 && original >= 0 && front < original,
            "reference-0(FrontView)가 reference-1(Original)보다 먼저 와야 한다");
    }

    private sealed class CapturingHandler(string payload) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        }
    }
}
