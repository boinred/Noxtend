import { describe, expect, it } from 'vitest'
import { spriteAnalysisSummary } from './SpriteAnalysisPanel'
import type { SpriteAssetPlan, SpriteState } from '@/domain/sprites/types'

function plan(id: string, loop: boolean, frameCount = 8): SpriteAssetPlan {
  return {
    id,
    name: id,
    order: id === 'back' ? 0 : 1,
    sourceBounds: { x: 0, y: 0, w: 1, h: 1 },
    requiresTransparency: id !== 'back',
    loop,
    frameCount,
    fps: 8,
    motionNotes: '',
  }
}

function sprite(overrides: Partial<SpriteState> = {}): SpriteState {
  return {
    settings: { view: 'sideView', outputKind: 'layers', tileWidth: 128, repeat: 'both' },
    sourceCanvas: { width: 1920, height: 1080 },
    generationCanvas: { width: 1536, height: 1024 },
    outputCanvas: { width: 1920, height: 1080 },
    transform: { scale: 1, offsetX: 0, offsetY: 0 },
    phase: 'planReview',
    reviewRevision: 1,
    completedExportId: null,
    assets: [plan('back', false), plan('water', true, 4)].map((p) => ({
      id: p.id,
      plan: p,
      planRevision: 1,
      anchor: { x: 0, y: 0 },
      approvedBaseImageId: null,
      approval: null,
      frames: [{ index: 0, currentTaskId: null, currentImageId: null }],
    })),
    images: [],
    exports: [],
    ...overrides,
  }
}

describe('spriteAnalysisSummary', () => {
  it('summarizes layers from the saved plan', () => {
    const props = spriteAnalysisSummary(sprite())
    expect(props.label).toBe('분석')
    expect(props.summary).toBe('횡스크롤 · 레이어 2개')
    expect(props.palette).toBeUndefined()
    expect(props.fields.map((f) => [f.label, f.value, f.testId])).toEqual([
      ['시점', '횡스크롤', 'sprite-analysis-view'],
      ['출력', '배경 레이어', 'sprite-analysis-output'],
      ['캔버스', '원본 1920×1080 → 요청 1536×1024 → 최종 1920×1080', 'sprite-analysis-canvas'],
      ['대상', '2개 · 총 5프레임', 'sprite-analysis-assets'],
    ])
    expect(props.testIds).toEqual({
      root: 'sprite-analysis-panel',
      summary: 'sprite-analysis-summary',
    })
  })

  it('names tiles and keeps an empty plan visible', () => {
    const props = spriteAnalysisSummary(
      sprite({
        settings: { view: 'isometric', outputKind: 'tiles', tileWidth: 128, repeat: 'y' },
        assets: [],
      }),
    )
    expect(props.summary).toBe('아이소메트릭 · 타일 0개')
    expect(props.fields[1]!.value).toBe('반복 타일 · 128px · 격자 Y축')
    expect(props.fields[3]!.value).toBe('0개 · 총 0프레임')
  })
})
