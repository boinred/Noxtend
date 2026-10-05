/**
 * Design Ref: §3.1 — 레이아웃 선호의 저장 계약.
 *
 * 지금은 localStorage 어댑터 하나뿐이지만, 사용자별 설정이 서버로 가면
 * 이 인터페이스를 구현한 Adapter 로 교체된다.
 * `ExecutionPort`·`GraphPersistence` 와 같은 방식이다.
 */
import type { SidebarState } from '@/domain/layout/types'

export interface LayoutPreferences {
  loadSidebarState(): SidebarState
  saveSidebarState(state: SidebarState): void
}
