using System.Text.RegularExpressions;

namespace Noxtend.Infrastructure.Persistence.Serialization;

/// <summary>
/// 구형 팔레트 문자열에서 색을 읽어낸다 (Design §4.4 · D-06).
///
/// **순수 함수다.** 조회 경로에서 도는 코드이므로 LLM 이나 이미지 API 를 부르지 않는다.
/// 같은 배포 버전에서 같은 입력은 항상 같은 색을 낸다 — 그러지 않으면 같은 작업이 볼
/// 때마다 다른 색으로 보인다.
///
/// **모르는 표현에는 기본색을 넣지 않는다.** 회색쯤 채워 두면 틀린 색이 사실처럼 보이고,
/// 사용자는 그것을 원본의 색으로 믿는다. 모르면 `null` 이 정직하다.
///
/// 여기서 나오는 값은 자연어가 말한 색의 **대표값**이지 원본 픽셀을 복원한 것이 아니다.
/// 정확한 복원이 필요하면 원본 이미지 재분석이 별도 기능으로 있어야 한다.
/// </summary>
internal static class LegacyPaletteColorResolver
{
    /// <summary>문자열 어딘가에 박힌 6자리 HEX. 있으면 이것이 가장 정확한 근거다.</summary>
    private static readonly Regex EmbeddedHex =
        new("#[0-9A-Fa-f]{6}", RegexOptions.Compiled);

    /// <summary>
    /// 등록된 한국어 색상어와 대표 HEX.
    ///
    /// **실제 레거시 데이터에서 뽑았다.** 설계가 든 예시 표만 옮겼을 때는 개발 DB 의
    /// 팔레트 값 145개 중 32%가 미해결로 남았다 — `청록색` · `황록색` · `베이지` 처럼
    /// 흔한 표현이 통째로 빠져 화면의 3분의 1이 미확정 칩이 됐다.
    ///
    /// **여기 없는 표현에 색을 지어내지 않는다** (FR-12). 사전을 넓히는 것과 아무 색이나
    /// 채우는 것은 다르다 — 틀린 색이 사실처럼 보이는 쪽이 빈 칩보다 나쁘다.
    ///
    /// 값은 그 낱말이 가리키는 색의 **대표값**이지 원본 픽셀이 아니다.
    /// </summary>
    private static readonly (string Phrase, string Hex)[] ColorWords =
    [
        // ── 무채색 ── 복합어가 먼저 걸려야 한다. `청회색` 을 `회색` 으로 읽으면 푸른 기가 사라진다
        ("청회색", "#6B7C85"),
        ("회청색", "#6B7C85"),
        ("회백색", "#808080"),
        ("회갈색", "#8A7A6A"),
        ("차콜", "#36393B"),
        ("회색", "#808080"),
        ("백색", "#FFFFFF"),
        ("흰색", "#FFFFFF"),
        ("하얀", "#FFFFFF"),
        ("검정", "#000000"),
        ("흑색", "#000000"),
        ("검은", "#000000"),

        // ── 청록·청 계열 ── 물과 발광에 가장 많이 쓰인다
        ("터키석", "#40C7C7"),
        ("청록색", "#17807E"),
        ("청록", "#17807E"),
        ("시안", "#22B8CF"),
        ("남청색", "#1F3A6E"),
        ("청람색", "#2B3A6B"),
        ("하늘색", "#87BCE8"),
        ("청색", "#2E5C8A"),
        ("파란", "#2E5C8A"),

        // ── 녹 계열 ── `황록색` 은 `녹색` 을 포함하지 않으므로 순서 다툼이 없다
        ("저채도 녹색", "#70856A"),
        ("올리브 그린", "#6B7043"),
        ("올리브", "#6B7043"),
        ("모스 그린", "#6B7A4A"),
        ("이끼색", "#6B7A4A"),
        ("이끼빛", "#6B7A4A"),
        ("이끼", "#6B7A4A"),
        ("황록색", "#9AAB4E"),
        ("녹색", "#4A7C59"),
        ("그린", "#4A7C59"),

        // ── 난색 ──
        ("황금색", "#C9A227"),
        ("황금빛", "#C9A227"),
        ("황토색", "#B08D57"),
        ("베이지", "#D9C7A7"),
        ("모래색", "#D9C7A0"),
        ("크림색", "#F1E4C3"),
        ("갈색", "#6B4F3A"),
        ("노란", "#E8C547"),
        ("노랑", "#E8C547"),
    ];

    /// <summary>긴 구문 우선으로 미리 정렬해 호출마다 다시 정렬하지 않는다.</summary>
    private static readonly (string Phrase, string Hex)[] ByLongestPhrase =
        [.. ColorWords.OrderByDescending(entry => entry.Phrase.Length)];

    /// <summary>
    /// 설명 문자열의 대표 색. 알아볼 수 없으면 <c>null</c>.
    /// </summary>
    public static string? Resolve(string description)
    {
        if (EmbeddedHex.Match(description) is { Success: true } match)
        {
            return match.Value.ToUpperInvariant();
        }

        foreach (var (phrase, hex) in ByLongestPhrase)
        {
            if (description.Contains(phrase, StringComparison.Ordinal))
            {
                return hex;
            }
        }

        return null;
    }

    /// <summary>
    /// 구형 문자열 하나를 팔레트 칸으로 올린다.
    ///
    /// 이름에서는 뒤에 붙은 HEX 를 떼어낸다 — `"안개 회색 #8B8980"` 의 이름은 `"안개 회색"`
    /// 이지 HEX 까지 포함한 문장이 아니다. 다만 값이 HEX 뿐이었다면 지어낼 이름이 없으므로
    /// 그대로 이름으로 쓴다.
    /// </summary>
    public static (string Name, string? Hex)? Promote(string legacy)
    {
        var trimmed = legacy.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var hex = Resolve(trimmed);
        var name = EmbeddedHex.Replace(trimmed, string.Empty).Trim();

        return (name.Length == 0 ? trimmed : name, hex);
    }
}
