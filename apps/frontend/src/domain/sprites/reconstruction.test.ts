import { describe, expect, it } from 'vitest'
import type { Bounds } from '../job/types'
import {
  fillTiles,
  latticeCells,
  placementRects,
  reconstructionCell,
  reconstructionVisible,
  tileFillKey,
  type CellSize,
  type Pixels,
} from './reconstruction'
import type {
  SpriteAsset,
  SpriteAssetPlan,
  SpriteAssetRole,
  SpritePhase,
  SpriteSettings,
  SpriteState,
  SpriteView,
} from './types'

type Color = readonly [number, number, number, number?]
const RED: Color = [255, 0, 0]
const GREEN: Color = [0, 255, 0]
const BLUE: Color = [0, 0, 255]
const YELLOW: Color = [255, 255, 0]
const BLACK: Color = [0, 0, 0]
const CLEAR: Color = [0, 0, 0, 0]
const SQUARE: CellSize = { width: 32, height: 32 }
const NO_COMPARABLE = '재구성에 비교할 수 있는 픽셀이 없습니다'

function paint(width: number, height: number, colorAt: (x: number, y: number) => Color): Pixels {
  const data = new Uint8ClampedArray(width * height * 4)
  for (let y = 0; y < height; y++)
    for (let x = 0; x < width; x++) {
      const [r, g, b, a = 255] = colorAt(x, y)
      data.set([r, g, b, a], (y * width + x) * 4)
    }
  return { width, height, data }
}
function solid(width: number, height: number, color: Color): Pixels {
  return paint(width, height, () => color)
}
function halves(width: number, height: number, left: Color, right: Color): Pixels {
  return paint(width, height, (x) => (x < width / 2 ? left : right))
}
function halvesY(width: number, height: number, top: Color, bottom: Color): Pixels {
  return paint(width, height, (_x, y) => (y < height / 2 ? top : bottom))
}
// 칸 중심 기준 다이아몬드 안쪽 여부
function insideDiamond(x: number, y: number, cell: CellSize): boolean {
  return (
    Math.abs(x + 0.5 - cell.width / 2) / (cell.width / 2) +
      Math.abs(y + 0.5 - cell.height / 2) / (cell.height / 2) <
    1
  )
}

function settingsOf(view: SpriteView): SpriteSettings {
  return { view, outputKind: 'tiles', tileWidth: 128, repeat: 'both' }
}
function planOf(
  id: string,
  role: SpriteAssetRole,
  sourceBounds: Bounds = { x: 0, y: 0, w: 1, h: 1 },
): SpriteAssetPlan {
  return {
    role,
    id,
    name: id,
    order: 0,
    sourceBounds,
    requiresTransparency: false,
    loop: false,
    frameCount: 1,
    fps: 8,
    motionNotes: '',
  }
}
function objectPlan(sourceBounds: Bounds): SpriteAssetPlan {
  return planOf('object', 'object', sourceBounds)
}
function assetOf(
  plan: SpriteAssetPlan,
  frames: { index: number; imageId: string | null }[],
): SpriteAsset {
  return {
    canvas: { width: 128, height: 64 },
    scale: 1,
    id: plan.id,
    plan,
    planRevision: 1,
    anchor: { x: 64, y: 32 },
    approvedBaseImageId: null,
    approval: null,
    frames: frames.map((frame) => ({
      index: frame.index,
      currentTaskId: null,
      currentImageId: frame.imageId,
    })),
  }
}
function spriteOf(phase: SpritePhase, assets: SpriteAsset[]): SpriteState {
  return {
    grid: null,
    settings: settingsOf('isometric'),
    sourceCanvas: { width: 800, height: 600 },
    generationCanvas: { width: 1024, height: 1024 },
    outputCanvas: { width: 128, height: 64 },
    transform: { scale: 1, offsetX: 0, offsetY: 0 },
    phase,
    reviewRevision: 1,
    completedExportId: null,
    assets,
    images: [],
    exports: [],
    source: null,
  }
}

