namespace Noxtend.Domain.Job;

/// <summary>
/// 캐릭터의 성별 — 베이스바디 구성을 가른다.
///
/// Design Ref: character-studio §D-01 — 캐릭터에서만 필수이고 타 카테고리는 보내지 않는다.
/// 그래서 <see cref="PipelineJob.Gender"/> 는 nullable 이고, "캐릭터면 필수" 규칙은 접수
/// 검증(§3.2)이 지킨다. 실행 백본은 이 값을 읽지 않는다 — 프롬프트 변수로만 흐른다.
///
/// 확장은 additive 다(§11-3) — 중성 등을 더해도 기존 값 순서는 그대로다.
/// </summary>
public enum Gender
{
    Male,
    Female,
}
