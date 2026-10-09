import { expect, test, type Page } from '@playwright/test'
import { installFakeApi } from './fakeApi'

const kinds = ['openai', 'anthropic', 'google', 'tripo', 'meshy'] as const
const names = ['OpenAI', 'Anthropic', 'Google', 'Tripo', 'Meshy']
const configs = kinds.map((kind, index) => ({
  id: `00000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
  displayName: `${names[index]} acceptance`,
  kind,
  apiKeyMasked: '••••',
  isEnabled: true,
}))
const terms = {
  inputPerMillion: 1.75,
  outputPerMillion: 14,
  longContextFrom: null,
  longInputPerMillion: null,
  longOutputPerMillion: null,
  perImage: null,
  officialEffectiveFrom: null,
}
const candidates = kinds.map((provider, index) => ({
  id: `10000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
  provider,
  model: `acceptance-${provider}`,
  area: index < 3 ? 'text' : 'mesh',
  operation: index < 3 ? 'text' : 'multi-image-to-3d',
  conditions: 'Paid Standard',
  providerConfigIds: [configs[index]!.id],
  currentTerms: { ...terms, inputPerMillion: 0.01 },
  terms,
  evidence: [
    {
      url: 'https://developers.openai.com/api/docs/pricing',
      collectedAt: '2026-10-09T00:00:00Z',
      sha256: 'a'.repeat(64),
      conditions: '<img src=x onerror=alert(1)> synthetic UI evidence',
      creditsPerTask: index > 2 ? 30 : null,
      usdPerCredit: null,
    },
  ],
  changeKind: 'priceChanged',
  executionSupport: 'supported',
  blockedReason: provider === 'meshy' ? '계정별 환산 지원 필요' : null,
}))
const preview = {
  id: '20000000-0000-4000-8000-000000000001',
  createdAt: '2026-10-09T00:00:00Z',
  expiresAt: '2026-10-09T00:30:00Z',
  providers: configs.map((config, index) => ({
    providerConfigId: config.id,
    provider: config.kind,
    status: config.kind === 'tripo' ? 'failed' : 'success',
    modelListStatus: config.kind === 'tripo' ? 'failed' : 'complete',
    collectedAt: '2026-10-09T00:00:00Z',
    modelError: config.kind === 'tripo' ? 'synthetic Tripo model query failure' : null,
    priceError: null,
    candidateIds:
      config.kind === 'tripo' || config.kind === 'google' ? [] : [candidates[index]!.id],
  })),
  candidates: candidates.filter((candidate) => !['tripo', 'google'].includes(candidate.provider)),
}