describe('reconstructionCell', () => {
  it('uses half height for isometric', () => {
    expect(
      reconstructionCell(settingsOf('isometric'), { width: 1536, height: 1024 }, 128 / 1536),
    ).toEqual({ width: 128, height: 64 })
    expect(reconstructionCell(settingsOf('topDown'), { width: 800, height: 600 }, 0.1)).toEqual({
      width: 80,
      height: 80,
    })
  })

  it('rejects invalid cell width', () => {
    for (const cellWidth of [null, 0, -1, Number.NaN, 1.5])
      expect(
        reconstructionCell(settingsOf('topDown'), { width: 800, height: 600 }, cellWidth),
      ).toBeNull()
  })
})

describe('latticeCells', () => {
  it('keeps diamond cells overlapping the source', () => {
    const cells = latticeCells({ width: 128, height: 64 }, { width: 128, height: 64 }, true, {
      x: 0,
      y: 0,
    })
    expect(cells).toHaveLength(5)
    expect(cells).toEqual(
      expect.arrayContaining([
        { x: -64, y: -32 },
        { x: 0, y: 0 },
        { x: 64, y: -32 },
        { x: -64, y: 32 },
        { x: 64, y: 32 },
      ]),
    )
  })

  it('places square cells from the offset in row-major order', () => {
    expect(
      latticeCells({ width: 64, height: 32 }, { width: 32, height: 16 }, false, { x: 8, y: 4 }),
    ).toEqual([
      { x: -24, y: -12 },
      { x: 8, y: -12 },
      { x: 40, y: -12 },
      { x: -24, y: 4 },
      { x: 8, y: 4 },
      { x: 40, y: 4 },
      { x: -24, y: 20 },
      { x: 8, y: 20 },
      { x: 40, y: 20 },
    ])
  })

  it('stops above the preview limit', () => {
    const one = { width: 1, height: 1 }
    expect(latticeCells({ width: 64, height: 64 }, one, false, { x: 0, y: 0 })).toHaveLength(4096)
    expect(() => latticeCells({ width: 65, height: 64 }, one, false, { x: 0, y: 0 })).toThrow(
      RangeError,
    )
    // 4,097번째 칸에서 중단
    expect(latticeCells({ width: 4096, height: 1 }, one, false, { x: 0, y: 0 })).toHaveLength(4096)
    expect(() => latticeCells({ width: 4097, height: 1 }, one, false, { x: 0, y: 0 })).toThrow(
      RangeError,
    )
    const tiny = { width: 1e-6, height: 1e-6 }
    const max = { width: 4096, height: 4096 }
    expect(() => latticeCells(max, tiny, false, { x: 0, y: 0 })).toThrow(RangeError)
    expect(() => latticeCells(max, tiny, true, { x: 0, y: 0 })).toThrow(RangeError)
  })
})

