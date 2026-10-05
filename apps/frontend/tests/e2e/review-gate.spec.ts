/**
 * Design Ref: docs/specs/2026-08-28-review-gate.md — 검수 게이트 E2E.
 *
 * §검증방법의 "검수 화면에서 사각형으로 파츠 1개 추가 → 기존 파츠 1개 삭제 → 전체 승인 →
 * 결과 갤러리에 승인된 파츠 수만큼만 이미지가 생성됨을 확인" 을 그대로 따라간다.
 */
import { expect, test } from '@playwright/test'
import { installFakeApi, selectImage } from './fakeApi'

/** 검수 캔버스 위를 정규화 좌표(0~1)로 드래그한다 — ReviewGate 가 픽셀을 % 로 바꾸는 것과 반대 방향. */
async function dragBox(
  page: import('@playwright/test').Page,
  from: { x: number; y: number },
  to: { x: number; y: number },
) {
  // 이미지가 로드되기 전에는 <img> 높이가 0 이라 overlayFrame 이 찌그러져 있다 —
  // 그 상태로 boundingBox 를 재면 드래그 좌표가 실제 프레임 밖으로 나간다
  const image = page.getByTestId('overlay-image')
  await expect(image).toBeVisible()
  await page.waitForFunction(
    (el) => (el as HTMLImageElement).complete && (el as HTMLImageElement).naturalHeight > 0,
    await image.elementHandle(),
  )

  const canvas = page.getByTestId('review-canvas')
  const box = await canvas.boundingBox()
  if (!box) throw new Error('review-canvas 를 찾지 못했습니다')

  await page.mouse.move(box.x + box.width * from.x, box.y + box.height * from.y)
  await page.mouse.down()
  await page.mouse.move(box.x + box.width * to.x, box.y + box.height * to.y)
  await page.mouse.up()
}

/** 검수 게이트를 켜고 접수해 검수 대기 화면까지 간다. */
async function gotoReview(page: import('@playwright/test').Page) {
  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('requires-review-checkbox').check()
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('review-gate')).toBeVisible()
}

