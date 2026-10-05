namespace Noxtend.Domain.Job;

/// <summary>
/// 검수 게이트 단계 (review-gate-staged). <see cref="PipelineJob.RequiresReview"/> 인 작업에서만 의미가 있다.
///
/// bool 을 단계마다 늘리지 않고 값 하나로 둔다 — 되돌리기에서 플래그 하나를 빠뜨려
/// "승인 전인데 다음 단계 확정" 같은 불가능한 조합이 저장되는 것을 막는다.
/// 문자열로 저장하므로 단계 값을 추가해도 스키마가 바뀌지 않는다.
/// </summary>
public enum ReviewPhase
{
    // 상자 검수 중 (승인 전)
    Boxes,

    // 서술 확인 중 — 상자 확정·재작성 뒤, 생성 전
    Descriptions,

    // 검수 통과, 생성 진행
    Approved,
}
