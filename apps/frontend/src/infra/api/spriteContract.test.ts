import { afterEach, expect, it, vi } from 'vitest'
import responses from './fixtures/sprite-roles.server.json'
import requests from './fixtures/sprite-roles.request.json'
import { getJob } from './jobApi'
import { updateSpritePlan } from './spriteApi'
import type { UpdateSpritePlanInput } from '@/domain/sprites/types'

afterEach(() => vi.unstubAllGlobals())
function serve(value: unknown) {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(value)))
  vi.stubGlobal('fetch', fetch)
  return fetch
}
it('reads server generated roles, asset canvases, grids and unchanged approval snapshots', async () => {
  for (const envelope of Object.values(responses)) {
    serve(envelope)
    const job = await getJob(envelope.data.id)
    expect(job.sprite).toEqual(envelope.data.sprite)
  }
})
it.each([
  { role: 'future' },
  { role: null },
  { canvas: null },
  { canvas: { width: 0, height: 1 } },
  { scale: 0 },
  { scale: null },
])('rejects malformed explicit asset fields %j', async (change) => {
  const envelope = structuredClone(responses.mixedJob)
  Object.assign(
    envelope.data.sprite!.assets[0]!,
    change.role !== undefined
      ? { plan: { ...envelope.data.sprite!.assets[0]!.plan, ...change } }
      : change,
  )
  serve(envelope)
  await expect(getJob(envelope.data.id)).rejects.toThrow('응답 계약')
})
it.each([responses.legacyJob, responses.mixedJob])(
  'defaults only absent legacy fields by output kind',
  async (raw) => {
    const envelope = structuredClone(raw)
    const sprite = envelope.data.sprite! as unknown as Record<string, unknown>
    delete sprite.grid
    for (const asset of sprite.assets as Record<string, unknown>[]) {
      delete (asset.plan as Record<string, unknown>).role
      delete asset.canvas
      delete asset.scale
    }
    serve(envelope)
    const job = await getJob(envelope.data.id)
    expect(job.sprite!.grid).toBeNull()
    for (const asset of job.sprite!.assets) {
      expect(asset.plan.role).toBe(job.sprite!.settings.outputKind === 'layers' ? 'layer' : 'tile')
      expect(asset.canvas).toEqual(job.sprite!.outputCanvas)
      expect(asset.scale).toBe(1)
    }
  },
)
it.each(Object.values(requests))('writes the exact server request fixture', async (body) => {
  const fetch = serve({
    data: { id: 'job', status: 'pendingReview', revision: 1, taskIds: [] },
    error: null,
  })
  await updateSpritePlan({ jobId: 'job', ...body } as UpdateSpritePlanInput)
  expect(JSON.parse(fetch.mock.calls[0]![1].body)).toEqual(body)
})

it.each([{ cellWidth: 0 }, { cellWidth: 1.1 }, { cellWidth: '0.2' }, []])(
  'rejects malformed explicit grid %j',
  async (grid) => {
    const envelope = structuredClone(responses.mixedJob)
    Object.assign(envelope.data.sprite!, { grid })
    serve(envelope)
    await expect(getJob(envelope.data.id)).rejects.toThrow('응답 계약')
  },
)
