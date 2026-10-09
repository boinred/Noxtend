import { afterEach, expect, it, vi } from 'vitest'
import { collectPriceUpdate, applyPriceUpdate } from './priceUpdateApi'

afterEach(() => vi.unstubAllGlobals())

it('손상된 변경안을 적용 가능한 응답으로 읽지 않는다', async () => {
  vi.stubGlobal(
    'fetch',
    vi
      .fn()
      .mockResolvedValue(
        new Response(JSON.stringify({ data: { id: 'bad', candidates: [] }, error: null })),
      ),
  )
  await expect(collectPriceUpdate(['config'])).rejects.toMatchObject({ code: 'MALFORMED_RESPONSE' })
})

it('응답 유실 뒤 같은 입력을 그대로 재전송한다', async () => {
  const input = {
    requestId: '10000000-0000-4000-8000-000000000001',
    candidateIds: ['20000000-0000-4000-8000-000000000001'],
    effectiveFrom: null,
  }
  const fetcher = vi.fn().mockRejectedValue(new TypeError('connection lost'))
  vi.stubGlobal('fetch', fetcher)
  await expect(applyPriceUpdate('30000000-0000-4000-8000-000000000001', input)).rejects.toThrow()
  await expect(applyPriceUpdate('30000000-0000-4000-8000-000000000001', input)).rejects.toThrow()
  expect(fetcher.mock.calls[0]?.[1].body).toBe(fetcher.mock.calls[1]?.[1].body)
  expect(JSON.parse(fetcher.mock.calls[0]?.[1].body)).toEqual(input)
})

it.each([
  { requestId: 'mismatched', items: [] },
  {
    requestId: '10000000-0000-4000-8000-000000000001',
    items: [
      {
        candidateId: 'other',
        priceId: '40000000-0000-4000-8000-000000000001',
        effectiveFrom: '2026-10-09T00:00:00Z',
      },
    ],
  },
])('손상된 receipt를 저장 완료로 표시하지 않는다: %j', async (fields) => {
  const previewId = '30000000-0000-4000-8000-000000000001'
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          data: { previewId, appliedAt: '2026-10-09T00:00:00Z', ...fields },
          error: null,
        }),
      ),
    ),
  )
  await expect(
    applyPriceUpdate(previewId, {
      requestId: '10000000-0000-4000-8000-000000000001',
      candidateIds: ['20000000-0000-4000-8000-000000000001'],
      effectiveFrom: null,
    }),
  ).rejects.toMatchObject({ code: 'MALFORMED_RESPONSE' })
})
