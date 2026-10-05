/**
 * 단가 관리 화면.
 *
 * 전에는 단가가 코드 안의 상수라 하나를 고치려면 재배포해야 했다. 화면에서 고칠 수
 * 있게 되면서 **잘못된 값이 들어올 경로가 생겼다** — 그 경계를 여기서 지킨다.
 *
 * 특히 "인상은 새 행, 오타는 수정" 이라는 구분이 화면에 드러나는지를 본다. 비용은
 * 호출 시각에 걸리는 단가로 계산되므로, 기존 행을 고치면 과거 지출이 함께 바뀐다.
 */
import { expect, test } from '@playwright/test'
import { installFakeApi } from './fakeApi'

test.describe('단가 관리', () => {
  test('#P0 데스크톱 부제목이 한 줄에 머문다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prices')

    const subtitle = page.getByTestId('prices-screen').locator('header p')
    const box = await subtitle.boundingBox()
    const lineHeight = await subtitle.evaluate((element) =>
      Number.parseFloat(getComputedStyle(element).lineHeight),
    )

    expect(box!.height).toBeLessThanOrEqual(lineHeight * 1.1)
  })

  test('#P1 탭으로 열리고 모델별로 묶여 보인다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/providers')

    await page.getByTestId('admin-tab-prices').click()
    await expect(page).toHaveURL(/\/admin\/prices$/)

    // gpt-5.6-luna 2행 + claude-opus-5 1행 + gpt-image-1 1행 (사이클 #7 장당 단가)
    await expect(page.getByTestId('price-row')).toHaveCount(4)
    await expect(page.getByTestId('price-count')).toContainText('3개 모델')
  })

  /**
   * **이 표가 존재하는 이유.** 행이 하나뿐이면 인상분이 과거 지출까지 소급된다.
   * 화면이 둘을 구분하지 못하면 사용자는 어느 단가가 지금 쓰이는지 알 수 없다.
   */
  test('#P2 적용 중인 행과 예정된 행을 구분한다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prices')

    // 적용 중: luna(2026-01) · opus · gpt-image-1 / 예정: luna(2099)
    await expect(page.getByTestId('price-current')).toHaveCount(3)
    await expect(page.getByTestId('price-scheduled')).toHaveCount(1)

    // 2099년 시행 행이 "예정" 이어야 한다 — 그것이 적용 중이면 계산이 틀린 것이다
    const scheduled = page.getByTestId('price-row').filter({ hasText: '2099-01-01' })
    await expect(scheduled.getByTestId('price-scheduled')).toBeVisible()
  })

  /**
   * 단순 내림차순이면 2099년 행이 맨 위에 오고 모델명도 거기 붙는다. 훑어볼 때
   * 그 값이 현재 단가처럼 읽히는데, 비용을 잘못 읽게 하지 않는 것이 이 표의 목적이다.
   */
  test('#P2b 묶음의 첫 행은 적용 중인 행이다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prices')

    const first = page.locator('[data-testid="price-row"][data-model="gpt-5.6-luna"]').first()

    // 2099년 예정 행이 아니라 지금 쓰이는 행이 위에 와야 한다
    await expect(first).toContainText('2026-01-01')
    await expect(first.getByTestId('price-current')).toBeVisible()

    // 모델명은 묶음의 첫 행에만 붙는다 — 그 행이 곧 적용 중인 행이다
    await expect(first).toContainText('gpt-5.6-luna')
  })

  test('#P3 장문 구간을 읽을 수 있게 보여준다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prices')

    // 구간이 안 보이면 왜 같은 모델의 비용이 튀는지 설명되지 않는다
    await expect(page.getByTestId('price-table')).toContainText('272,000 초과')
  })

  test('#P4 새 행을 추가한다 — 공급자가 단가를 바꿨을 때의 길', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prices')

    await page.getByTestId('price-add').click()
    await page.getByTestId('price-model-input').fill('gpt-5.4-mini')
    await page.getByTestId('price-input-input').fill('0.75')
    await page.getByTestId('price-output-input').fill('4.5')
    await page.getByTestId('price-effective-input').fill('2026-08-01')
    await page.getByTestId('price-save').click()

    await expect(page.getByTestId('price-form')).toHaveCount(0)
    await expect(page.getByTestId('price-row')).toHaveCount(5)
    await expect(page.getByTestId('price-table')).toContainText('gpt-5.4-mini')
  })

  test('#P5 반쪽 장문 구간은 저장을 막는다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prices')

    await page.getByTestId('price-add').click()
    await page.getByTestId('price-model-input').fill('gpt-5.9')
    await page.getByTestId('price-input-input').fill('1')
    await page.getByTestId('price-output-input').fill('5')

    // 시작 토큰만 채우고 단가를 비우면 짧은 단가로 계산되어 실제보다 싼 값이 나온다
    await page.getByTestId('price-long-from-input').fill('272000')

    await expect(page.getByTestId('price-long-warning')).toBeVisible()
    await expect(page.getByTestId('price-save')).toBeDisabled()
  })

  test('#P6 수정은 과거 비용이 바뀐다고 알린다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prices')

    await page.getByTestId('price-edit').first().click()

    // 되돌리기 어려운 결과는 누르기 전에 말해야 한다
    await expect(page.getByTestId('price-edit-warning')).toBeVisible()

    // 모델명은 행의 정체성이다 — 바꿔야 한다면 그것은 다른 모델이고 새 행이 맞다
    await expect(page.getByTestId('price-model-input')).toBeDisabled()
  })

  test('#P7 같은 모델·같은 시행일은 서버가 거부하고 화면이 그 사유를 보여준다', async ({
    page,
  }) => {
    await installFakeApi(page)
    await page.goto('/admin/prices')

    await page.getByTestId('price-add').click()
    await page.getByTestId('price-model-input').fill('gpt-5.6-luna')
    await page.getByTestId('price-input-input').fill('0.2')
    await page.getByTestId('price-output-input').fill('1.2')
    await page.getByTestId('price-effective-input').fill('2026-01-01')
    await page.getByTestId('price-save').click()

    await expect(page.getByTestId('price-error')).toBeVisible()
    // 폼이 닫히면 사용자가 입력한 값을 잃는다
    await expect(page.getByTestId('price-form')).toBeVisible()
  })

  test('#P8 삭제는 확인을 거친다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prices')

    // 지우면 그 구간의 호출이 이전 행이나 미등록으로 계산된다 — 조용히 바뀌면 안 된다
    page.on('dialog', (dialog) => void dialog.accept())

    await page.getByTestId('price-delete').first().click()

    await expect(page.getByTestId('price-row')).toHaveCount(3)
  })
})
