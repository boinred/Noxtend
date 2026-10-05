import { describe, expect, it } from 'vitest'
import { computeStepStates } from './CharacterPipelineStepper'
import type { JobTask } from '@/domain/job/types'

function mockTask(kind: JobTask['kind'], status: JobTask['status'], ordinal = 1): JobTask {
  return {
    id: `task-${kind}`,
    kind,
    ordinal,
    status,
    attemptCount: 1,
    failureReason: null,
    startedAt: null,
    completedAt: null,
    partId: null,
    viewDirection: null,
    progress: null,
  }
}

describe('computeStepStates', () => {
  it('computes initial upcoming states for empty tasks', () => {
    const states = computeStepStates([], 'running')

    expect(states.analyze).toBe('upcoming')
    expect(states.extract).toBe('upcoming')
    expect(states.decompose).toBe('upcoming')
    expect(states.review).toBe('upcoming')
    expect(states.rewriteDescriptions).toBe('upcoming')
    expect(states.generate).toBe('upcoming')
    expect(states.reconstruct).toBe('upcoming')
  })

  it('marks running task state correctly', () => {
    const tasks = [mockTask('analyze', 'succeeded', 1), mockTask('extract', 'running', 2)]

    const states = computeStepStates(tasks, 'running')

    expect(states.analyze).toBe('succeeded')
    expect(states.extract).toBe('running')
    expect(states.decompose).toBe('upcoming')
  })

  it('handles pendingReview jobStatus correctly', () => {
    const tasks = [
      mockTask('analyze', 'succeeded', 1),
      mockTask('extract', 'succeeded', 2),
      mockTask('decompose', 'succeeded', 3),
    ]

    const states = computeStepStates(tasks, 'pendingReview')

    expect(states.review).toBe('pendingReview')
  })
})
