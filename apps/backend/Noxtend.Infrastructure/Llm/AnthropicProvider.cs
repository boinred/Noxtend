using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// Anthropic 어댑터 — 공식 SDK 사용.
///
/// Design Ref: §3.2 · §4.2 #13
///
/// **평문 키는 이 객체 안에서만 산다.** Factory 가 복호화해 넘기고, 클라이언트 옵션에
/// 실린 뒤 어디에도 기록되지 않는다. 예외 메시지에도 넣지 않는다 (§2.2).
///
/// raw HttpClient 대신 SDK 를 쓰는 이유는 편의가 아니라 **API 변화를 따라가는 비용**이다.
/// thinking·구조화 출력·거절 처리가 타입으로 드러나므로, 요청 모양이 바뀌면 컴파일이
/// 깨져서 알려준다 — 손으로 만든 JSON 은 런타임에야 알려준다.
///
/// **단계를 모른다** (§3.3 G-1). 프롬프트·스키마를 인자로 받아 원문 JSON 을 돌려줄 뿐이라,
/// 넷째·다섯째 단계가 붙어도 이 파일은 바뀌지 않는다.
///
/// OpenAI 와 프롬프트는 같고 전송 형식만 다르다. 공급자를 바꿔도 같은 작업이라는
/// 전제가 여기서 지켜진다.
/// </summary>
public sealed class AnthropicProvider : ILlmProvider
{
    /// <summary>
    /// thinking 과 응답 텍스트를 **합쳐서** 제한하는 값이다.
    ///
    /// 최신 모델은 thinking 이 기본으로 켜져 있어, 작게 잡으면 thinking 이 예산을 먹고
    /// 응답이 중간에 잘린다. 잘린 JSON 은 파서에서 형식 위반으로 떨어지므로
    /// **어댑터가 고장난 것처럼 보이지만 실제로는 예산 부족**이다.
    /// 추출 결과는 프롬프트 몇 문장 + 파츠 이름 십여 개라 본문 자체는 작다.
    /// </summary>
    private const int MaxTokens = 8192;

    private readonly AnthropicClient _client;
    private readonly string _model;

    public AnthropicProvider(string apiKey, string model)
    {
        _client = new AnthropicClient(new ClientOptions
        {
            ApiKey = apiKey,
            // 공급자 호출이 10분 이상 걸릴 수 있다 (§2.2). SDK 기본값으로는 정상 호출이 끊긴다
            Timeout = TimeSpan.FromMinutes(20),
        });

        _model = model;
    }

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        Message message;
        try
        {
            message = await _client.Messages.Create(BuildRequest(request), cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 클라이언트에는 종류만. 원문은 InnerException 으로 로그에 남는다 (§4.2 #13)
            throw new ProviderCallFailedException(
                ex.GetType().Name, ex, isTransient: IsTransient(ex));
        }

        // 안전 분류기가 요청을 거절하면 HTTP 200 에 stop_reason=refusal 로 온다.
        // Content 를 먼저 읽는 코드는 여기서 빈 목록을 만나 엉뚱한 곳에서 터진다
        if (message.StopReason == StopReason.Refusal)
        {
            throw new ProviderCallFailedException("공급자가 요청을 거절했습니다");
        }

        // 해석하지 않는다. 기대한 모양인지는 단계가 판단한다 (§9.2)
        return new LlmResult(
            FirstText(message) ?? throw new ProviderBadResponseException("응답에 텍스트가 없습니다"),
            message.Usage?.InputTokens is { } input ? (int)input : null,
            message.Usage?.OutputTokens is { } output ? (int)output : null);
    }

    private MessageCreateParams BuildRequest(LlmRequest request) => new()
    {
        Model = _model,
        MaxTokens = MaxTokens,
        System = request.System,

        // 구조화 출력. 프롬프트로 "JSON 만 달라" 부탁하는 것과 달리 스키마가 강제되므로,
        // 파서의 코드펜스 벗기기는 방어용으로만 남는다 (§6 PROVIDER_BAD_RESPONSE)
        OutputConfig = new OutputConfig
        {
            Format = new JsonOutputFormat
            {
                Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    request.JsonSchema)!,
            },
        },

        Messages = [new MessageParam { Role = "user", Content = BuildContent(request) }],
    };

    /// <summary>
    /// 이미지는 0..4장 (§7.2). OpenAI 어댑터와 같은 label 규칙 — 각 장 바로 앞에
    /// <c>[image: name]</c> 텍스트를 넣어 여러 장의 순서·역할을 못박는다.
    /// </summary>
    private static List<ContentBlockParam> BuildContent(LlmRequest request)
    {
        var blocks = new List<ContentBlockParam>();

        foreach (var image in request.Images)
        {
            blocks.Add(new TextBlockParam { Text = $"[image: {image.Name}]" });
            blocks.Add(new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    MediaType = image.Content.ContentType,
                    Data = Convert.ToBase64String(image.Content.Bytes),
                },
            });
        }

        blocks.Add(new TextBlockParam { Text = request.User });
        return blocks;
    }

    /// <summary>
    /// 다시 걸어볼 만한 실패인가.
    ///
    /// SDK 가 상태 코드를 예외 **타입**으로 나눠 준다. `RateLimit` 은 "지금 말고 나중에",
    /// `Service`(5xx)는 공급자 쪽 사정, `IO` 는 네트워크다 — 셋 다 다시 걸면 된다.
    /// `Unauthorized`·`BadRequest`·`Forbidden` 은 몇 번을 걸어도 같다.
    ///
    /// 타입 이름으로 판별하는 것이 마음에 들지는 않지만, SDK 가 상태 코드를 공개
    /// 속성으로 내주지 않는다. 새 예외 타입이 생기면 보수적으로 "영구" 로 떨어져
    /// 무한 재시도가 아니라 즉시 실패가 된다 — 틀렸을 때 덜 나쁜 쪽이다.
    /// </summary>
    private static bool IsTransient(Exception ex)
        => ex.GetType().Name is "AnthropicRateLimitException"
            or "AnthropicServiceException"
            or "AnthropicIOException"
            || ex is HttpRequestException or TimeoutException;

    /// <summary>응답의 첫 텍스트 블록. thinking 블록이 앞에 올 수 있어 순회한다.</summary>
    private static string? FirstText(Message message)
    {
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var text))
            {
                return text.Text;
            }
        }

        return null;
    }
}