async function setup(page: Page, mode: 'normal' | 'expired' | 'collection-error' = 'normal') {
  await installFakeApi(page, { providers: configs })
  const calls = { collect: 0, apply: [] as { candidateIds: string[]; requestId: string }[] }
  const prices = [...kinds, null].map((provider, index) => ({
    id: `30000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
    model: provider ? `existing-${provider}` : 'gpt-looks-like-openai-but-unclassified',
    ...terms,
    effectiveFrom: '2020-01-01T00:00:00Z',
    note: 'Synthetic UI data; no official parser evidence',
    provider,
    allowHistoricalFallback: true,
    sourceEvidenceJson: null,
  }))
  await page.route('**/api/prices**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (path === '/api/prices' && request.method() === 'GET') {
      await route.fulfill({ json: { data: prices, error: null } })
    } else if (path === '/api/prices/update-previews' && request.method() === 'POST') {
      calls.collect++
      await route.fulfill(
        mode === 'collection-error' && calls.collect === 1
          ? {
              status: 503,
              json: { data: null, error: { code: 'Unavailable', message: '수집 연결 실패' } },
            }
          : { json: { data: preview, error: null } },
      )
    } else if (path.endsWith('/apply') && request.method() === 'POST') {
      const body = request.postDataJSON() as { candidateIds: string[]; requestId: string }
      calls.apply.push(body)
      await route.fulfill(
        mode === 'expired'
          ? {
              status: 409,
              json: {
                data: null,
                error: {
                  code: 'PriceUpdateExpired',
                  message: '검토 결과가 만료되어 재수집이 필요합니다',
                },
              },
            }
          : {
              json: {
                data: {
                  requestId: body.requestId,
                  previewId: preview.id,
                  appliedAt: '2026-10-09T00:01:00Z',
                  items: body.candidateIds.map((candidateId) => ({
                    candidateId,
                    priceId: '40000000-0000-4000-8000-000000000001',
                    effectiveFrom: '2026-10-09T00:01:00Z',
                  })),
                },
                error: null,
              },
            },
      )
    } else {
      await route.fallback()
    }
  })
  await page.goto('/admin/prices')
  return calls
}

async function filter(page: Page, testId: string, label: string) {
  const control = page.getByTestId(testId)
  await expect(control).toBeVisible()
  if ((await control.evaluate((element) => element.tagName)) === 'SELECT') {
    await control.selectOption({ label })
  } else {
    await control.click()
    await page.getByRole('option', { name: label, exact: true }).click()
  }
}
async function collect(page: Page) {
  await expect(page.getByTestId('price-update-collect')).toBeVisible()
  const configsToSelect = page.getByTestId('price-update-config')
  for (const control of await configsToSelect.all()) await control.check()
  await page.getByTestId('price-update-collect').click()
  await expect(page.getByTestId('price-update-candidate')).toHaveCount(3)
}

test.describe('model price update common acceptance — synthetic UI only', () => {
  test('existing provider filter includes five providers and unknown without collecting', async ({
    page,
  }) => {
    const calls = await setup(page)
    await expect(page.getByTestId('price-row')).toHaveCount(6)
    for (const label of [...names, '미분류']) {
      await filter(page, 'price-provider-filter', label)
      await expect(page.getByTestId('price-row')).toHaveCount(1)
    }
    await expect(page.getByTestId('price-row')).toContainText(
      'gpt-looks-like-openai-but-unclassified',
    )
    await filter(page, 'price-provider-filter', '전체')
    await expect(page.getByTestId('price-row')).toHaveCount(6)
    expect(calls.collect).toBe(0)
    expect(calls.apply).toHaveLength(0)
  })

  test('preview filter is independent and clears hidden selections before apply', async ({
    page,
  }) => {
    const calls = await setup(page)
    await filter(page, 'price-provider-filter', '미분류')
    await collect(page)
    await page.getByTestId('price-update-select-visible').click()
    await filter(page, 'price-update-provider-filter', 'Anthropic')
    await expect(page.getByRole('status')).toContainText(
      '공급자 필터가 변경되어 적용 선택을 해제했습니다',
    )
    await expect(page.getByTestId('price-update-apply')).toBeDisabled()
    await expect(page.getByTestId('price-row')).toHaveCount(1)
    await page.getByTestId('price-update-select-visible').click()
    await page.getByTestId('price-update-apply').click()
    await expect.poll(() => calls.apply.length).toBe(1)
    expect(calls.apply[0]!.candidateIds).toEqual([candidates[1]!.id])
    expect(calls.collect).toBe(1)
  })

  test('failed provider summary stays visible with distinct empty and blocked results', async ({
    page,
  }) => {
    await setup(page)
    await collect(page)
    await filter(page, 'price-update-provider-filter', 'Google')
    await expect(page.getByTestId('price-update-provider-summary')).toContainText('Tripo')
    await expect(page.getByTestId('price-update-provider-summary')).toContainText(/실패/)
    await expect(page.getByTestId('price-update-panel')).toContainText(/결과 없음/)
    await filter(page, 'price-update-provider-filter', 'Meshy')
    await expect(page.getByTestId('price-update-panel')).toContainText('계정별 환산 지원 필요')
    await expect(page.getByTestId('price-update-panel')).toContainText(/적용 가능 항목 없음/)
    await expect(page.getByTestId('price-update-apply')).toBeDisabled()
  })

  test('explicit recollection clears the previous selection', async ({ page }) => {
    const calls = await setup(page)
    await collect(page)
    await page.getByTestId('price-update-select-visible').click()
    await page.getByTestId('price-update-collect').click()
    await expect.poll(() => calls.collect).toBe(2)
    await expect(page.getByRole('status')).toContainText('새 수집 결과로 적용 선택을 해제했습니다')
    await expect(page.getByTestId('price-update-apply')).toBeDisabled()
  })

  test('collection error supports deliberate retry and no automatic requests', async ({ page }) => {
    const calls = await setup(page, 'collection-error')
    await expect(page.getByTestId('price-update-collect')).toBeVisible()
    for (const control of await page.getByTestId('price-update-config').all()) await control.check()
    await page.getByTestId('price-update-collect').click()
    await expect(page.getByTestId('price-update-panel')).toContainText('수집 연결 실패')
    expect(calls.collect).toBe(1)
    await page.getByTestId('price-update-collect').click()
    await expect(page.getByTestId('price-update-candidate')).toHaveCount(3)
    expect(calls.collect).toBe(2)
  })

  test('expiry explains recollection and does not resubmit automatically', async ({ page }) => {
    const calls = await setup(page, 'expired')
    await collect(page)
    await page.getByTestId('price-update-select-visible').click()
    await page.getByTestId('price-update-apply').click()
    await expect(page.getByTestId('price-update-panel')).toContainText(/만료/)
    expect(calls.apply).toHaveLength(1)
    expect(calls.collect).toBe(1)
  })

  for (const width of [390, 1440]) {
    test(`keyboard, literal source text, ${width}px and theme`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 })
      await page.addInitScript(
        (theme) => localStorage.setItem('nextend.theme', theme),
        width === 390 ? 'dark' : 'light',
      )
      await setup(page)
      await expect(page.locator('html')).toHaveAttribute(
        'data-theme',
        width === 390 ? 'dark' : 'light',
      )
      await collect(page)
      const control = page.getByTestId('price-update-provider-filter')
      await control.focus()
      await expect(control).toBeFocused()
      await page.keyboard.press('Tab')
      await expect(control).not.toBeFocused()
      await expect(page.getByTestId('price-update-panel').locator('img[src="x"]')).toHaveCount(0)
      await expect(page.getByTestId('price-update-panel')).toContainText('synthetic UI evidence')
      expect(
        await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
      ).toBe(true)
    })
  }
})
