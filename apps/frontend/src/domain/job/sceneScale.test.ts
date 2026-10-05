import { describe, expect, it } from 'vitest'
import { formatScaleReference } from './sceneScale'

describe('formatScaleReference', () => {
  it('구조화된 미터 높이를 우선 표시한다', () => {
    expect(
      formatScaleReference({ object: '부두 기둥', realWorldSize: '약 2~4m', heightMeters: 3 }),
    ).toBe('부두 기둥 — 높이 3m (구조화 기준)')
  })

  it('구조화 높이가 없는 과거 장면은 기존 설명을 표시한다', () => {
    expect(
      formatScaleReference({
        object: '부두 기둥',
        realWorldSize: '높이 약 3m',
        heightMeters: null,
      }),
    ).toBe('부두 기둥 — 높이 약 3m')
  })
})
