import { test, expect, type Page } from '@playwright/test'
import { installFakeApi } from './fakeApi'
import { SPRITE_IDS, type SpriteFakeOptions } from './spriteFakeApi'
const settings = { view: 'isometric', outputKind: 'tiles', tileWidth: 128, repeat: 'both' } as const

test('plan review has no reconstruction before plan approval', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'planReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  await expect(review.getByTestId('overlay-image')).toBeVisible()
  await expect(review.getByRole('img', { name: '원본 재구성' })).toHaveCount(0)
  await expect(review.getByRole('slider', { name: '재구성 투명도' })).toHaveCount(0)
})

test('reconstruction follows opacity after plan approval', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const layer = review.getByRole('img', { name: '원본 재구성' })
  const images = layer.getByTestId('reconstruction-images')
  const opacity = review.getByRole('slider', { name: '재구성 투명도' })
  await expect(layer).toHaveAttribute('viewBox', '0 0 320 180')
  await expect(layer).toHaveAttribute('preserveAspectRatio', 'none')
  await expect(opacity).toHaveValue('50')
  await expect(images).toHaveAttribute('opacity', '0.5')
  await opacity.fill('80')
  await expect(opacity).toHaveAttribute('aria-valuetext', '80%')
  await expect(images).toHaveAttribute('opacity', '0.8')
  await expect(images.locator('image[data-tile-id]').first()).toHaveAttribute('width', '320')
  await expect(images.locator('image[data-asset-id]')).toHaveCount(2) // 원경 배경 1·오브젝트 자리 1
  const { data } = await page.evaluate(
    (id) => fetch(`/api/jobs/${id}`).then((response) => response.json()),
    SPRITE_IDS.job,
  )
  const idOf = (role: string) =>
    (data.sprite.assets as { id: string; plan: { role: string } }[]).find(
      (asset) => asset.plan.role === role,
    )!.id
  // 원경 배경은 ROI가 아닌 원본 전체, 오브젝트는 자기 ROI
  for (const [role, rect] of [
    ['backdrop', { x: 0, y: 0, width: 320, height: 180 }],
    ['object', { x: 0, y: 0, width: 160, height: 90 }],
  ] as const)
    for (const [name, value] of Object.entries(rect))
      await expect(images.locator(`image[data-asset-id="${idOf(role)}"]`)).toHaveAttribute(
        name,
        String(value),
      )
  // 재구성 층이 ROI 선택을 막지 않음
  const image = review.getByTestId('overlay-image')
  await image.scrollIntoViewIfNeeded()
  const frame = (await image.boundingBox())!
  await page.mouse.click(frame.x + frame.width * 0.1, frame.y + frame.height * 0.1)
  await expect(review.getByTestId('overlay-handle')).toHaveCount(8)
})

test('missing base images show dashed outlines regardless of opacity', async ({ page }) => {
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
  await expect(images.locator('image[data-asset-id]')).toHaveCount(2)
  await review.getByRole('slider', { name: '재구성 투명도' }).fill('0')
  await expect(images).toHaveAttribute('opacity', '0')
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
})

test('failed displayed images show dashed outlines and a reason', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const layer = review.getByRole('img', { name: '원본 재구성' })
  const images = layer.getByTestId('reconstruction-images')
  await expect(images.locator('image[data-asset-id]')).toHaveCount(2)
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
  await expect(missing).toHaveCount(1)
  for (const [name, value] of Object.entries({ x: 0, y: 0, width: 160, height: 90 }))
    await expect(missing).toHaveAttribute(name, String(value))
  await expect(images.locator(`[data-asset-id="${object.id}"]`)).toHaveCount(0)
  await expect(images.locator('rect')).toHaveCount(0)
  // 나머지 재구성은 유지
  await expect(images.locator('image[data-asset-id]')).toHaveCount(1)
  await expect(images.locator('image[data-tile-id]').first()).toBeAttached()
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
  await expect(review.getByRole('img', { name: '원본 재구성' })).toHaveCount(0)
  await expect(review.locator('image[data-tile-id]')).toHaveCount(0)
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
  await expect(calculating).toHaveCount(0)
  await expect.poll(() => tileWorkers(page).length).toBe(0)
  await release(page)
  await expect(layer).toHaveCount(0)
  await expect(review.getByTestId('overlay-box')).toHaveCount(3)

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
