/**
 * Design Ref: §8.2 · FR-07 · D-08 — 배치가 여럿인 파츠를 오버레이가 어떻게 보이는가.
 *
 * **이 사이클이 존재하는 이유가 F-01 이다.** 전에는 파츠당 상자가 하나뿐이라 가로등 여덟 개를
 * 한 상자로 감쌌고, 그 상자는 화면의 절반을 덮었다. 이제 지목하면 그 파츠가 실제로 있는
 * 자리가 전부 켜진다.
 */
import { expect, test } from '@playwright/test'
import { installFakeApi, selectImage } from './fakeApi'

async function gotoResult(page: import('@playwright/test').Page) {
  await installFakeApi(page)
  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('run-result')).toBeVisible()
}

const shownBoxes = (page: import('@playwright/test').Page) =>
  page.locator('[data-testid="overlay-box"][data-shown="true"]')

test.describe('파츠 오버레이', () => {
  test('F-03 평소에는 상자가 하나도 안 보인다', async ({ page }) => {
    await gotoResult(page)

    // 상자를 미리 다 그리면 화면이 어지럽고 클릭이 어렵다 — 그것이 이 재설계의 출발점이다
    await expect(shownBoxes(page)).toHaveCount(0)
    await expect(page.getByTestId('overlay-chip').first()).toBeVisible()
    await expect(page.getByTestId('overlay-anchor').first()).toBeVisible()
  })

  test('F-01 파츠를 지목하면 그 배치가 전부 켜진다', async ({ page }) => {
    await gotoResult(page)

    // 첫 파츠는 배치가 셋이다 — 하나만 켜지면 D-08 이 깨진 것이다
    await page.getByTestId('overlay-chip').first().hover()
    await expect(shownBoxes(page)).toHaveCount(3)
  })

  test('F-02 배치가 하나인 파츠는 하나만 켜진다', async ({ page }) => {
    await gotoResult(page)

    await page.getByTestId('overlay-chip').nth(1).hover()
    await expect(shownBoxes(page)).toHaveCount(1)
  })

  test('F-04 한 파츠의 배치는 같은 색이다', async ({ page }) => {
    await gotoResult(page)
    await page.getByTestId('overlay-chip').first().hover()

    const colors = await shownBoxes(page).evaluateAll((nodes) =>
      nodes.map((node) => getComputedStyle(node).borderColor),
    )

    // 색이 파츠를 뜻해야 배치들이 묶여 읽힌다
    expect(new Set(colors).size).toBe(1)
  })

  test('클릭하면 고정되고 "이동" 이 파츠 상세로 데려간다', async ({ page }) => {
    await gotoResult(page)

    await page.getByTestId('overlay-chip').first().click()
    await expect(page.getByTestId('overlay-jump')).toHaveCount(1)

    await page.getByTestId('overlay-jump').click()

    // 도착한 줄을 밝히지 않으면 어디로 내려왔는지 모른다
    await expect(page.locator('[data-testid="part-row"][data-landed="true"]')).toHaveCount(1)

    // 다시 누르면 고정이 풀린다
    await page.getByTestId('overlay-chip').first().click()
    await expect(page.getByTestId('overlay-jump')).toHaveCount(0)
  })

  test('파츠 목록이 이름 옆에 개체 수를 보여준다', async ({ page }) => {
    await gotoResult(page)

    // 배치 수가 곧 "장면에 몇 개 있는가" 다. 한 개짜리는 적지 않는다
    const badges = page.getByTestId('part-count')
    await expect(badges).toHaveCount(1)
    await expect(badges.first()).toHaveText('×3')
  })
})
