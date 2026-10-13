import { describe, expect, it } from 'vitest'
import { activeJobProgressText, sourceDraftProgressText } from './ActiveJobSpotlight'

describe('홈 활성 작업 진행 문구', () => {
  it('10~20단계에서도 현재 위치와 완료율을 함께 요약한다', () => {
    expect(activeJobProgressText(7, 18, '파츠 식별', 33)).toBe('7/18 · 파츠 식별 · 33% 완료')
  })

  it('상세 작업을 아직 받지 못했으면 상태를 추측하지 않는다', () => {
    expect(activeJobProgressText(0, 0, null, 0)).toBe('진행 정보를 불러오는 중')
  })

  it('원본 준비 중에는 프레임 수 대신 후보 수를 보인다', () => {
    expect(sourceDraftProgressText(0)).toBe('원본 준비 중 · 후보 0/8')
    expect(sourceDraftProgressText(3)).toBe('원본 준비 중 · 후보 3/8')
  })
})
