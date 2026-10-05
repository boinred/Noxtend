import { describe, expect, it } from 'vitest'
import { canAddMeshProduction, meshReadyPartCount } from './types'
import type { AssetPart, Job, JobStatus, ViewDirection } from './types'

/**
 * 끝난 작업에 3D 를 붙일 수 있는가.
 *
 * Design Ref: §7.1 · §7.3
 *
 * **조건이 셋이라 도메인에서 센다.** JSX 안에서 엮으면 어떤 조합이 빠졌는지 읽어서는
 * 알 수 없고, 테스트도 렌더를 거쳐야 한다.
 */
describe('meshReadyPartCount', () => {
  it('네 방향이 다 있는 파츠만 센다', () => {
    const job = jobWith([ALL, ['front', 'right'], ALL, []])

    // 유료 호출이 파츠당 1건이라 이 숫자가 곧 나갈 크레딧이다
    expect(meshReadyPartCount(job)).toBe(2)
  })

  it('같은 방향이 여러 장이어도 하나로 센다', () => {
    // 재생성이 이미지 행을 쌓는다 — 방향 종류가 넷이어야 한다
    const part = partWith(['front', 'front', 'right', 'back', 'left'])

    expect(meshReadyPartCount({ ...jobWith([]), parts: [part] })).toBe(1)
  })

  it('파츠가 없으면 0 이다', () => {
    expect(meshReadyPartCount(jobWith([]))).toBe(0)
  })
})

describe('canAddMeshProduction', () => {
  it('이미지가 다 있고 3D 를 안 골랐으면 붙일 수 있다', () => {
    expect(canAddMeshProduction(jobWith([ALL]))).toBe(true)
  })

  it('이미 3D 를 고른 작업은 붙일 수 없다', () => {
    const job = jobWith([ALL])
    job.models.mesh = { providerConfigId: 'p', model: 'P1-20260311' }

    // 한 작업에 모델 조합이 둘이 되면 어느 mesh 가 무엇인지 알 수 없다
    expect(canAddMeshProduction(job)).toBe(false)
  })

  it('취소된 작업은 붙일 수 없다', () => {
    // 취소는 "끝났다" 가 아니라 "하지 말라" 다
    expect(canAddMeshProduction({ ...jobWith([ALL]), status: 'canceled' })).toBe(false)
  })

  it('네 장 있는 파츠가 없으면 붙일 수 없다', () => {
    expect(canAddMeshProduction(jobWith([['front', 'right']]))).toBe(false)
  })

  it('실패한 작업도 이미지가 남아 있으면 붙일 수 있다', () => {
    // 작업이 실패했다는 사실이 남은 재료를 못 쓰게 하지 않는다
    expect(canAddMeshProduction({ ...jobWith([ALL]), status: 'failed' })).toBe(true)
  })
})

const ALL: ViewDirection[] = ['front', 'right', 'back', 'left']

function partWith(directions: ViewDirection[], ordinal = 0): AssetPart {
  return {
    id: `p${ordinal}`,
    name: `파츠${ordinal}`,
    ordinal,
    description: null,
    category: null,
    placements: [],
    depthOrder: null,
    occludedBy: [],
    generatedImageId: null,
    generatedImages: directions.map((viewDirection, index) => ({
      id: `i${ordinal}-${index}`,
      viewDirection,
    })),
    generatedMesh: null,
  }
}

function jobWith(parts: ViewDirection[][], status: JobStatus = 'succeeded'): Job {
  return {
    id: 'j1',
    category: 'background',
    sourceImageId: 's1',
    status,
    scene: null,
    models: {
      text: null,
      image: { providerConfigId: 'p-image', model: 'gemini-image' },
      mesh: null,
    },
    tasks: [],
    parts: parts.map((directions, index) => partWith(directions, index)),
    failureReason: null,
    createdAt: '2026-08-12T00:00:00Z',
    completedAt: '2026-08-12T00:10:00Z',
    gender: null,
    partHints: [],
  }
}
