using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// 공급자 호출을 감싸 내역을 남긴다.
///
/// Design Ref: §2.0 단서 · §2.3-4 · G-4
///
/// **왜 데코레이터인가.** 설계 원안(Option B)은 튜닝 유스케이스가 기록하는 것이었으나,
/// 그러면 세 단계 핸들러가 각자 `try { record } catch { }` 를 반복하고 **한 곳만
/// 빠뜨려도 조용히 기록이 안 남는다.** 여기 한 곳에 두면 G-4 가 구조로 보장된다.
///
/// **어댑터는 이 존재를 모른다.** `AnthropicProvider` 는 계속 순수하고, DI 가 이것으로
/// 감쌀 뿐이다 — `Llm:UseFake` 스위치도 그대로 산다.
///
/// **기록 실패를 삼킨다.** 관측을 위해 넣은 장치가 시스템을 더 약하게 만들면 안 된다.
/// 대신 로그에 경고를 남겨, 기록이 조용히 사라지는 것도 알아챌 수 있게 한다.
/// </summary>
internal sealed class RecordingLlmProvider(
    ILlmProvider inner,
    ILlmCallRecorder recorder,
    ILogger<RecordingLlmProvider> logger) : ILlmProvider
{
    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        // 요청 본문은 성공·실패 양쪽에서 같으므로 미리 만든다.
        // 이미지 바이트는 담지 않는다 — 표가 수십 배로 부푼다 (§7 S-3).
        // 대신 이름·형식·크기·hash 로 "무엇을 보냈나" 는 추적 가능하게 남긴다 (§7.2)
        var payload = $"[system]\n{request.System}\n\n[user]\n{request.User}";
        if (request.Images.Count > 0)
        {
            var lines = request.Images.Select(image =>
                $"{image.Name} {image.Content.ContentType} {image.Content.Bytes.Length}B " +
                $"sha256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.Content.Bytes))}");
            payload += $"\n\n[images]\n{string.Join('\n', lines)}";
        }

        try
        {
            var result = await inner.CompleteAsync(request, ct);

            await RecordAsync(
                new LlmCallEntry(
                    request.Context, payload, result.RawJson,
                    result.InputTokens, result.OutputTokens,
                    (int)stopwatch.ElapsedMilliseconds, Succeeded: true, FailureReason: null));

            return result;
        }
        catch (OperationCanceledException)
        {
            // 취소는 실패가 아니다. 사용자의 결정이므로 실패로 기록하면 통계가 왜곡된다
            throw;
        }
        catch (Exception ex)
        {
            await RecordAsync(
                new LlmCallEntry(
                    request.Context, payload, ResponsePayload: null,
                    InputTokens: null, OutputTokens: null,
                    (int)stopwatch.ElapsedMilliseconds, Succeeded: false,
                    // 원문이 아니라 정규화된 사유다 (§4.2 #13 유지)
                    FailureReason: ex is ProviderCallFailedException or ProviderBadResponseException
                        ? ex.Message
                        : ex.GetType().Name));

            throw;
        }
    }

    /// <summary>
    /// 기록 실패는 삼킨다 (G-4).
    ///
    /// **취소 토큰을 받지 않는 것이 의도적이다.** 공정이 취소돼도 그때까지의 호출은
    /// 일어난 사실이므로 남아야 한다. 취소된 실행이 내역에서 통째로 사라지면
    /// "왜 이 작업은 기록이 없나" 라는 답 없는 질문이 생긴다.
    /// </summary>
    private async Task RecordAsync(LlmCallEntry entry)
    {
        try
        {
            await recorder.RecordAsync(entry, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "공정 {TaskId} 의 LLM 내역을 남기지 못했습니다 (공정은 계속됩니다)",
                entry.Context.TaskId);
        }
    }
}
