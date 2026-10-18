import type { Bounds } from '../job/types'
import { assetRole } from './rules'
import type {
  SpriteAsset,
  SpriteAssetPlan,
  SpriteCanvas,
  SpriteSettings,
  SpriteState,
} from './types'

export interface Pixels {
  width: number
  height: number
  data: Uint8ClampedArray
}
export interface CellSize {
  width: number
  height: number
}
// 칸 외접 사각형 왼쪽 위(원본 px), tileId null 은 원경 배경 표시
export interface TileCell {
  x: number
  y: number
  tileId: string | null
}
export interface TileFill {
  offset: { x: number; y: number }
  cells: TileCell[]
}

// 화면 렌더링 부담 상한
const MAX_CELLS = 4096
const OFFSET_STEP = 8
// 칸을 가로 4 × 세로 2 구역으로 나눈 평균색
const ZONES_X = 4
const ZONES_Y = 2
const MIN_ALPHA = 128
// 칸 좌표가 이 간격의 배수인 점만 표본
const SAMPLE_STEP = 4

// 타일 픽셀과 1:1 로 맞추는 정수 px 칸
export function reconstructionCell(
  settings: SpriteSettings,
  source: SpriteCanvas,
  cellWidth: number | null,
): CellSize | null {
  if (cellWidth === null || !Number.isFinite(cellWidth) || cellWidth <= 0 || cellWidth > 1)
    return null
  const width = Math.max(1, Math.round(cellWidth * source.width))
  const height = settings.view === 'isometric' ? Math.max(1, Math.round(width / 2)) : width
  return { width, height }
}

export function latticeCells(
  source: SpriteCanvas,
  cell: CellSize,
  diamond: boolean,
  offset: { x: number; y: number },
): { x: number; y: number }[] {
  const { width, height } = cell
  if (!(Number.isFinite(width) && Number.isFinite(height) && width > 0 && height > 0))
    throw new Error('재구성 칸 크기가 유효하지 않습니다')
  // 다이아몬드 중심은 반 칸 간격, i + j 짝수만 사용
  const stepX = diamond ? width / 2 : width
  const stepY = diamond ? height / 2 : height
  const shiftX = diamond ? width / 2 : 0
  const shiftY = diamond ? height / 2 : 0
  const cells: { x: number; y: number }[] = []
  // 시작 인덱스는 한 칸 앞에서 잡고 겹침 조건으로 선별
  for (
    let j = Math.floor((shiftY - offset.y - height) / stepY) - 1;
    offset.y + j * stepY - shiftY < source.height;
    j++
  ) {
    const y = offset.y + j * stepY - shiftY
    if (y + height <= 0) continue
    for (
      let i = Math.floor((shiftX - offset.x - width) / stepX) - 1;
      offset.x + i * stepX - shiftX < source.width;
      i++
    ) {
      const x = offset.x + i * stepX - shiftX
      if (x + width <= 0 || (diamond && (i + j) % 2 !== 0)) continue
      if (cells.length === MAX_CELLS) throw new RangeError('재구성 칸이 너무 많습니다')
      cells.push({ x, y })
    }
  }
  return cells
}

interface Candidate {
  id: string | null
  pixels: Pixels
  // 원경 배경은 칸 위치의 원본 크기 이미지, 타일은 칸 크기 이미지
  anchored: boolean
}

function requireSize(pixels: Pixels, width: number, height: number): void {
  if (
    pixels.width !== width ||
    pixels.height !== height ||
    pixels.data.length !== width * height * 4
  )
    throw new Error('재구성 입력 이미지 크기가 맞지 않습니다')
}

interface Layout {
  // 구역 경계와 행마다 마스크가 차지하는 가로 구간 [left, right)
  xBounds: number[]
  yBounds: number[]
  left: Int32Array
  right: Int32Array
}

