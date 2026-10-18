import { describe, expect, it } from 'vitest'
import { productionModeOf, spriteProgressCounts, tileOffsets } from './rules'
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
    source: null as unknown,
  }
}

const draftSource = {
  description: '노을 진 숲',
  prompt: null,
  candidates: [
    {
      id: 'candidate',
      prompt: '노을 진 숲, 측면 시점',
      width: 1024,
      height: 1024,
      createdAt: '2026-10-13T00:00:00Z',
    },
  ],
}
function draftFixture() {
  return {
    ...spriteFixture(),
    phase: 'sourceDraft',
    assets: [],
    images: [],
    exports: [],
    source: draftSource,
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
    'source',
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

  describe('plan placements', () => {
    const readPlacements = (placements?: unknown) => {
      const sprite = spriteFixture()
      const plan = sprite.assets[0]!.plan as Record<string, unknown>
      if (placements !== undefined) plan.placements = placements
      return readSpriteJob({ productionMode: 'twoD', sprite }).sprite!.assets[0]!.plan.placements
    }
    it('normalizes a missing field to null', () => {
      expect(readPlacements()).toBeNull()
    })
    it('keeps an explicit null', () => {
      expect(readPlacements(null)).toBeNull()
    })
    it('keeps a valid list as is', () => {
      const placements = [
        { x: 0.1, y: 0.2, w: 0.3, h: 0.4 },
        { x: 0.5, y: 0.5, w: 0.25, h: 0.25 },
      ]
      expect(readPlacements(placements)).toEqual(placements)
    })
    it.each([
      [[{ x: 0, y: 0, w: 1 }]],
      [[null]],
      ['x'],
      [[{ x: 0, y: 0, w: 1, h: Infinity }]],
      [[{ x: 0, y: 0, w: 1, h: Number.NaN }]],
      [[{ x: 0, y: 0, w: 1, h: '1' }]],
    ])('rejects malformed placements %j', (placements) => {
      expect(() => readPlacements(placements)).toThrow()
    })
  })

  it('reads a sourceDraft job with a null sourceImageId and its source draft', () => {
    const sprite = draftFixture()
    const job = readSpriteJob({ productionMode: 'twoD', sourceImageId: null, sprite })
    expect(job.sourceImageId).toBeNull()
    expect(job.sprite?.phase).toBe('sourceDraft')
    expect(job.sprite?.source).toEqual(draftSource)
    const prompted = { ...sprite, source: { ...draftSource, prompt: '저장된 프롬프트' } }
    expect(
      readSpriteJob({ productionMode: 'twoD', sourceImageId: null, sprite: prompted }).sprite
        ?.source?.prompt,
    ).toBe('저장된 프롬프트')
    expect(
      readSpriteJob({
        productionMode: 'twoD',
        sourceImageId: null,
        sprite: { ...sprite, source: { ...draftSource, candidates: [] } },
      }).sprite?.source?.candidates,
    ).toEqual([])
  })

  it('reads a sourceDraft summary with a null sourceImageId', () => {
    const raw = {
      productionMode: 'twoD',
      sourceImageId: null,
      sprite: { ...summary, phase: 'sourceDraft', assetCount: 0, imageCount: 0 },
    }
    expect(readSpriteSummary(raw)).toMatchObject({ sourceImageId: null })
  })

  it.each([
    'idle',
    'sourceDraft',
    'analyzing',
    'planReview',
    'baseGeneration',
    'baseReview',
    'frameGeneration',
    'frameReview',
    'exportReady',
    'packaging',
    'completed',
  ])('accepts every SpritePhase on wire: %s', (phase) => {
    const read = () =>
      readSpriteJob({ productionMode: 'twoD', sprite: { ...spriteFixture(), phase } })
    if (phase === 'idle') expect(read).toThrow()
    else expect(read).not.toThrow()
  })

  it.each([
    { description: 1 },
    { prompt: 1 },
    { prompt: undefined },
    { candidates: null },
    { candidates: [{}] },
    { candidates: [{ ...draftSource.candidates[0], id: '' }] },
    { candidates: [{ ...draftSource.candidates[0], prompt: null }] },
    { candidates: [{ ...draftSource.candidates[0], width: 0 }] },
    { candidates: [{ ...draftSource.candidates[0], height: 1.5 }] },
    { candidates: [{ ...draftSource.candidates[0], createdAt: undefined }] },
  ])('rejects a malformed source draft: %j', (change) => {
    const sprite = { ...draftFixture(), source: { ...draftSource, ...change } }
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

describe('static tile offsets', () => {
  it.each(['x', 'y', 'both'] as const)('square %s uses pixel axes', (repeat) => {
    const offsets = tileOffsets(128, 128, repeat, 'square')
    expect(offsets).toHaveLength(repeat === 'both' ? 9 : 3)
    expect(offsets).toContainEqual({ x: 0, y: 0 })
    if (repeat !== 'y') expect(offsets).toContainEqual({ x: 128, y: 0 })
    if (repeat !== 'x') expect(offsets).toContainEqual({ x: 0, y: 128 })
  })
  it.each(['x', 'y', 'both'] as const)('diamond %s uses cell-grid axes', (repeat) => {
    const offsets = tileOffsets(128, 64, repeat, 'diamond')
    expect(offsets).toHaveLength(repeat === 'both' ? 9 : 3)
    expect(offsets).toContainEqual({ x: 0, y: 0 })
    if (repeat !== 'y') expect(offsets).toContainEqual({ x: 64, y: 32 })
    if (repeat !== 'x') expect(offsets).toContainEqual({ x: -64, y: 32 })
    if (repeat === 'both') expect(offsets).toContainEqual({ x: 0, y: 64 })
  })
})

it('sprite progress counts current slots, approvals and stored current ZIPs', () => {
  const sprite = readSpriteJob({ productionMode: 'twoD', sprite: spriteFixture() }).sprite!
  expect(spriteProgressCounts(sprite, [])).toEqual({
    analysis: 0,
    backgrounds: 1,
    approvedBases: 1,
    frames: 0,
    frameTotal: 3,
    loops: 1,
    approvedLoops: 1,
    exported: 0,
  })
  sprite.assets[0]!.frames[0]!.currentImageId = null
  sprite.assets[0]!.approval = null
  expect(spriteProgressCounts(sprite, [])).toMatchObject({
    backgrounds: 0,
    approvedBases: 0,
    approvedLoops: 0,
    exported: 0,
  })
  sprite.assets[0]!.frames.push({ index: 1, currentTaskId: 'new', currentImageId: 'frame' })
  expect(spriteProgressCounts(sprite, [])).toMatchObject({ frames: 1, frameTotal: 3 })
  sprite.assets[0]!.plan.loop = false
  expect(spriteProgressCounts(sprite, [])).toMatchObject({ loops: 0, frames: 0, frameTotal: 0 })
  sprite.exports[0]!.isCurrent = true
  expect(spriteProgressCounts(sprite, [])).toMatchObject({ exported: 1 })
})

it('matches server asset sizing and tile median', async () => {
  const { assetOutputSize, cellWidthOf } = await import('./rules')
  const sprite = readSpriteJob({ productionMode: 'twoD', sprite: spriteFixture() }).sprite!
  const settings = sprite.settings
  const object = {
    ...sprite.assets[0]!.plan,
    role: 'object' as const,
    sourceBounds: { x: 0, y: 0, w: 0.2, h: 0.25 },
  }
  expect(assetOutputSize(settings, { width: 1536, height: 1024 }, 128 / 1536, object)).toEqual({
    canvas: { width: 307, height: 256 },
    scale: 1,
  })
  const backdrop = { ...object, role: 'backdrop' as const }
  expect(assetOutputSize(settings, { width: 1200, height: 800 }, 0.1, backdrop)).toEqual({
    canvas: { width: 1024, height: 683 },
    scale: 0.8,
  })
  expect(
    assetOutputSize(settings, { width: 1025, height: 1000 }, 128 / 1025, {
      ...object,
      sourceBounds: { x: 0, y: 0, w: 1, h: 1 },
    }),
  ).toEqual({ error: '오브젝트 출력 크기는 1~1024픽셀이어야 합니다' })
  expect(assetOutputSize(settings, sprite.sourceCanvas, null, object)).toEqual({
    error: '격자 칸 너비가 필요합니다',
  })
  expect(
    cellWidthOf(
      null,
      [
        { ...object, role: 'tile', sourceBounds: { x: 0, y: 0, w: 0.2, h: 1 } },
        { ...object, role: 'tile', sourceBounds: { x: 0, y: 0, w: 0.4, h: 1 } },
      ],
      settings,
    ),
  ).toBeCloseTo(0.3)
})

it.each([
  [0.5, 1],
  [1024.49, 1024],
  [1024.5, null],
])('rounds positive midpoint %s away from zero before bounds checking', async (width, expected) => {
  const { assetOutputSize } = await import('./rules')
  const sprite = readSpriteJob({ productionMode: 'twoD', sprite: spriteFixture() }).sprite!
  const result = assetOutputSize(sprite.settings, { width: 1536, height: 1024 }, 128 / 1536, {
    ...sprite.assets[0]!.plan,
    role: 'object',
    sourceBounds: { x: 0, y: 0, w: width! / 1536, h: 0.25 },
  })
  if (expected === null) expect(result).toHaveProperty('error')
  else expect(result).toEqual({ canvas: { width: expected, height: 256 }, scale: 1 })
})
