import type { ProductionMode } from './types'

export function productionModeOf(job: { productionMode?: ProductionMode }): ProductionMode {
  return job.productionMode ?? 'threeD'
}
