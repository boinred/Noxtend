import { describe, expect, it } from 'vitest'
import { getPreviewStages } from './CharacterPipelinePreview'

describe('getPreviewStages', () => {
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
})
