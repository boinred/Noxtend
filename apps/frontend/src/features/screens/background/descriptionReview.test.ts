import { describe, expect, it } from 'vitest'
import {
  descriptionChips,
  emptyDescriptionParts,
  normalizeHex,
  sortForDescriptionReview,
  thumbnailStyle,
} from './descriptionReview'
import type { ReviewPart } from '@/domain/job/types'

function part(overrides: Partial<ReviewPart>): ReviewPart {
  return {
    id: 'p',
    partRef: 'P01',
    name: '바지',
    category: null,
    description: '긴 바지',
    placements: [{ x: 0, y: 0, w: 0.5, h: 0.5 }],
    source: 'detected',
    occludedBy: [],
    occludes: [],
    descriptionSource: 'model',
    ...overrides,
  }
}

describe('sortForDescriptionReview', () => {
  // 다시 쓴 파츠가 상단 — 모델러가 먼저 확인할 대상. 나머지는 원래 순서 유지
  it('다시 쓴 파츠를 위로 올리고 나머지 순서는 유지한다', () => {
    const parts = [
      part({ id: 'a' }),
      part({ id: 'b', descriptionSource: 'rewritten' }),
      part({ id: 'c' }),
      part({ id: 'd', descriptionSource: 'rewritten' }),
    ]

    expect(sortForDescriptionReview(parts).map((p) => p.id)).toEqual(['b', 'd', 'a', 'c'])
  })
})

describe('emptyDescriptionParts', () => {
  // 빈 서술로 유료 생성이 나가지 않게 — 공백만 있어도 빈 것
  it('null·공백 서술 파츠만 고른다', () => {
    const parts = [
      part({ id: 'a' }),
      part({ id: 'b', description: null }),
      part({ id: 'c', description: '  ' }),
    ]

    expect(emptyDescriptionParts(parts).map((p) => p.id)).toEqual(['b', 'c'])
  })
})

describe('descriptionChips', () => {
  it('출처별 칩 문구', () => {
    expect(descriptionChips(part({ source: 'manual' }))).toEqual(['직접 추가'])
    expect(descriptionChips(part({ descriptionSource: 'rewritten' }))).toEqual(['다시 씀'])
    expect(descriptionChips(part({ source: 'manual', descriptionSource: 'human' }))).toEqual([
      '직접 추가',
      '직접 수정',
    ])
    expect(descriptionChips(part({}))).toEqual([])
  })
})

describe('thumbnailStyle', () => {
  // 상자 비율 유지 — 원본 비율(가로/세로)을 곱해야 픽셀 비율이 된다
  it('긴 변을 한도에 맞추고 상자 영역만 보이게 배경을 확대·이동한다', () => {
    const style = thumbnailStyle({ x: 0.25, y: 0.5, w: 0.5, h: 0.25 }, 1, 64)

    expect(style.width).toBe(64)
    expect(style.height).toBe(32)
    expect(style.backgroundSize).toBe('200% 400%')
    expect(style.backgroundPosition).toBe('50% 66.66666666666666%')
  })

  // 세로로 긴 상자는 높이가 한도
  it('세로로 긴 상자는 높이를 한도에 맞춘다', () => {
    const style = thumbnailStyle({ x: 0, y: 0, w: 0.1, h: 0.5 }, 1, 64)

    expect(style.height).toBe(64)
    expect(style.width).toBeCloseTo(12.8)
  })

  // 원본 전체 폭 상자 — 0 나누기 방지
  it('전체 폭·높이 상자는 위치 0%', () => {
    expect(thumbnailStyle({ x: 0, y: 0, w: 1, h: 1 }, 1, 64).backgroundPosition).toBe('0% 0%')
  })
})

describe('normalizeHex', () => {
  // 서버는 대문자 #RRGGBB 만 받는다 — 색 입력은 소문자를 낸다
  it('소문자 hex 를 대문자로', () => {
    expect(normalizeHex('#c8102e')).toBe('#C8102E')
  })
})
