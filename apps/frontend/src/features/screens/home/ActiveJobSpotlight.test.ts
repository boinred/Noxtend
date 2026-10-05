import { describe, expect, it } from 'vitest'
import { activeJobProgressText } from './ActiveJobSpotlight'

describe('홈 활성 작업 진행 문구', () => {
  it('10~20단계에서도 현재 위치와 완료율을 함께 요약한다', () => {
    expect(activeJobProgressText(7, 18, '파츠 식별', 33)).toBe('7/18 · 파츠 식별 · 33% 완료')
  })

  it('상세 작업을 아직 받지 못했으면 상태를 추측하지 않는다', () => {
    expect(activeJobProgressText(0, 0, null, 0)).toBe('진행 정보를 불러오는 중')
  })
})