describe('fillTiles', () => {
  it('picks the closest tile per square cell', () => {
    const fill = fillTiles({
      source: halves(64, 32, RED, BLUE),
      tiles: [
        { id: 'red', pixels: solid(32, 32, RED) },
        { id: 'blue', pixels: solid(32, 32, BLUE) },
      ],
      backdrop: null,
      cell: SQUARE,
      diamond: false,
    })
    expect(fill).toEqual({
      offset: { x: 0, y: 0 },
      cells: [
        { x: 0, y: 0, tileId: 'red' },
        { x: 32, y: 0, tileId: 'blue' },
      ],
    })
  })

  it('leaves cells empty where the backdrop matches better', () => {
    const fill = fillTiles({
      source: halvesY(64, 64, GREEN, RED),
      tiles: [{ id: 'red', pixels: solid(32, 32, RED) }],
      backdrop: solid(64, 64, GREEN),
      cell: SQUARE,
      diamond: false,
    })
    expect(fill).toEqual({
      offset: { x: 0, y: 0 },
      cells: [
        { x: 0, y: 0, tileId: null },
        { x: 32, y: 0, tileId: null },
        { x: 0, y: 32, tileId: 'red' },
        { x: 32, y: 32, tileId: 'red' },
      ],
    })
  })

  it('prefers the tile when the backdrop ties', () => {
    const fill = fillTiles({
      source: solid(32, 32, RED),
      tiles: [{ id: 'red', pixels: solid(32, 32, RED) }],
      backdrop: solid(32, 32, RED),
      cell: SQUARE,
      diamond: false,
    })
    expect(fill.cells).toEqual([{ x: 0, y: 0, tileId: 'red' }])
  })

  it('ignores transparent tile pixels', () => {
    // 투명 칸 RGB 가 검정이라 알파 무시 시 darkRed 선택
    const centerRed = paint(32, 32, (x, y) => (x >= 8 && x < 24 && y >= 8 && y < 24 ? RED : CLEAR))
    const fill = fillTiles({
      source: halves(64, 32, RED, BLUE),
      tiles: [
        { id: 'centerRed', pixels: centerRed },
        { id: 'darkRed', pixels: solid(32, 32, [128, 0, 0]) },
        { id: 'blue', pixels: solid(32, 32, BLUE) },
      ],
      backdrop: null,
      cell: SQUARE,
      diamond: false,
    })
    expect(fill).toEqual({
      offset: { x: 0, y: 0 },
      cells: [
        { x: 0, y: 0, tileId: 'centerRed' },
        { x: 32, y: 0, tileId: 'blue' },
      ],
    })
  })

  it('samples every fourth pixel of the cell', () => {
    // 빨강 원본에서 격자 간격이 다른 후보 — 4px 표본이면 grid4 부터 거리 0, 8px 이면 grid8, 2px·전체 픽셀이면 grid2
    const grid = (step: number) =>
      paint(32, 32, (x, y) => (x % step === 0 && y % step === 0 ? RED : BLUE))
    const fill = fillTiles({
      source: solid(32, 32, RED),
      tiles: [
        { id: 'grid8', pixels: grid(8) },
        { id: 'grid4', pixels: grid(4) },
        { id: 'grid2', pixels: grid(2) },
      ],
      backdrop: null,
      cell: SQUARE,
      diamond: false,
    })
    expect(fill).toEqual({ offset: { x: 0, y: 0 }, cells: [{ x: 0, y: 0, tileId: 'grid4' }] })
  })

  it('finds a nonzero offset', () => {
    // 칸 격자가 (8, 8) 만큼 밀린 4x4 색 배치
    const ids = ['red', 'green', 'blue', 'yellow']
    const palette = [RED, GREEN, BLUE, YELLOW]
    const pattern = [
      [0, 1, 2, 3],
      [2, 3, 1, 0],
      [1, 0, 3, 2],
      [3, 2, 0, 1],
    ]
    const index = (p: number) => Math.floor((p - 8) / 32) + 1
    const source = paint(96, 96, (x, y) => palette[pattern[index(y)]![index(x)]!]!)
    const fill = fillTiles({
      source,
      tiles: ids.map((id, i) => ({ id, pixels: solid(32, 32, palette[i]!) })),
      backdrop: null,
      cell: SQUARE,
      diamond: false,
    })
    expect(fill.offset).toEqual({ x: 8, y: 8 })
    expect(fill.cells).toEqual(
      pattern.flatMap((row, j) =>
        row.map((colorIndex, i) => ({
          x: 8 + 32 * (i - 1),
          y: 8 + 32 * (j - 1),
          tileId: ids[colorIndex],
        })),
      ),
    )
  })

  it('finds a nonzero offset on a diamond lattice', () => {
    // 중심 (8 + 16i, 8j), i + j 짝수 다이아몬드마다 색 5종
    // (24, 8) 도 같은 격자라 탐색 순서상 첫 정답은 (8, 0)
    const cell = { width: 32, height: 16 }
    const ids = ['red', 'green', 'blue', 'yellow', 'black']
    const palette = [RED, GREEN, BLUE, YELLOW, BLACK]
    const colorIndex = (i: number, j: number) => (((i + 2 * j) % 5) + 5) % 5
    const source = paint(64, 32, (px, py) => {
      const u = (px + 0.5 - 8) / 16
      const v = (py + 0.5) / 8
      for (const i of [Math.floor(u), Math.floor(u) + 1])
        for (const j of [Math.floor(v), Math.floor(v) + 1])
          if ((i + j) % 2 === 0 && Math.abs(u - i) + Math.abs(v - j) < 1)
            return palette[colorIndex(i, j)]!
      throw new Error('fixture')
    })
    // 가장자리 칸은 4px 표본이 원본 안 마름모에 하나도 없으면 비교 불가라 빠진다
    const sampled = (x: number, y: number) => {
      for (let ly = 0; ly < cell.height; ly += 4)
        for (let lx = 0; lx < cell.width; lx += 4)
          if (
            insideDiamond(lx, ly, cell) &&
            x + lx >= 0 &&
            x + lx < 64 &&
            y + ly >= 0 &&
            y + ly < 32
          )
            return true
      return false
    }
    const expected = []
    for (let j = -2; j <= 6; j++)
      for (let i = -2; i <= 6; i++) {
        const x = 16 * i - 8
        const y = 8 * j - 8
        if ((i + j) % 2 === 0 && x + 32 > 0 && y + 16 > 0 && x < 64 && y < 32 && sampled(x, y))
          expected.push({ x, y, tileId: ids[colorIndex(i, j)] })
      }
    expect(expected.length).toBeLessThan(15)
    const fill = fillTiles({
      source,
      tiles: ids.map((id, i) => ({ id, pixels: solid(32, 16, palette[i]!) })),
      backdrop: null,
      cell,
      diamond: true,
    })
    expect(fill.offset).toEqual({ x: 8, y: 0 })
    expect(fill.cells).toEqual(expected)
  })

  it('masks diamond corners and clips edge cells', () => {
    // 마스크 밖 방해색: 원본 모서리 검정, masked 타일 모서리 흰색
    const cell = { width: 32, height: 16 }
    const diamond = fillTiles({
      source: paint(32, 16, (x, y) => (insideDiamond(x, y, cell) ? [200, 0, 0] : BLACK)),
      tiles: [
        {
          id: 'masked',
          pixels: paint(32, 16, (x, y) =>
            insideDiamond(x, y, cell) ? [200, 0, 0] : [255, 255, 255],
          ),
        },
        { id: 'dull', pixels: solid(32, 16, [120, 0, 0]) },
        { id: 'black', pixels: solid(32, 16, BLACK) },
      ],
      backdrop: null,
      cell,
      diamond: true,
    })
    expect(diamond).toEqual({
      offset: { x: 0, y: 0 },
      cells: [
        { x: -16, y: -8, tileId: 'black' },
        { x: 16, y: -8, tileId: 'black' },
        { x: 0, y: 0, tileId: 'masked' },
        { x: -16, y: 8, tileId: 'black' },
        { x: 16, y: 8, tileId: 'black' },
      ],
    })

    // 오른쪽 끝 칸은 원본 안 8px 만 비교(밖을 검정으로 보면 black 선택)
    const clipped = fillTiles({
      source: solid(40, 32, RED),
      tiles: [
        { id: 'black', pixels: solid(32, 32, BLACK) },
        { id: 'red', pixels: solid(32, 32, RED) },
      ],
      backdrop: null,
      cell: SQUARE,
      diamond: false,
    })
    expect(clipped).toEqual({
      offset: { x: 0, y: 0 },
      cells: [
        { x: 0, y: 0, tileId: 'red' },
        { x: 32, y: 0, tileId: 'red' },
      ],
    })
  })

  it('rejects candidates without valid samples', () => {
    const input = { source: solid(32, 32, RED), backdrop: null, cell: SQUARE, diamond: false }
    const ghost = { id: 'ghost', pixels: solid(32, 32, [255, 0, 0, 127]) }
    const blue = { id: 'blue', pixels: solid(32, 32, BLUE) }

    // 알파 127 은 색이 같아도 후보 제외, 128 부터 사용
    expect(fillTiles({ ...input, tiles: [ghost, blue] }).cells).toEqual([
      { x: 0, y: 0, tileId: 'blue' },
    ])
    const edge = { id: 'edge', pixels: solid(32, 32, [255, 0, 0, 128]) }
    expect(fillTiles({ ...input, tiles: [edge, blue] }).cells).toEqual([
      { x: 0, y: 0, tileId: 'edge' },
    ])

    // 표본 없는 구역은 0점이 아니라 평균에서 제외
    const sparse = paint(32, 32, (x, y) => (x < 8 && y < 16 ? BLUE : CLEAR))
    const dull = solid(32, 32, [100, 0, 0])
    expect(
      fillTiles({
        ...input,
        tiles: [
          { id: 'sparse', pixels: sparse },
          { id: 'dull', pixels: dull },
        ],
      }),
    ).toEqual({ offset: { x: 0, y: 0 }, cells: [{ x: 0, y: 0, tileId: 'dull' }] })

    // 모든 오프셋에 비교 가능한 칸이 없으면 오류, 후보 목록이 빈 경우만 빈 결과
    expect(() => fillTiles({ ...input, tiles: [ghost] })).toThrow(NO_COMPARABLE)
    expect(fillTiles({ ...input, tiles: [] })).toEqual({ offset: { x: 0, y: 0 }, cells: [] })
    expect(fillTiles({ ...input, tiles: [], backdrop: solid(32, 32, RED) })).toEqual({
      offset: { x: 0, y: 0 },
      cells: [],
    })
  })

  it('skips incomparable edge cells without dropping the offset', () => {
    const cell = { width: 16, height: 16 }
    // 왼쪽 8px 투명 타일, 원본 가장자리에 8px 이하로 걸친 칸은 비교 불가
    const margin = (color: Color) => paint(16, 16, (x) => (x < 8 ? CLEAR : color))

    // 오프셋 (8, 0), 칸 40 은 여백만 걸쳐 cells 에서 제외
    const bands = [RED, GREEN, BLUE, GREEN, RED, GREEN]
    const edge = fillTiles({
      source: paint(48, 16, (x) => bands[Math.floor(x / 8)]!),
      tiles: [
        { id: 'red', pixels: margin(RED) },
        { id: 'blue', pixels: margin(BLUE) },
      ],
      backdrop: null,
      cell,
      diamond: false,
    })
    expect(edge).toEqual({
      offset: { x: 8, y: 0 },
      cells: [
        { x: -8, y: 0, tileId: 'red' },
        { x: 8, y: 0, tileId: 'blue' },
        { x: 24, y: 0, tileId: 'red' },
      ],
    })

    // 합계는 (0, 0) 190 이 (8, 0) 250 을 이기고 평균은 (8, 0) 125 가 (0, 0) 190 을 이김
    const windows: Color[] = [RED, [130, 0, 190], RED]
    const mean = fillTiles({
      source: paint(24, 16, (x) => windows[Math.floor(x / 8)]!),
      tiles: [{ id: 'dull', pixels: margin([130, 0, 0]) }],
      backdrop: null,
      cell,
      diamond: false,
    })
    expect(mean).toEqual({
      offset: { x: 8, y: 0 },
      cells: [
        { x: -8, y: 0, tileId: 'dull' },
        { x: 8, y: 0, tileId: 'dull' },
      ],
    })
  })

  it('propagates the cell limit error', () => {
    expect(() =>
      fillTiles({
        source: solid(65, 64, RED),
        tiles: [{ id: 'red', pixels: solid(1, 1, RED) }],
        backdrop: null,
        cell: { width: 1, height: 1 },
        diamond: false,
      }),
    ).toThrow(RangeError)
  })

  it('rejects images that do not match the cell or source size', () => {
    const input = {
      source: solid(32, 32, RED),
      tiles: [{ id: 'red', pixels: solid(32, 32, RED) }],
      backdrop: null,
      cell: SQUARE,
      diamond: false,
    }
    expect(() =>
      fillTiles({ ...input, tiles: [{ id: 'small', pixels: solid(16, 16, RED) }] }),
    ).toThrow('크기')
    expect(() => fillTiles({ ...input, backdrop: solid(16, 16, RED) })).toThrow('크기')
  })
})

