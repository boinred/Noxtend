import { test, expect, type Locator, type Page } from '@playwright/test'
import { installFakeApi } from './fakeApi'
import { SPRITE_IDS, editBoxes, type SpriteFakeOptions } from './spriteFakeApi'
const settings = { view: 'isometric', outputKind: 'tiles', tileWidth: 128, repeat: 'both' } as const

test('plan review has no reconstruction before plan approval', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'planReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  await expect(review.getByTestId('overlay-image')).toBeVisible()
  await expect(review.getByRole('img', { name: '원본 재구성' })).toHaveCount(0)
  await expect(review.getByRole('group', { name: '재구성 보기' })).toHaveCount(0)
  await expect(review.getByRole('slider', { name: '원본·재구성 경계' })).toHaveCount(0)
})

// 프레임 비율 좌표를 화면 좌표로
async function frameOf(review: Locator) {
  const image = review.getByTestId('overlay-image')
  await image.scrollIntoViewIfNeeded()
  const rect = (await image.boundingBox())!
  return (x: number, y: number) => ({ x: rect.x + rect.width * x, y: rect.y + rect.height * y })
}

test('reconstruction compares by dragging the divider', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const layer = review.getByRole('img', { name: '원본 재구성' })
  const images = layer.getByTestId('reconstruction-images')
  const divider = review.getByRole('slider', { name: '원본·재구성 경계' })
  const clip = review.getByTestId('reconstruction-clip')
  await expect(layer).toHaveAttribute('viewBox', '0 0 320 180')
  await expect(layer).toHaveAttribute('preserveAspectRatio', 'none')
  await expect(images).not.toHaveAttribute('opacity')
  await expect(images.locator('image[data-tile-id]').first()).toHaveAttribute('width', '320')
  await expect(images.locator('image[data-asset-id]')).toHaveCount(3) // 원경 배경 1·오브젝트 자리 2
  // 처음은 비교 모드, 경계 50%
  await expect(review.getByRole('button', { name: '비교', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(review.getByTestId('overlay-box')).toHaveCount(0)
  await expect(divider).toHaveAttribute('aria-valuenow', '50')
  await expect(divider).toHaveAttribute('aria-valuetext', '50%')
  await expect(clip).toHaveCSS('clip-path', 'inset(0px 0px 0px 50%)')
  const at = await frameOf(review)
  const from = at(0.25, 0.5)
  const to = at(0.1, 0.5)
  await page.mouse.move(from.x, from.y)
  await page.mouse.down()
  await page.mouse.move(to.x, to.y)
  await page.mouse.up()
  await expect(divider).toHaveAttribute('aria-valuenow', '10')
  await expect(clip).toHaveCSS('clip-path', 'inset(0px 0px 0px 10%)')
  await divider.focus()
  await page.keyboard.press('ArrowRight')
  await expect(divider).toHaveAttribute('aria-valuenow', '11')
  await expect(divider).toHaveAttribute('aria-valuetext', '11%')
})

test('edit mode keeps the divider and edits boxes', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const divider = review.getByRole('slider', { name: '원본·재구성 경계' })
  const clip = review.getByTestId('reconstruction-clip')
  // 모드 버튼이 나타나 이미지가 밀린 뒤에 위치 측정
  await expect(divider).toBeVisible()
  const at = await frameOf(review)
  const middle = at(0.3, 0.5)
  await page.mouse.click(middle.x, middle.y)
  await expect(divider).toHaveAttribute('aria-valuenow', '30')
  await expect(clip).toHaveCSS('clip-path', 'inset(0px 0px 0px 30%)')
  await editBoxes(review)
  await expect(review.getByTestId('overlay-box').first()).toBeVisible()
  // 경계선은 남고 끌기 면과 손잡이는 사라짐
  await expect(divider).toHaveCount(0)
  await expect(review.getByTestId('compare-surface')).toHaveCount(0)
  await expect(review.getByTestId('compare-line')).toBeVisible()
  await expect(clip).toHaveCSS('clip-path', 'inset(0px 0px 0px 30%)')
  // 재구성 층이 ROI 선택을 막지 않음
  const corner = at(0.1, 0.1)
  await page.mouse.click(corner.x, corner.y)
  await expect(review.getByTestId('overlay-handle')).toHaveCount(8)
  await expect(clip).toHaveCSS('clip-path', 'inset(0px 0px 0px 30%)')
})

test('selecting a box from a card switches to edit mode', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const compare = review.getByRole('button', { name: '비교', exact: true })
  const edit = review.getByRole('button', { name: '편집', exact: true })
  // 오브젝트는 3번째 대상
  for (const name of ['대상 선택 3', '생성 기준 선택 3', '자리 1 선택 3']) {
    await compare.click()
    await expect(compare).toHaveAttribute('aria-pressed', 'true')
    await expect(review.getByTestId('overlay-handle')).toHaveCount(0)
    await review.getByRole('button', { name, exact: true }).click()
    await expect(edit).toHaveAttribute('aria-pressed', 'true')
    await expect(review.getByTestId('overlay-handle')).toHaveCount(8)
  }
})

