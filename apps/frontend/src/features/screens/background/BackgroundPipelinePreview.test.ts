import { describe, expect, it } from 'vitest'
import { getPreviewStages } from './BackgroundPipelinePreview'

describe('getPreviewStages (Background)', () => {
  it('enables review stage when requiresReview is true', () => {
    const stages = getPreviewStages(true, false)
    const review = stages.find((s) => s.id === 'review')
    expect(review?.enabled).toBe(true)

    const reconstruct = stages.find((s) => s.id === 'reconstruct')
    expect(reconstruct?.enabled).toBe(false)
  })

  it('enables reconstruct stage when producesMeshes is true', () => {
    const stages = getPreviewStages(false, true)
    const review = stages.find((s) => s.id === 'review')
    expect(review?.enabled).toBe(false)

    const reconstruct = stages.find((s) => s.id === 'reconstruct')
    expect(reconstruct?.enabled).toBe(true)
  })

  it('uses background stage labels matching TASK_KIND_LABELS', () => {
    const stages = getPreviewStages(true, true)
    const extract = stages.find((s) => s.id === 'extract')
    expect(extract?.label).toBe('파츠 식별')

    const reconstruct = stages.find((s) => s.id === 'reconstruct')
    expect(reconstruct?.label).toBe('3D 제작')
  })
})
