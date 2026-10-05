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
