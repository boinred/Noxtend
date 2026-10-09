import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiUrl } from './client'
import { generateSpriteSource } from './uploadApi'

const input = {
  requestId: 'request',
  prompt: '노을 진 숲',
  imageProviderConfigId: 'image-provider',
  imageModel: 'image-model',
}
const upload = {
  id: 'upload',
  originalName: 'sprite-prompt.png',
  contentType: 'image/png',
  sizeBytes: 1024,
}

afterEach(() => vi.unstubAllGlobals())

describe('sprite source generation API', () => {
  it('posts only the four contract fields and returns the created upload', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(
        new Response(JSON.stringify({ data: upload, error: null }), { status: 201 }),
      )
    vi.stubGlobal('fetch', fetch)
    const signal = new AbortController().signal
    const draft = { ...input, preview: true, uploadId: 'ignored' }

    expect(await generateSpriteSource(draft, signal)).toEqual(upload)
    expect(fetch).toHaveBeenCalledExactlyOnceWith(
      apiUrl('/api/uploads/generate'),
      expect.objectContaining({ method: 'POST', signal, body: JSON.stringify(input) }),
    )
  })

  it('preserves the provider error status and message as ApiError', async () => {
    const fetch = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          data: null,
          error: { code: 'PROVIDER_CALL_FAILED', message: '이미지 생성에 실패했습니다' },
        }),
        { status: 502 },
      ),
    )
    vi.stubGlobal('fetch', fetch)

    const result = generateSpriteSource(input)
    await expect(result).rejects.toBeInstanceOf(ApiError)
    await expect(result).rejects.toMatchObject({
      code: 'PROVIDER_CALL_FAILED',
      status: 502,
      message: '이미지 생성에 실패했습니다',
    })
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