function layoutOf(cell: CellSize, diamond: boolean): Layout {
  const bounds = (zones: number, size: number) =>
    Array.from({ length: zones + 1 }, (_, zone) => Math.ceil((zone * size) / zones))
  const left = new Int32Array(cell.height)
  const right = new Int32Array(cell.height)
  for (let y = 0; y < cell.height; y++) {
    right[y] = cell.width
    if (!diamond) continue
    const rest = 1 - Math.abs(y + 0.5 - cell.height / 2) / (cell.height / 2)
    let first = -1
    let last = -1
    for (let x = 0; x < cell.width; x++)
      if (Math.abs(x + 0.5 - cell.width / 2) / (cell.width / 2) <= rest) {
        if (first < 0) first = x
        last = x
      }
    left[y] = Math.max(first, 0)
    right[y] = last + 1
  }
  return {
    xBounds: bounds(ZONES_X, cell.width),
    yBounds: bounds(ZONES_Y, cell.height),
    left,
    right,
  }
}

// 원본·마스크·후보 알파 128 이상 교집합의 구역 평균색 거리, 표본 없으면 Infinity
function distance(
  source: Pixels,
  candidate: Candidate,
  layout: Layout,
  cell: CellSize,
  cx: number,
  cy: number,
): number {
  const windowX0 = Math.max(0, -cx)
  const windowX1 = Math.min(cell.width, source.width - cx)
  const windowY0 = Math.max(0, -cy)
  const windowY1 = Math.min(cell.height, source.height - cy)
  const s = source.data
  const c = candidate.pixels.data
  const stride = candidate.anchored ? source.width : cell.width
  const base = candidate.anchored ? cy * source.width + cx : 0
  let total = 0
  let valid = 0
  for (let zy = 0; zy < ZONES_Y; zy++) {
    const y0 = Math.max(layout.yBounds[zy]!, windowY0)
    const y1 = Math.min(layout.yBounds[zy + 1]!, windowY1)
    for (let zx = 0; zx < ZONES_X; zx++) {
      const x0 = Math.max(layout.xBounds[zx]!, windowX0)
      const x1 = Math.min(layout.xBounds[zx + 1]!, windowX1)
      let n = 0
      let sr = 0
      let sg = 0
      let sb = 0
      let cr = 0
      let cg = 0
      let cb = 0
      for (let y = Math.ceil(y0 / SAMPLE_STEP) * SAMPLE_STEP; y < y1; y += SAMPLE_STEP) {
        const xa = Math.ceil(Math.max(x0, layout.left[y]!) / SAMPLE_STEP) * SAMPLE_STEP
        const xb = Math.min(x1, layout.right[y]!)
        let si = ((cy + y) * source.width + cx + xa) * 4
        let ci = (base + y * stride + xa) * 4
        for (let x = xa; x < xb; x += SAMPLE_STEP, si += SAMPLE_STEP * 4, ci += SAMPLE_STEP * 4) {
          if (c[ci + 3]! < MIN_ALPHA) continue
          n++
          sr += s[si]!
          sg += s[si + 1]!
          sb += s[si + 2]!
          cr += c[ci]!
          cg += c[ci + 1]!
          cb += c[ci + 2]!
        }
      }
      if (n === 0) continue
      const dr = (sr - cr) / n
      const dg = (sg - cg) / n
      const db = (sb - cb) / n
      total += dr * dr + dg * dg + db * db
      valid++
    }
  }
  return valid === 0 ? Infinity : Math.sqrt(total / valid)
}

export function fillTiles(input: {
  source: Pixels
  tiles: { id: string; pixels: Pixels }[]
  backdrop: Pixels | null
  cell: CellSize
  diamond: boolean
}): TileFill {
  const { source, tiles, backdrop, cell, diamond } = input
  if (tiles.length === 0) return { offset: { x: 0, y: 0 }, cells: [] }
  if (
    !Number.isInteger(cell.width) ||
    !Number.isInteger(cell.height) ||
    cell.width < 1 ||
    cell.height < 1
  )
    throw new Error('재구성 칸 크기가 유효하지 않습니다')
  requireSize(source, source.width, source.height)
  for (const tile of tiles) requireSize(tile.pixels, cell.width, cell.height)
  if (backdrop) requireSize(backdrop, source.width, source.height)

  const candidates: Candidate[] = tiles.map((tile) => ({
    id: tile.id,
    pixels: tile.pixels,
    anchored: false,
  }))
  if (backdrop) candidates.push({ id: null, pixels: backdrop, anchored: true })
  const layout = layoutOf(cell, diamond)

  let best: TileFill | null = null
  let bestMean = Infinity
  for (let oy = 0; oy < cell.height; oy += OFFSET_STEP)
    for (let ox = 0; ox < cell.width; ox += OFFSET_STEP) {
      const picked: TileCell[] = []
      let sum = 0
      // 상한 초과 RangeError 는 탐색 전체 중단
      for (const lattice of latticeCells(source, cell, diamond, { x: ox, y: oy })) {
        const x = Math.floor(lattice.x)
        const y = Math.floor(lattice.y)
        let nearest = Infinity
        let tileId: string | null = null
        for (const candidate of candidates) {
          const value = distance(source, candidate, layout, cell, x, y)
          if (value < nearest) {
            nearest = value
            tileId = candidate.id
          }
        }
        // 비교 불가 칸은 점수·결과에서만 제외하고 오프셋 유지
        if (nearest === Infinity) continue
        sum += nearest
        picked.push({ x, y, tileId })
      }
      if (picked.length === 0) continue
      const mean = sum / picked.length
      if (mean < bestMean) {
        bestMean = mean
        best = { offset: { x: ox, y: oy }, cells: picked }
      }
    }
  if (best === null) throw new Error('재구성에 비교할 수 있는 픽셀이 없습니다')
  return best
}

