/**
 * Design Ref: background-similarity-tuning §14 · §18.3 — 유사도 inspector 기준 흐름.
 *
 * 렌더된 픽셀은 보지 않는다 — 여기서 보는 것은 계약이다: 자격 사유가 버튼을 잠그는가,
 * 시작 전에 비용이 보이는가, 캡처가 준비돼야 실행되는가, 결과가 여섯 축과 보정
 * 카드로 나오는가, 낮은 confidence 는 기본 선택에서 빠지는가.
 */
import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { DEFAULT_PROVIDER, MESH_PROVIDER, installFakeApi, selectImage } from './fakeApi'

const WITH_MESH = { providers: [DEFAULT_PROVIDER, MESH_PROVIDER] }

async function openSceneTab(page: Page, options: Parameters<typeof installFakeApi>[1]) {
  await installFakeApi(page, options)
  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('run-result')).toBeVisible()
  await page.getByTestId('tab-scene').click()
  await expect(page.getByTestId('scene-assembly')).toBeVisible()
}

test.describe('원본 유사도', () => {
  /** F-01 — 시작 전: 모델·반복·최대 호출·예상 비용이 먼저 보인다 (§11.2). */
  test('F-01 시작 전에 비용과 호출 수를 보여준다', async ({ page }) => {
    await openSceneTab(page, WITH_MESH)

    await expect(page.getByTestId('similarity-inspector')).toBeVisible()
    await expect(page.getByTestId('similarity-model')).toBeVisible()
    await expect(page.getByTestId('similarity-estimate')).toContainText('최대 평가 2회')
    await expect(page.getByTestId('similarity-estimate')).toContainText('$')
  })

  /** F-02 — 캡처 준비 후 실행 → 평가 중 → 여섯 축 점수와 overall (§14.2). */
  test('F-02 기준 평가가 여섯 축 점수로 돌아온다', async ({ page }) => {
    await openSceneTab(page, WITH_MESH)

    // 캡처 손잡이가 올라와야 버튼이 산다 (§8.2) — GLTF 로드 + 첫 프레임 뒤
    const start = page.getByTestId('similarity-start')
    await expect(start).toBeEnabled()
    await start.click()

    // 평가 중 상태를 지나 결과가 나온다 — 폴링이 실제 워커 상태를 따른다
    await expect(page.getByTestId('similarity-status')).toBeVisible()
    await expect(page.getByTestId('similarity-overall')).toHaveText(/68/)
    await expect(page.getByTestId('similarity-dimension')).toHaveCount(6)
  })

  /** F-03 — 보정 카드: confidence 0.75 이상만 기본 선택, 재생성 노트는 분리 (§6·§14.1). */
  test('F-03 보정 제안의 기본 선택과 자동 적용 불가 분리', async ({ page }) => {
    await openSceneTab(page, WITH_MESH)
    await page.getByTestId('similarity-start').click()
    await expect(page.getByTestId('similarity-overall')).toBeVisible()

    const adjustments = page.getByTestId('similarity-adjustment')
    await expect(adjustments).toHaveCount(2)
    // confidence 0.85 → 기본 선택, 0.6 → 해제 상태
    await expect(adjustments.nth(0).locator('input')).toBeChecked()
    await expect(adjustments.nth(1).locator('input')).not.toBeChecked()

    await expect(page.getByTestId('similarity-notes')).toContainText('자동 적용 불가')
  })

  /** F-04 — 자격 미충족: 3D 없는 파츠가 있으면 사유가 보이고 시작이 없다 (§11.1). */
  test('F-04 자격 미충족 사유가 버튼을 대신한다', async ({ page }) => {
    await installFakeApi(page, { ...WITH_MESH, meshOutcome: 'mixed' })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()
    await page.getByTestId('tab-scene').click()

    await expect(page.getByTestId('similarity-blocking')).toContainText('3D 가 없는 파츠')
    await expect(page.getByTestId('similarity-start')).toHaveCount(0)
  })
})

