/**
 * Design Ref: §8 · FR-07·FR-12 · SC — 팔레트가 이름과 **실제 색**을 함께 보인다.
 *
 * **이 사이클이 존재하는 이유가 F-01 과 F-05 다.** 전에는 화면이 팔레트 문자열에서 HEX 를
 * 정규식으로 긁어 칩을 칠했고, 모델이 `"백색 건물 외벽"` 처럼 자연어만 주는 순간
 * `transparent` 가 됐다. 이름은 보이는데 색이 없었고, 아무도 그 사실을 듣지 못했다.
 *
 * computed style 로 보는 이유는 인라인 값이 아니라 **실제로 칠해진 색**이 관심사이기
 * 때문이다. 클래스만 확인하면 같은 결함이 다시 통과한다.
 */
import { expect, test } from '@playwright/test'
import { installFakeApi, selectImage } from './fakeApi'

async function gotoScene(page: import('@playwright/test').Page) {
  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('scene-panel')).toBeVisible()
}

/** 칩 하나의 실제 배경색. */
function chipBackground(page: import('@playwright/test').Page, index: number) {
  return page
    .getByTestId('scene-swatch-chip')
    .nth(index)
    .evaluate((node) => getComputedStyle(node).backgroundColor)
}

test.describe('장면 팔레트', () => {
  test('F-01 이름과 실제 색이 함께 보인다', async ({ page }) => {
    await installFakeApi(page)
    await gotoScene(page)

    await expect(page.getByTestId('scene-palette')).toContainText('청회색 바다')

    // #2E5C6E
    expect(await chipBackground(page, 0)).toBe('rgb(46, 92, 110)')
  })

  test('F-05 색을 아는 칩은 하나도 투명하지 않다', async ({ page }) => {
    await installFakeApi(page)
    await gotoScene(page)

    const resolved = page
      .getByTestId('scene-swatch-chip')
      .and(page.locator('[data-resolved="true"]'))
    const count = await resolved.count()
    expect(count).toBeGreaterThan(0)

    // 이것이 원래 결함이다 — 한 칸이라도 투명하면 그 색은 사용자에게 전달되지 않았다
    for (let index = 0; index < count; index++) {
      const background = await resolved
        .nth(index)
        .evaluate((node) => getComputedStyle(node).backgroundColor)

      expect(background).not.toBe('transparent')
      expect(background).not.toBe('rgba(0, 0, 0, 0)')
    }
  })

  test('F-03 색상 미확정은 투명이 아니라 구분되는 상태다', async ({ page }) => {
    await installFakeApi(page)
    await gotoScene(page)

    const unresolved = page.locator('[data-testid="scene-swatch-chip"][data-resolved="false"]')

    // 미확정을 투명으로 두면 지금 고치는 결함과 똑같이 "빈 칩" 으로 읽힌다 (D-11)
    await expect(unresolved).toHaveCount(1)
    expect(await unresolved.evaluate((node) => getComputedStyle(node).borderStyle)).toBe('dashed')

    // 이름은 살아 있고, 색을 모른다는 사실이 함께 적힌다
    await expect(
      page.getByTestId('scene-swatch').filter({ hasText: '젖은 포장 표면' }),
    ).toHaveAttribute('title', '젖은 포장 표면 · 색상 미확정')
  })

  test('F-02 흰색도 경계가 보인다', async ({ page }) => {
    await installFakeApi(page, { palette: [{ name: '백색 건물 외벽', hex: '#FFFFFF' }] })
    await gotoScene(page)

    const chip = page.getByTestId('scene-swatch-chip').first()

    // 흰 배경 위의 흰 칩은 border 가 없으면 사라진다 (NFR-06)
    expect(await chip.evaluate((node) => getComputedStyle(node).backgroundColor)).toBe(
      'rgb(255, 255, 255)',
    )
    expect(await chip.evaluate((node) => getComputedStyle(node).borderWidth)).not.toBe('0px')
  })
})
