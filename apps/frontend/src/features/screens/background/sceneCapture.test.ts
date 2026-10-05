/**
 * 결정적 평가 캡처의 순수 부품. Design Ref: background-similarity-tuning §8
 *
 * WebGL 픽셀 읽기는 아래가 원점이라 세로로 뒤집혀 나온다 — 뒤집기가 틀리면
 * 평가 모델이 뒤집힌 장면을 본다. 빈 캡처(전부 같은 색·alpha 0)는 업로드 전에
 * 걸러야 유료 평가가 빈 그림에 낭비되지 않는다.
 */
import { describe, expect, it } from 'vitest'
import { Object3D, Scene } from 'three'
import {
  RENDER_FRAME_SIZE,
  MAX_RENDER_BYTES,
  captureFrameFor,
  flipPixelsVertically,
  hasPixelVariance,
  renderBlobProblem,
  withOverrides,
} from './sceneCapture'

// 픽셀 헬퍼 — (r,g,b,a) 넷씩
function pixels(rows: number[][]): Uint8Array {
  return new Uint8Array(rows.flat())
}

describe('flipPixelsVertically', () => {
  it('위아래 줄을 맞바꾼다 — WebGL 읽기는 아래가 원점이다', () => {
    // 2×2, 각 픽셀을 단색으로 구분
    const source = pixels([
      [1, 1, 1, 255],
      [2, 2, 2, 255], // 아랫줄 (WebGL 원점)
      [3, 3, 3, 255],
      [4, 4, 4, 255], // 윗줄
    ])

    const flipped = flipPixelsVertically(source, 2, 2)

    expect([...flipped.slice(0, 4)]).toEqual([3, 3, 3, 255])
    expect([...flipped.slice(12, 16)]).toEqual([2, 2, 2, 255])
  })

  it('홀수 높이의 가운데 줄은 그대로다', () => {
    const source = pixels([
      [1, 0, 0, 255],
      [2, 0, 0, 255],
      [5, 5, 5, 255],
      [6, 6, 6, 255],
      [9, 0, 0, 255],
      [8, 0, 0, 255],
    ])

    const flipped = flipPixelsVertically(source, 2, 3)

    expect([...flipped.slice(8, 12)]).toEqual([5, 5, 5, 255])
  })
})

describe('hasPixelVariance', () => {
  it('전부 같은 색이면 빈 캡처다', () => {
    const flat = new Uint8Array(4 * 16).fill(200)
    expect(hasPixelVariance(flat)).toBe(false)
  })

  it('alpha 만 있는 캡처도 빈 캡처다', () => {
    const alphaOnly = new Uint8Array(4 * 16)
    for (let i = 3; i < alphaOnly.length; i += 4) alphaOnly[i] = 255
    expect(hasPixelVariance(alphaOnly)).toBe(false)
  })

  it('색이 한 픽셀이라도 다르면 유효하다', () => {
    const varied = new Uint8Array(4 * 16).fill(200)
    varied[0] = 10
    expect(hasPixelVariance(varied)).toBe(true)
  })
})

describe('renderBlobProblem', () => {
  it('8 MiB 초과를 업로드 전에 거른다', () => {
    expect(renderBlobProblem(MAX_RENDER_BYTES + 1)).toContain('8')
    expect(renderBlobProblem(MAX_RENDER_BYTES)).toBeNull()
    expect(renderBlobProblem(0)).toContain('비어')
  })

  it('프레임 계약은 1024다 (§8.1)', () => {
    expect(RENDER_FRAME_SIZE).toBe(1024)
  })
})

describe('captureFrameFor', () => {
  it('긴 변을 1024 로 고정하고 원본 비율을 유지한다 (사용자 결정)', () => {
    // 세로형 832×1216 → 701×1024
    expect(captureFrameFor(832, 1216)).toEqual({ width: 701, height: 1024 })
    // 가로형 1920×1080 → 1024×576
    expect(captureFrameFor(1920, 1080)).toEqual({ width: 1024, height: 576 })
    // 정사각은 그대로
    expect(captureFrameFor(512, 512)).toEqual({ width: 1024, height: 1024 })
  })

  it('극단 비율은 짧은 변 하한 256 으로 누르고, 잘못된 입력은 정사각으로', () => {
    expect(captureFrameFor(100, 4000).width).toBe(256)
    expect(captureFrameFor(0, 100)).toEqual({ width: 1024, height: 1024 })
    expect(captureFrameFor(Number.NaN, 100)).toEqual({ width: 1024, height: 1024 })
  })
})

/**
 * 평가 캡처의 축별 배율 (background-surface-parts #20 §4.2).
 *
 * 후보 보정을 그릴 때 배율을 단일 값으로 되돌리면 표면이 무너져 평가 렌더가 화면과
 * 달라진다 — (18.17, 7.88, 29.91) 이 (7.88, 7.88, 7.88) 이 된다. 평가는 유료라
 * 틀린 그림을 보내면 그대로 돈이 나간다.
 */
describe('withOverrides', () => {
  function sceneWith(key: string) {
    const scene = new Scene()
    const child = new Object3D()
    child.userData.instanceKey = key
    child.position.set(1, 2, 3)
    child.scale.set(4, 4, 4)
    scene.add(child)

    return { scene, child }
  }

  const laid = {
    position: { x: 0, y: 0, z: -20 },
    rotationY: 0,
    scale: { x: 18.17, y: 7.88, z: 29.91 },
  }

  it('표면의 세 축을 각각 건다 — 균일로 무너뜨리지 않는다', () => {
    const { scene, child } = sceneWith('p-0')

    withOverrides(scene, { instances: { 'p-0': laid } }, () => {
      expect(child.scale.toArray()).toEqual([18.17, 7.88, 29.91])
    })
  })

  it('그린 뒤 원래 배율로 되돌린다 — 캡처가 화면을 바꾸면 안 된다', () => {
    const { scene, child } = sceneWith('p-0')

    withOverrides(scene, { instances: { 'p-0': laid } }, () => undefined)

    expect(child.scale.toArray()).toEqual([4, 4, 4])
    expect(child.position.toArray()).toEqual([1, 2, 3])
  })

  it('오버라이드에 없는 인스턴스는 건드리지 않는다', () => {
    const { scene, child } = sceneWith('other-0')

    withOverrides(scene, { instances: { 'p-0': laid } }, () => {
      expect(child.scale.toArray()).toEqual([4, 4, 4])
    })
  })
})
