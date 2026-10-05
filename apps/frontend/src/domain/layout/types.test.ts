/** Design Ref: §8.2 L1 #1~2 — 사이드바 상태 도메인 */
import { describe, expect, it } from 'vitest'
import { DEFAULT_SIDEBAR_STATE, isSidebarState, toggleSidebarState } from '@/domain/layout/types'

describe('#1 isSidebarState — 화이트리스트', () => {
  it.each(['expanded', 'collapsed'])('%s 를 받아들인다', (value) => {
    expect(isSidebarState(value)).toBe(true)
  })

  it.each([null, undefined, '', 'EXPANDED', 'open', 0, 1, true, {}, []])(
    '%o 는 거부한다',
    (value) => {
      expect(isSidebarState(value)).toBe(false)
    },
  )
})

describe('#2 toggleSidebarState — 상호 전환', () => {
  it('펼침 ↔ 접힘', () => {
    expect(toggleSidebarState('expanded')).toBe('collapsed')
    expect(toggleSidebarState('collapsed')).toBe('expanded')
  })

  it('두 번 전환하면 제자리', () => {
    expect(toggleSidebarState(toggleSidebarState('expanded'))).toBe('expanded')
  })
})

describe('기본값', () => {
  it('첫 방문은 펼침이다 (§4.2 #1)', () => {
    expect(DEFAULT_SIDEBAR_STATE).toBe('expanded')
  })
})
