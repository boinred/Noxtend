using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Image;

/// <summary>
/// 이미지 공급자 호출을 감싸 내역을 남긴다.
///
/// Design Ref: §2.3 A-5 · G-4 · NFR-05
///
/// <c>RecordingLlmProvider</c> 와 같은 구조다. **내역은 같은 표로 간다** — Option B 의
/// 분리는 실행 경로에 대한 것이지 관측 데이터에 대한 것이 아니다. "이 작업이 무엇을 보내
/// 무엇을 받았나" 는 하나의 질문이고 화면도 하나다 (FR-18).
///
/// **어댑터는 이 존재를 모른다.** <c>OpenAiImageProvider</c> 는 순수하고 DI 가 감쌀 뿐이다.
///
/// **API 키가 들어올 경로가 없다** (NFR-05): 여기서 보는 것은 프롬프트와 크기뿐이고,
/// 키는 어댑터 생성자 안에서만 존재한다.
///
/// **응답 바이트를 남기지 않는다** (NFR-10). Blob 에 이미 있고, 담으면 표가 수백 배로 부푼다.
/// </summary>
internal sealed class RecordingImageProvider(
    IImageProvider inner,
    ILlmCallRecorder recorder,
    ILogger<RecordingImageProvider> logger) : IImageProvider
{
    public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        // 요청 본문은 성공·실패 양쪽에서 같으므로 미리 만든다.
        // 참조 이미지는 담지 않는다 — StoredImage 에 이미 있다
        var payload = $"[prompt]\n{request.Prompt}\n\n[size] {request.Size}"
                    + $"\n[reference] {(request.Reference.Count == 0 ? "없음" : $"{request.Reference.Count}장")}";

        if (request.Background is { } background)
            payload += $"\n[background] {background}\n[output_format] png";

        var context = ToCallContext(request.Context);

        try
        {
            var result = await inner.GenerateAsync(request, ct);

            await RecordAsync(
                new LlmCallEntry(
                    context, payload,
                    // 바이트 대신 무엇이 왔는지만 남긴다 — 진단에 필요한 것은 형식과 크기다
                    ResponsePayload: $"{result.ContentType} · {result.Bytes.Length}바이트",
                    // 공급자가 준 실측 — 참조 원본의 입력 토큰까지 잡힌다 (D-5)
                    result.InputTokens, result.OutputTokens,
                    (int)stopwatch.ElapsedMilliseconds, Succeeded: true, FailureReason: null,
                    OutputImages: result.ImageCount));

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
                    context, payload, ResponsePayload: null,
                    InputTokens: null, OutputTokens: null,
                    (int)stopwatch.ElapsedMilliseconds, Succeeded: false,
                    // 원문이 아니라 정규화된 사유다 (§4.2 #13 유지)
                    FailureReason: ex is ProviderCallFailedException or ProviderBadResponseException
                        ? ex.Message
                        : ex.GetType().Name,
                    OutputImages: null));

            throw;
        }
    }

    /// <summary>
    /// 이미지 호출 맥락을 공통 내역 맥락으로 옮긴다.
    ///
    /// 단계는 요청 맥락을 따른다. <c>PartId</c> 는 싣지 않는다 —
    /// 파츠·sprite asset/index는 공정이 가리키므로 화면이 <c>TaskId</c> 로
    /// 이어 붙이면 된다. 두 곳에 두면 재생성 때 어긋난다.
    /// </summary>
    private static LlmCallContext ToCallContext(ImageCallContext context)
        => context.SourceGenerationId is { } id
            ? LlmCallContext.ForSourceGeneration(id, context.PromptVersionId, context.ProviderConfigId, context.Model)
            : LlmCallContext.ForTask(context.JobId!.Value, context.TaskId!.Value, context.Kind,
                context.PromptVersionId, context.ProviderConfigId, context.Model);

    /// <summary>
    /// 기록 실패는 삼킨다 (G-4 · NFR-06).
    ///
    /// **취소 토큰을 받지 않는 것이 의도적이다.** 공정이 취소돼도 그때까지의 호출은
    /// 일어난 사실이므로 남아야 한다 — 이미지 호출은 특히 비싸서 취소된 실행이 내역에서
    /// 사라지면 비용이 설명되지 않는다.
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
                "공정 {TaskId} 의 이미지 호출 내역을 남기지 못했습니다 (공정은 계속됩니다)",
                entry.Context.TaskId);
        }
    }
}
