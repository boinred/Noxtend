import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiUrl } from './client'
import {
  startSpriteJob,
  updateSpritePlan,
  approveSpritePlan,
  approveSpriteBases,
  regenerateSpriteFrame,
  approveSpriteAsset,
  exportSprites,
  spriteImageUrl,
  spriteExportUrl,
  createSpriteDraft,
  enhanceSpriteSource,
  saveSpriteSourcePrompt,
  generateSpriteSourceCandidate,
  selectSpriteSource,
  spriteSourceCandidateUrl,
} from './spriteApi'

const context = { jobId: 'job', requestId: 'request', expectedRevision: 3 }
const receipt = { id: 'job', status: 'running', revision: 4, taskIds: ['task'] }
const input = {
  requestId: 'request',
  uploadId: 'upload',
  providerConfigId: 'text',
  model: 'text-model',
  imageProviderConfigId: 'image',
  imageModel: 'image-model',
  settings: {
    view: 'sideView' as const,
    outputKind: 'layers' as const,
    tileWidth: 128 as const,
    repeat: 'both' as const,
  },
}
const plan = {
  role: 'layer' as const,
  id: 'asset',
  name: '앞 레이어',
  order: 1,
  sourceBounds: { x: 0, y: 0, w: 1, h: 1 },
  placements: null,
  requiresTransparency: true,
  loop: false,
  frameCount: 8,
  fps: 8,
  motionNotes: '',
}

function success(data: unknown = receipt) {
  const mock = vi
    .fn()
    .mockImplementation(
      async () => new Response(JSON.stringify({ data, error: null }), { status: 202 }),
    )
  vi.stubGlobal('fetch', mock)
  return mock
}

afterEach(() => vi.unstubAllGlobals())

