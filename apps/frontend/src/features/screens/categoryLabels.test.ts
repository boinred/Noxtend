import { describe, expect, it } from 'vitest'
import { jobSummaryCount } from './categoryLabels'
import type { JobSummary } from '@/domain/job/types'
import type { SpritePhase } from '@/domain/sprites/types'

function twoD(phase: SpritePhase): JobSummary {
  return {
    productionMode: 'twoD',
    sprite: { phase, assetCount: 2, approvedAssetCount: 1, imageCount: 3, exportCount: 0 },
    partCount: 0,
  } as unknown as JobSummary
}

describe('jobSummaryCount', () => {
  it('원본 준비 중인 2D 작업은 빈 에셋 집계 대신 단계로 요약한다', () => {
    expect(jobSummaryCount(twoD('sourceDraft'))).toBe('원본 준비 중')
  })

  it('그 밖의 2D 단계는 에셋·이미지 집계를 보인다', () => {
    expect(jobSummaryCount(twoD('planReview'))).toBe('에셋 2개 · 승인 1개 · 누적 이미지 3개')
  })
})
