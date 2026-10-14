import type { JobTask } from '../job/types'
import type {
  ProductionMode,
  SpriteRepeat,
  SpriteState,
  SpriteTileLayout,
  SpriteSettings,
  SpriteAssetPlan,
  SpriteAssetRole,
  SpriteGrid,
  SpriteCanvas,
} from './types'

export function productionModeOf(job: { productionMode?: ProductionMode }): ProductionMode {
  return job.productionMode ?? 'threeD'
}

export function spriteProgressCounts(sprite: SpriteState, tasks: readonly JobTask[]) {
  const loops = sprite.assets.filter((asset) => asset.plan.loop)
  return {
    analysis: Number(
      tasks.filter((task) => task.kind === 'analyzeSprites').at(-1)?.status === 'succeeded',
    ),
    backgrounds: sprite.assets.filter(
      (asset) => asset.frames.find((frame) => frame.index === 0)?.currentImageId,
    ).length,
    approvedBases: sprite.assets.filter(
      (asset) =>
        asset.approvedBaseImageId &&
        asset.approvedBaseImageId ===
          asset.frames.find((frame) => frame.index === 0)?.currentImageId,
    ).length,
    frames: loops.reduce(
      (count, asset) =>
        count +
        asset.frames.filter(
          (frame) => frame.index > 0 && frame.index < asset.plan.frameCount && frame.currentImageId,
        ).length,
      0,
    ),
    frameTotal: loops.reduce((count, asset) => count + Math.max(0, asset.plan.frameCount - 1), 0),
    loops: loops.length,
    approvedLoops: loops.filter((asset) => asset.approval !== null).length,
    exported: Number(sprite.exports.some((item) => item.isCurrent)),
  }
}

export function tileOffsets(
  width: number,
  height: number,
  repeat: SpriteRepeat,
  layout: SpriteTileLayout,
): readonly { x: number; y: number }[] {
  const offsets = []
  for (const row of repeat === 'x' ? [0] : [-1, 0, 1]) {
    for (const column of repeat === 'y' ? [0] : [-1, 0, 1]) {
      offsets.push(
        layout === 'diamond'
          ? { x: ((column - row) * width) / 2, y: ((column + row) * height) / 2 }
          : { x: column * width, y: row * height },
      )
    }
  }
  return offsets
}

export function assetRole(
  settings: SpriteSettings,
  plan: Pick<SpriteAssetPlan, 'role'>,
): SpriteAssetRole {
  return plan.role ?? (settings.outputKind === 'layers' ? 'layer' : 'tile')
}
export function cellWidthOf(
  grid: SpriteGrid | null,
  plans: readonly SpriteAssetPlan[],
  settings: SpriteSettings,
): number | null {
  if (grid !== null) return grid.cellWidth
  const widths = plans
    .filter((plan) => assetRole(settings, plan) === 'tile')
    .map((plan) => plan.sourceBounds.w)
    .sort((a, b) => a - b)
  if (!widths.length) return null
  return (widths[Math.floor((widths.length - 1) / 2)]! + widths[Math.floor(widths.length / 2)]!) / 2
}
export function assetOutputSize(
  settings: SpriteSettings,
  source: SpriteCanvas,
  cellWidth: number | null,
  plan: SpriteAssetPlan,
): { canvas: SpriteCanvas; scale: number } | { error: string } {
  const role = assetRole(settings, plan)
  if (role === 'tile')
    return {
      canvas: {
        width: settings.tileWidth,
        height: settings.view === 'isometric' ? settings.tileWidth / 2 : settings.tileWidth,
      },
      scale: 1,
    }
  if (role === 'layer') {
    const scale = Math.min(1, 1024 / Math.max(source.width, source.height))
    return {
      canvas: {
        width: Math.max(1, Math.round(source.width * scale)),
        height: Math.max(1, Math.round(source.height * scale)),
      },
      scale: 1,
    }
  }
  if (cellWidth === null || !Number.isFinite(cellWidth) || cellWidth <= 0 || cellWidth > 1)
    return { error: '격자 칸 너비가 필요합니다' }
  const worldScale = settings.tileWidth / (cellWidth * source.width)
  let width = source.width * worldScale,
    height = source.height * worldScale,
    scale = 1
  if (role === 'object') {
    width *= plan.sourceBounds.w
    height *= plan.sourceBounds.h
  } else {
    scale = Math.min(1, 1024 / Math.max(width, height))
    width *= scale
    height *= scale
  }
  width = Math.round(width)
  height = Math.round(height)
  if (
    !Number.isFinite(width) ||
    !Number.isFinite(height) ||
    width < 1 ||
    width > 1024 ||
    height < 1 ||
    height > 1024
  )
    return {
      error:
        role === 'object'
          ? '오브젝트 출력 크기는 1~1024픽셀이어야 합니다'
          : '배경 출력 크기는 1~1024픽셀이어야 합니다',
    }
  return { canvas: { width, height }, scale }
}
