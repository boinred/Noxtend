import { describe, expect, it } from 'vitest'
import { canOpenSceneTab } from './types'
import type { AssetPart, GeneratedMesh, Job } from './types'

/**
 * "3D 배경" 탭을 열 수 있는가. Design Ref: scene-assembly §5.1 · FR-04
 *
 * **완성 GLB 가 하나는 있어야 연다.** 없는데 열면 빈 캔버스가 실패로 읽힌다 —
 * 비활성 탭 + 이유가 정직하다.
 */
describe('canOpenSceneTab', () => {
  it('완성 3D 가 하나라도 있으면 연다', () => {
    expect(canOpenSceneTab(job([part(mesh()), part(null)]))).toBe(true)
  })

  it('완성 3D 가 없으면 닫는다', () => {
    expect(canOpenSceneTab(job([part(null), part(null)]))).toBe(false)
    expect(canOpenSceneTab(job([]))).toBe(false)
  })

  /** live-result 와 같은 문법 — 진행 중에도 GLB 가 도착했으면 볼 수 있다 */
  it('진행 중이어도 GLB 가 도착했으면 연다', () => {
    expect(canOpenSceneTab(job([part(mesh())], 'running'))).toBe(true)
  })
})

// ─── 설정 ───

function job(parts: AssetPart[], status: Job['status'] = 'succeeded'): Job {
  return {
    id: 'j1',
    category: 'background',
    sourceImageId: 's1',
    status,
    scene: null,
    models: { text: { providerConfigId: 'p', model: 'm' }, image: null, mesh: null },
    tasks: [],
    parts,
    failureReason: null,
    gender: null,
    partHints: [],
    createdAt: '2026-08-17T00:00:00Z',
    completedAt: null,
  }
}

function part(generatedMesh: GeneratedMesh | null): AssetPart {
  return {
    id: `p-${Math.random()}`,
    name: '파츠',
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
    id: 'm1',
    hasPreview: true,
    hasFbx: false,
    fbxSizeBytes: null,
    sizeBytes: 1024,
    creditsConsumed: 30,
    createdAt: '2026-08-17T00:00:00Z',
  }
}
