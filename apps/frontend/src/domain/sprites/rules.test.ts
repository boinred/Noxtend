import { describe, expect, it } from 'vitest'
import { productionModeOf } from './rules'
import { readSpriteJob, readSpriteSummary } from './types'
import { queryKeys } from '@/app/queries/keys'

function spriteFixture() {
  return {
    settings: { view: 'isometric', outputKind: 'tiles', tileWidth: 128, repeat: 'both' },
    sourceCanvas: { width: 800, height: 600 },
    generationCanvas: { width: 1024, height: 1024 },
    outputCanvas: { width: 128, height: 64 },
    transform: { scale: 0.0625, offsetX: 32, offsetY: 0 },
    phase: 'frameReview',
    reviewRevision: 5,
    completedExportId: null,
    assets: [
      {
        id: 'asset',
        plan: {
          id: 'asset',
          name: '물',
          order: 1,
          sourceBounds: { x: 0, y: 0, w: 1, h: 1 },
          requiresTransparency: true,
          loop: true,
          frameCount: 4,
          fps: 8,
          motionNotes: '잔물결',
        },
        planRevision: 2,
        anchor: { x: 64, y: 32 },
        approvedBaseImageId: 'base',
        approval: {
          planRevision: 2,
          snapshot: {
            id: 'asset',
            name: '물',
            order: 1,
            fps: 8,
            loop: true,
            anchor: { x: 64, y: 32 },
            repeat: 'both',
            layout: 'diamond',
            baseImageId: 'base',
            imageIds: ['base'],
          },
        },
        frames: [{ index: 0, currentTaskId: 'task', currentImageId: 'base' }],
      },
    ],
    images: [
      {
        id: 'base',
        taskId: 'task',
        assetId: 'asset',
        frameIndex: 0,
        planRevision: 2,
        baseImageId: null,
        width: 128,
        height: 64,
        contentType: 'image/png',
        createdAt: '2026-10-06T00:00:00Z',
      },
    ],
    exports: [
      {
        id: 'export',
        taskId: 'pack',
        reviewRevision: 4,
        isCurrent: false,
        createdAt: '2026-10-06T00:00:00Z',
        includedAssetIds: ['asset'],
        excludedAssetIds: [],
      },
    ],
  }
}

const summary = {
  phase: 'baseReview',
  assetCount: 1,
  approvedAssetCount: 0,
  imageCount: 2,
  exportCount: 0,
}

describe('sprite wire boundary', () => {
  it('legacy productionMode defaults to threeD without inferring mesh', () => {
    expect(productionModeOf({ productionMode: undefined })).toBe('threeD')
    expect(readSpriteJob({ models: { mesh: null } })).toMatchObject({
      productionMode: 'threeD',
      sprite: null,
    })
    expect(productionModeOf({ productionMode: 'twoD' })).toBe('twoD')
  })

  it('job list caches separate modes and limits', () => {
    expect(queryKeys.jobList('active', 10, 'twoD')).not.toEqual(
      queryKeys.jobList('active', 20, 'threeD'),
    )
    expect(queryKeys.jobList('active', 10)).not.toEqual(queryKeys.jobList('active', 20))
    expect(queryKeys.jobList('active', 10)).not.toEqual(queryKeys.jobList('active', 10, 'threeD'))
  })

  it('reads the actual nested detail and summary DTOs without changing history', () => {
    const sprite = spriteFixture()
    expect(readSpriteJob({ productionMode: 'twoD', sprite }).sprite).toEqual(sprite)
    expect(readSpriteSummary({ productionMode: 'twoD', sprite: summary }).sprite).toEqual(summary)
    expect(readSpriteSummary({})).toMatchObject({ productionMode: 'threeD', sprite: null })
  })

  it.each([
    { productionMode: 'future', sprite: null },
    { productionMode: null, sprite: null },
    { productionMode: 'twoD', sprite: null },
    { productionMode: 'threeD' },
    { productionMode: 'threeD', sprite: spriteFixture() },
    { sprite: spriteFixture() },
  ])('rejects unknown mode and missing or mismatched sprite fields: %j', (raw) => {
    expect(() => readSpriteJob(raw)).toThrow()
  })

  it.each([
    'settings',
    'sourceCanvas',
    'generationCanvas',
    'outputCanvas',
    'transform',
    'phase',
    'reviewRevision',
    'completedExportId',
    'assets',
    'images',
    'exports',
  ])('requires TwoD %s', (key) => {
    const sprite: Record<string, unknown> = spriteFixture()
    delete sprite[key]
    expect(() => readSpriteJob({ productionMode: 'twoD', sprite })).toThrow()
  })

  it('rejects unknown phase, settings enums, nonfinite coordinates and malformed arrays', () => {
    for (const change of [
      { phase: 'future' },
      { settings: { ...spriteFixture().settings, view: 'future' } },
      { settings: { ...spriteFixture().settings, outputKind: 'future' } },
      { settings: { ...spriteFixture().settings, repeat: 'future' } },
      { assets: null },
      { images: [{}] },
      { exports: [{}] },
      { transform: { scale: Infinity, offsetX: 0, offsetY: 0 } },
    ])
      expect(() =>
        readSpriteJob({ productionMode: 'twoD', sprite: { ...spriteFixture(), ...change } }),
      ).toThrow()
    const sprite = spriteFixture()
    sprite.assets[0]!.approval!.snapshot.layout = 'future'
    expect(() => readSpriteJob({ productionMode: 'twoD', sprite })).toThrow()
  })

  it.each([
    { phase: 'future' },
    { assetCount: -1 },
    { approvedAssetCount: 1.5 },
    { imageCount: '2' },
    { exportCount: null },
  ])('rejects malformed summary: %j', (change) => {
    expect(() =>
      readSpriteSummary({ productionMode: 'twoD', sprite: { ...summary, ...change } }),
    ).toThrow()
  })
})
