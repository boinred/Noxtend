/**
 * Design Ref: §3.1 — 테마 저장과 해석.
 *
 * 해석 규칙은 index.html 의 부트스트랩 스크립트와 **동일해야 한다**.
 * 둘이 어긋나면 첫 페인트와 React 렌더 사이에 테마가 바뀌어 번쩍인다.
 * 규칙: 저장된 명시적 선택이 있으면 그것, 없으면 시스템 설정, 둘 다 실패하면 dark.
 */
export type Theme = 'dark' | 'light'

export const THEME_STORAGE_KEY = 'nextend.theme'
export const DEFAULT_THEME: Theme = 'dark'

/** 저장 값은 화이트리스트로만 받는다 (§7 저장 값 검증) */
export function isTheme(value: unknown): value is Theme {
  return value === 'dark' || value === 'light'
}

export function loadThemeChoice(storage: Storage = window.localStorage): Theme | null {
  try {
    const raw = storage.getItem(THEME_STORAGE_KEY)
    return isTheme(raw) ? raw : null
  } catch {
    // 프라이빗 모드 등에서 접근이 막혀도 앱을 멈추지 않는다
    return null
  }
}

export function saveThemeChoice(theme: Theme, storage: Storage = window.localStorage): void {
  try {
    storage.setItem(THEME_STORAGE_KEY, theme)
  } catch {
    // 저장 실패는 무시한다. 이번 세션에서는 동작하고, 재방문 시 시스템 설정으로 돌아간다
  }
}

export function systemTheme(matcher: typeof window.matchMedia = window.matchMedia): Theme {
  try {
    return matcher('(prefers-color-scheme: light)').matches ? 'light' : 'dark'
  } catch {
    return DEFAULT_THEME
  }
}

/** 부트스트랩 스크립트와 같은 결과를 내야 한다 */
export function resolveTheme(
  storage: Storage = window.localStorage,
  matcher: typeof window.matchMedia = window.matchMedia,
): Theme {
  return loadThemeChoice(storage) ?? systemTheme(matcher)
}

/** DOM 이 테마의 단일 진실이다 (§1.2) */
export function applyTheme(theme: Theme, root: HTMLElement = document.documentElement): void {
  root.dataset.theme = theme
}

export function readAppliedTheme(root: HTMLElement = document.documentElement): Theme {
  return isTheme(root.dataset.theme) ? root.dataset.theme : DEFAULT_THEME
}
