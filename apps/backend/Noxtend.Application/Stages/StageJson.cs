using System.Text.Json;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Stages;

/// <summary>
/// 단계들이 공유하는 JSON 읽기.
///
/// Design Ref: §6 — 파싱 실패는 <c>PROVIDER_BAD_RESPONSE</c> 다.
///
/// 구조화 출력을 요구하므로 코드 펜스는 오지 않아야 하지만, 모델이 감싸 보내는 일이
/// 여전히 있다. 재시도로 해결하기엔 비싼 실패라 벗겨낸다.
///
/// **원문을 예외 메시지에 싣지 않는다** (§4.2 #13) — 응답 본문에 무엇이 섞여 있을지 모른다.
/// 원문 자체는 내역(`LlmCalls.ResponsePayload`)에 남으므로 진단은 거기서 한다.
/// </summary>
internal static class StageJson
{
    public static JsonDocument Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ProviderBadResponseException("공급자 응답이 비어 있습니다");
        }

        try
        {
            return JsonDocument.Parse(StripCodeFence(raw));
        }
        catch (JsonException ex)
        {
            throw new ProviderBadResponseException($"응답을 해석할 수 없습니다: {ex.GetType().Name}");
        }
    }

    public static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new ProviderBadResponseException($"{name} 이 없습니다");
        }

        return element.GetString()!.Trim();
    }

    /// <summary>있으면 다듬어 내고 없으면 null — 옛 프롬프트 응답과 함께 살아야 하는 필드용.</summary>
    public static string? OptionalString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(element.GetString())
                ? element.GetString()!.Trim()
                : null;

    public static double RequiredNumber(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number)
        {
            throw new ProviderBadResponseException($"{name} 이 없거나 숫자가 아닙니다");
        }

        return element.GetDouble();
    }

    public static int RequiredInt(JsonElement parent, string name)
        => (int)RequiredNumber(parent, name);

    public static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Object)
        {
            throw new ProviderBadResponseException($"{name} 이 없습니다");
        }

        return element;
    }

    public static IReadOnlyList<string> StringArray(JsonElement parent, string name, bool required)
    {
        if (!parent.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            if (required)
            {
                throw new ProviderBadResponseException($"{name} 배열이 없습니다");
            }

            return [];
        }

        return [.. element
            .EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())];
    }

    /// <summary>
    /// 장면 명세를 프롬프트에 끼울 문자열로.
    ///
    /// **구조화된 값을 그대로 직렬화한다.** 사람이 읽을 요약을 따로 저장하지 않기로 했으므로
    /// (§2.3-2) 여기서 파생한다 — 두 번째 진실을 만들지 않는다.
    /// </summary>
    public static string Describe(SceneSpec scene) => JsonSerializer.Serialize(scene,
        new JsonSerializerOptions
        {
            WriteIndented = true,
            // 한글이 \uXXXX 로 부풀면 프롬프트 토큰이 3배가 된다
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    private static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
        {
            return trimmed;
        }

        var body = trimmed[(firstNewline + 1)..];
        var closing = body.LastIndexOf("```", StringComparison.Ordinal);

        return closing < 0 ? body.Trim() : body[..closing].Trim();
    }
}
