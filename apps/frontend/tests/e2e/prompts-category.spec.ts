/**
 * 프롬프트 카테고리 축 (prompt-category-axis §12.1·§12.2 · NFR-04).
 *
 * 프롬프트를 제작 카테고리(캐릭터·소품·배경)별로 두고 편집·활성화한다. 전용이 없으면
 * 기본으로 폴백한다 — 이 폴백을 **서버가 계산해** 격자로 내려주므로 화면은 표로 펼치기만
 * 한다 (§12.1). 행이 없는 카테고리의 첫 프롬프트를 만드는 진입점도 여기서 지킨다 (§12.2).
 *
 * **캐시가 섞이지 않는지**가 핵심 회귀다 — 쿼리 키에 카테고리가 없으면 캐릭터 이력을 본 뒤
 * 기본 이력을 열 때 캐릭터 행이 남는다 (§12.1 쿼리 키 결정).
 */
import { expect, test } from '@playwright/test'
import { installFakeApi } from './fakeApi'

test.describe('프롬프트 카테고리 축', () => {
  test('#C1 격자 기본 열은 전용, 카테고리 열은 기본으로 폴백한다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prompts')

    const extract = page.locator('[data-testid="prompt-row"][data-kind="extract"]')

    // 기본 슬롯에만 시드가 있으므로 기본 열은 전용, 캐릭터 열은 폴백
    await expect(extract.locator('[data-category="default"]')).toHaveAttribute(
      'data-status',
      'dedicated',
    )
    await expect(extract.locator('[data-category="character"]')).toHaveAttribute(
      'data-status',
      'fallback',
    )
  })

  test('#C2 없는 카테고리를 골라 첫 프롬프트를 만들 수 있다 (§12.2)', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prompts')

    // 격자의 폴백 칸을 눌러 캐릭터 편집으로 들어간다
    await page
      .locator('[data-testid="prompt-row"][data-kind="extract"] [data-category="character"]')
      .click()
    await expect(page).toHaveURL(/\/admin\/prompts\/extract\?category=character$/)

    // 캐릭터 슬롯은 비어 있다 — 첫 프롬프트 안내가 뜬다
    await expect(page.getByTestId('prompt-empty-category')).toBeVisible()
    await expect(page.getByTestId('prompt-version')).toHaveCount(0)

    await page.getByTestId('prompt-system-input').fill('캐릭터 전용 {{scene}}')
    await page.getByTestId('prompt-save').click()

    // 캐릭터 v1 이 생겼고 아직 비활성이다
    await expect(page.getByTestId('prompt-version')).toHaveCount(1)
    await page.getByTestId('prompt-activate').click()
    await expect(page.getByTestId('prompt-version-active')).toHaveCount(1)

    // 격자로 돌아오면 캐릭터 칸이 전용으로 바뀐다
    await page.goto('/admin/prompts')
    await expect(
      page.locator('[data-testid="prompt-row"][data-kind="extract"] [data-category="character"]'),
    ).toHaveAttribute('data-status', 'dedicated')
  })

  test('#C3 카테고리별 이력이 섞이지 않는다 — 쿼리 키에 카테고리가 있다', async ({ page }) => {
    await installFakeApi(page)

    // 기본 이력: v1 하나
    await page.goto('/admin/prompts/extract')
    await expect(page.getByTestId('prompt-version')).toHaveCount(1)

    // 캐릭터 탭으로 전환 — 캐릭터 슬롯은 비어 있어야 한다(기본 v1 이 남으면 캐시가 섞인 것)
    await page.locator('[data-testid="prompt-category-tab"][data-category="character"]').click()
    await expect(page).toHaveURL(/\?category=character$/)
    await expect(page.getByTestId('prompt-version')).toHaveCount(0)

    // 기본 탭으로 돌아오면 다시 v1 하나 — 캐릭터의 빈 목록이 남지 않는다
    await page.locator('[data-testid="prompt-category-tab"][data-category="default"]').click()
    await expect(page.getByTestId('prompt-version')).toHaveCount(1)
  })

  test('#C4 카테고리 활성 전환은 그 카테고리에만 영향한다 (§8-8 격리)', async ({ page }) => {
    await installFakeApi(page)

    // 캐릭터 전용 v1 을 만들고 켠다
    await page.goto('/admin/prompts/extract?category=character')
    await page.getByTestId('prompt-system-input').fill('캐릭터 전용 {{scene}}')
    await page.getByTestId('prompt-save').click()
    await page.getByTestId('prompt-activate').click()
    await expect(page.getByTestId('prompt-version-active')).toHaveCount(1)

    // 기본 슬롯의 활성은 그대로 v1 이다 — 캐릭터를 켜도 기본이 내려가지 않는다
    await page.goto('/admin/prompts')
    await expect(
      page.locator('[data-testid="prompt-row"][data-kind="extract"] [data-category="default"]'),
    ).toHaveAttribute('data-status', 'dedicated')
    await expect(
      page.locator('[data-testid="prompt-row"][data-kind="extract"] [data-category="character"]'),
    ).toHaveAttribute('data-status', 'dedicated')
  })
})

test.describe('유사도 평가 슬롯 (background-similarity-tuning §15.1)', () => {
  test('격자에서 Background 전용임이 보이고, 편집은 Background 탭으로 바로 간다', async ({
    page,
  }) => {
    await installFakeApi(page)
    await page.goto('/admin/prompts')

    // 유사도 평가 행 — Background 만 전용, 나머지는 "해당 없음" (실행 불가가 아니다)
    const row = page.getByTestId('prompt-row').filter({ hasText: '유사도 평가' })
    await expect(row.getByTestId('prompt-cell-dedicated')).toHaveCount(1)
    await expect(row.getByTestId('prompt-cell-na')).toHaveCount(3)
    await expect(row.getByTestId('prompt-cell-unavailable')).toHaveCount(0)

    // 카테고리 없이 진입해도 기본(빈) 탭이 아니라 Background 슬롯이 열린다
    await page.goto('/admin/prompts/similarityEvaluate')
    await expect(page.getByTestId('prompt-category-tab')).toHaveCount(1)
    await expect(page.getByTestId('prompt-version')).toHaveCount(1)
    await expect(page.getByTestId('prompt-empty-category')).toHaveCount(0)
  })
})