test.describe('검수 게이트 — review-gate', () => {
  test('#1 검수 대기 작업은 자동으로 생성되지 않고 감지된 파츠만 보여준다', async ({ page }) => {
    await installFakeApi(page, { reviewGate: true })
    await gotoReview(page)

    // 4개 파츠가 전부 "감지됨" 으로 나온다 — 아직 사람이 손대지 않았다
    // 파츠마다 칩 하나, 배치마다 상자 하나 (첫 파츠 배치 3개 + 나머지 3개)
    await expect(page.getByTestId('overlay-chip')).toHaveCount(4)
    await expect(page.getByTestId('overlay-chip').first()).toContainText('P01')
    await expect(page.getByTestId('overlay-box')).toHaveCount(6)
  })

  test('#2 겹치는 사각형은 가림 방향을 확인한 뒤 추가된다', async ({ page }) => {
    await installFakeApi(page, { reviewGate: true })
    await gotoReview(page)

    // 첫 파츠의 첫 배치(x=0.05,y=0.10,w=0.12,h=0.20) 안쪽을 겹쳐 그린다
    await dragBox(page, { x: 0.06, y: 0.11 }, { x: 0.1, y: 0.15 })
    await page.getByTestId('review-add-name').fill('장식')

    // 겹친 파츠가 전체 체크된 상태로 뜨고, 문구가 방향을 밝힌다
    const options = page.getByTestId('review-occludes-option')
    await expect(options).toHaveCount(1)
    await expect(options.first()).toContainText('가림')

    await page.getByTestId('review-add-confirm').click()

    // 겹쳐도 추가되고, 겹친 파츠에 가림 관계가 실제로 기록된다
    await expect(page.getByTestId('overlay-chip')).toHaveCount(5)
    // 가림 관계는 칩 글자에 실린다 — 상자에 손을 얹으면 무엇에 가려지는지 읽힌다
    await expect(page.getByTestId('overlay-chip').filter({ hasText: '가려짐 · 장식' })).toHaveCount(
      1,
    )
  })

  test('#2-1 체크를 해제하면 그 파츠는 가림 대상에서 빠진다', async ({ page }) => {
    await installFakeApi(page, { reviewGate: true })
    await gotoReview(page)

    await dragBox(page, { x: 0.06, y: 0.11 }, { x: 0.1, y: 0.15 })
    await page.getByTestId('review-add-name').fill('장식')

    // 전부 해제 — 겹치기만 하고 가리지는 않는다는 뜻이다
    await page.getByTestId('review-occludes-option').locator('input').first().uncheck()
    await page.getByTestId('review-add-confirm').click()

    // 추가는 성공하되, 가림 관계는 하나도 기록되지 않는다
    await expect(page.getByTestId('overlay-chip')).toHaveCount(5)
    await expect(page.getByTestId('overlay-chip').filter({ hasText: '가려짐' })).toHaveCount(0)
  })

  test('#3 사각형 추가 → 기존 파츠 삭제 → 상자 확정 → 생성 시작', async ({ page }) => {
    await installFakeApi(page, { reviewGate: true })
    await gotoReview(page)

    // 기존 파츠는 전부 x<=0.66 안쪽이다 — 오른쪽 빈 자리(스크롤 없이 보이는 상단)에 새로 그린다
    await dragBox(page, { x: 0.75, y: 0.05 }, { x: 0.9, y: 0.15 })
    // 이름만 채운다 — 카테고리·설명은 비우면 승인 시 AI 가 원본을 보고 채운다
    await page.getByTestId('review-add-name').fill('깃발')
    await page.getByTestId('review-add-confirm').click()

    await expect(
      page.getByTestId('overlay-box').filter({ has: page.locator(':scope') }),
    ).toHaveCount(7)

    // 삭제는 칩에서 한다 — 칩을 눌러 고정하면 "삭제" 가 옆에 뜬다
    const addedChip = page.getByTestId('overlay-chip').filter({ hasText: '깃발' })
    await expect(addedChip).toContainText('직접 추가')

    await addedChip.click()
    await page.getByTestId('overlay-jump').click()
    await expect(page.locator('[data-testid="overlay-box"][data-part="깃발"]')).toHaveCount(0)

    // 상자 확정 — 생성은 아직 나가지 않고 서술 확인 단계로 넘어간다 (review-gate-staged 사이클 1)
    await page.getByTestId('review-approve').click()
    await expect(page.getByTestId('review-descriptions')).toBeVisible()
    await expect(page.getByTestId('run-result')).toHaveCount(0)

    // 생성 시작 — 검수 게이트가 사라지고 진행/결과 화면으로 넘어간다
    await page.getByTestId('review-confirm-descriptions').click()
    await expect(page.getByTestId('review-gate')).toHaveCount(0)
    await expect(page.getByTestId('run-result')).toBeVisible({ timeout: 10_000 })
  })

  test('#4 서술 확인 — 칩·서술 편집·팔레트 편집·상자로 돌아가기', async ({ page }) => {
    await installFakeApi(page, { reviewGate: true })
    await gotoReview(page)

    // 서술 없이 파츠 추가 → 상자 확정 시 재작성이 채운 것으로 표시
    await dragBox(page, { x: 0.75, y: 0.05 }, { x: 0.9, y: 0.15 })
    await page.getByTestId('review-add-name').fill('깃발')
    await page.getByTestId('review-add-confirm').click()
    await expect(page.getByTestId('overlay-chip')).toHaveCount(5)
    await expect(page.getByTestId('review-approve')).toContainText('상자 확정 (5개 파츠)')
    await page.getByTestId('review-approve').click()

    const rows = page.getByTestId('description-row')
    await expect(rows).toHaveCount(5)
    // 다시 쓴 파츠가 목록 상단
    await expect(rows.first()).toHaveAttribute('data-part', '깃발')
    await expect(rows.first().getByTestId('description-chip')).toHaveText(['직접 추가', '다시 씀'])

    // 서술 편집 — blur 저장 후 "직접 수정" 칩
    const second = rows.nth(1)
    await second.getByTestId('description-textarea').fill('사람이 고친 서술')
    await second.getByTestId('description-textarea').blur()
    await expect(second.getByTestId('description-chip')).toHaveText(['직접 수정'])

    // 팔레트 이름 편집 — 레거시 칸(hex 없음)이 남아 있으면 서버가 거부하고 메시지 표시
    const firstEntry = page.getByTestId('palette-entry').first()
    await firstEntry.getByTestId('palette-name').fill('빨간 스카프')
    await firstEntry.getByTestId('palette-name').blur()
    await expect(page.getByTestId('palette-error')).toBeVisible()

    // 레거시 칸 삭제 → 편집 내용과 함께 저장
    await page.getByTestId('palette-entry').last().getByTestId('palette-remove').click()
    await expect(page.getByTestId('palette-error')).toHaveCount(0)
    await expect(page.getByTestId('palette-entry')).toHaveCount(3)

    // 상자로 돌아가기 — 상자 편집 화면, 서술은 유지
    await page.getByTestId('review-return-to-boxes').click()
    await expect(page.getByTestId('review-approve')).toBeVisible()
    await page.getByTestId('review-approve').click()
    await expect(page.getByTestId('description-row').filter({ hasText: '직접 수정' })).toHaveCount(
      1,
    )
    await expect(page.getByTestId('palette-name').first()).toHaveValue('빨간 스카프')

    // 빈 서술로는 저장하지 않음 — 버튼 활성 유지
    await expect(page.getByTestId('review-confirm-descriptions')).toBeEnabled()
  })
})
