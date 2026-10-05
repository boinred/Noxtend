import { describe, expect, it } from 'vitest'
import { meshState } from './types'
import type { AssetPart, GeneratedMesh, Job, JobTask, ViewDirection } from './types'

/**
 * 파츠 한 줄이 3D 를 어떤 상태로 보여주는가.
 *
 * Design Ref: §11.3
 *
 * **화면이 아니라 여기서 정하는 이유**는 갈래가 일곱이기 때문이다. JSX 안에서
 * 삼항으로 엮으면 어떤 조합이 빠졌는지 읽어서는 알 수 없고, 테스트도 렌더를 거쳐야 한다.
 */
describe('meshState', () => {
  it('3D 를 고르지 않은 작업은 아무것도 만들지 않는다', () => {
    const state = meshState(job({ mesh: null }), part(), [])

    // 옛 작업과 3D 를 원하지 않는 신규 작업이 같은 자리에 온다
    expect(state.kind).toBe('notRequested')
  })

  it('이미지가 모자라면 몇 장인지 센다', () => {
    const state = meshState(job(), part(['front', 'right']), [])

    expect(state).toMatchObject({ kind: 'awaitingImages', ready: 2 })
  })

  it('네 장이 모였는데 공정이 아직 없으면 준비 중이다', () => {
    // 오케스트레이터가 계획하기까지의 짧은 틈 — 사용자에게는 이것도 진행 중이다
    const state = meshState(job(), part(ALL), [])

    expect(state.kind).toBe('planning')
  })

  it('대기 중인 공정은 대기로 보여준다', () => {
    const state = meshState(job(), part(ALL), [task({ status: 'pending' })])

    expect(state.kind).toBe('queued')
  })

  it('진행 중이면 진행률을 함께 낸다', () => {
    const state = meshState(job(), part(ALL), [task({ status: 'running', progress: 64 })])

    expect(state).toMatchObject({ kind: 'running', progress: 64 })
  })

  it('진행률이 아직 없으면 0 으로 시작한다', () => {
    const state = meshState(job(), part(ALL), [task({ status: 'running' })])

    // 막대를 그리려면 숫자가 있어야 한다. 없는 것과 0% 는 화면에서 같은 그림이다
    expect(state).toMatchObject({ kind: 'running', progress: 0 })
  })

  it('실패는 공정 id 를 들고 있어야 다시 시도할 수 있다', () => {
    const failed = task({ status: 'failed', failureReason: 'MESH_TASK_FAILED' })
    const state = meshState(job(), part(ALL), [failed])

    expect(state).toMatchObject({
      kind: 'failed',
      taskId: failed.id,
      failureReason: 'MESH_TASK_FAILED',
    })
  })

  it('성공했으면 결과를 낸다', () => {
    const mesh: GeneratedMesh = {
      id: 'm1',
      hasPreview: true,
      hasFbx: false,
      fbxSizeBytes: null,
      sizeBytes: 4_821_900,
      creditsConsumed: 50,
      createdAt: '2026-08-12T00:00:00Z',
    }

    const state = meshState(job(), part(ALL, mesh), [task({ status: 'succeeded' })])

    expect(state).toMatchObject({ kind: 'ready', mesh })
  })

  /**
   * 공정은 성공했는데 결과가 안 붙은 순간이 있다 — 취소가 그 사이에 들어온 경우다.
   *
   * 성공으로 그리면 내려받기 버튼이 눌리지 않는 상태로 남는다.
   */
  it('성공했는데 결과가 없으면 준비 중으로 남는다', () => {
    const state = meshState(job(), part(ALL), [task({ status: 'succeeded' })])

    expect(state.kind).toBe('planning')
  })
})

const ALL: ViewDirection[] = ['front', 'right', 'back', 'left']

function job(models: Partial<Job['models']> = {}): Job {
  return {
    id: 'j1',
    category: 'background',
    sourceImageId: 's1',
    status: 'running',
    scene: null,
    models: {
      text: null,
      image: { providerConfigId: 'p-image', model: 'gemini-image' },
      mesh: { providerConfigId: 'p-mesh', model: 'P1-20260311' },
      ...models,
    },
    tasks: [],
    parts: [],
    failureReason: null,
    createdAt: '2026-08-12T00:00:00Z',
    completedAt: null,
    gender: null,
    partHints: [],
  }
}

function part(
  directions: ViewDirection[] = [],
  generatedMesh: GeneratedMesh | null = null,
): AssetPart {
  return {
    id: 'p1',
    name: '가로등',
    ordinal: 0,
    description: null,
    category: null,
    placements: [],
    depthOrder: null,
    occludedBy: [],
    generatedImageId: null,
    generatedImages: directions.map((viewDirection) => ({
      id: `i-${viewDirection}`,
      viewDirection,
    })),
    generatedMesh,
  }
}

function task(overrides: Partial<JobTask> = {}): JobTask {
  return {
    id: 't-mesh',
    kind: 'reconstruct',
    ordinal: 10,
    status: 'pending',
    failureReason: null,
    attemptCount: 1,
    startedAt: null,
    completedAt: null,
    partId: 'p1',
    viewDirection: null,
    progress: null,
    ...overrides,
  }
}
