import { describe, expect, it } from 'vitest'
import { resizeBounds } from './boxDrag'
import type { HandleDrag } from './boxDrag'
import type { OverlayHandle } from './PartsOverlay'

const ORIGIN = { x: 0.3, y: 0.4, w: 0.2, h: 0.2 }

function drag(handle: OverlayHandle): HandleDrag {
  return { partId: 'p', ordinal: 0, handle, origin: ORIGIN, start: { x: 0.5, y: 0.5 } }
}

describe('resizeBounds', () => {
  // 변 손잡이의 존재 이유 — 위쪽 면만 올려도 좌우가 그대로여야 한다
  it('한 변만 끌면 나머지 세 변은 그대로다', () => {
    const next = resizeBounds(drag('n'), { x: 0.9, y: 0.45 })

    expect(next.y).toBeCloseTo(0.35)
    expect(next.h).toBeCloseTo(0.25)
    expect(next.x).toBeCloseTo(ORIGIN.x)
    expect(next.w).toBeCloseTo(ORIGIN.w)
  })

  // 모서리는 두 축이 같이 잡힌다
  it('모서리를 끌면 가로세로가 함께 바뀐다', () => {
    const next = resizeBounds(drag('se'), { x: 0.6, y: 0.6 })

    expect(next.w).toBeCloseTo(0.3)
    expect(next.h).toBeCloseTo(0.3)
    expect(next.x).toBeCloseTo(ORIGIN.x)
    expect(next.y).toBeCloseTo(ORIGIN.y)
  })

  // 반대쪽 변을 지나쳐 끌어도 상자가 음수 크기로 뒤집히지 않는다
  it('반대쪽 변을 넘겨 끌면 최소 크기에서 멈춘다', () => {
    const next = resizeBounds(drag('w'), { x: 0.95, y: 0.5 })

    expect(next.w).toBeCloseTo(0.005)
    expect(next.x).toBeCloseTo(0.495)
  })

  // 크기를 유지한 채 옮기고, 프레임 밖으로 나가지 않는다
  it('본체를 끌면 크기가 유지된다', () => {
    const next = resizeBounds(drag('body'), { x: 0.6, y: 0.55 })

    expect(next.x).toBeCloseTo(0.4)
    expect(next.y).toBeCloseTo(0.45)
    expect(next.w).toBeCloseTo(ORIGIN.w)
    expect(next.h).toBeCloseTo(ORIGIN.h)
  })

  it('본체를 프레임 밖으로 끌면 가장자리에서 멈춘다', () => {
    const next = resizeBounds(drag('body'), { x: 1.5, y: 1.5 })

    expect(next.x).toBeCloseTo(0.8)
    expect(next.y).toBeCloseTo(0.8)
    expect(next.w).toBeCloseTo(0.2)
  })
})
