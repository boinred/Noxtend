import type { JobTask } from '../job/types'
import type { ProductionMode, SpriteRepeat, SpriteState, SpriteTileLayout } from './types'

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
