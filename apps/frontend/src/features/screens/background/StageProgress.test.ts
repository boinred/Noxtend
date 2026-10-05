import { describe, expect, it } from 'vitest'
import { shouldCollapseStageList } from './StageProgress'

describe('단계 목록 표시 밀도', () => {
  it('6단계까지는 바로 보여준다', () => {
    expect(shouldCollapseStageList(6)).toBe(false)
  })

  it('10~20단계로 늘어나면 접을 수 있는 상세 목록으로 바꾼다', () => {
    expect(shouldCollapseStageList(10)).toBe(true)
    expect(shouldCollapseStageList(20)).toBe(true)
  })
})
