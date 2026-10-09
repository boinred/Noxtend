import { afterEach, expect, it, vi } from 'vitest'
import { createModelPrice, listModelPrices, updateModelPrice } from './tuningApi'
import type { ModelPriceDraft } from '@/domain/tuning/types'

afterEach(() => vi.unstubAllGlobals())

it('구형 단가 응답은 CRUD 모두 레거시 메타데이터로 읽는다', async () => {
  const old = {
    id: 'price',
    model: 'model',
    inputPerMillion: 1,
    outputPerMillion: 2,
    longContextFrom: null,
    longInputPerMillion: null,
    longOutputPerMillion: null,
    perImage: null,
    effectiveFrom: '2026-01-01T00:00:00Z',
    note: '',
  }
  const fetch = vi.fn().mockImplementation(async (_url: string, init: RequestInit) => ({
    status: 200,
    ok: true,
    json: async () => ({ data: init.method === 'GET' ? [old] : old, error: null }),
  }))
  vi.stubGlobal('fetch', fetch)
  const [listed] = await listModelPrices()
  const draft: ModelPriceDraft = { ...old, provider: 'openai' }
  const created = await createModelPrice(draft)
  const updated = await updateModelPrice(old.id, draft)
  for (const row of [listed, created, updated]) {
    expect(row).toMatchObject({
      provider: null,
      allowHistoricalFallback: true,
      sourceEvidenceJson: null,
    })
  }
  expect(JSON.parse(fetch.mock.calls[2]![1].body)).toMatchObject({ provider: 'openai' })
})
