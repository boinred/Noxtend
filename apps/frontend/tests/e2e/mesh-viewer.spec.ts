/**
 * Design Ref: §8.2 — 3D 결과를 크게 보고, 돌려 보고, 골라 받는다.
 *
 * **렌더링된 픽셀을 보지 않는다.** headless 브라우저의 WebGL 은 환경마다 있고 없고,
 * 있어도 소프트웨어 래스터라 실물과 다르다. 여기서 보는 것은 **계약**이다 —
 * 무엇이 어떤 주소를 가리키고, 어떤 상태가 어떤 문장으로 나오는가.
 *
 * 실제로 그려지는지는 사람이 본다 (Plan 성공 기준 마지막).
 */
import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import {
  DEFAULT_PROVIDER,
  MESHY_PROVIDER,
  MESH_PROVIDER,
  installFakeApi,
  selectImage,
} from './fakeApi'

const WITH_MESH = { providers: [DEFAULT_PROVIDER, MESH_PROVIDER] }
const WITH_MESHY = { providers: [DEFAULT_PROVIDER, MESHY_PROVIDER], meshProducesFbx: true }

/** 3D 결과가 붙은 결과 화면까지 간다. */
async function runToResult(page: Page, options: Parameters<typeof installFakeApi>[1]) {
  await installFakeApi(page, options)
  await page.goto('/background')
  await selectImage(page)
  // 3D 팬아웃 opt-in — 기본이 꺼짐이라 켜야 접수에 3D 선택이 실린다
  await page.getByTestId('produces-meshes-checkbox').check()
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('run-result')).toBeVisible()
}

