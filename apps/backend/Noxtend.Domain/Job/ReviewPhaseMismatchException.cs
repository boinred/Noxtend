namespace Noxtend.Domain.Job;

/// <summary>
/// 검수 단계가 맞지 않는 동작 (review-gate-staged 사이클 1).
///
/// <see cref="InvalidOperationException"/> 을 상속하되 타입을 나눈다 — 핸들러가 이미
/// <see cref="InvalidOperationException"/> 을 "검수 대기 아님" 으로 잡으므로 구분이 필요하다.
/// </summary>
public sealed class ReviewPhaseMismatchException(ReviewPhase expected, ReviewPhase actual)
    : InvalidOperationException($"검수 단계가 맞지 않습니다. 필요: {expected}, 현재: {actual}")
{
    public ReviewPhase Expected { get; } = expected;
    public ReviewPhase Actual { get; } = actual;
}
