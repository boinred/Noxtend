import { describe, expect, it } from 'vitest'
import { sceneCameraPose } from './types'

/**
 * 원본 이미지의 카메라 시점 재현. Design Ref: scene-assembly §5.2
 *
 * **모델이 내는 eyeLevel 은 자유 문장이다** — 실측: "지면에서 1.7m", "low angle".
 * 고정 매핑표는 실데이터에서 전부 기본값으로 떨어졌다. 문장에서 숫자를 꺼내고,
 * 못 꺼내면 키워드, 그것도 없으면 기본 높이를 쓴다.
 */
describe('sceneCameraPose', () => {
  it('미터 표기에서 높이를 꺼낸다', () => {
    expect(sceneCameraPose({ eyeLevel: '지면에서 1.7m', horizonY: 0.5 }).height).toBeCloseTo(1.7)
    expect(
      sceneCameraPose({ eyeLevel: 'about 2.5 m above ground', horizonY: 0.5 }).height,
    ).toBeCloseTo(2.5)
  })

  it('키워드로도 가늠한다', () => {
    expect(sceneCameraPose({ eyeLevel: 'low angle', horizonY: 0.5 }).height).toBeLessThan(2)
    expect(sceneCameraPose({ eyeLevel: '높은 부감', horizonY: 0.5 }).height).toBeGreaterThan(3)
  })

  it('모르면 기본 높이다', () => {
    expect(sceneCameraPose({ eyeLevel: '???', horizonY: 0.5 }).height).toBeCloseTo(2.2)
    expect(sceneCameraPose(null).height).toBeCloseTo(2.2)
  })

  /** 드론 샷 같은 극단값이 와도 장면을 벗어나지 않는다 */
  it('높이를 0.8~6 으로 누른다', () => {
    expect(sceneCameraPose({ eyeLevel: '120m 상공', horizonY: 0.5 }).height).toBe(6)
    expect(sceneCameraPose({ eyeLevel: '0.1m', horizonY: 0.5 }).height).toBe(0.8)
  })

  /**
   * 지평선이 화면 가운데보다 위(0.5 미만)면 카메라가 아래를 본다 —
   * 내려다볼수록 지평선은 화면 위로 올라간다.
   */
  it('지평선 위치가 내려보는 각을 정한다', () => {
    expect(sceneCameraPose({ eyeLevel: 'eye', horizonY: 0.5 }).pitchDown).toBeCloseTo(0)
    expect(sceneCameraPose({ eyeLevel: 'eye', horizonY: 0.4 }).pitchDown).toBeGreaterThan(0)
    expect(sceneCameraPose({ eyeLevel: 'eye', horizonY: 0.6 }).pitchDown).toBeLessThan(0)
  })
})