test('adding or deleting a placement keeps the compare mode', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const compare = review.getByRole('button', { name: '비교', exact: true })
  await expect(compare).toHaveAttribute('aria-pressed', 'true')
  await review.getByRole('button', { name: '자리 추가 3', exact: true }).click()
  await expect(compare).toHaveAttribute('aria-pressed', 'true')
  await review.getByRole('button', { name: '선택한 자리 삭제 3', exact: true }).click()
  await expect(review.getByRole('button', { name: '자리 추가 3', exact: true })).toBeEnabled()
  await expect(compare).toHaveAttribute('aria-pressed', 'true')
  await expect(review.getByTestId('overlay-box')).toHaveCount(0)
})

test('entering compare cancels a box drag', async ({ page }) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings, records } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  await editBoxes(review)
  const object = review.locator('[data-testid="overlay-box"][data-kind="source"]').nth(2)
  const at = await frameOf(review)
  // 끌기 미리보기가 바꾸는 상자 위치
  const geometry = () =>
    object.evaluate((el) => [el.style.left, el.style.top, el.style.width, el.style.height].join())
  const before = await geometry()
  const start = at(0.25, 0.25)
  const end = at(0.35, 0.35)
  await page.mouse.move(start.x, start.y)
  await page.mouse.down()
  await page.mouse.move(end.x, end.y)
  await expect.poll(geometry).not.toBe(before)
  // 끄는 중 키보드로 비교 모드 진입
  await review.getByRole('button', { name: '비교', exact: true }).focus()
  await page.keyboard.press('Enter')
  await expect(review.getByTestId('overlay-box')).toHaveCount(0)
  await page.mouse.up()
  await editBoxes(review)
  await expect.poll(geometry).toBe(before)
  await expect(review.getByRole('button', { name: '계획 저장', exact: true })).toBeDisabled()
  expect(records.filter((record) => record.path.endsWith('/plan'))).toHaveLength(0)
})

test('recomputing keeps the chosen mode', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const tiles = review.getByRole('img', { name: '원본 재구성' }).locator('image[data-tile-id]')
  await expect(tiles.first()).toHaveAttribute('width', '320')
  await editBoxes(review)
  await review.getByLabel('격자 칸 너비 (원본 px)', { exact: true }).fill('160')
  await expect(tiles.first()).toHaveAttribute('width', '160')
  await expect(review.getByRole('button', { name: '편집', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(review.getByRole('slider', { name: '원본·재구성 경계' })).toHaveCount(0)
})

test('touch scroll restores the divider', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const surface = review.getByTestId('compare-surface')
  const divider = review.getByRole('slider', { name: '원본·재구성 경계' })
  await expect(surface).toHaveCSS('touch-action', 'pan-y')
  const at = await frameOf(review)
  await surface.dispatchEvent('pointerdown', {
    clientX: at(0.2, 0.5).x,
    clientY: at(0.2, 0.5).y,
    pointerId: 1,
    isPrimary: true,
    button: 0,
    bubbles: true,
  })
  await expect(divider).toHaveAttribute('aria-valuenow', '20')
  // 세로 스크롤로 넘어가면 브라우저가 보내는 pointercancel
  await page.evaluate(() =>
    document.dispatchEvent(new PointerEvent('pointercancel', { pointerId: 1 })),
  )
  await expect(divider).toHaveAttribute('aria-valuenow', '50')
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
})

test('backdrop fills the source canvas while objects without placements keep their own ROI', async ({
  page,
}) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const body = await page.evaluate(
    (id) => fetch(`/api/jobs/${id}`).then((response) => response.json()),
    SPRITE_IDS.job,
  )
  const assets = body.data.sprite.assets as {
    id: string
    plan: { role: string; sourceBounds: Record<string, number>; placements: unknown }
  }[]
  const backdrop = assets.find((asset) => asset.plan.role === 'backdrop')!
  const object = assets.find((asset) => asset.plan.role === 'object')!
  // 시드의 원경 배경 ROI는 원본 전체와 같아 응답에서만 어긋나게 바꿈
  backdrop.plan.sourceBounds = { x: 0.25, y: 0.25, w: 0.5, h: 0.5 }
  // 저장된 자리가 없으면 ROI 한 자리
  object.plan.sourceBounds = { x: 0.25, y: 0.5, w: 0.5, h: 0.5 }
  object.plan.placements = null
  await page.route(`**/api/jobs/${SPRITE_IDS.job}`, (route) =>
    route.request().method() === 'GET' ? route.fulfill({ json: body }) : route.fallback(),
  )
  await page.reload()
  const images = page
    .getByRole('region', { name: '제작 계획 검수' })
    .getByRole('img', { name: '원본 재구성' })
    .getByTestId('reconstruction-images')
  for (const [asset, rect] of [
    [backdrop, { x: 0, y: 0, width: 320, height: 180 }],
    [object, { x: 80, y: 90, width: 160, height: 90 }],
  ] as const)
    for (const [name, value] of Object.entries(rect))
      await expect(images.locator(`image[data-asset-id="${asset.id}"]`)).toHaveAttribute(
        name,
        String(value),
      )
})

