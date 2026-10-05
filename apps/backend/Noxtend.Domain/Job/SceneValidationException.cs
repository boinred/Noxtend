namespace Noxtend.Domain.Job;

/// <summary>
/// 장면 명세에 조립을 좌우하는 필드가 없다.
///
/// Design Ref: §4.2 · FR-05 · Plan D-13
///
/// **`ProviderBadResponseException` 과 구분하는 이유.** 둘 다 "응답이 기대와 다르다"
/// 이지만 사용자가 할 일이 다르다:
///
/// - `PROVIDER_BAD_RESPONSE` — JSON 이 깨졌거나 형식이 아예 어긋났다. 대개 일시적이고
///   다시 돌리면 된다.
/// - `SCENE_INCOMPLETE` — 형식은 맞는데 `camera`·`light`·`scaleReference` 가 없다.
///   **프롬프트를 고쳐야 하는 문제다.** 다시 돌려도 같은 프롬프트면 같은 결과가 나온다.
///
/// Check 단계에서 이 구분이 코드에 없다는 것이 드러났다 — 설계 §4.2 는 두 코드를
/// 문서화했는데 실제로는 전부 `PROVIDER_BAD_RESPONSE` 로 나가고 있었다 (G-1).
/// </summary>
public sealed class SceneValidationException(string missingField)
    : Exception($"장면 명세에 {missingField} 이(가) 없습니다 — 파츠를 따로 그려 합칠 수 없습니다")
{
    /// <summary>없는 필드 이름. 화면이 "무엇을 프롬프트에 추가해야 하나" 를 안내한다.</summary>
    public string MissingField { get; } = missingField;
}
