/**
 * Design Ref: scene-assembly §8.3 — 분석/3D 배경 탭과 조립 뷰어.
 *
 * **렌더링된 픽셀을 보지 않는다** (mesh-viewer 와 같은 이유) — Canvas 내부는 DOM 이
 * 아니다. 여기서 보는 것은 계약이다: 탭이 무엇을 켜고, 요약이 몇을 세고, 이름표가
 * 몇 개 뜨는가. 실제로 그려지는지는 사람이 본다 (SC-08).
 */
import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { DEFAULT_PROVIDER, MESH_PROVIDER, installFakeApi, selectImage } from './fakeApi'

const WITH_MESH = { providers: [DEFAULT_PROVIDER, MESH_PROVIDER] }

/** 3D 결과가 붙은 결과 화면까지 간다 — mesh-viewer 와 같은 길. */
async function runToResult(page: Page, options: Parameters<typeof installFakeApi>[1]) {
  await installFakeApi(page, options)
  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('run-result')).toBeVisible()
}

test.describe('3D 배경 조립', () => {
  /** F-01 · SC-04 — 탭이 전환되고, 분석 탭 내용은 기존 그대로다. */
  test('F-01 분석과 3D 배경을 오간다', async ({ page }) => {
    await runToResult(page, WITH_MESH)

    // 분석 탭이 기본 — 기존 결과 화면이 그대로 보인다
    await expect(page.getByTestId('tab-analysis')).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByTestId('run-result')).toBeVisible()

    await page.getByTestId('tab-scene').click()
    await expect(page.getByTestId('scene-assembly')).toBeVisible()
    await expect(page.getByTestId('run-result')).toHaveCount(0)

    // 돌아오면 분석 내용이 그대로다
    await page.getByTestId('tab-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()
  })

  /**
   * F-10 — 유도가 내린 판단을 화면이 말한다 (background-scale-calibration #18 FR-07).
   *
   * 접지로 못 본 배치가 있으면 그 크기를 대표 깊이로 잡았다는 뜻이라, 운영자가
   * "왜 이 크기인가" 를 되짚을 근거가 된다.
   */
  test('F-10 고도 미상 배치 수를 요약이 보여준다', async ({ page }) => {
    await runToResult(page, WITH_MESH)
    await page.getByTestId('tab-scene').click()

    await expect(page.getByTestId('scene-composition')).toContainText('고도 미상 1 / 5 배치')

    // 기준 편차가 정상 범위(1.2배)면 경고를 띄우지 않는다 — 늘 켜져 있으면 아무도 안 본다
    await expect(page.getByTestId('scene-anchor-spread')).toHaveCount(0)
  })

  /**
   * F-11 — 크기 기준이 흔들리면 화면이 말한다 (#18 D-05 · SC-06).
   *
   * 같은 물체를 두 자리에서 재는데 크게 갈리면 보정 계수가 그 사이 임의 지점에 찍힌다는
   * 뜻이다 — 실측 유적 광장에서 3.9배가 나왔다. 경고가 *켜지는* 쪽을 잠근다.
   */
  test('F-11 크기 기준 편차가 크면 경고가 켜진다', async ({ page }) => {
    await runToResult(page, { ...WITH_MESH, anchorSpread: 3.9 })
    await page.getByTestId('tab-scene').click()

    await expect(page.getByTestId('scene-anchor-spread')).toContainText('3.9배')
  })

  /**
   * F-12 — 면을 덮는 파츠를 화면이 구분해 말한다 (background-surface-parts #20 FR-07).
   *
   * 표면은 배치 사각형이 곧 크기라 낱개와 규칙이 다르다. 그 사실이 화면에 드러나지
   * 않으면 운영자가 "왜 이 포장만 납작한가" 를 되짚을 근거가 없다.
   */
  test('F-12 표면 배치 수를 요약이 보여준다', async ({ page }) => {
    await runToResult(page, WITH_MESH)
    await page.getByTestId('tab-scene').click()

    // 표면이 없으면 배지를 띄우지 않는다 — 늘 켜져 있으면 아무도 안 본다
    await expect(page.getByTestId('scene-surface')).toHaveCount(0)

    await runToResult(page, { ...WITH_MESH, surfaceParts: true })
    await page.getByTestId('tab-scene').click()

    // 가짜 서버: 첫 파츠가 표면이고 배치 셋이다
    await expect(page.getByTestId('scene-surface')).toContainText('표면 3 배치')

    // 축별 배율이 살아 있어야 뷰어가 눕힌다 — 붕괴하면 3D 화면이 아예 안 뜬다
    await expect(page.getByTestId('scene-summary')).toHaveText('파츠 4 · 인스턴스 6')
  })

  /**
   * F-13 — 크기 기준 물체가 표면이면 화면이 말한다 (#20 §6).
   *
   * 표면의 세로 범위를 높이로 읽어 보정 계수를 뽑으면 장면 전체가 어긋난다. 막지 않고
   * 알리는 쪽을 골랐으므로, 그 알림이 실제로 화면에 닿는지를 잠근다.
   */
  test('F-13 기준 물체가 표면이면 경고가 켜진다', async ({ page }) => {
    await runToResult(page, WITH_MESH)
    await page.getByTestId('tab-scene').click()
    await expect(page.getByTestId('scene-anchor-surface')).toHaveCount(0)

    await runToResult(page, { ...WITH_MESH, anchorIsSurface: true })
    await page.getByTestId('tab-scene').click()

    await expect(page.getByTestId('scene-anchor-surface')).toContainText(
      '보정 계수를 믿을 수 없습니다',
    )
  })

  /**
   * F-14 — 상한이 버린 배치와 형태 어긋남을 화면이 말한다 (#20 FR-06 · §4.1).
   *
   * 상한에 걸리면 인스턴스가 배치보다 적어지는데, 그 이유가 화면에 없으면 운영자는
   * 3D 가 빠진 것으로 읽는다. 형태 어긋남은 표시를 뒤집지 않고 의심할 근거만 준다.
   */
  test('F-14 버려진 배치와 형태 어긋남을 요약이 보여준다', async ({ page }) => {
    await runToResult(page, WITH_MESH)
    await page.getByTestId('tab-scene').click()

    // 평소에는 둘 다 켜지지 않는다
    await expect(page.getByTestId('scene-dropped')).toHaveCount(0)
    await expect(page.getByTestId('scene-shape-mismatch')).toHaveCount(0)

    await runToResult(page, { ...WITH_MESH, droppedCount: 7, surfaceShapeMismatch: 2 })
    await page.getByTestId('tab-scene').click()

    await expect(page.getByTestId('scene-dropped')).toContainText('배치 7개가 상한에 걸려')
    await expect(page.getByTestId('scene-shape-mismatch')).toContainText('바닥 표시 2개')
  })

  /** F-02 · SC-05 — 인스턴스 수가 배치 수와 같다. 첫 파츠는 배치 셋이다. */
  test('F-02 배치 수만큼 인스턴스를 센다', async ({ page }) => {
    await runToResult(page, WITH_MESH)
    await page.getByTestId('tab-scene').click()

    // 가짜 서버: 파츠 4 전부 GLB — 배치 3+1+1+1 = 인스턴스 6
    await expect(page.getByTestId('scene-summary')).toHaveText('파츠 4 · 인스턴스 6')
  })

  /** F-03 · SC-06 — 이름표 토글. drei Html 은 DOM 이라 셀 수 있다. */
  test('F-03 이름표를 켜고 끈다', async ({ page }) => {
    await runToResult(page, WITH_MESH)
    await page.getByTestId('tab-scene').click()
    await expect(page.getByTestId('scene-assembly')).toBeVisible()

    // 기본은 꺼짐
    await expect(page.getByTestId('scene-label')).toHaveCount(0)

    await page.getByTestId('scene-labels-toggle').click()
    await expect(page.getByTestId('scene-label')).toHaveCount(6)

    await page.getByTestId('scene-labels-toggle').click()
    await expect(page.getByTestId('scene-label')).toHaveCount(0)
  })

  /** F-04 · FR-04 — 완성 GLB 가 없으면 탭이 비활성이다. 빈 캔버스는 실패로 읽힌다. */
  test('F-04 3D 가 없으면 탭이 잠긴다', async ({ page }) => {
    // 3D 가 하나도 완성되지 않은 작업
    await installFakeApi(page, { ...WITH_MESH, meshOutcome: 'none' })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    await expect(page.getByTestId('tab-scene')).toBeDisabled()
  })

  /** F-05 · §7.3-7 — 탭 상태가 URL 에 산다. 새로고침에 살아남는다. */
  test('F-05 ?view=scene 이 새로고침 후에도 유지된다', async ({ page }) => {
    await runToResult(page, WITH_MESH)
    await page.getByTestId('tab-scene').click()

    await expect(page).toHaveURL(/view=scene/)

    await page.reload()
    await expect(page.getByTestId('scene-assembly')).toBeVisible()
    await expect(page.getByTestId('tab-scene')).toHaveAttribute('aria-selected', 'true')
  })

  /** FR-08 — GLB 없는 파츠는 빠지고, 빠졌다고 말한다. */
  test('F-06 빠진 파츠를 화면이 말한다', async ({ page }) => {
    await runToResult(page, { ...WITH_MESH, meshOutcome: 'mixed' })
    await page.getByTestId('tab-scene').click()

    // mixed: 첫 파츠만 성공 — 나머지 셋이 빠졌다고 말해야 한다
    await expect(page.getByTestId('scene-missing')).toContainText('3D 없음 3')
  })

  /**
   * F-07 · FR-05 — **뷰포트가 실제로 크게 그려진다.**
   *
   * 높이 클래스가 R3F Canvas 의 인라인 height:100% 에 눌려 한 번도 적용된 적이
   * 없었다 — 스타일이 있다는 것과 그 스타일이 실제로 그려진다는 것은 다르다.
   * 실측으로 못박는다.
   */
  test('F-07 3D 뷰포트가 남은 화면 높이를 쓴다', async ({ page }) => {
    await runToResult(page, WITH_MESH)
    await page.getByTestId('tab-scene').click()

    const box = await page.getByTestId('scene-assembly').boundingBox()

    // 900px 뷰포트 기준: 100dvh - 240px = 660. 스크롤·테두리 여유로 620 이상을 요구한다
    expect(box).not.toBeNull()
    expect(box!.height).toBeGreaterThanOrEqual(620)
  })

  /**
   * F-08 — **클릭해 지목하면 나머지가 물러난다.**
   *
   * 호버 즉발은 마우스가 지나갈 때마다 번쩍여 눈이 아프다(실측 피드백) — 호버는
   * 이름표 색 예고만, 효과는 클릭으로 확정한다. 같은 것을 다시 클릭하면 풀린다.
   * Canvas 안 재질은 셀 수 없으므로 이름표의 상태 속성으로 검증한다.
   */
  test('F-08 이름표를 클릭하면 나머지가 물러나고, 다시 클릭하면 풀린다', async ({ page }) => {
    await runToResult(page, WITH_MESH)
    await page.getByTestId('tab-scene').click()
    await page.getByTestId('scene-labels-toggle').click()

    const labels = page.getByTestId('scene-label')
    await expect(labels).toHaveCount(6)

    // 평소에는 아무것도 흐려지지 않는다 — 호버만으로는 효과가 확정되지 않는다
    await expect(page.locator('[data-testid="scene-label"][data-dimmed="true"]')).toHaveCount(0)

    // 이름표가 화면에서 겹칠 수 있어 실마우스 대신 이벤트를 직접 쏜다 —
    // 검증 대상은 "클릭 → 지목 → 나머지 흐림" 배선이지 겹침 우선순위가 아니다
    await labels.first().dispatchEvent('click')

    await expect(page.locator('[data-testid="scene-label"][data-dimmed="true"]')).toHaveCount(5)
    await expect(labels.first()).toHaveAttribute('data-focused', 'true')

    // 같은 것을 다시 클릭하면 풀린다
    await labels.first().dispatchEvent('click')
    await expect(page.locator('[data-testid="scene-label"][data-dimmed="true"]')).toHaveCount(0)
    await expect(labels.first()).toHaveAttribute('data-focused', 'false')
  })

  /**
   * F-09 — **탭을 아무리 오가도 WebGL 컨텍스트가 쌓이지 않는다.**
   *
   * 언마운트가 컨텍스트를 즉시 반납하지 않으면 GC 를 기다리는 좀비가 쌓이고,
   * 브라우저 상한(~16)에서 가장 오래된 것이 강제로 죽는다 — 그게 지금 보고 있는
   * 캔버스면 화면이 죽는다 (실측: 왕복 5회 + 뷰어 8회에서 재현).
   */
  test('F-09 탭 왕복이 WebGL 컨텍스트를 쌓지 않는다', async ({ page }) => {
    const contextWarnings: string[] = []
    page.on('console', (message) => {
      // "Context Lost." 로그는 잡지 않는다 — 언마운트 시 우리가 의도적으로 반납한
      // 흔적이다. 증상은 누적이 상한에 닿았다는 경고 쪽이다
      if (/Too many active WebGL contexts/i.test(message.text())) {
        contextWarnings.push(message.text())
      }
    })

    await runToResult(page, WITH_MESH)

    for (let cycle = 0; cycle < 18; cycle++) {
      await page.getByTestId('tab-scene').click()
      await expect(page.getByTestId('scene-assembly')).toBeVisible()
      await page.getByTestId('tab-analysis').click()
      await expect(page.getByTestId('run-result')).toBeVisible()
    }

    expect(contextWarnings).toEqual([])
  })
})
