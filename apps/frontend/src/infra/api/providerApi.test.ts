import { afterEach, describe, expect, it, vi } from 'vitest'
import { listProviders } from './providerApi'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('listProviders', () => {
  it('서버가 준 종류·능력을 그대로 쓴다 — 클라이언트가 다시 만들지 않는다', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        status: 200,
        ok: true,
        json: async () => ({
          data: [
            {
              id: 'openai-1',
              displayName: 'OpenAI 운영',
              kind: 'openai',
              capabilities: ['textAnalysis', 'imageGeneration'],
              apiKeyMasked: '••••••••abcd',
              isEnabled: true,
            },
            {
              id: 'google-1',
              displayName: 'Gemini 운영',
              kind: 'google',
              capabilities: ['imageGeneration'],
              apiKeyMasked: '••••••••efgh',
              isEnabled: true,
            },
          ],
          error: null,
        }),
      } as Response),
    )

    // 클라이언트가 능력을 재구성하면 백엔드가 바뀔 때 조용히 어긋난다.
    // 서버가 값을 빠뜨리면 화면이 비는 것이 맞다 — 계약이 깨진 사실이 보여야 한다
    await expect(listProviders()).resolves.toMatchObject([
      { kind: 'openai', capabilities: ['textAnalysis', 'imageGeneration'] },
      { kind: 'google', capabilities: ['imageGeneration'] },
    ])
  })
})