// 시드 오브젝트의 두 자리(원본 320×180 px), ROI(0,0,160,90)와 다름
const OBJECT_RECTS = [
  { x: 160, y: 0, width: 80, height: 90 },
  { x: 80, y: 90, width: 80, height: 90 },
]

test('objects are drawn at each stored placement in ascending bottom y', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const body = await page.evaluate(
    (id) => fetch(`/api/jobs/${id}`).then((response) => response.json()),
    SPRITE_IDS.job,
  )
  const object = (
    body.data.sprite.assets as { id: string; plan: { role: string; placements: unknown[] } }[]
  ).find((asset) => asset.plan.role === 'object')!
  // 시드 저장 순서가 이미 정렬 순서라 응답에서만 역순
  object.plan.placements.reverse()
  await page.route(`**/api/jobs/${SPRITE_IDS.job}`, (route) =>
    route.request().method() === 'GET' ? route.fulfill({ json: body }) : route.fallback(),
  )
  await page.reload()
  const drawn = page
    .getByRole('region', { name: '제작 계획 검수' })
    .getByRole('img', { name: '원본 재구성' })
    .getByTestId('reconstruction-images')
    .locator(`image[data-asset-id="${object.id}"]`)
  await expect(drawn).toHaveCount(2)
  // 아래쪽 y 가 작은 자리부터 그림
  for (const [index, rect] of OBJECT_RECTS.entries())
    for (const [name, value] of Object.entries(rect))
      await expect(drawn.nth(index)).toHaveAttribute(name, String(value))
})

interface WirePlan {
  role: string
  motionNotes: string
  placements: { x: number; y: number; w: number; h: number }[] | null
}
interface WireSprite {
  phase: string
  reviewRevision: number
  grid: unknown
  images: unknown[]
  assets: { planRevision: number; approval: unknown; plan: WirePlan }[]
}

test('saving only placements keeps approvals and images and bumps only the revision', async ({
  page,
}) => {
  await installFakeApi(page, { sprites: { seed: 'completed', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const { before, after, changed } = await page.evaluate(async (id) => {
    const read = async (): Promise<WireSprite> =>
      (await (await fetch(`/api/jobs/${id}`)).json()).data.sprite
    const save = async (sprite: WireSprite, edit: (plan: WirePlan) => void) => {
      const assets = sprite.assets.map((asset) => structuredClone(asset.plan))
      assets.forEach(edit)
      await fetch(`/api/jobs/${id}/sprites/plan`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          requestId: crypto.randomUUID(),
          expectedRevision: sprite.reviewRevision,
          assets,
          grid: sprite.grid,
        }),
      })
      return read()
    }
    const before = await read()
    const after = await save(before, (plan) => {
      if (plan.role === 'object') plan.placements = [{ x: 0.1, y: 0.1, w: 0.2, h: 0.2 }]
    })
    const changed = await save(after, (plan) => {
      if (plan.role === 'object') plan.motionNotes = '다른 동작'
    })
    return { before, after, changed }
  }, SPRITE_IDS.job)
  const objectOf = (sprite: WireSprite) =>
    sprite.assets.find((asset) => asset.plan.role === 'object')!
  expect(objectOf(after).plan.placements).toEqual([{ x: 0.1, y: 0.1, w: 0.2, h: 0.2 }])
  expect(objectOf(before).approval).not.toBeNull()
  expect(objectOf(after).approval).toEqual(objectOf(before).approval)
  expect(objectOf(after).planRevision).toBe(objectOf(before).planRevision)
  expect(after.images).toEqual(before.images)
  expect(after.phase).toBe(before.phase)
  expect(after.reviewRevision).toBe(before.reviewRevision + 1)
  // 생성 입력이 바뀌면 기존 규칙대로 승인 해제
  expect(objectOf(changed).approval).toBeNull()
})

