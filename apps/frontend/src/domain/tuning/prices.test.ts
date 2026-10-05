import { describe, expect, it, vi, afterEach } from 'vitest'
import { groupPricesByModel, isScheduled } from './types'
import { modelPriceHint } from './usage'
import type { ModelPrice } from './types'

function price(model: string, effectiveFrom: string, input = 1): ModelPrice {
  return {
    id: `${model}-${effectiveFrom}`,
    model,
    inputPerMillion: input,
    outputPerMillion: input * 5,
    longContextFrom: null,
    longInputPerMillion: null,
    longOutputPerMillion: null,
    perImage: null,
    effectiveFrom,
    note: '',
  }
}

describe('groupPricesByModel', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('모델별로 묶고 지난 행은 최신 순으로 둔다', () => {
    // 시행일 순으로 늘어놓으면 같은 모델의 행이 흩어져 "지금 적용되는 단가" 를 못 읽는다
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-01T00:00:00Z'))

    const groups = groupPricesByModel([
      price('gpt-5', '2026-01-01T00:00:00Z'),
      price('claude-opus-5', '2026-03-01T00:00:00Z'),
      price('gpt-5', '2026-06-01T00:00:00Z'),
    ])

    expect(groups.map((g) => g.model)).toEqual(['claude-opus-5', 'gpt-5'])
    expect(groups[1]?.rows.map((r) => r.effectiveFrom)).toEqual([
      '2026-06-01T00:00:00Z',
      '2026-01-01T00:00:00Z',
    ])
  })

  /**
   * **묶음의 첫 행은 언제나 실제로 쓰이는 행이어야 한다.** 인상을 미리 등록해 두면
   * 단순 내림차순에서는 미래 행이 맨 위에 오고, 모델명도 거기 붙어 훑어볼 때 그 값이
   * 현재 단가처럼 읽힌다.
   */
  it('적용 중인 행을 맨 위에, 예정은 빠른 순으로 둔다', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-01T00:00:00Z'))

    const groups = groupPricesByModel([
      price('gpt-5', '2027-01-01T00:00:00Z', 4),
      price('gpt-5', '2026-06-01T00:00:00Z', 1),
      price('gpt-5', '2026-12-01T00:00:00Z', 3),
      price('gpt-5', '2026-01-01T00:00:00Z', 0.5),
    ])

    expect(groups[0]?.rows.map((r) => r.inputPerMillion)).toEqual([1, 3, 4, 0.5])
  })

  it('지금 적용되는 행을 고른다 — 미래 시행 행은 아직 아니다', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-01T00:00:00Z'))

    const groups = groupPricesByModel([
      price('gpt-5', '2026-01-01T00:00:00Z', 1),
      price('gpt-5', '2026-12-01T00:00:00Z', 2),
    ])

    expect(groups[0]?.current.inputPerMillion).toBe(1)
  })

  /**
   * 인상을 미리 등록해 둔 상태. 백엔드가 "모든 행보다 오래된 호출은 가장 오래된
   * 행으로 소급" 하는 것과 같은 규칙이라 화면과 계산이 어긋나지 않는다.
   */
  it('미래 시행 행만 있으면 가장 이른 행을 적용 중으로 본다', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-01T00:00:00Z'))

    const groups = groupPricesByModel([
      price('gpt-5', '2026-12-01T00:00:00Z', 2),
      price('gpt-5', '2026-10-01T00:00:00Z', 3),
    ])

    expect(groups[0]?.current.inputPerMillion).toBe(3)
  })

  it('빈 목록은 빈 묶음이다', () => {
    expect(groupPricesByModel([])).toEqual([])
  })
})

describe('isScheduled', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('아직 오지 않은 시행일을 구분한다', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-01T00:00:00Z'))

    expect(isScheduled(price('gpt-5', '2026-12-01T00:00:00Z'))).toBe(true)
    expect(isScheduled(price('gpt-5', '2026-01-01T00:00:00Z'))).toBe(false)
  })
})

/**
 * 모델을 고르는 자리에서 읽는 단가 (사이클 #8).
 *
 * 관리자 표까지 가야 알 수 있던 값을 고르는 순간에 붙인다 — 판단 근거가 선택 시점에
 * 없으면 사람은 그냥 첫 항목을 고른다.
 */
describe('modelPriceHint', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('토큰 과금 모델은 입력과 출력을 단위와 함께 보여준다', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-01T00:00:00Z'))

    expect(modelPriceHint([price('claude-opus-5', '2026-01-01', 3)], 'claude-opus-5')).toBe(
      '100만 토큰당 입력 $3.000 · 출력 $15.000',
    )
  })

  it('이미지 모델은 장당 단가다 — 토큰 단위로 환산하지 않는다', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-01T00:00:00Z'))

    const image: ModelPrice = { ...price('gpt-image-2', '2026-01-01'), perImage: 0.04 }

    expect(modelPriceHint([image], 'gpt-image-2')).toBe('장당 $0.040')
  })

  it('등록된 단가가 없으면 $0 이 아니라 낱말이다 (C-7 · FR-20)', () => {
    expect(modelPriceHint([], 'claude-opus-5')).toBe('단가 미등록')
  })

  it('인상을 미리 등록해 둬도 지금 적용되는 행을 읽는다', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-01T00:00:00Z'))

    const hint = modelPriceHint(
      [price('claude-opus-5', '2026-01-01', 3), price('claude-opus-5', '2026-12-01', 9)],
      'claude-opus-5',
    )

    // 미래 행이 최신이라는 이유로 앞에 오면 사람이 그 값을 현재 단가로 읽는다
    expect(hint).toBe('100만 토큰당 입력 $3.000 · 출력 $15.000')
  })
})
