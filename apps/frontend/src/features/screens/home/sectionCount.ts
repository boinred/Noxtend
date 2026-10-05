/**
 * 섹션 제목 옆에 붙는 개수.
 *
 * **크게 그린 한 건을 빼먹지 않는다.** "실행 중" 은 맨 앞 작업을 따로 크게 그리고
 * 나머지만 목록으로 넘기므로, 목록 길이만 세면 화면에 두 건이 보이는데 "1" 이 뜬다.
 * 세는 규칙을 화면 밖으로 빼서 두 섹션이 같은 것을 쓰게 한다.
 */
export function sectionCount(section: { jobs?: unknown[]; hasFeatured?: boolean }): number {
  return (section.jobs?.length ?? 0) + (section.hasFeatured === true ? 1 : 0)
}

/**
 * 배지 문구.
 *
 * **목록은 상한까지만 싣는다.** 그래서 하나를 지우면 다음 것이 올라와 보이는 수가
 * 그대로다 — 그것만 보면 삭제가 안 된 줄로 읽힌다. 전체가 더 많을 때만 둘 다 써서
 * "더 있다" 를 화면에서 설명한다.
 *
 * 전체가 보이는 수보다 작게 오는 순간이 있다 — 삭제 직후 목록 캐시는 남고 전체만
 * 먼저 줄어드는 경우다. 그때 "10 / 9" 를 그리면 고장으로 읽히므로 하나만 쓴다.
 */
export function sectionCountLabel(shown: number, total: number | undefined): string {
  return typeof total === 'number' && total > shown ? `${shown} / ${total}` : String(shown)
}