test.describe('후보 보정', () => {
  /** F-05 — 보정 적용 → 후보 캡처·업로드 → 재평가 → 채택 (§9.2). */
  test('F-05 선택한 보정이 후보로 평가되고 채택된다', async ({ page }) => {
    await openSceneTab(page, WITH_MESH)
    await page.getByTestId('similarity-start').click()
    await expect(page.getByTestId('similarity-overall')).toHaveText(/68/)

    // 기본 선택(confidence 0.85)이 있으니 바로 적용 가능하다
    const apply = page.getByTestId('similarity-apply')
    await expect(apply).toBeEnabled()
    await apply.click()

    // 후보 렌더 캡처·업로드를 지나 채택 결과 — 활성 revision 이 후보로 교대된다
    await expect(page.getByTestId('similarity-settled')).toContainText('개선이 채택', {
      timeout: 20_000,
    })
    await expect(page.getByTestId('similarity-overall')).toHaveText(/74/)

    // 다음 비교를 다시 시작할 수 있다
    await page.getByTestId('similarity-reset').click()
    await expect(page.getByTestId('similarity-start')).toBeVisible()
  })
})

test.describe('실행 생명주기와 이력', () => {
  /** F-06 — "이 결과로 마치기" (§14.2·§9.3): 보정을 적용하지 않고 현재 결과로 끝낸다. */
  test('F-06 현재 결과로 종료하면 이전 장면이 유지된다', async ({ page }) => {
    await openSceneTab(page, WITH_MESH)
    await page.getByTestId('similarity-start').click()
    await expect(page.getByTestId('similarity-overall')).toBeVisible()

    await page.getByTestId('similarity-complete').click()

    await expect(page.getByTestId('similarity-settled')).toContainText('이전 장면 유지됨')
    // 채택이 없었으니 점수는 기준 평가의 것이다
    await expect(page.getByTestId('similarity-overall')).toHaveText(/68/)
  })

  /** F-07 — 원본/렌더 비교 슬라이더 (§14.1): 캡처가 있는 세션에서만, native range 로. */
  test('F-07 비교 슬라이더가 키보드 접근 가능한 range 로 뜬다', async ({ page }) => {
    await openSceneTab(page, WITH_MESH)
    await page.getByTestId('similarity-start').click()
    await expect(page.getByTestId('similarity-overall')).toBeVisible()

    const compare = page.getByTestId('similarity-compare')
    await expect(compare).toBeVisible()
    const range = compare.getByRole('slider', { name: '원본과 렌더 비교 위치' })
    await expect(range).toBeVisible()
    // native range — 키보드 화살표가 그대로 동작한다 (§14.3)
    await range.focus()
    await page.keyboard.press('ArrowLeft')
    await expect(range).toHaveValue('49')
  })

  /** F-08 — 배치 이력·복원 (§14.1·§4.3): 채택 뒤 이전 revision 을 복원할 수 있다. */
  test('F-08 채택 뒤 이전 revision 을 복원한다', async ({ page }) => {
    await openSceneTab(page, WITH_MESH)
    await page.getByTestId('similarity-start').click()
    await expect(page.getByTestId('similarity-overall')).toBeVisible()
    await page.getByTestId('similarity-apply').click()
    await expect(page.getByTestId('similarity-settled')).toContainText('개선이 채택', {
      timeout: 20_000,
    })

    // 이력을 펼친다 — 채택으로 r2(사용 중) + r1(대체됨)이 있다
    await page.getByTestId('similarity-history').locator('summary').click()
    const rows = page.getByTestId('similarity-revision')
    await expect(rows).toHaveCount(2)
    await expect(rows.first()).toContainText('사용 중')

    // 대체된 r1 을 복원 — 새 활성 revision 이 맨 위에 선다 (§4.3 값 복사)
    await page.getByTestId('similarity-restore').first().click()
    await expect(rows).toHaveCount(3)
    await expect(rows.first()).toContainText('복원')
    await expect(rows.first()).toContainText('사용 중')
  })
})
