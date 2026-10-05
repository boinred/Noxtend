/**
 * Design Ref: §2.2, §3.1 — 사이드바 접힘 상태.
 *
 * `useState` 초기화 함수는 첫 렌더에 한 번만 실행된다. 저장된 값을 여기서 읽으면
 * 첫 페인트부터 올바른 폭으로 그려진다 (§4.2 #5, Plan NFR "레이아웃 점프 없음").
 *
 * 테마와 달리 부트스트랩 스크립트가 필요 없다. 테마는 `<html>` 속성이라
 * React 마운트보다 먼저 확정돼야 하지만, 사이드바는 React 가 그리는 DOM 이라
 * 첫 렌더 이전에 존재하지 않는다.
 */
import { createContext, useCallback, useContext, useState } from 'react'
import { DEFAULT_SIDEBAR_STATE, toggleSidebarState } from '@/domain/layout/types'
import type { SidebarState } from '@/domain/layout/types'
import type { LayoutPreferences } from '@/domain/layout/port'

/** 구현체는 providers.tsx 에서만 고른다 (§9.3 유일한 조립 지점) */
export const LayoutPreferencesContext = createContext<LayoutPreferences | null>(null)

export interface SidebarController {
  state: SidebarState
  collapsed: boolean
  toggle: () => void
}

export function useSidebar(): SidebarController {
  const preferences = useContext(LayoutPreferencesContext)

  const [state, setState] = useState<SidebarState>(
    () => preferences?.loadSidebarState() ?? DEFAULT_SIDEBAR_STATE,
  )

  /**
   * 저장은 setState 갱신 함수 밖에서 한다.
   * StrictMode 는 갱신 함수를 두 번 호출하므로 그 안에 부수 효과를 두면 두 번 쓰인다.
   */
  const toggle = useCallback(() => {
    const next = toggleSidebarState(state)
    setState(next)
    preferences?.saveSidebarState(next)
  }, [state, preferences])

  return { state, collapsed: state === 'collapsed', toggle }
}
