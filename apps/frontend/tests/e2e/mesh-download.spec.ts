/**
 * Design Ref: download-view-consistency §2 — 뷰어 안 내려받기 수복.
 *
 * **실클릭과 download 이벤트로 검증한다.** 기존 검사는 href 계약만 봤고, 항목이
 * DOM 에 있어도 네이티브 dialog 의 최상위 레이어 **아래**에 깔려 있으면 못 잡았다 —
 * `dispatchEvent` 는 가림을 뚫고 지나가므로 여기서는 금지다. 실클릭은 가려진 요소에서
 * "다른 요소가 가로챈다" 로 실패한다. 그것이 이 버그의 실제 증상이다.
 */
import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { DEFAULT_PROVIDER, MESH_PROVIDER, installFakeApi, selectImage } from './fakeApi'

const WITH_MESH = { providers: [DEFAULT_PROVIDER, MESH_PROVIDER], meshProducesFbx: true }

/** 3D 뷰어가 열린 상태까지 간다. */
async function openViewer(page: Page) {
  await installFakeApi(page, WITH_MESH)
  await page.goto('/background')
  await selectImage(page)
  // 3D 팬아웃 opt-in — 기본이 꺼짐이라 켜야 접수에 3D 선택이 실린다
  await page.getByTestId('produces-meshes-checkbox').check()
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('run-result')).toBeVisible()

  await page.getByTestId('mesh-tile').first().click()
  await expect(page.getByTestId('mesh-viewer-dialog')).toBeVisible()
}

test.describe('뷰어 내려받기', () => {
  /** F-01 · SC-01 — 메뉴가 펼쳐지고 항목이 실클릭 가능하다. */
  test('F-01 메뉴 항목이 실제로 열리고 GLB 다운로드가 일어난다', async ({ page }) => {
    await openViewer(page)

    await page.getByTestId('mesh-download-menu').click()

    // 실클릭 — 최상위 레이어 아래 깔려 있으면 여기서 가로채기로 실패한다
    const download = page.waitForEvent('download', { timeout: 10000 })
    await page.getByTestId('mesh-download-glb').click()

    // 다운로드 내비게이션은 라우트 가로채기를 우회한다 — 파일명(Content-Disposition)은
    // 실서버 계약이라 Plan §1 의 curl 실측이 정본이고, 여기서는 주소가 맞는지 본다
    const file = await download
    expect(file.url()).toMatch(/\/api\/generated-meshes\/[^/]+$/)
  })

  /** SC-01 — FBX 도 같은 길. Meshy 결과에만 있으므로 producesFbx 가짜로 연다. */
  test('F-02 FBX 다운로드가 일어난다', async ({ page }) => {
    await openViewer(page)

    await page.getByTestId('mesh-download-menu').click()

    const download = page.waitForEvent('download', { timeout: 10000 })
    await page.getByTestId('mesh-download-fbx').click()

    expect((await download).url()).toMatch(/\/fbx$/)
  })

  /**
   * F-03 · SC-02 — **Esc 는 메뉴부터 닫는다.** 메뉴가 열린 채 Esc 한 번에
   * 다이얼로그까지 닫히면, 형식을 고르다 취소한 사람이 뷰어 밖으로 튕긴다.
   */
  test('F-03 메뉴 열림 중 Esc 는 메뉴만, 다음 Esc 가 다이얼로그를 닫는다', async ({ page }) => {
    await openViewer(page)

    await page.getByTestId('mesh-download-menu').click()
    await expect(page.getByTestId('mesh-download-glb')).toBeVisible()

    await page.keyboard.press('Escape')
    await expect(page.getByTestId('mesh-download-glb')).toHaveCount(0)
    await expect(page.getByTestId('mesh-viewer-dialog')).toBeVisible()

    await page.keyboard.press('Escape')
    await expect(page.getByTestId('mesh-viewer-dialog')).toHaveCount(0)
  })

  /** F-04 · FR-03 — 메뉴 항목 클릭이 다이얼로그를 닫으면 안 된다 (바깥 클릭 판정 오발동). */
  test('F-04 다운로드 후에도 뷰어가 열려 있다', async ({ page }) => {
    await openViewer(page)

    await page.getByTestId('mesh-download-menu').click()
    const download = page.waitForEvent('download', { timeout: 10000 })
    await page.getByTestId('mesh-download-glb').click()
    await download

    await expect(page.getByTestId('mesh-viewer-dialog')).toBeVisible()
  })
})
