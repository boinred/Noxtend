import { expect, test } from '@playwright/test'
import { installFakeApi } from './fakeApi'

const configId = '00000000-0000-4000-8000-000000000001'
const previewId = '20000000-0000-4000-8000-000000000001'
const candidateId = '10000000-0000-4000-8000-000000000001'
// 합성 UI 계약 · 실제 공급자 가격 근거와 별개
const preview = {
  id: previewId,
  createdAt: '2026-10-09T00:00:00Z',
  expiresAt: '2026-10-09T00:30:00Z',
  providers: [
    {
      providerConfigId: configId,
      provider: 'openai',
      status: 'success',
      modelListStatus: 'complete',
      collectedAt: '2026-10-09T00:00:00Z',
      modelError: null,
      priceError: null,
      candidateIds: [candidateId],
    },
  ],
  candidates: [
    {
      id: candidateId,
      provider: 'openai',
      model: 'synthetic-long-model-name-for-layout-contract',
      area: 'text',
      operation: 'text',
      conditions: 'Paid Standard; synthetic UI terms',
      providerConfigIds: [configId],
      currentTerms: null,
      terms: {
        inputPerMillion: 1,
        outputPerMillion: 2,
        longContextFrom: 100001,
        longInputPerMillion: 0.5,
        longOutputPerMillion: 2.5,
        perImage: null,
        officialEffectiveFrom: '2099-01-01T00:00:00Z',
      },
      evidence: [
        {
          url: 'https://developers.openai.com/api/docs/pricing',
          collectedAt: '2026-10-09T00:00:00Z',
          sha256: 'a'.repeat(64),
          conditions: '<img src=x onerror=alert(1)> synthetic evidence',
          creditsPerTask: null,
          usdPerCredit: null,
        },
      ],
      changeKind: 'newModel',
      executionSupport: 'unverified',
      blockedReason: null,
    },
  ],
}

for (const width of [390, 1440]) {
  test(`예약 하한·응답 유실 동일 재전송·${width}px 시각 확인`, async ({ page }, testInfo) => {
    const theme = width === 390 ? 'dark' : 'light'
    await page.setViewportSize({ width, height: 900 })
    await page.addInitScript((value) => localStorage.setItem('nextend.theme', value), theme)
    await installFakeApi(page, {
      providers: [
        {
          id: configId,
          displayName: 'Synthetic OpenAI',
          kind: 'openai',
          isEnabled: true,
          capabilities: ['textAnalysis'],
          apiKeyMasked: '••••',
        },
      ],
    })
    const bodies: unknown[] = []
    await page.route('**/api/prices/update-previews**', async (route) => {
      if (!route.request().url().endsWith('/apply')) {
        await route.fulfill({ json: { data: preview, error: null } })
        return
      }
      const body = route.request().postDataJSON()
      bodies.push(body)
      if (bodies.length === 1) await route.abort('failed')
      else
        await route.fulfill({
          json: {
            data: {
              requestId: body.requestId,
              previewId,
              appliedAt: '2026-10-09T00:02:00Z',
              items: [
                {
                  candidateId,
                  priceId: '40000000-0000-4000-8000-000000000001',
                  effectiveFrom: body.effectiveFrom,
                },
              ],
            },
            error: null,
          },
        })
    })
    await page.goto('/admin/prices')
    await page.getByTestId('price-update-config').check()
    await page.getByTestId('price-update-collect').click()
    await page.getByTestId('price-update-select-visible').click()
    await expect(page.getByTestId('price-update-panel')).toContainText(
      '100,001 tokens 이상 입력 $0.5 / 출력 $2.5 per 1M',
    )
    const schedule = page.locator('#price-update-effective')
    await schedule.fill('2098-12-31T00:00')
    await expect(page.getByTestId('price-update-apply')).toBeDisabled()
    await schedule.fill('2099-01-02T12:00')
    await page.getByText('출처·조건·수집 근거', { exact: true }).click()
    await expect(page.getByTestId('price-update-panel').locator('img')).toHaveCount(0)
    await page.getByTestId('price-update-provider-filter').focus()
    await expect(page.getByTestId('price-update-provider-filter')).toBeFocused()
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({
      path: testInfo.outputPath(`price-update-${width}-${theme}.png`),
      fullPage: true,
    })
    await page.getByTestId('price-update-apply').click()
    await expect(page.getByRole('alert')).toContainText('같은 입력')
    await expect(page.getByTestId('price-update-collect')).toBeDisabled()
    await expect(schedule).toBeDisabled()
    await expect(page.getByTestId('price-update-provider-filter')).toBeDisabled()
    expect(bodies).toHaveLength(1)
    await page.getByTestId('price-update-apply').click()
    await expect(page.getByRole('status')).toContainText('저장했습니다')
    expect(bodies).toHaveLength(2)
    expect(bodies[1]).toEqual(bodies[0])
    expect((bodies[0] as { effectiveFrom: string }).effectiveFrom).toBe(
      await page.evaluate(() => new Date('2099-01-02T12:00').toISOString()),
    )
  })
}