describe('placementRects', () => {
  it('maps placements to source pixels', () => {
    const plan = objectPlan({ x: 0.5, y: 0.25, w: 0.25, h: 0.5 })
    expect(placementRects(plan, { width: 800, height: 600 })).toEqual([
      { x: 400, y: 150, width: 200, height: 300 },
    ])
  })
})

describe('reconstructionVisible', () => {
  const withBase = [assetOf(planOf('tile', 'tile'), [{ index: 0, imageId: 'base' }])]

  it('requires an approved plan and a base image', () => {
    for (const phase of ['sourceDraft', 'analyzing', 'planReview'] as const)
      expect(reconstructionVisible(spriteOf(phase, withBase))).toBe(false)
    expect(reconstructionVisible(spriteOf('baseReview', withBase))).toBe(true)
    expect(reconstructionVisible(spriteOf('frameReview', withBase))).toBe(true)
  })

  it('stays hidden without an index 0 image', () => {
    const none = assetOf(planOf('none', 'tile'), [{ index: 0, imageId: null }])
    const later = assetOf(planOf('later', 'tile'), [{ index: 1, imageId: 'frame' }])
    expect(reconstructionVisible(spriteOf('baseReview', [none, later]))).toBe(false)
    expect(reconstructionVisible(spriteOf('baseReview', [none, ...withBase]))).toBe(true)
  })
})