test('missing base images stay outlined in compare mode', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings, partialBases: true } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const layer = review.getByRole('img', { name: '원본 재구성' })
  const images = layer.getByTestId('reconstruction-images')
  const missing = layer.locator('rect[data-missing]')
  await expect(missing).toHaveCount(1)
  // 기준 이미지 없는 타일은 ROI(원본 전체) 윤곽
  for (const [name, value] of Object.entries({ x: 0, y: 0, width: 320, height: 180 }))
    await expect(missing).toHaveAttribute(name, String(value))
  await expect(images.locator('rect')).toHaveCount(0)
  await expect(images.locator('image[data-tile-id]')).toHaveCount(0)
  await expect(images.locator('image[data-asset-id]')).toHaveCount(3)
  await expect(review.getByRole('button', { name: '비교', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(missing).toBeVisible()
})

test('unreadable images show a reason instead of a partial reconstruction', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.route('**/sprites/images/**', (route) => route.fulfill({ status: 500 }))
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  await expect(
    review.getByRole('alert').filter({ hasText: '재구성에 필요한 이미지를 불러오지 못했습니다' }),
  ).toBeVisible()
  await expect(review.getByRole('img', { name: '원본 재구성' })).toHaveCount(0)
  await expect(review.getByTestId('overlay-image')).toBeVisible()
  // 계산 실패는 모드 없이 편집처럼 동작
  await expect(review.getByRole('group', { name: '재구성 보기' })).toHaveCount(0)
})

test('failed displayed images show dashed outlines and a reason', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const layer = review.getByRole('img', { name: '원본 재구성' })
  const images = layer.getByTestId('reconstruction-images')
  await expect(images.locator('image[data-asset-id]')).toHaveCount(3)
  const { data } = await page.evaluate(
    (id) => fetch(`/api/jobs/${id}`).then((response) => response.json()),
    SPRITE_IDS.job,
  )
  const object = (
    data.sprite.assets as {
      id: string
      plan: { role: string }
      frames: { currentImageId: string }[]
    }[]
  ).find((asset) => asset.plan.role === 'object')!
  await page.route(`**/sprites/images/${object.frames[0]!.currentImageId}`, (route) =>
    route.fulfill({ status: 500 }),
  )
  await page.reload()
  await expect(
    review.getByRole('alert').filter({ hasText: '재구성 이미지 일부를 불러오지 못했습니다' }),
  ).toBeVisible()
  const missing = layer.locator(`rect[data-missing="${object.id}"]`)
  await expect(missing).toHaveCount(2)
  // 자리마다 윤곽, 저장된 자리 순서
  for (const [index, rect] of OBJECT_RECTS.entries())
    for (const [name, value] of Object.entries(rect))
      await expect(missing.nth(index)).toHaveAttribute(name, String(value))
  await expect(images.locator(`[data-asset-id="${object.id}"]`)).toHaveCount(0)
  await expect(images.locator('rect')).toHaveCount(0)
  // 나머지 재구성은 유지
  await expect(images.locator('image[data-asset-id]')).toHaveCount(1)
  await expect(images.locator('image[data-tile-id]').first()).toBeAttached()
})

test('retrying a failed reconstruction keeps unsaved placement edits', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  // 재구성 계산의 첫 이미지 읽기만 실패
  let failed = false
  await page.route('**/sprites/images/**', (route) => {
    if (failed || route.request().resourceType() !== 'fetch') return route.fallback()
    failed = true
    return route.fulfill({ status: 500 })
  })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const layer = review.getByRole('img', { name: '원본 재구성' })
  const alert = review
    .getByRole('alert')
    .filter({ hasText: '재구성에 필요한 이미지를 불러오지 못했습니다' })
  const retry = review.getByRole('button', { name: '재구성 다시 계산', exact: true })
  await expect(alert).toBeVisible()
  await expect(retry).toBeVisible()
  await expect(layer).toHaveCount(0)
  await review.getByRole('button', { name: '자리 1 선택 3', exact: true }).click()
  await review.getByText('정밀 조정 3', { exact: true }).click()
  await review.getByLabel('자리 1 ROI x 3', { exact: true }).fill('0.4')
  await retry.click()
  await expect(layer.locator('image[data-tile-id]').first()).toBeAttached()
  await expect(alert).toHaveCount(0)
  await expect(retry).toHaveCount(0)
  // 미저장 자리 편집 유지
  await expect(review.getByLabel('자리 1 ROI x 3', { exact: true })).toHaveValue('0.4')
  await expect(review.getByRole('button', { name: '계획 저장', exact: true })).toBeEnabled()
  await expect(
    layer.getByTestId('reconstruction-images').locator('image[data-asset-id]').nth(1),
  ).toHaveAttribute('x', '128')
})

