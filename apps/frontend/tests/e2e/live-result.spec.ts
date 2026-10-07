/**
 * Design Ref: live-result §6 — 완성 전에 열리는 결과 화면.
 *
 * 진행 화면과 결과 화면의 경계, 부분 산출물, 취소, 고정된 파츠 자리를 검증한다.
 */
import { expect, test } from '@playwright/test'
import { DEFAULT_PROVIDER, installFakeApi, MESH_PROVIDER, selectImage } from './fakeApi'

const LIVE_PROVIDERS = [DEFAULT_PROVIDER, MESH_PROVIDER]

test('파츠가 생긴 진행 작업은 부분 결과와 취소 동작을 보여준다', async ({ page }) => {
  await installFakeApi(page, {
    providers: LIVE_PROVIDERS,
    liveResult: true,
    runningPolls: 50,
  })
  await page.goto('/background')
  await selectImage(page)
  // 3D 팬아웃 opt-in — 기본이 꺼짐이라 켜야 접수에 3D 선택이 실린다
  await page.getByTestId('produces-meshes-checkbox').check()
  await page.getByTestId('start-analysis').click()

  await expect(page.getByTestId('run-result')).toBeVisible()
  await expect(page.getByTestId('live-action-bar')).toContainText('이미지 8/16')
  await expect(page.getByTestId('live-action-bar')).toContainText('3D 1/4')
  await expect(page.getByTestId('part-image')).toHaveCount(8)
  await expect(page.getByTestId('run-progress')).toHaveCount(0)
  await expect(page.getByTestId('restart-analysis')).toHaveCount(0)

  // 대기와 실행은 정적인 한 단어가 아니라 서로 다른 활동 상태로 읽힌다
  const drawing = page.locator('[data-testid="part-pending"][data-activity="running"]')
  const queued = page.locator('[data-testid="part-pending"][data-activity="queued"]')
  await expect(drawing).toHaveCount(1)
  await expect(drawing).toContainText('그리는 중')
  await expect(queued).toHaveCount(7)
  await expect(queued.first()).toContainText('대기열')

  const runningMesh = page.locator('[data-testid="mesh-status-card"][data-activity="running"]')
  const queuedMeshes = page.locator('[data-testid="mesh-status-card"][data-activity="queued"]')
  await expect(runningMesh).toHaveAttribute('aria-busy', 'true')
  await expect(queuedMeshes).toHaveCount(2)

  // 진행 중 결과 화면의 유료 작업 중단 경로
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: '작업 취소', exact: true }).click()
  await expect(page).toHaveURL(/\/background\?image=/)
  await expect(page.getByTestId('live-action-bar')).toHaveCount(0)
})

test('파츠가 없는 진행 작업은 기존 진행 화면을 유지한다', async ({ page }) => {
  await installFakeApi(page, { runningPolls: 50 })
  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('start-analysis').click()

  await expect(page.getByTestId('run-progress')).toBeVisible()
  await expect(page.getByTestId('run-result')).toHaveCount(0)
  await expect(page.getByTestId('live-action-bar')).toHaveCount(0)
})

test('3D 결과가 도착해도 보고 있던 파츠 위치가 유지된다', async ({ page }) => {
  await installFakeApi(page, {
    providers: LIVE_PROVIDERS,
    liveResult: true,
    runningPolls: 50,
  })
  await page.goto('/background')
  await selectImage(page)
  // 3D 팬아웃 opt-in — 기본이 꺼짐이라 켜야 접수에 3D 선택이 실린다
  await page.getByTestId('produces-meshes-checkbox').check()
  await page.getByTestId('start-analysis').click()

  await expect(page.getByTestId('mesh-tile')).toHaveCount(1)
  const row = page.getByTestId('part-row').nth(2)
  await row.scrollIntoViewIfNeeded()

  // 같은 크기의 3D 자리 교체 전 위치
  const beforeScroll = await page.evaluate(() => window.scrollY)
  const beforeTop = (await row.boundingBox())!.y

  await expect(page.getByTestId('mesh-tile')).toHaveCount(2, { timeout: 10_000 })

  const afterScroll = await page.evaluate(() => window.scrollY)
  const afterTop = (await row.boundingBox())!.y
  expect(Math.abs(afterScroll - beforeScroll)).toBeLessThanOrEqual(2)
  expect(Math.abs(afterTop - beforeTop)).toBeLessThanOrEqual(2)
})

test('완료된 결과는 진행 액션 없이 기존 동작을 유지한다', async ({ page }) => {
  await installFakeApi(page)
  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('start-analysis').click()

  await expect(page.getByTestId('run-result')).toBeVisible()
  await expect(page.getByTestId('live-action-bar')).toHaveCount(0)
  await expect(page.getByTestId('restart-analysis')).toBeEnabled()
})

test('동작 줄이기 환경에서는 상태 모션을 멈춘다', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await installFakeApi(page, {
    providers: LIVE_PROVIDERS,
    liveResult: true,
    runningPolls: 50,
  })
  await page.goto('/background')
  await selectImage(page)
  // 3D 팬아웃 opt-in — 기본이 꺼짐이라 켜야 접수에 3D 선택이 실린다
  await page.getByTestId('produces-meshes-checkbox').check()
  await page.getByTestId('start-analysis').click()

  const activity = page.locator('[data-testid="status-activity"][data-activity="running"]').first()
  await expect(activity).toBeVisible()
  const animationName = await activity.evaluate(
    (element) => getComputedStyle(element).animationName,
  )
  expect(animationName).toBe('none')
})
