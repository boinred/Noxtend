/**
 * Design Ref: §8.3 L1-F #5·#6 — 봉투 해석과 오류 정규화.
 *
 * #6 이 §6 의 "API 자체가 없음 → 빈 상태로 표시" 를 떠받친다. 네트워크 실패가
 * 서버 오류와 같은 타입으로 나와야 훅이 한 가지 분기만 갖는다.
 */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, API_ERROR_CODES, apiRequest } from './client'

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

describe('apiRequest', () => {
  // #5 — 봉투 해석
  it('성공 봉투에서 data 만 꺼낸다', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(mockResponse(200, { data: { id: 'job-1' }, error: null })),
    )

    await expect(apiRequest('/api/jobs/job-1')).resolves.toEqual({ id: 'job-1' })
  })

  it('오류 봉투를 ApiError 로 바꾼다', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        mockResponse(400, {
          data: null,
          error: { code: 'UPLOAD_TOO_LARGE', message: '최대 12 MB', fields: null },
        }),
      ),
    )

    const error = await apiRequest('/api/uploads').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).code).toBe('UPLOAD_TOO_LARGE')
    expect((error as ApiError).status).toBe(400)
    expect((error as ApiError).message).toBe('최대 12 MB')
  })

  it('200 이지만 error 가 채워진 응답도 실패로 다룬다', async () => {
    // 봉투 규약을 어긴 응답이다. data 를 그대로 흘리면 화면이 null 을 렌더한다
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(
          mockResponse(200, { data: null, error: { code: 'JOB_NOT_FOUND', message: '없음' } }),
        ),
    )

    await expect(apiRequest('/api/jobs/x')).rejects.toBeInstanceOf(ApiError)
  })

  it('204 는 본문 없이 성공한다', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ status: 204, ok: true, json: async () => undefined } as Response),
    )

    await expect(apiRequest('/api/providers/x')).resolves.toBeUndefined()
  })

  // #6 — 네트워크 실패 정규화
  it('연결 실패를 ApiError 로 정규화한다', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    const error = (await apiRequest('/api/jobs').catch((e: unknown) => e)) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.code).toBe(API_ERROR_CODES.networkUnreachable)
    expect(error.status).toBe(0)
    // 이 플래그로 훅이 "빈 상태" 와 "진짜 오류" 를 가른다 (§6)
    expect(error.isUnreachable).toBe(true)
  })

  it('JSON 이 아닌 응답을 정규화한다', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        status: 502,
        ok: false,
        json: async () => {
          throw new SyntaxError('Unexpected token <')
        },
      } as unknown as Response),
    )

    const error = (await apiRequest('/api/jobs').catch((e: unknown) => e)) as ApiError

    // 게이트웨이 오류 페이지가 여기로 온다
    expect(error.code).toBe(API_ERROR_CODES.malformedResponse)
    expect(error.isUnreachable).toBe(false)
  })

  it('취소는 ApiError 로 감싸지 않는다', async () => {
    const abort = new DOMException('aborted', 'AbortError')
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(abort))

    // 취소를 오류로 바꾸면 훅이 이전 데이터를 오류 상태로 덮는다
    await expect(apiRequest('/api/jobs')).rejects.toBe(abort)
  })

  it('FormData 에는 Content-Type 을 지정하지 않는다', async () => {
    const fetchMock = vi.fn().mockResolvedValue(mockResponse(201, { data: {}, error: null }))
    vi.stubGlobal('fetch', fetchMock)

    await apiRequest('/api/uploads', { method: 'POST', body: new FormData() })

    // 직접 지정하면 boundary 가 빠져 서버가 본문을 파싱하지 못한다
    const init = fetchMock.mock.calls[0]?.[1] as RequestInit
    expect(init.headers).toBeUndefined()
  })
})