describe('tileFillKey', () => {
  const cell = { width: 128, height: 64 }
  const sprite = (plans: SpriteAssetPlan[], images: Record<string, string>) =>
    spriteOf(
      'baseReview',
      plans.map((plan) => assetOf(plan, [{ index: 0, imageId: images[plan.id] ?? null }])),
    )
  const plans = [
    planOf('t1', 'tile', { x: 0, y: 0, w: 0.1, h: 0.1 }),
    planOf('t2', 'tile', { x: 0.2, y: 0, w: 0.1, h: 0.1 }),
    planOf('bg', 'backdrop'),
    planOf('o1', 'object', { x: 0.5, y: 0.5, w: 0.2, h: 0.2 }),
  ]
  const images = { t1: 'img-t1', t2: 'img-t2', bg: 'img-bg', o1: 'img-o1' }
  const base = tileFillKey(plans, sprite(plans, images), cell)

  it('ignores placements', () => {
    const edited = plans.map((plan) => ({
      ...plan,
      name: `${plan.name} 수정`,
      sourceBounds: { x: 0.4, y: 0.4, w: 0.3, h: 0.3 },
    }))
    expect(tileFillKey(edited, sprite(edited, images), cell)).toBe(base)
    const otherObject = { ...images, o1: 'img-o1-new' }
    expect(tileFillKey(plans, sprite(plans, otherObject), cell)).toBe(base)
  })

  it('changes with tile images, roles, cell size and backdrop image', () => {
    const keys = new Set([base])
    keys.add(tileFillKey(plans, sprite(plans, { ...images, t1: 'img-t1-new' }), cell))
    const asObject = plans.map((plan) =>
      plan.id === 't2' ? { ...plan, role: 'object' as const } : plan,
    )
    keys.add(tileFillKey(asObject, sprite(asObject, images), cell))
    keys.add(tileFillKey(plans, sprite(plans, images), { width: 64, height: 32 }))
    keys.add(tileFillKey(plans, sprite(plans, images), null))
    keys.add(tileFillKey(plans, sprite(plans, { ...images, bg: 'img-bg-new' }), cell))
    expect(keys.size).toBe(6)
  })
})
