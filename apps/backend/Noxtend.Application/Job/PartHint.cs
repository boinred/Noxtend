using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Noxtend.Domain.Job;

namespace Noxtend.Application.Job;

/// <summary>
/// 파츠 힌트 한 줄 — API·프롬프트 경계의 값 (character-studio §D-02).
///
/// <paramref name="Type"/> 상위 종류(예: "팔찌"), <paramref name="Count"/> 개수,
/// <paramref name="Variant"/> 변형(예: "손목형", 없으면 null). 도메인 저장은 이 배열의
/// JSON 문자열이고 백본은 해석하지 않는다.
/// </summary>
public sealed record PartHint(string Type, int Count, string? Variant);

/// <summary>
/// 파츠 힌트의 JSON 직렬화·역직렬화 (character-studio §4.3).
///
/// **Application 계층에 둔다.** `BuildVariables` 가 Application 에 있고 Application 은
/// Infrastructure 를 참조할 수 없어 Persistence 계층의 직렬화기를 쓸 수 없다.
/// <c>System.Text.Json</c> 은 계층 제약이 없으므로 여기 작은 헬퍼로 둔다.
/// 접수(`StartJobHandler`)가 직렬화하고, 스테이지가 역직렬화·렌더(slice 3)한다.
/// </summary>
public static class PartHintCodec
{
    // 한글 종류명을 \uXXXX 로 굽지 않아 저장·디버깅 시 읽을 수 있게 한다. HTML 민감 문자는
    // 그대로 이스케이프하므로 UnsafeRelaxed 가 아니다 (DB·프롬프트 저장용이라 위험도 낮다).
    // 대소문자 무시 — 외부(프론트·ASP.NET camelCase)에서 온 JSON 도 값이 조용히 사라지지 않게 한다
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 힌트 목록을 저장용 JSON 문자열로 굳힌다. 빈 목록·null 은 <c>null</c> —
    /// "없음" 폴백은 렌더 단계(slice 3)가 만든다.
    /// </summary>
    public static string? Serialize(IReadOnlyList<PartHint>? hints)
        => hints is null || hints.Count == 0 ? null : JsonSerializer.Serialize(hints, Options);

    /// <summary>JSON 문자열을 힌트 목록으로 되돌린다. null·빈 문자열은 빈 목록.</summary>
    public static IReadOnlyList<PartHint> Deserialize(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<PartHint>>(json, Options) ?? [];

    /// <summary>
    /// 저장 JSON 을 사람이 읽는 줄글로 렌더한다 (character-studio §4.3, slice 3).
    ///
    /// 변형이 있으면 <c>"종류(변형) 개수"</c>, 없으면 <c>"종류 개수"</c> 로 굳히고 항목은
    /// <c>", "</c> 로 잇는다(예: <c>"팔찌 3, 장갑(손목형) 2"</c>). 빈 목록·null 은
    /// <see cref="CharacterVariables.Absent"/> — 프롬프트 변수의 "없음" 폴백과 일치한다.
    /// </summary>
    public static string Render(string? json)
    {
        var hints = Deserialize(json);
        if (hints.Count == 0)
        {
            return CharacterVariables.Absent;
        }

        return string.Join(", ", hints.Select(hint =>
            hint.Variant is null ? $"{hint.Type} {hint.Count}" : $"{hint.Type}({hint.Variant}) {hint.Count}"));
    }
}

/// <summary>
/// 캐릭터 고유 프롬프트 변수 렌더 (character-studio §4.2 · §D-03).
///
/// **값이 없어도 키는 항상 넣는다.** 조건부로 더하면 시드의 <c>{{...}}</c> 가 치환되지 않아
/// <see cref="Noxtend.Domain.Prompt.PromptTemplate.Render"/> 가 예외를 던지고, 그 단계 실행이
/// 작업 전체 실패로 이어진다. 그래서 값이 없으면 빈 문자열이 아니라 모델이 읽고 무시할 수 있는
/// <see cref="Absent"/> 로 폴백한다.
/// </summary>
public static class CharacterVariables
{
    /// <summary>값이 없을 때의 폴백 — 성별·힌트 양쪽이 같은 문자열을 쓴다.</summary>
    public const string Absent = "없음";

    /// <summary>성별 프롬프트 값 — enum 을 소문자로("male"/"female"), 없으면 "없음".</summary>
    public static string GenderText(Gender? gender)
        => gender?.ToString().ToLowerInvariant() ?? Absent;
}
