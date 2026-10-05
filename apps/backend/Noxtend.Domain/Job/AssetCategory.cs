namespace Noxtend.Domain.Job;

/// <summary>
/// 에셋 카테고리 — 캐릭터 · 소품 · 배경.
///
/// Design Ref: §2.4 — 실행 백본은 이 값을 읽지 않는다. 큐·워커·오케스트레이터·스위퍼는
/// 카테고리를 모르고, 나뉘는 것은 공정 구성(어떤 단계를 어떤 순서로)뿐이다.
/// 스튜디오가 3번 반복되므로 백본에 배경을 하드코딩하면 복사본이 생긴다.
/// </summary>
public enum AssetCategory
{
    Character,
    Object,
    Background,
}