test('failed tile images outline their cells and offer a retry', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const layer = review.getByRole('img', { name: '원본 재구성' })
  const images = layer.getByTestId('reconstruction-images')
  await expect(images.locator('image[data-tile-id]').first()).toBeAttached()
  const { data } = await page.evaluate(
    (id) => fetch(`/api/jobs/${id}`).then((response) => response.json()),
    SPRITE_IDS.job,
  )
  const tile = (
    data.sprite.assets as {
      id: string
      plan: { role: string }
      frames: { currentImageId: string }[]
    }[]
  ).find((asset) => asset.plan.role === 'tile')!
  // 계산 읽기는 성공, 표시만 실패
  await page.route(`**/sprites/images/${tile.frames[0]!.currentImageId}`, (route) =>
    route.request().resourceType() === 'image' ? route.fulfill({ status: 500 }) : route.fallback(),
  )
  await page.reload()
  await expect(
    review.getByRole('alert').filter({ hasText: '재구성 이미지 일부를 불러오지 못했습니다' }),
  ).toBeVisible()
  await expect(review.getByRole('button', { name: '재구성 다시 계산', exact: true })).toBeVisible()
  await expect(images.locator('image[data-tile-id]')).toHaveCount(0)
  // 빈칸 대신 칸 윤곽, 투명도와 무관
  await expect(layer.locator(`polygon[data-failed-tile="${tile.id}"]`).first()).toBeAttached()
  await expect(images.locator('polygon')).toHaveCount(0)
})

test('small cells keep editing usable without excessive SVG nodes', async ({ page }) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, {
    sprites: {
      seed: 'baseReview',
      settings,
      tileOnly: true,
      records,
      // 최대 원본 16,777,216px, 1px 칸
      sourceCanvas: { width: 4096, height: 4096 },
      grid: { cellWidth: 1 / 4096 },
    },
  })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  await expect(
    review
      .getByRole('alert')
      .filter({ hasText: '재구성 칸이 너무 많습니다. 격자 칸 너비를 늘려 주세요' }),
  ).toBeVisible()
  // 입력 조정 안내라 다시 계산 없음
  await expect(review.getByRole('button', { name: '재구성 다시 계산' })).toHaveCount(0)
  await expect(review.getByRole('img', { name: '원본 재구성' })).toHaveCount(0)
  await expect(review.locator('image[data-tile-id]')).toHaveCount(0)
  await expect(review.getByRole('group', { name: '재구성 보기' })).toHaveCount(0)
  // 원본·ROI·계획 저장 유지
  await expect(review.getByTestId('overlay-image')).toBeVisible()
  await expect(review.getByTestId('overlay-box')).toHaveCount(3)
  await review.getByText('정밀 조정 1', { exact: true }).click()
  await review.getByLabel('ROI w 1', { exact: true }).fill('0.5')
  await review.getByLabel('대상 이름 1', { exact: true }).fill('넓은 바닥')
  await review.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect.poll(() => records.filter((r) => r.path.endsWith('/plan')).length).toBe(1)
  const body = records.find((r) => r.path.endsWith('/plan'))!.body
  expect((body.assets as { name: string }[])[0]!.name).toBe('넓은 바닥')
})

const tileWorkers = (page: Page) =>
  page.workers().filter((worker) => worker.url().includes('tileFill.worker'))
// 시험용 Worker, 입력의 칸 너비를 x 로 돌려줘 어느 요청의 결과인지 구분
const STUB_WORKER = `
let input = null
self.onmessage = (event) => { input = event.data }
new BroadcastChannel('tile-fill-e2e').onmessage = () => {
  if (input) self.postMessage({ fill: { offset: { x: 0, y: 0 }, cells: [{ x: input.cell.width, y: 0, tileId: input.tiles[0].id }] } })
}
`
const release = (page: Page) =>
  page.evaluate(() => new BroadcastChannel('tile-fill-e2e').postMessage('release'))

