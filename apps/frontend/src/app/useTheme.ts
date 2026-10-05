/**
 * Design Ref: §2.2, §3.1 — 테마 토글과 DOM 반영.
 *
 * 테마는 Zustand 도 Context 도 소유하지 않는다. `<html data-theme>` 가 단일 진실이고
 * 이 훅은 토글 아이콘 표시를 위해 그 값을 미러링할 뿐이다.
 * 부트스트랩 스크립트와 React 가 같은 곳을 보기 때문에 둘이 어긋날 수 없다.
 */
import { useCallback, useEffect, useState } from 'react'
import {
  applyTheme,
  loadThemeChoice,
  readAppliedTheme,
  saveThemeChoice,
  systemTheme,
} from '@/infra/theme/themeStorage'
import type { Theme } from '@/infra/theme/themeStorage'

export function useTheme() {
  // 부트스트랩이 이미 심어둔 값에서 출발한다. 여기서 다시 계산하면 깜빡임이 생긴다.
  const [theme, setTheme] = useState<Theme>(() => readAppliedTheme())

  const setAndApply = useCallback((next: Theme) => {
    applyTheme(next)
    setTheme(next)
  }, [])

  const toggle = useCallback(() => {
    const next: Theme = readAppliedTheme() === 'dark' ? 'light' : 'dark'
    setAndApply(next)
    // 토글은 명시적 선택이므로 저장한다. 이후 시스템 설정 변경을 따라가지 않는다 (§4.2 #5)
    saveThemeChoice(next)
  }, [setAndApply])

  // 명시적 선택이 없는 동안에는 시스템 설정을 따라간다 (§4.2 #4)
  useEffect(() => {
    if (loadThemeChoice() !== null) return

    let media: MediaQueryList
    try {
      media = window.matchMedia('(prefers-color-scheme: light)')
    } catch {
      return
    }

    const onChange = () => {
      if (loadThemeChoice() !== null) return
      setAndApply(systemTheme())
    }

    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [setAndApply])

  return { theme, toggle }
}
