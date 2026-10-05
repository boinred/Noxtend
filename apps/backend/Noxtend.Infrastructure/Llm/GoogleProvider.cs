using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// Google Gemini 텍스트 어댑터 — Extract/Decompose 등 문면·JSON 생성용.
///
/// Design Ref: gemini-text-provider (2026-08-28) — 이미지 전용이던 Gemini를 텍스트
/// 공급자로도 쓸 수 있게 한다.
///
/// <see cref="Image.GoogleImageProvider"/>와 전송 방식(raw HTTP, `generateContent`
/// 엔드포인트, 헤더로 키 전달)은 같지만, 구조화 출력을 스키마로 강제하는 지점과
/// 시스템 프롬프트 전달 방식이 다르다 — Gemini는 `system_instruction`을 `contents`와
/// 별도 필드로 받는다.
///
/// **단계를 모른다** (§3.3 G-1). Anthropic·OpenAI 어댑터와 같은 이유다.
/// </summary>
public sealed class GoogleProvider(HttpClient http, string apiKey, string model) : ILlmProvider
{
    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var parts = new List<object> { new { text = request.User } };

        // 이미지는 0..4장 (background-similarity-tuning §7.2) — OpenAI·Anthropic 어댑터와
        // 같은 label 규칙: 각 장 바로 앞에 [image: name] 텍스트로 순서·역할을 못박는다
        foreach (var image in request.Images)
        {
            parts.Add(new { text = $"[image: {image.Name}]" });
            parts.Add(new
            {
                inline_data = new
                {
                    mime_type = image.Content.ContentType,
                    data = Convert.ToBase64String(image.Content.Bytes),
                },
            });
        }

        var payload = new
        {
            system_instruction = new { parts = new object[] { new { text = request.System } } },
            contents = new object[] { new { parts = parts.ToArray() } },

            // 구조화 출력. 문자열을 그대로 실으면 스키마가 무시될 수 있어 파싱해서 넣는다
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = JsonDocument.Parse(request.JsonSchema).RootElement,
            },
        };

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
        {
            Content = JsonContent.Create(payload),
        };
        message.Headers.Add("x-goog-api-key", apiKey);

        using var response = await ProviderHttp.SendAsync(http, message, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderCallFailedException(
                $"{(int)response.StatusCode}",
                isTransient: ProviderHttp.IsTransient(response.StatusCode));
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;

        var text = FirstText(root)
            ?? throw new ProviderBadResponseException("응답에 텍스트가 없습니다");

        var usage = root.TryGetProperty("usageMetadata", out var meta) ? meta : (JsonElement?)null;
        int? Tokens(string name) =>
            usage is { } m && m.TryGetProperty(name, out var value) && value.TryGetInt32(out var n)
                ? n
                : null;

        return new LlmResult(text, Tokens("promptTokenCount"), Tokens("candidatesTokenCount"));
    }

    /// <summary>
    /// 후보의 첫 텍스트 파트를 찾는다.
    ///
    /// 이미지 어댑터의 <see cref="Image.GoogleImageProvider"/>와 같은 이유로 파트를
    /// 순회한다 — 안전 분류기 코멘트 등 다른 파트가 텍스트보다 먼저 올 수 있어
    /// "첫 파트가 텍스트"라고 가정하면 그 조합에서만 깨진다.
    /// </summary>
    private static string? FirstText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return null;
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts))
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.GetString() is { } value)
                {
                    return value;
                }
            }
        }

        return null;
    }

}