test.describe('3D 뷰어', () => {
  test('F-01 3D 타일이 방향 이미지 한 칸과 같은 크기다', async ({ page }) => {
    await runToResult(page, WITH_MESH)

    const view = await page.getByTestId('part-view').first().boundingBox()
    const tile = await page.getByTestId('mesh-tile').first().boundingBox()

    expect(view).not.toBeNull()
    expect(tile).not.toBeNull()

    // 64px 고정이던 시절에는 가장 비싼 산출물이 가장 작게 보였고, 칸을 채우게 하니
    // 이번에는 혼자 커 보였다. **두 타일이 같은 크기로 서야 줄이 가지런하다.**
    //
    // 1px 여유는 소수점 반올림 — 그보다 벌어지면 격자 비율이나 간격 보정이 어긋난 것이다
    expect(Math.abs(tile!.width - view!.width)).toBeLessThanOrEqual(1)
    expect(Math.abs(tile!.height - view!.height)).toBeLessThanOrEqual(1)
  })

  test('F-02 타일을 눌러 뷰어가 열린다', async ({ page }) => {
    await runToResult(page, WITH_MESH)

    await page.getByTestId('mesh-tile').first().click()

    await expect(page.getByTestId('mesh-viewer-dialog')).toBeVisible()
  })

  test('F-03 키보드로도 열린다', async ({ page }) => {
    await runToResult(page, WITH_MESH)

    const tile = page.getByTestId('mesh-tile').first()
    await tile.focus()
    await page.keyboard.press('Enter')

    await expect(page.getByTestId('mesh-viewer-dialog')).toBeVisible()
  })

  test('F-04 뷰어가 그 파츠의 GLB endpoint 를 가리킨다', async ({ page }) => {
    await runToResult(page, WITH_MESH)
    await page.getByTestId('mesh-tile').first().click()

    const model = page.getByTestId('mesh-viewer-model')

    // **custom element 의 속성이 조용히 안 실릴 수 있다** — React 가 그것을 어떻게
    // 넘기는지에 기대지 않고 실제 DOM 속성을 읽는다
    await expect(model).toHaveAttribute('src', /\/api\/generated-meshes\/[0-9a-z-]+$/)

    // model-viewer 는 보일 때만 모델을 받는다. 이것이 없으면 조용히 빈 화면이다
    await expect(model).toHaveAttribute('loading', 'eager')
  })

  test('F-05 목록에는 내려받기가 없다 — 받기 전에 결과를 본다', async ({ page }) => {
    await runToResult(page, WITH_MESH)

    // 행마다 누를 것이 둘이면 목록이 시끄럽다. 타일 하나가 들어가는 문이다
    await expect(page.getByTestId('mesh-download-menu')).toHaveCount(0)

    await page.getByTestId('mesh-tile').first().click()
    await expect(page.getByTestId('mesh-viewer-dialog')).toBeVisible()

    await page.getByTestId('mesh-download-menu').click()
    await expect(page.getByTestId('mesh-download-glb')).toBeVisible()

    // Tripo 결과다 — 없는 것을 비활성으로 보여 주면 "곧 생긴다" 로 읽힌다
    await expect(page.getByTestId('mesh-download-fbx')).toHaveCount(0)
  })

  test('F-06 뷰어 안에도 같은 메뉴가 있다', async ({ page }) => {
    await runToResult(page, WITH_MESHY)
    await page.getByTestId('mesh-tile').first().click()

    const dialog = page.getByTestId('mesh-viewer-dialog')
    await expect(dialog).toBeVisible()

    // 돌려 보다 마음에 든 순간 닫고 다시 찾지 않아도 된다 (D-05)
    await dialog.getByTestId('mesh-download-menu').click()

    await expect(page.getByTestId('mesh-download-glb')).toBeVisible()
    await expect(page.getByTestId('mesh-download-fbx')).toBeVisible()
  })

  test('F-07 Esc 로 닫히고 3D 요소가 사라진다', async ({ page }) => {
    await runToResult(page, WITH_MESH)
    await page.getByTestId('mesh-tile').first().click()
    await expect(page.getByTestId('mesh-viewer-model')).toHaveCount(1)

    await page.keyboard.press('Escape')

    // **감추는 것으로는 부족하다** — WebGL 컨텍스트가 살아 있으면 여닫을수록 탭이
    // 무거워진다. 요소 자체가 사라져야 한다 (NFR-03)
    await expect(page.getByTestId('mesh-viewer-dialog')).toHaveCount(0)
    await expect(page.getByTestId('mesh-viewer-model')).toHaveCount(0)
  })

  test('F-08 미리보기가 없는 결과도 타일을 누를 수 있다', async ({ page }) => {
    await runToResult(page, { ...WITH_MESH, meshHasPreview: false })

    const tile = page.getByTestId('mesh-tile').first()
    await expect(tile).toBeVisible()

    await tile.click()
    await expect(page.getByTestId('mesh-viewer-dialog')).toBeVisible()
  })

  test('F-09 WebGL 이 없으면 문장이 나오고 내려받기는 남는다', async ({ page }) => {
    // **headless 의 WebGL 유무에 기대지 않고 우리가 끈다.** `hasWebgl()` 이
    // `getContext` 하나만 보는 이유가 이것이다 — 끌 수 있는 지점이 하나다
    await page.addInitScript(() => {
      const original = HTMLCanvasElement.prototype.getContext

      function patched(this: HTMLCanvasElement, type: string, ...rest: unknown[]): unknown {
        return /webgl/i.test(String(type))
          ? null
          : (original as (...args: unknown[]) => unknown).call(this, type, ...rest)
      }

      HTMLCanvasElement.prototype.getContext =
        patched as unknown as typeof HTMLCanvasElement.prototype.getContext
    })

    await runToResult(page, WITH_MESH)
    await page.getByTestId('mesh-tile').first().click()

    // 빈 사각형을 보여주면 결과가 깨진 것으로 읽힌다 (D-07)
    await expect(page.getByTestId('mesh-viewer-unsupported')).toBeVisible()
    await expect(page.getByTestId('mesh-viewer-model')).toHaveCount(0)

    // **볼 수 없는 것과 받을 수 없는 것은 다르다**
    await page.getByTestId('mesh-viewer-dialog').getByTestId('mesh-download-menu').click()
    await expect(page.getByTestId('mesh-download-glb')).toBeVisible()
  })

  test('F-10 FBX 가 있으면 뷰어에서 못 본다고 적는다', async ({ page }) => {
    await runToResult(page, WITH_MESHY)
    await page.getByTestId('mesh-tile').first().click()

    // 없으면 "왜 FBX 는 안 보이지" 를 묻게 된다 (D-12)
    await expect(page.getByTestId('mesh-viewer-dialog')).toContainText('FBX 는 이 뷰어에서')
  })
})