for (const theme of ['light', 'dark']) {
  for (const width of [390, 1440]) {
    test(`공급자 실패·경고 색상 · ${theme} · ${width}px`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 900 })
      await page.addInitScript((value) => localStorage.setItem('nextend.theme', value), theme)
      await installFakeApi(page, {
        providers: [
          {
            id: configId,
            displayName: 'Claude-Free-Plan',
            kind: 'anthropic',
            isEnabled: true,
            apiKeyMasked: '••••',
          },
        ],
      })
      const states = [
        {
          status: 'failed',
          modelListStatus: 'partial',
          modelError: 'HTTP 401',
          label: '수집 실패',
          token: '--destructive',
        },
        {
          status: 'partial',
          modelListStatus: 'failed',
          modelError: 'HTTP 401',
          label: '목록 실패',
          token: '--destructive',
        },
        {
          status: 'partial',
          modelListStatus: 'partial',
          modelError: null,
          label: '부분 실패',
          token: '--accent-amber',
        },
        {
          status: 'success',
          modelListStatus: 'partial',
          modelError: null,
          label: '목록 부분 실패',
          token: '--accent-amber',
        },
        {
          status: 'success',
          modelListStatus: 'complete',
          modelError: null,
          label: '성공',
          token: '--foreground',
        },
      ]
      await page.route('**/api/prices/update-previews', (route) =>
        route.fulfill({
          json: {
            data: {
              ...preview,
              providers: states.map((state, index) => ({
                ...preview.providers[0],
                ...state,
                provider: 'anthropic',
                providerConfigId: `00000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
              })),
              candidates: [{ ...preview.candidates[0], blockedReason: '계정별 환산 지원 필요' }],
            },
            error: null,
          },
        }),
      )
      await page.goto('/admin/prices')
      await page.getByTestId('price-update-config').check()
      await page.getByTestId('price-update-collect').click()
      const rows = page.getByTestId('price-update-provider-summary').locator('p')
      await expect(rows).toHaveCount(states.length)
      for (const [index, state] of states.entries()) {
        await expect(rows.nth(index)).toContainText(state.label)
        if (state.modelError) await expect(rows.nth(index)).toContainText('모델 실패: HTTP 401')
        const colors = await rows.nth(index).evaluate((row, token) => {
          const probe = document.createElement('span')
          probe.style.color = `var(${token})`
          document.body.append(probe)
          const expected = getComputedStyle(probe).color
          probe.remove()
          return { actual: getComputedStyle(row).color, expected }
        }, state.token)
        expect(colors.actual).toBe(colors.expected)
      }
      await expect(page.getByText('적용 보류: 계정별 환산 지원 필요')).toBeVisible()
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      )
      await page.screenshot({
        path: testInfo.outputPath(`status-${theme}-${width}.png`),
        fullPage: true,
      })
    })
  }
}