// 저장된 자리가 없으면 ROI 한 자리
export function placementsOf(plan: SpriteAssetPlan): Bounds[] {
  return plan.placements ?? [plan.sourceBounds]
}

// 서버 규칙과 같은 오브젝트 자리 상한
export const MAX_PLACEMENTS = 20

// 저장된 자리 없는 오브젝트는 이전 ROI 를 자리로 고정, 자리는 생성 기준을 따라가지 않음
export function moveGenerationBox(
  plan: SpriteAssetPlan,
  bounds: Bounds,
  settings: SpriteSettings,
): SpriteAssetPlan {
  const pinned = assetRole(settings, plan) === 'object' && plan.placements === null
  return {
    ...plan,
    sourceBounds: bounds,
    placements: pinned ? [plan.sourceBounds] : plan.placements,
  }
}

// 선택한 자리 또는 마지막 자리를 오른쪽 아래로 옮긴 복사본, 원본 안에 맞춤
export function addPlacement(plan: SpriteAssetPlan, ordinal: number | null): SpriteAssetPlan {
  const placements = placementsOf(plan)
  const from = placements[ordinal ?? placements.length - 1]!
  // 반복 복사의 부동소수 꼬리가 정밀 입력에 보이지 않게 반올림
  const shift = (start: number, size: number) =>
    Math.max(0, Math.min(Math.round((start + 0.02) * 1e6) / 1e6, 1 - size))
  return {
    ...plan,
    placements: [...placements, { ...from, x: shift(from.x, from.w), y: shift(from.y, from.h) }],
  }
}

export function placementRects(
  plan: SpriteAssetPlan,
  source: SpriteCanvas,
): { x: number; y: number; width: number; height: number }[] {
  return placementsOf(plan).map((bounds) => ({
    x: bounds.x * source.width,
    y: bounds.y * source.height,
    width: bounds.w * source.width,
    height: bounds.h * source.height,
  }))
}

function baseImageId(asset: SpriteAsset): string | null {
  return asset.frames.find((frame) => frame.index === 0)?.currentImageId ?? null
}

export function reconstructionVisible(sprite: SpriteState): boolean {
  return (
    !['sourceDraft', 'analyzing', 'planReview'].includes(sprite.phase) &&
    sprite.assets.some((asset) => baseImageId(asset) !== null)
  )
}

// 자리·이름 편집은 타일 재계산 대상 아님
export function tileFillKey(
  plans: SpriteAssetPlan[],
  sprite: SpriteState,
  cell: CellSize | null,
): string {
  const imageIdOf = (plan: SpriteAssetPlan) => {
    const asset = sprite.assets.find((item) => item.id === plan.id)
    return asset ? baseImageId(asset) : null
  }
  const tiles: (string | null)[][] = []
  const backdrops: (string | null)[] = []
  for (const plan of plans) {
    const role = assetRole(sprite.settings, plan)
    if (role === 'tile') tiles.push([plan.id, role, imageIdOf(plan)])
    else if (role === 'backdrop') backdrops.push(imageIdOf(plan))
  }
  return JSON.stringify({ cell, tiles, backdrops })
}
