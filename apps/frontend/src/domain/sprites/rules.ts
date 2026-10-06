import type { ProductionMode, SpriteRepeat, SpriteTileLayout } from './types'

export function productionModeOf(job: { productionMode?: ProductionMode }): ProductionMode {
  return job.productionMode ?? 'threeD'
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
