namespace Noxtend.Domain.Common;

/// <summary>
/// 돌려줄 값이 없다는 뜻.
///
/// **`Result&lt;bool&gt;` 로 성공을 표현하지 않는 이유는 그 `bool` 이 늘 `true` 이기
/// 때문이다.** 늘 같은 값을 돌려주는 계약은 읽는 사람에게 "언제 `false` 인가" 를 묻게
/// 하고, 그 답이 "없다" 라면 그 자리는 값이 아니라 잡음이다.
///
/// 전송 계층에서는 204 No Content 로 나간다.
/// </summary>
public readonly record struct Unit
{
    public static Unit Value => default;
}
