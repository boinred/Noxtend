import { describe, expect, it } from 'vitest'
import { sectionCount, sectionCountLabel } from './sectionCount'

/**
 * 섹션 제목 옆 개수.
 *
 * **화면에 그려진 수와 같아야 한다.** "실행 중" 은 맨 앞 한 건을 크게 따로 그리고
 * 나머지만 목록으로 넘긴다 — 목록 길이만 세면 그 한 건이 빠져 개수가 1 적게 나온다.
 * 사용자는 카드 두 장을 보면서 "1" 을 읽게 된다.
 */
describe('sectionCount', () => {
  it('목록만 있으면 목록 수를 센다', () => {
    expect(sectionCount({ jobs: [{}, {}, {}] })).toBe(3)
  })

  it('크게 그린 한 건도 함께 센다', () => {
    // 실행 중 3건 = 크게 1건 + 목록 2건
    expect(sectionCount({ jobs: [{}, {}], hasFeatured: true })).toBe(3)
  })

  it('크게 그린 것만 있으면 1이다', () => {
    expect(sectionCount({ jobs: [], hasFeatured: true })).toBe(1)
  })

  /** 아직 못 받은 것과 없는 것을 여기서 가르지 않는다 — 둘 다 배지를 감춘다 */
  it('없거나 못 받았으면 0이다', () => {
    expect(sectionCount({ jobs: [] })).toBe(0)
    expect(sectionCount({})).toBe(0)
  })
})

/**
 * 배지에 뭐라고 쓰는가.
 *
 * **목록은 상한(10)까지만 싣는다.** 그래서 하나를 지우면 열한 번째가 올라와 수가
 * 그대로 열이다 — 그것만 보면 삭제가 안 된 줄로 읽힌다. 전체가 더 많을 때만
 * "보이는 수 / 전체" 로 그 어긋남을 화면에서 설명한다.
 */
describe('sectionCountLabel', () => {
  it('전체가 더 많으면 둘 다 보여준다', () => {
    expect(sectionCountLabel(10, 28)).toBe('10 / 28')
  })

  /** 같으면 숫자 하나면 된다 — 같은 수를 두 번 쓰면 읽는 사람이 차이를 찾는다 */
  it('전부 보이면 하나만 쓴다', () => {
    expect(sectionCountLabel(3, 3)).toBe('3')
  })

  /** 전체를 못 받았을 때 목록 길이를 전체인 양 쓰지 않는다 */
  it('전체를 모르면 보이는 수만 쓴다', () => {
    expect(sectionCountLabel(3, undefined)).toBe('3')
  })

  /**
   * **전체가 더 작게 오는 순간이 있다.** 삭제 직후 목록은 캐시가 남고 전체만 먼저
   * 줄어들 수 있다. 그때 "10 / 9" 를 그리면 사용자가 고장으로 읽는다.
   */
  it('전체가 보이는 수보다 작으면 하나만 쓴다', () => {
    expect(sectionCountLabel(10, 9)).toBe('10')
  })
})
