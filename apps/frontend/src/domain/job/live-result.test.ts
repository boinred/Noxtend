import { describe, expect, it } from 'vitest'
import { hasResultToShow, meshTally } from './types'
import type { AssetPart, GeneratedMesh, Job, JobStatus, JobTask } from './types'

describe('hasResultToShow', () => {
  it.each<JobStatus>(['failed', 'canceled'])('%s 작업은 전용 화면을 유지한다', (status) => {
    expect(hasResultToShow(job({ status, parts: [part('p1')] }))).toBe(false)
  })

  it('진행 중이어도 파츠가 있으면 결과를 연다', () => {
    expect(hasResultToShow(job({ status: 'running', parts: [part('p1')] }))).toBe(true)
  })

  it('파츠가 없는 진행 작업은 진행 화면을 유지한다', () => {
    expect(hasResultToShow(job({ status: 'running', parts: [] }))).toBe(false)
  })

  it.each<JobStatus>(['succeeded', 'partiallySucceeded'])('%s 작업은 결과를 연다', (status) => {
    expect(hasResultToShow(job({ status, parts: [] }))).toBe(true)
  })
})

describe('meshTally', () => {
  it('3D 공정이 있는 파츠만 분모에 넣는다', () => {
    const ready = part('ready', mesh())
    const running = part('running')
    const withoutMeshTask = part('without-mesh-task')
    const current = job({
      parts: [ready, running, withoutMeshTask],
      tasks: [reconstructTask('ready', 'succeeded'), reconstructTask('running', 'running')],
    })

    expect(meshTally(current)).toEqual({ ready: 1, running: 1, planned: 2 })
  })

  it('3D 공정이 없으면 빈 집계를 낸다', () => {
    expect(meshTally(job({ parts: [part('p1')] }))).toEqual({
      ready: 0,
      running: 0,
      planned: 0,
    })
  })
})

function job(overrides: Partial<Job> = {}): Job {
  return {
    id: 'job-live',
    category: 'background',
    sourceImageId: 'source-live',
    status: 'running',
    scene: null,
    models: {
      text: null,
      image: null,
      mesh: { providerConfigId: 'mesh-provider', model: 'mesh-model' },
    },
    tasks: [],
    parts: [],
    failureReason: null,
    gender: null,
    partHints: [],
    createdAt: '2026-08-17T00:00:00Z',
    completedAt: null,
    ...overrides,
  }
}

function part(id: string, generatedMesh: GeneratedMesh | null = null): AssetPart {
  return {
    id,
    name: id,
    ordinal: 0,
    description: null,
    category: null,
    placements: [],
    depthOrder: null,
    occludedBy: [],
    generatedImageId: null,
    generatedImages: [],
    generatedMesh,
  }
}

function mesh(): GeneratedMesh {
  return {
    id: 'mesh-ready',
    hasPreview: true,
    hasFbx: false,
    fbxSizeBytes: null,
    sizeBytes: 1024,
    creditsConsumed: 30,
    createdAt: '2026-08-17T00:00:00Z',
  }
}

function reconstructTask(partId: string, status: JobStatus): JobTask {
  return {
    id: `task-${partId}`,
    kind: 'reconstruct',
    ordinal: 10,
    status,
    failureReason: null,
    attemptCount: 1,
    startedAt: status === 'pending' ? null : '2026-08-17T00:00:00Z',
    completedAt: status === 'succeeded' ? '2026-08-17T00:01:00Z' : null,
    partId,
    viewDirection: null,
    progress: status === 'running' ? 50 : null,
  }
}
