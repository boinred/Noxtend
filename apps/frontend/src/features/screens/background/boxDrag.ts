/**
 * 검수 화면에서 상자를 끄는 계산.
 *
 * 화면에서 떼어낸 이유는 **테스트 때문이다** — 이 저장소의 vitest 는 순수 함수만 다루고,
 * 뒤집힘·최소 크기·프레임 가둠은 눈으로 확인하기 어려운 경계값이다.
 */
import type { OverlayHandle } from './PartsOverlay'
import type { Bounds } from '@/domain/job/types'

export interface Point {
  x: number
  y: number
}

export function clamp01(value: number): number {
  return Math.min(1, Math.max(0, value))
}

/** 드래그 시작점과 현재점에서 정규화 사각형을 만든다 — 어느 방향으로 끌어도 x,y 는 좌상단이다. */
export function boundsFromDrag(start: Point, current: Point): Bounds {
  return {
    x: Math.min(start.x, current.x),
    y: Math.min(start.y, current.y),
    w: Math.abs(start.x - current.x),
    h: Math.abs(start.y - current.y),
  }
}

/** 끄는 중인 상자 — 시작 시점의 좌표와 마우스 위치를 함께 들고 있어야 delta 를 낸다. */
export interface HandleDrag {
  partId: string
  ordinal: number
  handle: OverlayHandle
  origin: Bounds
  start: Point
}

/** 상자가 뒤집히지 않게 하는 최소 크기 (0~1). 이보다 작아지면 손잡이끼리 붙어 못 잡는다. */
const MIN_SIDE = 0.005

/**
 * 손잡이를 끈 결과 좌표.
 *
 * `body` 는 크기를 유지한 채 옮기고 프레임 안에 가둔다. 나머지는 잡은 변만 움직이므로
 * 각 축을 따로 계산한다 — 모서리는 두 축이 같이 잡히는 경우다.
 */
export function resizeBounds(drag: HandleDrag, point: Point): Bounds {
  const dx = point.x - drag.start.x
  const dy = point.y - drag.start.y
  const { x, y, w, h } = drag.origin

  if (drag.handle === 'body') {
    return {
      x: Math.min(1 - w, Math.max(0, x + dx)),
      y: Math.min(1 - h, Math.max(0, y + dy)),
      w,
      h,
    }
  }

  // 잡은 변이 어느 쪽인지 — 이름 한 글자가 곧 방향이다
  const west = drag.handle.includes('w')
  const east = drag.handle.includes('e')
  const north = drag.handle.startsWith('n')
  const south = drag.handle.startsWith('s')

  // 왼쪽 변을 끌면 오른쪽 변이 고정이다. 최소 크기에서 멈춰 뒤집히지 않는다
  const left = west ? Math.min(clamp01(x + dx), x + w - MIN_SIDE) : x
  const right = east ? Math.max(clamp01(x + w + dx), x + MIN_SIDE) : x + w
  const top = north ? Math.min(clamp01(y + dy), y + h - MIN_SIDE) : y
  const bottom = south ? Math.max(clamp01(y + h + dy), y + MIN_SIDE) : y + h

  return { x: left, y: top, w: right - left, h: bottom - top }
}

export function sameBounds(a: Bounds, b: Bounds): boolean {
  return a.x === b.x && a.y === b.y && a.w === b.w && a.h === b.h
}