test('a worker that fails to start shows a fixed reason and a retry', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.route('**/tileFill.worker-*.js', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'text/javascript',
      body: "throw new Error('worker boot failed')",
    }),
  )
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  await expect(
    review.getByRole('alert').filter({ hasText: '재구성 계산을 시작하지 못했습니다' }),
  ).toBeVisible()
  // 브라우저 원문 오류 미노출
  await expect(review.getByText('worker boot failed')).toHaveCount(0)
  await expect(review.getByRole('button', { name: '재구성 다시 계산', exact: true })).toBeVisible()
  await expect(review.getByRole('group', { name: '재구성 보기' })).toHaveCount(0)
})

test('timed out and superseded workers cannot publish results', async ({ page }) => {
  const errors: string[] = []
  page.on('pageerror', (error) => errors.push(error.message))
  let created = 0
  page.on('worker', (worker) => {
    if (worker.url().includes('tileFill.worker')) created++
  })
  const start = new Date('2026-10-18T00:00:00Z')
  await page.clock.install({ time: start })
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.route('**/tileFill.worker-*.js', (route) =>
    route.fulfill({ status: 200, contentType: 'text/javascript', body: STUB_WORKER }),
  )
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const layer = review.getByRole('img', { name: '원본 재구성' })
  const tiles = layer.locator('image[data-tile-id]')
  const calculating = review.getByRole('status').filter({ hasText: '재구성 계산 중…' })
  const timedOut = review
    .getByRole('alert')
    .filter({ hasText: '재구성 계산 시간이 초과되었습니다. 격자 칸 너비를 조정해 주세요' })
  const cellWidth = review.getByLabel('격자 칸 너비 (원본 px)', { exact: true })
  await expect(calculating).toBeVisible()
  await expect(review.getByRole('group', { name: '재구성 보기' })).toHaveCount(0)
  await expect.poll(() => tileWorkers(page).length).toBe(1)
  const first = tileWorkers(page)[0]!

  // 자리 편집은 계산을 다시 시작하지 않음
  await review.getByText('정밀 조정 3', { exact: true }).click()
  await review.getByLabel('ROI x 3', { exact: true }).fill('0.25')
  await expect(calculating).toBeVisible()
  expect(tileWorkers(page)).toHaveLength(1)
  expect(tileWorkers(page)[0]).toBe(first)

  // 칸 변경은 이전 Worker 종료, 새 결과만 반영
  const firstClosed = first.waitForEvent('close')
  await cellWidth.fill('160')
  await firstClosed
  await expect.poll(() => tileWorkers(page).length).toBe(1)
  await release(page)
  await expect(tiles).toHaveCount(1)
  await expect(tiles).toHaveAttribute('x', '160')
  await expect(tiles).toHaveAttribute('width', '160')
  await expect(calculating).toHaveCount(0)
  await expect.poll(() => tileWorkers(page).length).toBe(0)
  expect(created).toBe(2)

  // 5초 경계, 시간 초과 뒤 결과 폐기
  await page.clock.pauseAt(start.getTime() + 60_000)
  await cellWidth.fill('200')
  await expect.poll(() => tileWorkers(page).length).toBe(1)
  await expect(layer).toHaveCount(0)
  await page.clock.runFor(4_999)
  await expect(calculating).toBeVisible()
  await expect(timedOut).toHaveCount(0)
  await page.clock.runFor(1)
  await expect(timedOut).toBeVisible()
  await expect(review.getByRole('button', { name: '재구성 다시 계산' })).toHaveCount(0)
  await expect(calculating).toHaveCount(0)
  await expect.poll(() => tileWorkers(page).length).toBe(0)
  await release(page)
  await expect(layer).toHaveCount(0)
  // 생성 기준 3·오브젝트 자리 2
  await expect(review.getByTestId('overlay-box')).toHaveCount(5)
  await expect(review.getByRole('group', { name: '재구성 보기' })).toHaveCount(0)

  // 화면 이탈 시 종료
  await page.clock.resume()
  await cellWidth.fill('240')
  await expect.poll(() => tileWorkers(page).length).toBe(1)
  await page.getByRole('link', { name: '홈', exact: true }).click()
  await expect(review).toHaveCount(0)
  await expect.poll(() => tileWorkers(page).length).toBe(0)
  await release(page)
  expect(errors).toEqual([])
})
