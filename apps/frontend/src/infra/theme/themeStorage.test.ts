/**
 * Design Ref: §8.2 L1 #1~5 — 테마 해석과 저장
 */
import { describe, expect, it, vi } from 'vitest'
import {
  DEFAULT_THEME,
  THEME_STORAGE_KEY,
  isTheme,
  loadThemeChoice,
  resolveTheme,
  saveThemeChoice,
  systemTheme,
} from '@/infra/theme/themeStorage'

function fakeStorage(initial: Record<string, string> = {}): Storage {
  const map = new Map(Object.entries(initial))
  return {
    get length() {
      return map.size
    },
    clear: () => map.clear(),
    getItem: (k: string) => map.get(k) ?? null,
    key: (i: number) => [...map.keys()][i] ?? null,
    removeItem: (k: string) => void map.delete(k),
    setItem: (k: string, v: string) => void map.set(k, v),
  }
}

function throwingStorage(): Storage {
  const fail = () => {
    throw new Error('storage blocked')
  }
  return {
    get length(): number {
      return fail()
    },
    clear: fail,
    getItem: fail,
    key: fail,
    removeItem: fail,
    setItem: fail,
  }
}

const matcher = (light: boolean) =>
  ((query: string) => ({
    matches: light && query.includes('light'),
  })) as unknown as typeof window.matchMedia

const throwingMatcher = (() => {
  throw new Error('matchMedia unavailable')
}) as unknown as typeof window.matchMedia

describe('#1~3 resolveTheme — 선택이 시스템 설정보다 우선한다', () => {
  it('#1 저장값 없음 + 시스템 라이트 → light', () => {
    expect(resolveTheme(fakeStorage(), matcher(true))).toBe('light')
  })

  it('#2 저장값 없음 + 시스템 다크 → dark', () => {
    expect(resolveTheme(fakeStorage(), matcher(false))).toBe('dark')
  })

  it('#3 저장값 light + 시스템 다크 → light', () => {
    const storage = fakeStorage({ [THEME_STORAGE_KEY]: 'light' })
    expect(resolveTheme(storage, matcher(false))).toBe('light')
  })

  it('저장값 dark + 시스템 라이트 → dark', () => {
    const storage = fakeStorage({ [THEME_STORAGE_KEY]: 'dark' })
    expect(resolveTheme(storage, matcher(true))).toBe('dark')
  })
})

describe('#4 신뢰하지 않는 저장 값', () => {
  it.each(['', 'DARK', 'blue', '{}', 'null'])('%s 는 무시하고 시스템 설정을 쓴다', (value) => {
    const storage = fakeStorage({ [THEME_STORAGE_KEY]: value })
    expect(loadThemeChoice(storage)).toBeNull()
    expect(resolveTheme(storage, matcher(true))).toBe('light')
  })

  it('isTheme 화이트리스트', () => {
    expect(isTheme('dark')).toBe(true)
    expect(isTheme('light')).toBe(true)
    expect(isTheme('Dark')).toBe(false)
    expect(isTheme(null)).toBe(false)
    expect(isTheme(undefined)).toBe(false)
  })
})

describe('#5 저장소 접근 실패', () => {
  it('읽기가 막혀도 throw 하지 않고 시스템 설정을 쓴다', () => {
    expect(() => loadThemeChoice(throwingStorage())).not.toThrow()
    expect(resolveTheme(throwingStorage(), matcher(true))).toBe('light')
  })

  it('쓰기가 막혀도 throw 하지 않는다', () => {
    expect(() => saveThemeChoice('light', throwingStorage())).not.toThrow()
  })

  it('matchMedia 가 없으면 dark 로 폴백한다', () => {
    expect(systemTheme(throwingMatcher)).toBe(DEFAULT_THEME)
    expect(resolveTheme(fakeStorage(), throwingMatcher)).toBe(DEFAULT_THEME)
  })

  it('저장소와 matchMedia 가 모두 실패해도 dark', () => {
    expect(resolveTheme(throwingStorage(), throwingMatcher)).toBe(DEFAULT_THEME)
  })
})

describe('saveThemeChoice', () => {
  it('선택을 기록한다', () => {
    const storage = fakeStorage()
    saveThemeChoice('light', storage)
    expect(storage.getItem(THEME_STORAGE_KEY)).toBe('light')
    expect(loadThemeChoice(storage)).toBe('light')
  })
})

describe('부트스트랩 스크립트와의 일치', () => {
  /**
   * index.html 의 인라인 스크립트와 해석 규칙이 같아야 한다.
   * 어긋나면 첫 페인트와 React 렌더 사이에 테마가 바뀌어 번쩍인다.
   * 여기서는 그 규칙을 그대로 옮겨 두 구현이 같은 답을 내는지 대조한다.
   */
  const bootstrapResolve = (saved: string | null, systemLight: boolean): string => {
    let theme = 'dark'
    if (saved === 'dark' || saved === 'light') theme = saved
    else if (systemLight) theme = 'light'
    return theme
  }

  it.each([
    [null, true],
    [null, false],
    ['light', false],
    ['dark', true],
    ['garbage', true],
    ['garbage', false],
  ])('saved=%s systemLight=%s 에서 같은 결과', (saved, systemLight) => {
    const storage = saved === null ? fakeStorage() : fakeStorage({ [THEME_STORAGE_KEY]: saved })
    expect(resolveTheme(storage, matcher(systemLight as boolean))).toBe(
      bootstrapResolve(saved as string | null, systemLight as boolean),
    )
  })
})

describe('applyTheme / readAppliedTheme', () => {
  it('DOM 이 단일 진실이다', async () => {
    const { applyTheme, readAppliedTheme } = await import('@/infra/theme/themeStorage')
    const root = { dataset: {} } as unknown as HTMLElement

    applyTheme('light', root)
    expect(root.dataset.theme).toBe('light')
    expect(readAppliedTheme(root)).toBe('light')

    applyTheme('dark', root)
    expect(readAppliedTheme(root)).toBe('dark')
  })

  it('알 수 없는 값이 심겨 있으면 dark 로 읽는다', async () => {
    const { readAppliedTheme } = await import('@/infra/theme/themeStorage')
    const root = { dataset: { theme: 'neon' } } as unknown as HTMLElement
    expect(readAppliedTheme(root)).toBe(DEFAULT_THEME)
  })
})

describe('systemTheme', () => {
  it('쿼리를 정확히 전달한다', () => {
    const spy = vi.fn(() => ({ matches: true }))
    systemTheme(spy as unknown as typeof window.matchMedia)
    expect(spy).toHaveBeenCalledWith('(prefers-color-scheme: light)')
  })
})
