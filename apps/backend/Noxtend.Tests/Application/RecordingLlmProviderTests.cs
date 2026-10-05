using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Application;

/// <summary>
/// 내역 데코레이터의 이미지 기록. Design Ref: background-similarity-tuning §7.2
///
/// **바이트는 남기지 않는다** — base64 를 넣으면 표가 수십 배로 부푼다 (§7 S-3).
/// 대신 이름·형식·크기·hash 를 남겨 "무엇을 보냈나" 는 추적 가능하게 한다.
/// </summary>
public sealed class RecordingLlmProviderTests
{
    [Fact]
    public async Task Record_KeepsImageMetadataButNeverTheBytes()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var recorder = new CapturingRecorder();
        var provider = new RecordingLlmProvider(
            new StubProvider(), recorder, NullLogger<RecordingLlmProvider>.Instance);

        await provider.CompleteAsync(
            Request([new LlmImage("reference", new ImageContent(bytes, "image/png"))]),
            CancellationToken.None);

        var payload = recorder.Entry!.RequestPayload;
        Assert.Contains("reference", payload);
        Assert.Contains("image/png", payload);
        Assert.Contains("5", payload);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(bytes)), payload);
        Assert.DoesNotContain(Convert.ToBase64String(bytes), payload);
    }

    [Fact]
    public async Task Record_OmitsTheImageSectionForTextOnlyCalls()
    {
        var recorder = new CapturingRecorder();
        var provider = new RecordingLlmProvider(
            new StubProvider(), recorder, NullLogger<RecordingLlmProvider>.Instance);

        await provider.CompleteAsync(Request([]), CancellationToken.None);

        Assert.DoesNotContain("[images]", recorder.Entry!.RequestPayload);
    }

    // ─── 설정 ───

    private static LlmRequest Request(IReadOnlyList<LlmImage> images)
        => new(
            LlmCallContext.ForTask(
                Guid.NewGuid(), Guid.NewGuid(), TaskKind.Analyze,
                Guid.NewGuid(), Guid.NewGuid(), "m"),
            "system", "user", images, "{}");

    private sealed class StubProvider : ILlmProvider
    {
        public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
            => Task.FromResult(new LlmResult("{}", 1, 1));
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
}
