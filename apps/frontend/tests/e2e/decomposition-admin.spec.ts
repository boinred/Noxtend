/**
 * Design Ref: §8.3 L2 — 관리자 신규 3화면.
 *
 * **Check 단계 G-4 로 추가됐다.** 사이클 #5 가치의 절반(프롬프트 편집·비교·판정)이
 * 화면 회귀 방어 없이 있었다. `fakeApi` 에 튜닝 엔드포인트가 아예 없어서 이 화면들은
 * E2E 에서 열리지도 않았다 — "테스트가 없다" 보다 나쁜 "열리지도 않는다" 상태였다.
 */
import { expect, test } from '@playwright/test'
import { installFakeApi } from './fakeApi'

test.describe('관리자 탐색 (§2.3-6)', () => {
  test('#A0 골든 세트가 다른 관리자 화면과 같은 1280px 폭을 사용한다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/golden')

    const box = await page.getByTestId('page-inner').boundingBox()
    expect(box!.width).toBeGreaterThan(960)
    expect(box!.width).toBeLessThanOrEqual(1280)
  })

  test('#A0b 골든 세트와 단가 폼이 같은 20px 내부 여백을 사용한다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/golden')

    const goldenPanel = await page.getByTestId('golden-form').boundingBox()
    const goldenLabel = await page.getByTestId('golden-form').locator('label').first().boundingBox()
    const goldenInset = goldenLabel!.x - goldenPanel!.x

    await page.goto('/admin/prices')
    await page.getByTestId('price-add').click()

    const pricePanel = await page.getByTestId('price-form').boundingBox()
    const priceLabel = await page.getByTestId('price-form').locator('label').first().boundingBox()
    const priceInset = priceLabel!.x - pricePanel!.x

    expect(goldenInset).toBeGreaterThanOrEqual(20)
    expect(priceInset).toBeGreaterThanOrEqual(20)
    expect(Math.abs(goldenInset - priceInset)).toBeLessThanOrEqual(1)
  })

  test('#A1 섹션 탭으로 세 화면을 오간다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin')

    // /admin 은 공급자로 리다이렉트한다 — 기존 북마크가 깨지지 않는다
    await expect(page).toHaveURL(/\/admin\/providers$/)

    await page.getByTestId('admin-tab-prompts').click()
    await expect(page).toHaveURL(/\/admin\/prompts$/)
    await expect(page.getByTestId('prompt-grid')).toBeVisible()

    await page.getByTestId('admin-tab-golden').click()
    await expect(page).toHaveURL(/\/admin\/golden$/)
    await expect(page.getByTestId('golden-screen')).toBeVisible()

    await page.getByTestId('admin-tab-providers').click()
    await expect(page.getByTestId('provider-table')).toBeVisible()
  })

  test('#A2 사이드바에는 관리자 항목이 하나뿐이다', async ({ page }) => {
    // 사이드바는 제작 흐름의 자리다. 관리자 화면이 넷이 됐다고 늘리지 않는다
    await installFakeApi(page)
    await page.goto('/admin/prompts')

    await expect(page.getByTestId('nav-admin')).toHaveCount(1)
    await expect(page.getByTestId('admin-tabs')).toBeVisible()
  })
})

test.describe('프롬프트 관리 (§4.2 #16~19)', () => {
  test('#A3 모든 분석·생성 격자의 활성 슬롯이 전용으로 보인다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prompts')

    // 기본 슬롯 5개와 배경 전용 분석·생성·기준 생성·평가 슬롯
    const rows = page.getByTestId('prompt-row')
    await expect(rows).toHaveCount(9)
    expect(
      await rows.evaluateAll((elements) =>
        elements.map((element) => element.getAttribute('data-kind')).sort(),
      ),
    ).toEqual([
      'analyze',
      'analyzeSprites',
      'decompose',
      'extract',
      'generate',
      'generateSprite',
      'generateSpriteSource',
      'rewriteDescriptions',
      'similarityEvaluate',
    ])
    await expect(page.getByTestId('prompt-cell-dedicated')).toHaveCount(9)

    // 편집 화면이 쓸 수 있는 변수를 보여준다 — 격자가 아니라 여기서
    await page.goto('/admin/prompts/decompose')
    await expect(page.getByTestId('prompt-variables-hint')).toContainText('{{scene}}')
    await expect(page.getByTestId('prompt-variables-hint')).toContainText('{{parts}}')

    await page.goto('/admin/prompts')
    await page
      .getByTestId('prompt-row')
      .filter({ hasText: '2D 배경 기준 생성' })
      .getByTestId('prompt-cell-dedicated')
      .click()
    await expect(page).toHaveURL(/\/admin\/prompts\/generateSpriteSource\?category=background$/)
    await expect(page.getByTestId('prompt-variables-hint')).toContainText('{{prompt}}')
    await expect(page.getByTestId('prompt-user-input')).toHaveValue('{{prompt}}')
  })

  test('#A4 새 버전은 비활성으로 저장된다 — 저장이 활성화가 아니다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prompts/extract')

    await page.getByTestId('prompt-system-input').fill('파츠를 세라 {{scene}}')
    await page.getByTestId('prompt-note-input').fill('E2E 검증')
    await page.getByTestId('prompt-save').click()

    // v2 가 생겼지만 켜지지 않았다 — 편집 중 실수가 즉시 운영에 나가지 않는다
    await expect(page.getByTestId('prompt-version')).toHaveCount(2)
    await expect(page.getByTestId('prompt-version-active')).toHaveCount(1)
    await expect(page.getByTestId('prompt-activate')).toHaveCount(1)
  })

  test('#A5 활성 전환 후 롤백된다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prompts/extract')

    await page.getByTestId('prompt-system-input').fill('새 프롬프트 {{scene}}')
    await page.getByTestId('prompt-save').click()
    await expect(page.getByTestId('prompt-version')).toHaveCount(2)

    // v2 켜기
    await page.getByTestId('prompt-activate').click()
    await expect(page.getByTestId('prompt-version-active')).toHaveCount(1)
    await expect(page.getByTestId('prompt-version').first()).toContainText('활성')

    // 롤백 = 이전 버전을 켜는 것. 별도 기능이 없다
    await page.getByTestId('prompt-activate').click()
    await expect(page.getByTestId('prompt-version').last()).toContainText('활성')
    await expect(page.getByTestId('prompt-version-active')).toHaveCount(1)
  })

  test('#A6 변수 오타는 저장 시점에 거부된다', async ({ page }) => {
    // 활성화한 뒤 그 단계의 모든 실행이 실패하는 것보다 낫다 (§2.3-5)
    await installFakeApi(page)
    await page.goto('/admin/prompts/extract')

    await page.getByTestId('prompt-system-input').fill('오타 {{scen}}')
    await page.getByTestId('prompt-save').click()

    await expect(page.getByTestId('prompt-error')).toContainText('scen')
    // 쓸 수 있는 변수를 함께 알려준다 — 무엇으로 고쳐야 하는지 알 수 있게
    await expect(page.getByTestId('prompt-error')).toContainText('{{scene}}')
    await expect(page.getByTestId('prompt-version')).toHaveCount(1)
  })
})

