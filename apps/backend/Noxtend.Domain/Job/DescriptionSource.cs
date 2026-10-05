namespace Noxtend.Domain.Job;

/// <summary>
/// 파츠 서술의 출처 (review-gate-staged 사이클 1).
///
/// 재승인이 사람 서술을 덮지 않게 하는 근거이자 서술 확인 화면 칩의 원천이다.
/// </summary>
public enum DescriptionSource
{
    // 분해 결과
    Model,

    // 서술 재작성 공정 결과
    Rewritten,

    // 검수자가 직접 씀
    Human,
}