describe('sprite API', () => {
  it('sends nested upload settings, stripping UI fields and preserving signal and requestId on resend', async () => {
    const fetch = success()
    const signal = new AbortController().signal
    const draft = { ...input, preview: true, settings: { ...input.settings, draft: true } }
    expect(await startSpriteJob(draft, signal)).toEqual(receipt)
    await startSpriteJob(draft, signal)
    expect(fetch.mock.calls[0]).toEqual([
      apiUrl('/api/jobs/sprites'),
      expect.objectContaining({ method: 'POST', signal, body: JSON.stringify(input) }),
    ])
    expect(fetch.mock.calls[1]![1].body).toEqual(fetch.mock.calls[0]![1].body)
  })

  it('sends the source pair without uploadId or external URL fields', async () => {
    const fetch = success()
    const common = {
      requestId: input.requestId,
      providerConfigId: input.providerConfigId,
      model: input.model,
      imageProviderConfigId: input.imageProviderConfigId,
      imageModel: input.imageModel,
      settings: input.settings,
    }
    const source = {
      ...common,
      sourceJobId: 'source',
      sourceGeneratedImageId: 'image',
      sourceUrl: 'https://ignored.invalid',
    }
    await startSpriteJob(source)
    expect(JSON.parse(fetch.mock.calls[0]![1].body)).toEqual({
      ...common,
      sourceJobId: 'source',
      sourceGeneratedImageId: 'image',
    })
  })

  it('writes assets and bounds using only allowed fields', async () => {
    const fetch = success()
    const draft = {
      ...context,
      grid: null,
      assets: [{ ...plan, selected: true, sourceBounds: { ...plan.sourceBounds, draft: true } }],
      draft: true,
    }
    await updateSpritePlan(draft)
    expect(fetch.mock.calls[0]).toEqual([
      apiUrl('/api/jobs/job/sprites/plan'),
      expect.objectContaining({ method: 'PUT' }),
    ])
    expect(JSON.parse(fetch.mock.calls[0]![1].body)).toEqual({
      requestId: 'request',
      expectedRevision: 3,
      grid: null,
      assets: [plan],
    })
  })

  it('uses route IDs only in paths and exact mutation DTOs', async () => {
    const fetch = success()
    const signal = new AbortController().signal
    await approveSpritePlan(context, signal)
    await approveSpriteBases({ ...context, assetIds: ['asset'] })
    await regenerateSpriteFrame({ ...context, assetId: 'asset', index: 2 })
    await approveSpriteAsset({ ...context, assetId: 'asset' })
    await exportSprites({ ...context, assetIds: ['asset'] })
    const paths = [
      '/plan/approve',
      '/base/approve',
      '/assets/asset/frames/2/regenerate',
      '/assets/asset/approve',
      '/exports',
    ]
    paths.forEach((path, index) => {
      const [url, init] = fetch.mock.calls[index]!
      expect(url).toBe(apiUrl(`/api/jobs/job/sprites${path}`))
      expect(init.method).toBe('POST')
      expect(JSON.parse(init.body)).toEqual({
        requestId: 'request',
        expectedRevision: 3,
        ...(index === 1 || index === 4 ? { assetIds: ['asset'] } : {}),
      })
    })
    expect(fetch.mock.calls[0]![1].signal).toBe(signal)
    expect(spriteImageUrl('job', 'image')).toBe(apiUrl('/api/jobs/job/sprites/images/image'))
    expect(spriteExportUrl('job', 'export')).toBe(apiUrl('/api/jobs/job/sprites/exports/export'))
  })

  it('posts the draft body with nested settings only, no source fields, and keeps the request ID on resend', async () => {
    const fetch = success()
    const signal = new AbortController().signal
    const draft = {
      ...input,
      description: '노을 진 숲',
      preview: true,
      settings: { ...input.settings, draft: true },
    }
    expect(await createSpriteDraft(draft, signal)).toEqual(receipt)
    await createSpriteDraft(draft, signal)
    const expected = {
      requestId: 'request',
      description: '노을 진 숲',
      providerConfigId: 'text',
      model: 'text-model',
      imageProviderConfigId: 'image',
      imageModel: 'image-model',
      settings: input.settings,
    }
    expect(fetch.mock.calls[0]).toEqual([
      apiUrl('/api/jobs/sprites/drafts'),
      expect.objectContaining({ method: 'POST', signal, body: JSON.stringify(expected) }),
    ])
    expect(fetch.mock.calls[1]![1].body).toEqual(fetch.mock.calls[0]![1].body)
  })

  it('uses source routes, methods, exact bodies and the signal', async () => {
    const fetch = success()
    const signal = new AbortController().signal
    await enhanceSpriteSource({ ...context, description: '숲', extra: 1 } as never, signal)
    await saveSpriteSourcePrompt({ ...context, prompt: '프롬프트' }, signal)
    await generateSpriteSourceCandidate({ ...context, prompt: '프롬프트' }, signal)
    await selectSpriteSource({ ...context, candidateId: 'candidate' }, signal)
    const base = { requestId: 'request', expectedRevision: 3 }
    const calls = [
      ['POST', '/source/enhance', { ...base, description: '숲' }],
      ['PUT', '/source/prompt', { ...base, prompt: '프롬프트' }],
      ['POST', '/source/generate', { ...base, prompt: '프롬프트' }],
      ['POST', '/source/select', { ...base, candidateId: 'candidate' }],
    ] as const
    calls.forEach(([method, path, body], index) => {
      const [url, init] = fetch.mock.calls[index]!
      expect(url).toBe(apiUrl(`/api/jobs/job/sprites${path}`))
      expect(init.method).toBe(method)
      expect(init.signal).toBe(signal)
      expect(JSON.parse(init.body)).toEqual(body)
    })
    expect(spriteSourceCandidateUrl('job', 'candidate')).toBe(
      apiUrl('/api/jobs/job/sprites/source/candidates/candidate'),
    )
  })

  it.each(['SPRITE_BUSY', 'SPRITE_NOT_READY', 'SPRITE_SOURCE_LIMIT'])(
    'surfaces %s as a 409 ApiError without resubmitting',
    async (code) => {
      const fetch = vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ data: null, error: { code, message: '충돌' } }), {
          status: 409,
        }),
      )
      vi.stubGlobal('fetch', fetch)
      await expect(
        generateSpriteSourceCandidate({ ...context, prompt: 'p' }),
      ).rejects.toMatchObject({ code, status: 409 })
      expect(fetch).toHaveBeenCalledTimes(1)
    },
  )

  it('rejects a malformed source receipt', async () => {
    success({ ...receipt, taskIds: null })
    await expect(selectSpriteSource({ ...context, candidateId: 'c' })).rejects.toThrow()
  })

  it('surfaces HTTP conflicts without resubmitting or masking', async () => {
    const fetch = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          data: null,
          error: { code: 'SPRITE_REVISION_CONFLICT', message: '충돌' },
        }),
        { status: 409 },
      ),
    )
    vi.stubGlobal('fetch', fetch)
    await expect(approveSpritePlan(context)).rejects.toMatchObject({
      code: 'SPRITE_REVISION_CONFLICT',
      status: 409,
    })
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('rejects a malformed accepted receipt', async () => {
    success({ ...receipt, status: 'future', taskIds: null })
    await expect(startSpriteJob(input)).rejects.toThrow()
  })

  it('preserves AbortError', async () => {
    const abort = new DOMException('canceled', 'AbortError')
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(abort))
    await expect(startSpriteJob(input)).rejects.toBe(abort)
    expect(abort).not.toBeInstanceOf(ApiError)
  })
})
