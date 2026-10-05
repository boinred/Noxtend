import { describe, expect, it } from 'vitest'
import { cn } from './utils'

describe('cn', () => {
  it('조건부 클래스를 결합한다', () => {
    expect(cn('flex', { hidden: false, 'items-center': true })).toBe('flex items-center')
  })

  it('충돌하는 Tailwind 클래스를 마지막 값으로 병합한다', () => {
    expect(cn('px-2 text-sm', 'px-4')).toBe('text-sm px-4')
  })
})
