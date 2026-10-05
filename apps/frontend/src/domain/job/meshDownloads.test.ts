/**
 * Design Ref: §3 · §8.1 · D-09 — 내려받기 메뉴에 무엇이 서는가.
 *
 * **판정이 컴포넌트에 있으면 이 프로젝트는 검사할 수 없다.** vitest 는 순수 로직만
 * 보고 UI 는 Playwright 가 본다. 그래서 "GLB 는 늘, FBX 는 있을 때만" 을 도메인으로
 * 내렸고, 그 결정이 여기서 값을 한다.
 */
import { describe, expect, it } from 'vitest'
import { formatMeshSize, meshDownloadOptions } from './types'
import type { GeneratedMesh } from './types'

function mesh(overrides: Partial<GeneratedMesh> = {}): GeneratedMesh {
  return {
    id: 'm1',
    hasPreview: true,
    hasFbx: false,
    sizeBytes: 10_170_000,
    fbxSizeBytes: null,
    creditsConsumed: 30,
    createdAt: '2026-08-13T00:00:00Z',
    ...overrides,
  }
}

describe('meshDownloadOptions', () => {
  it('D-01 FBX 가 있으면 둘이고 GLB 가 먼저다', () => {
    // 순서가 고정이어야 하는 이유는 GLB 가 3D 결과 자체이기 때문이다
    expect(meshDownloadOptions(mesh({ hasFbx: true })).map((o) => o.format)).toEqual(['glb', 'fbx'])
  })

  it('D-02 FBX 가 없으면 GLB 하나뿐이다', () => {
    // Tripo 결과다. 없는 것을 비활성으로 보여 주면 "곧 생긴다" 로 읽힌다
    expect(meshDownloadOptions(mesh({ hasFbx: false })).map((o) => o.format)).toEqual(['glb'])
  })

  it('D-04 두 형식 모두 크기를 적는다 — 고르는 순간의 판단 재료다', () => {
    const [glb, fbx] = meshDownloadOptions(
      mesh({ hasFbx: true, sizeBytes: 10_170_000, fbxSizeBytes: 10_800_000 }),
    )

    expect(glb!.label).toBe('GLB · 9.7 MB')
    expect(fbx!.label).toBe('FBX · 10.3 MB')
  })

  /** 구계약 응답에는 FBX 크기가 없다 — 모르는 것을 지어내면 사용자가 그 숫자를 믿는다. */
  it('D-04a FBX 크기를 모르면 이름만 적는다', () => {
    const [, fbx] = meshDownloadOptions(mesh({ hasFbx: true, fbxSizeBytes: null }))

    expect(fbx!.label).toBe('FBX')
  })

  it('주소를 만들지 않는다 — 계층 규칙상 도메인은 그럴 수단이 없다', () => {
    for (const option of meshDownloadOptions(mesh({ hasFbx: true }))) {
      expect(option).not.toHaveProperty('href')
    }
  })
})

describe('formatMeshSize', () => {
  it('1MB 미만은 KB 로 읽는다', () => {
    expect(formatMeshSize(400 * 1024)).toBe('400 KB')
  })

  it('0 바이트도 최소 1KB 로 읽는다 — "0 KB" 는 실패로 보인다', () => {
    expect(formatMeshSize(0)).toBe('1 KB')
  })
})
