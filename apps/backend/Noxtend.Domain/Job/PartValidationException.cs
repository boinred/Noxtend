namespace Noxtend.Domain.Job;

/// <summary>
/// 분해 결과가 명백히 잘못됐다.
///
/// Design Ref: §6 · FR-05 · Plan D-9
///
/// **유효성은 품질 판단과 다르다.** 여기서 걸리는 것은 전부 기계가 판정할 수 있는
/// 오류이고, 사람의 눈은 "서술이 쓸 만한가" 에 써야 한다.
///
/// 예외인 이유는 이것이 정상 흐름이 아니기 때문이다. 검증 실패를 `Result` 로 돌려주는
/// 유스케이스 경계와 달리, 여기는 공급자가 계약을 어긴 경우라 공정 실패로 이어진다.
/// </summary>
public sealed class PartValidationException(PartValidationError error, string message)
    : Exception(message)
{
    /// <summary>유스케이스가 이 값으로 `ErrorCode` 를 고른다 — 메시지 문자열로 분기하지 않게.</summary>
    public PartValidationError Error { get; } = error;
}

public enum PartValidationError
{
    /// <summary>분해가 추출과 다른 파츠 이름을 냈다.</summary>
    NameMismatch,

    /// <summary>
    /// 같은 파츠를 두 번 냈다.
    ///
    /// **`NameMismatch` 와 가르는 이유**: 이름은 하나도 안 틀렸는데 개수만 맞지 않는
    /// 경우가 실제로 나왔다. "이름이 다르다" 는 안내를 받은 사용자는 이름 지시를
    /// 고치려 들지만, 실제로 고칠 것은 "각 파츠를 한 번씩만" 이다.
    /// </summary>
    Duplicate,

    /// <summary>좌표가 정규화 범위를 벗어났다.</summary>
    BoundsOutOfRange,

    /// <summary><c>occludedBy</c> 가 없는 파츠를 가리킨다.</summary>
    UnknownReference,

    /// <summary>깊이 순서가 중복됐다.</summary>
    DepthDuplicate,

    /// <summary>
    /// 검수 화면에서 "이 파츠를 가린다" 고 지목한 대상이 실제로는 겹치지 않는다
    /// (occludedby-recompute §함정9).
    ///
    /// 통과시키면 그 파츠의 서술이 근거 없이 깎인다 — 화면이 본 겹침 목록과 저장되는
    /// 가림 관계가 어긋나므로 서버가 겹침을 다시 판정해 거른다.
    /// </summary>
    NotOverlapping,

    /// <summary>서술이 빈 파츠가 있다 — 빈 서술로 유료 생성이 나가지 않게 (review-gate-staged 사이클 1).</summary>
    DescriptionEmpty,

    /// <summary>팔레트 개수(3~8)·이름·Hex(대문자 <c>#RRGGBB</c>) 위반.</summary>
    PaletteInvalid,
}