test.describe('골든 세트와 판정 (§4.2 #20~25)', () => {
  const SAMPLE = {
    id: 'golden-1',
    storedImageId: 'upload-seed-job-0',
    name: '안개 낀 성문',
    expectedNote: '성문·문루·성벽이 나와야 하고 안개는 파츠가 아니다',
    createdAt: '2026-07-31T00:00:00Z',
  }

  const RUNS = [
    {
      jobId: 'seed-job-0',
      status: 'succeeded',
      model: 'gpt-5.5',
      promptVersions: { analyze: 1, extract: 1, decompose: 1 },
      partCount: 4,
      verdict: null,
      createdAt: '2026-07-31T01:00:00Z',
    },
    {
      jobId: 'seed-job-1',
      status: 'succeeded',
      model: 'gpt-5.5',
      // 같은 버전의 다른 실행 — 비결정성 폭을 보는 용도다 (FR-13 · R-2)
      promptVersions: { analyze: 1, extract: 1, decompose: 1 },
      partCount: 4,
      verdict: null,
      createdAt: '2026-07-31T02:00:00Z',
    },
  ]

  test('#A7 골든 샘플을 등록한다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/golden')

    await expect(page.getByTestId('golden-empty')).toBeVisible()

    await page.getByTestId('golden-image-input').fill('upload-seed-job-0')
    await page.getByTestId('golden-name-input').fill('안개 낀 성문')
    await page.getByTestId('golden-expected-input').fill('성문·문루·성벽이 나와야 한다')
    await page.getByTestId('golden-create').click()

    await expect(page.getByTestId('golden-item')).toHaveCount(1)
    await expect(page.getByTestId('golden-list')).toContainText('안개 낀 성문')
  })

  test('#A8 실행 이력에 단계별 프롬프트 버전이 함께 온다', async ({ page }) => {
    // 이것이 없으면 목록에서 무엇을 비교하는지 알 수 없다
    await installFakeApi(page, {
      seedTerminalJobs: 2,
      goldenSamples: [SAMPLE],
      goldenRuns: RUNS,
    })
    await page.goto('/admin/golden/golden-1')

    await expect(page.getByTestId('run-row')).toHaveCount(2)
    await expect(page.getByTestId('runs-table')).toContainText('v1/v1/v1')
    await expect(page.getByTestId('runs-table')).toContainText('gpt-5.5')
  })

  test('#A9 두 실행을 나란히 놓고 판정한다', async ({ page }) => {
    await installFakeApi(page, {
      seedTerminalJobs: 2,
      goldenSamples: [SAMPLE],
      goldenRuns: RUNS,
    })
    await page.goto('/admin/golden/golden-1')

    // 고르기 전에는 양쪽 다 비어 있다
    await expect(page.getByTestId('run-pane-empty')).toHaveCount(2)

    await page.getByTestId('run-pick-left').first().click()
    await page.getByTestId('run-pick-right').last().click()

    await expect(page.getByTestId('run-pane')).toHaveCount(2)
    // 스튜디오 결과와 같은 오버레이 컴포넌트를 쓴다 — 두 화면이 어긋나면 안 된다
    await expect(page.getByTestId('parts-overlay')).toHaveCount(2)

    await page.getByTestId('verdict-memo').first().fill('가림 관계 정확')
    await page.getByTestId('verdict-pass').first().click()

    await expect(page.getByTestId('run-verdict')).toContainText('쓸만함')
    await expect(page.getByTestId('run-verdict')).toContainText('가림 관계 정확')
  })

  test('#A10 판정이 다시 조회된다', async ({ page }) => {
    await installFakeApi(page, {
      seedTerminalJobs: 2,
      goldenSamples: [SAMPLE],
      goldenRuns: RUNS,
    })
    await page.goto('/admin/golden/golden-1')

    await page.getByTestId('run-pick-left').first().click()
    await page.getByTestId('verdict-memo').first().fill('판정 재조회 검증')
    await page.getByTestId('verdict-fail').first().click()

    await expect(page.getByTestId('run-verdict')).toContainText('아님')

    // 화면을 새로 열어도 남아 있다 — 판단 근거가 머릿속에만 남지 않는다 (D-8)
    await page.reload()
    await expect(page.getByTestId('run-verdict')).toContainText('판정 재조회 검증')
  })
})
