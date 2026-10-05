/**
 * Design Ref: §3.1, §6 — `LayoutPreferences` 의 localStorage 구현.
 *
 * 저장 계층 문제는 앱을 멈추지 않는다 (§6 원칙).
 * 프라이빗 모드나 저장소 차단 환경에서는 조용히 기본값으로 돌아간다.
 * 테마 어댑터(`themeStorage.ts`) 와 같은 방침이다.
 */
import { DEFAULT_SIDEBAR_STATE, isSidebarState } from '@/domain/layout/types'
import type { SidebarState } from '@/domain/layout/types'
import type { LayoutPreferences } from '@/domain/layout/port'

export const SIDEBAR_STORAGE_KEY = 'nextend.layout.sidebar'

export interface LocalLayoutPreferencesOptions {
  /** 테스트에서 저장소를 갈아끼우는 주입 지점. `null` 이면 저장 없이 동작한다. */
  storage?: Storage | null
}

/** `window.localStorage` 접근 자체가 던지는 환경이 있다 (샌드박스 iframe 등) */
function resolveStorage(injected: Storage | null | undefined): Storage | null {
  if (injected !== undefined) return injected
  try {
    return window.localStorage
  } catch {
    return null
  }
}

export function createLocalLayoutPreferences(
  options: LocalLayoutPreferencesOptions = {},
): LayoutPreferences {
  const storage = resolveStorage(options.storage)

  return {
    loadSidebarState(): SidebarState {
      if (!storage) return DEFAULT_SIDEBAR_STATE
      try {
        const raw = storage.getItem(SIDEBAR_STORAGE_KEY)
        // 유효하지 않은 값은 무시한다 — 손상된 저장소가 레이아웃을 깨지 않게 (§4.2 #7)
        return isSidebarState(raw) ? raw : DEFAULT_SIDEBAR_STATE
      } catch {
        return DEFAULT_SIDEBAR_STATE
      }
    },

    saveSidebarState(state: SidebarState): void {
      if (!storage) return
      try {
        storage.setItem(SIDEBAR_STORAGE_KEY, state)
      } catch {
        // 저장 실패는 무시한다. 이번 세션에서는 동작하고, 재방문 시 기본값으로 돌아간다
      }
    },
  }
}
