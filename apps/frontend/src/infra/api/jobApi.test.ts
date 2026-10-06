import { afterEach, describe, expect, it, vi } from 'vitest'
import { deleteJob } from './jobApi'
import { apiUrl } from './client'

/**
 * `fetch` 가 돌려주는 응답의 **최소 흉내**.
 *
 * `Response` 는 멤버가 스무 개가 넘는데 `apiRequest` 가 읽는 것은 셋뿐이다 —
 * 상태, 성공 여부, 본문. 나머지를 채우면 무엇이 검사에 쓰이는지 가려진다.
 *
 * 그래서 `as Response` 로 단언한다. **이 단언이 안전한 근거는 호출부가 좁다는 것**이고,
 * `apiRequest` 가 다른 멤버를 읽기 시작하면 여기가 먼저 깨져야 한다.
 */
function mockResponse(status: number, body: unknown): Response {
  return {
    status,
    ok: status >= 200 && status < 300,
    json: async () => body,
  } as Response
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('jobApi - deleteJob', () => {
  it('DELETE /api/jobs/{id} 호출을 수행하고 성공 결과를 반환한다', async () => {
    const fetchMock = vi.fn().mockResolvedValue(mockResponse(200, { data: true, error: null }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await deleteJob('job-123')

    expect(result).toBe(true)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(apiUrl('/api/jobs/job-123'))
    expect(init.method).toBe('DELETE')
  })
})

describe('jobApi sprite response boundaries', () => {
  it('normalizes legacy job fields and propagates AbortSignal', async () => {
    const { getJob } = await import('./jobApi')
    const signal = new AbortController().signal
    const fetch = vi
      .fn()
      .mockResolvedValue(
        mockResponse(200, { data: { id: 'job', models: { mesh: null } }, error: null }),
      )
    vi.stubGlobal('fetch', fetch)
    expect(await getJob('job', signal)).toMatchObject({ productionMode: 'threeD', sprite: null })
    expect(fetch.mock.calls[0]![1].signal).toBe(signal)
  })

  it('filters lists by mode and validates new summary fields', async () => {
    const { listJobs } = await import('./jobApi')
    const summary = {
      phase: 'baseReview',
      assetCount: 1,
      approvedAssetCount: 0,
      imageCount: 1,
      exportCount: 0,
    }
    const fetch = vi.fn().mockResolvedValue(
      mockResponse(200, {
        data: { items: [{ id: 'job', productionMode: 'twoD', sprite: summary }], total: 1 },
        error: null,
      }),
    )
    vi.stubGlobal('fetch', fetch)
    const signal = new AbortController().signal
    expect((await listJobs('active', 20, signal, 'twoD')).items[0]!.sprite).toEqual(summary)
    expect(fetch.mock.calls[0]).toEqual([
      apiUrl('/api/jobs?status=active&limit=20&productionMode=twoD'),
      expect.objectContaining({ signal }),
    ])
    fetch.mockResolvedValue(
      mockResponse(200, {
        data: { items: [{ productionMode: 'future', sprite: null }] },
        error: null,
      }),
    )
    await expect(listJobs('active')).rejects.toThrow()
    fetch.mockResolvedValue(
      mockResponse(200, {
        data: { items: [{ productionMode: 'twoD', sprite: { ...summary, imageCount: '1' } }] },
        error: null,
      }),
    )
    await expect(listJobs('active')).rejects.toThrow()
  })

  it('keeps successful empty lists and missing legacy total, rejects malformed collection', async () => {
    const { listJobs } = await import('./jobApi')
    const fetch = vi.fn().mockResolvedValue(mockResponse(200, { data: { items: [] }, error: null }))
    vi.stubGlobal('fetch', fetch)
    expect(await listJobs('terminal')).toEqual({ items: [], total: undefined })
    fetch.mockResolvedValue(mockResponse(200, { data: { items: null }, error: null }))
    await expect(listJobs('terminal')).rejects.toThrow()
  })
})
