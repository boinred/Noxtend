/** Design Ref: §8.2 L1 #3~7 — 레이아웃 선호 어댑터 */
import { beforeEach, describe, expect, it } from 'vitest'
import {
  SIDEBAR_STORAGE_KEY,
  createLocalLayoutPreferences,
} from '@/infra/layout/localLayoutPreferences'

/** 최소 Storage 구현. 실패 주입이 가능해야 §6 폴백을 검증할 수 있다. */
function memoryStorage(seed: Record<string, string> = {}): Storage {
  const map = new Map(Object.entries(seed))
  return {
    get length() {
      return map.size
    },
    clear: () => map.clear(),
    getItem: (key: string) => map.get(key) ?? null,
    key: (index: number) => [...map.keys()][index] ?? null,
    removeItem: (key: string) => void map.delete(key),
    setItem: (key: string, value: string) => void map.set(key, value),
  }
}

function throwingStorage(): Storage {
  const boom = () => {
    throw new Error('저장소 접근 차단')
  }
  return {
    length: 0,
    clear: boom,
    getItem: boom,
    key: boom,
    removeItem: boom,
    setItem: boom,
  }
}

describe('#3 저장값 없음 → 기본값', () => {
  it('펼침으로 시작한다', () => {
    const prefs = createLocalLayoutPreferences({ storage: memoryStorage() })
    expect(prefs.loadSidebarState()).toBe('expanded')
  })
})

describe('#4 유효하지 않은 값 → 기본값', () => {
  it.each(['', 'open', 'COLLAPSED', '{"state":"collapsed"}'])('%s 를 무시한다', (raw) => {
    const prefs = createLocalLayoutPreferences({
      storage: memoryStorage({ [SIDEBAR_STORAGE_KEY]: raw }),
    })
    expect(prefs.loadSidebarState()).toBe('expanded')
  })
})

describe('#5 저장소 예외 → 기본값, throw 없음', () => {
  it('읽기가 던져도 앱이 멈추지 않는다', () => {
    const prefs = createLocalLayoutPreferences({ storage: throwingStorage() })
    expect(() => prefs.loadSidebarState()).not.toThrow()
    expect(prefs.loadSidebarState()).toBe('expanded')
  })

  it('저장소가 아예 없어도 동작한다', () => {
    const prefs = createLocalLayoutPreferences({ storage: null })
    expect(prefs.loadSidebarState()).toBe('expanded')
    expect(() => prefs.saveSidebarState('collapsed')).not.toThrow()
  })
})

describe('#6 저장 후 재로드 시 유지', () => {
  let storage: Storage

  beforeEach(() => {
    storage = memoryStorage()
  })

  it.each(['collapsed', 'expanded'] as const)('%s 가 유지된다', (state) => {
    createLocalLayoutPreferences({ storage }).saveSidebarState(state)
    // 새로고침을 흉내내려면 어댑터를 새로 만들어야 한다 — 인스턴스 캐시에 속지 않기 위해
    expect(createLocalLayoutPreferences({ storage }).loadSidebarState()).toBe(state)
  })

  it('저장 키가 규약을 따른다 (§10.1)', () => {
    createLocalLayoutPreferences({ storage }).saveSidebarState('collapsed')
    expect(storage.getItem('nextend.layout.sidebar')).toBe('collapsed')
  })
})

describe('#7 저장 예외 → throw 없음', () => {
  it('쓰기가 던져도 전파되지 않는다', () => {
    const prefs = createLocalLayoutPreferences({ storage: throwingStorage() })
    expect(() => prefs.saveSidebarState('collapsed')).not.toThrow()
  })
})
