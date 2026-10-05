/**
 * Design Ref: §3.1 — 사이드바 접힘은 사용자 선호다.
 *
 * UI 부수 상태였다면 컴포넌트 안에 두면 그만이지만,
 * 저장하고 복구하는 계약이 붙는 순간 도메인 개념이 된다.
 * 그래서 `GraphDocument` 와 분리된 자리에 둔다 (Plan §4.2 품질 기준 3).
 */
export type SidebarState = 'expanded' | 'collapsed'

export const DEFAULT_SIDEBAR_STATE: SidebarState = 'expanded'

/** 저장 값은 화이트리스트로만 받는다 (§7 저장 값 검증) */
export function isSidebarState(value: unknown): value is SidebarState {
  return value === 'expanded' || value === 'collapsed'
}

export function toggleSidebarState(state: SidebarState): SidebarState {
  return state === 'expanded' ? 'collapsed' : 'expanded'
}
