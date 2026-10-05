import { describe, expect, it } from 'vitest'
import { sceneLightRig } from './types'

/**
 * 광원 힌트 → 광원 자세 (G-01). Design Ref: scene-assembly §5.2
 *
 * **모델이 내는 광원 설명은 자유 문장이다** — 실측:
 * direction "좌측 후방 20° 방위, 28° 고도"
 * temperature "차가운 회청색 환경광과 좌측 후방의 약한 따뜻한 황갈색 광원"
 * 8방위 고정 표는 실데이터에서 한 번도 맞지 않았다. 키워드와 숫자를 꺼낸다.
 */
describe('sceneLightRig', () => {
  it('실측 문장에서 방위와 고도를 꺼낸다', () => {
    const rig = sceneLightRig({
      direction: '좌측 후방 20° 방위, 28° 고도',
      temperature: '차가운 회청색 환경광과 좌측 후방의 약한 따뜻한 황갈색 광원',
    })

    expect(rig.azimuthDeg).toBe(225) // 좌 + 후방 → 좌후방 사분면
    expect(rig.elevationDeg).toBeCloseTo(28) // "28° 고도"
  })

  /** 환경광과 주광의 온도가 다르게 서술된다 — 따로 읽는다. */
  it('주광은 따뜻하고 환경광은 차갑다는 서술을 가른다', () => {
    const rig = sceneLightRig({
      direction: '좌측',
      temperature: '차가운 회청색 환경광과 따뜻한 황갈색 광원',
    })

    expect(rig.key).toBe('warm')
    expect(rig.ambient).toBe('cool')
  })

  it('영문 키워드도 읽는다', () => {
    const rig = sceneLightRig({ direction: 'upper-right front', temperature: 'warm golden hour' })

    expect(rig.azimuthDeg).toBe(45) // 우 + 전방
    expect(rig.elevationDeg).toBeGreaterThan(40) // upper → 높은 고도
    expect(rig.key).toBe('warm')
  })

  it('모르면 회화 관습(좌후방 상단)이다', () => {
    const unknown = sceneLightRig({ direction: '???', temperature: '???' })
    expect(unknown.azimuthDeg).toBe(225)
    expect(unknown.key).toBe('neutral')

    expect(sceneLightRig(null).azimuthDeg).toBe(225)
  })

  /** 극단 고도(정수리·수평선)는 그림자가 무너진다 — 15~75 로 누른다. */
  it('고도를 15~75 로 누른다', () => {
    expect(sceneLightRig({ direction: '89° 고도', temperature: '' }).elevationDeg).toBe(75)
    expect(sceneLightRig({ direction: '2° 고도', temperature: '' }).elevationDeg).toBe(15)
  })
})
