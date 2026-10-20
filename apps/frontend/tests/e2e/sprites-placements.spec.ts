import { test, expect, type Page } from '@playwright/test'
import { installFakeApi } from './fakeApi'
import { SPRITE_IDS, editBoxes, type SpriteFakeOptions } from './spriteFakeApi'
const settings = { view: 'isometric', outputKind: 'tiles', tileWidth: 128, repeat: 'both' } as const

interface Box {
  x: number
  y: number
  w: number
  h: number
}
interface SavedPlan {
  id: string
  role: string
  sourceBounds: Box
  placements: Box[] | null
}
type Records = NonNullable<SpriteFakeOptions['records']>

// 시드 오브젝트(3번째 대상) ROI 0,0,.5,.5 · 자리 1 .5,0,.25,.5 · 자리 2 .25,.5,.25,.5
const ROI = { x: 0, y: 0, w: 0.5, h: 0.5 }
const FIRST = { x: 0.5, y: 0, w: 0.25, h: 0.5 }
const SECOND = { x: 0.25, y: 0.5, w: 0.25, h: 0.5 }

const reviewOf = (page: Page) => page.getByRole('region', { name: '제작 계획 검수' })
const savedObject = (records: Records, id?: string) => {
  const plans = records.filter((record) => record.path.endsWith('/plan')).at(-1)!.body
    .assets as SavedPlan[]
  return plans.find((plan) => (id ? plan.id === id : plan.role === 'object'))!
}

async function frameOf(page: Page) {
  const image = reviewOf(page).getByTestId('overlay-image')
  await image.scrollIntoViewIfNeeded()
  const rect = (await image.boundingBox())!
  return (x: number, y: number) => ({ x: rect.x + rect.width * x, y: rect.y + rect.height * y })
}

async function expectBounds(page: Page, prefix: string, box: Box) {
  for (const axis of ['x', 'y', 'w', 'h'] as const)
    await expect
      .poll(async () =>
        Number(await page.getByLabel(`${prefix} ${axis} 3`, { exact: true }).inputValue()),
      )
      .toBeCloseTo(box[axis], 2)
}

test('placement boxes are edited separately from the generation box', async ({ page }) => {
  const records: Records = []
  // 자리만 바꾼 저장의 승인 유지 확인용 completed
  await installFakeApi(page, { sprites: { seed: 'completed', settings, records } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = reviewOf(page)
  await editBoxes(review)
  const object = review.locator('[data-testid="overlay-box"][data-part="오브젝트 바위"]')
  const placements = object.and(review.locator('[data-kind="placement"]'))
  const source = object.and(review.locator('[data-kind="source"]'))
  await expect(
    review.getByTestId('overlay-chip').filter({ hasText: '3. 오브젝트 바위 · 자리' }),
  ).toHaveCount(1)
  await expect(placements).toHaveCount(2)
  await expect(source).toHaveCount(1)
  await expect(placements.first()).toHaveCSS('border-top-style', 'dashed')
  await expect(source).toHaveCSS('border-top-style', 'solid')
  const images = review
    .getByRole('img', { name: '원본 재구성' })
    .getByTestId('reconstruction-images')
    .locator('image[data-asset-id]')
  await expect(images).toHaveCount(3)
  await expect(page.getByText('최종 승인 완료 · 1프레임', { exact: true })).toHaveCount(3)

  const at = await frameOf(page)
  const start = at(0.625, 0.25)
  const end = at(0.525, 0.35)
  await page.mouse.move(start.x, start.y)
  await page.mouse.down()
  await page.mouse.move(end.x, end.y)
  await page.mouse.up()
  await expect(review.getByRole('button', { name: '자리 1 선택 3', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await review.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect.poll(() => records.filter((r) => r.path.endsWith('/plan')).length).toBe(1)
  const saved = savedObject(records)
  expect(saved.sourceBounds).toEqual(ROI)
  expect(saved.placements).toHaveLength(2)
  expect(saved.placements![0]!.x).toBeCloseTo(0.4, 2)
  expect(saved.placements![0]!.y).toBeCloseTo(0.1, 2)
  expect(saved.placements![0]!.w).toBe(FIRST.w)
  expect(saved.placements![0]!.h).toBe(FIRST.h)
  expect(saved.placements![1]).toEqual(SECOND)
  await expect(review.getByRole('button', { name: '계획 저장', exact: true })).toBeDisabled()
  await expect(page.getByText('최종 승인 완료 · 1프레임', { exact: true })).toHaveCount(3)
  await expect(images.and(page.locator(`[data-asset-id="${saved.id}"]`))).toHaveCount(2)
})

test('add and delete placements stay within one to twenty', async ({ page }, testInfo) => {
  const records: Records = []
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings, records } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = reviewOf(page)
  await editBoxes(review)
  const boxes = review.locator('[data-testid="overlay-box"][data-kind="placement"]')
  const add = review.getByRole('button', { name: '자리 추가 3', exact: true })
  const remove = review.getByRole('button', { name: '선택한 자리 삭제 3', exact: true })
  const pick = (k: number) => review.getByRole('button', { name: `자리 ${k} 선택 3`, exact: true })
  await expect(remove).toBeDisabled()
  await add.click()
  await expect(boxes).toHaveCount(3)
  await expect(pick(3)).toHaveAttribute('aria-pressed', 'true')
  await review.getByText('정밀 조정 3', { exact: true }).click()
  // 마지막 자리의 오른쪽 아래 복사본, 원본 아래 끝에 맞춤
  await expectBounds(page, '자리 3 ROI', { ...SECOND, x: 0.27 })
  await remove.click()
  await expect(boxes).toHaveCount(2)
  await expect(pick(2)).toHaveAttribute('aria-pressed', 'true')
  await remove.click()
  await expect(boxes).toHaveCount(1)
  await expect(pick(1)).toHaveAttribute('aria-pressed', 'true')
  await expect(remove).toBeDisabled()
  await expectBounds(page, '자리 1 ROI', FIRST)
  for (let count = 1; count < 20; count++) await add.click()
  await expect(boxes).toHaveCount(20)
  await expect(pick(20)).toHaveAttribute('aria-pressed', 'true')
  await expect(add).toBeDisabled()
  await add.dispatchEvent('click')
  await expect(boxes).toHaveCount(20)
  await review.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect.poll(() => records.filter((r) => r.path.endsWith('/plan')).length).toBe(1)
  expect(savedObject(records).placements).toHaveLength(20)
  await expect(review.getByRole('button', { name: '계획 저장', exact: true })).toBeDisabled()
  const card = review.getByTestId('sprite-plan-card').nth(2)
  await card.screenshot({ path: testInfo.outputPath('placements-card-1440.png') })
  await review
    .getByTestId('parts-overlay')
    .screenshot({ path: testInfo.outputPath('placements-overlay-1440.png') })
  await page.setViewportSize({ width: 390, height: 844 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
  await card.screenshot({ path: testInfo.outputPath('placements-card-390.png') })
})

test('clicking the second placement deletes only that placement', async ({ page }) => {
  const records: Records = []
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings, records } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = reviewOf(page)
  await editBoxes(review)
  const pick = (k: number) => review.getByRole('button', { name: `자리 ${k} 선택 3`, exact: true })
  const remove = review.getByRole('button', { name: '선택한 자리 삭제 3', exact: true })
  // 자리 1 복사본을 세 번째 자리로
  const first = (await frameOf(page))(0.625, 0.25)
  await page.mouse.click(first.x, first.y)
  await expect(pick(1)).toHaveAttribute('aria-pressed', 'true')
  await review.getByRole('button', { name: '자리 추가 3', exact: true }).click()
  await expect(pick(3)).toHaveAttribute('aria-pressed', 'true')
  // 버튼 클릭 스크롤 뒤 프레임 위치 재측정
  const second = (await frameOf(page))(0.375, 0.75)
  await page.mouse.click(second.x, second.y)
  await expect(pick(2)).toHaveAttribute('aria-pressed', 'true')
  await expect(review.getByTestId('overlay-handle')).toHaveCount(8)
  await review.getByText('정밀 조정 3', { exact: true }).click()
  await expectBounds(page, '자리 2 ROI', SECOND)
  await remove.click()
  // 같은 번호의 다음 자리
  await expect(pick(2)).toHaveAttribute('aria-pressed', 'true')
  await expectBounds(page, '자리 2 ROI', { ...FIRST, x: 0.52, y: 0.02 })
  await remove.click()
  // 마지막 남은 자리
  await expect(pick(1)).toHaveAttribute('aria-pressed', 'true')
  await expect(remove).toBeDisabled()
  await expectBounds(page, '자리 1 ROI', FIRST)
  await review.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect.poll(() => records.filter((r) => r.path.endsWith('/plan')).length).toBe(1)
  expect(savedObject(records).placements).toEqual([FIRST])
  expect(savedObject(records).sourceBounds).toEqual(ROI)
})

for (const kind of ['generation', 'placement'] as const)
  test(`dragging inside exactly overlapping boxes moves only the selected ${kind} box`, async ({
    page,
  }) => {
    const records: Records = []
    await installFakeApi(page, { sprites: { seed: 'baseReview', settings, records } })
    await page.goto(`/2d/background/${SPRITE_IDS.job}`)
    const body = await page.evaluate(
      (id) => fetch(`/api/jobs/${id}`).then((response) => response.json()),
      SPRITE_IDS.job,
    )
    const object = (body.data.sprite.assets as { plan: SavedPlan }[]).find(
      (asset) => asset.plan.role === 'object',
    )!
    // 저장된 자리 없는 오브젝트, 자리 상자가 ROI 와 완전히 겹침
    object.plan.placements = null
    await page.route(`**/api/jobs/${SPRITE_IDS.job}`, (route) =>
      route.request().method() === 'GET' ? route.fulfill({ json: body }) : route.fallback(),
    )
    await page.reload()
    const review = reviewOf(page)
    await editBoxes(review)
    const button = review.getByRole('button', {
      name: kind === 'generation' ? '생성 기준 선택 3' : '자리 1 선택 3',
      exact: true,
    })
    await button.click()
    const at = await frameOf(page)
    const start = at(0.25, 0.25)
    const end = at(0.35, 0.3)
    await page.mouse.move(start.x, start.y)
    await page.mouse.down()
    await page.mouse.move(end.x, end.y)
    await page.mouse.up()
    // 겹친 상자 중 선택한 상자를 끌고 선택 유지
    await expect(button).toHaveAttribute('aria-pressed', 'true')
    await review.getByRole('button', { name: '계획 저장', exact: true }).click()
    await expect.poll(() => records.filter((r) => r.path.endsWith('/plan')).length).toBe(1)
    const saved = savedObject(records, object.plan.id)
    const moved = kind === 'generation' ? saved.sourceBounds : saved.placements![0]!
    expect(moved.x).toBeCloseTo(0.1, 2)
    expect(moved.y).toBeCloseTo(0.05, 2)
    expect(moved.w).toBe(ROI.w)
    expect(moved.h).toBe(ROI.h)
    // 다른 상자는 끌기 전 좌표, 자리 복사본은 생성 기준을 따라가지 않음
    if (kind === 'generation') expect(saved.placements).toEqual([ROI])
    else expect(saved.sourceBounds).toEqual(ROI)
  })

test('a chip lying over a smaller box selects its own target', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const body = await page.evaluate(
    (id) => fetch(`/api/jobs/${id}`).then((response) => response.json()),
    SPRITE_IDS.job,
  )
  const object = (body.data.sprite.assets as { plan: SavedPlan }[]).find(
    (asset) => asset.plan.role === 'object',
  )!
  // 원경 배경 칩(원본 중앙)이 더 작은 오브젝트 상자 위에 놓임
  object.plan.sourceBounds = { x: 0.4, y: 0.4, w: 0.5, h: 0.5 }
  object.plan.placements = null
  await page.route(`**/api/jobs/${SPRITE_IDS.job}`, (route) =>
    route.request().method() === 'GET' ? route.fulfill({ json: body }) : route.fallback(),
  )
  await page.reload()
  const review = reviewOf(page)
  await editBoxes(review)
  const chip = review.getByTestId('overlay-chip').filter({ hasText: '1. 후경 지면' })
  await chip.scrollIntoViewIfNeeded()
  const rect = (await chip.boundingBox())!
  await page.mouse.click(rect.x + rect.width / 2, rect.y + rect.height / 2)
  await expect(chip).toHaveAttribute('aria-pressed', 'true')
  await expect(
    review.getByRole('button', { name: '생성 기준 선택 3', exact: true }),
  ).toHaveAttribute('aria-pressed', 'false')
})

test('overlapping source and placement boxes are keyboard selectable', async ({ page }) => {
  const records: Records = []
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings, records } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const body = await page.evaluate(
    (id) => fetch(`/api/jobs/${id}`).then((response) => response.json()),
    SPRITE_IDS.job,
  )
  const object = (body.data.sprite.assets as { plan: SavedPlan }[]).find(
    (asset) => asset.plan.role === 'object',
  )!
  // 저장된 자리 없는 오브젝트, 자리 상자가 ROI 와 완전히 겹침
  object.plan.placements = null
  await page.route(`**/api/jobs/${SPRITE_IDS.job}`, (route) =>
    route.request().method() === 'GET' ? route.fulfill({ json: body }) : route.fallback(),
  )
  await page.reload()
  const review = reviewOf(page)
  await editBoxes(review)
  const boxes = review.locator('[data-testid="overlay-box"][data-part="오브젝트 바위"]')
  await expect(boxes).toHaveCount(2)
  expect(await boxes.nth(0).boundingBox()).toEqual(await boxes.nth(1).boundingBox())
  const source = review.getByRole('button', { name: '생성 기준 선택 3', exact: true })
  const placement = review.getByRole('button', { name: '자리 1 선택 3', exact: true })
  await source.focus()
  await page.keyboard.press('Enter')
  await expect(source).toHaveAttribute('aria-pressed', 'true')
  await expect(placement).toHaveAttribute('aria-pressed', 'false')
  await expect(review.getByTestId('overlay-handle')).toHaveCount(8)
  await review.getByText('정밀 조정 3', { exact: true }).click()
  await page.getByLabel('ROI x 3', { exact: true }).fill('0.1')
  await placement.focus()
  await page.keyboard.press('Space')
  await expect(placement).toHaveAttribute('aria-pressed', 'true')
  await expect(source).toHaveAttribute('aria-pressed', 'false')
  await expect(page.getByLabel('ROI x 3', { exact: true })).toHaveCount(0)
  // 생성 기준 이동에도 자리는 이전 ROI 에 고정
  await expectBounds(page, '자리 1 ROI', ROI)
  await page.getByLabel('자리 1 ROI y 3', { exact: true }).fill('0.2')
  await review.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect.poll(() => records.filter((r) => r.path.endsWith('/plan')).length).toBe(1)
  const saved = savedObject(records, object.plan.id)
  expect(saved.sourceBounds).toEqual({ ...ROI, x: 0.1 })
  expect(saved.placements).toEqual([{ ...ROI, y: 0.2 }])
})

test('role change clears placements in the saved body', async ({ page }) => {
  const records: Records = []
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings, records } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = reviewOf(page)
  const role = review.getByRole('combobox', { name: '에셋 역할 3', exact: true })
  const pick = review.getByRole('button', { name: '자리 1 선택 3', exact: true })
  await pick.click()
  await expect(pick).toHaveAttribute('aria-pressed', 'true')
  await role.click()
  await page.getByRole('option', { name: '반복 타일', exact: true }).click()
  // 사라진 자리 선택 해제
  await expect(pick).toHaveCount(0)
  await expect(review.getByRole('button', { name: '대상 선택 3', exact: true })).toHaveAttribute(
    'aria-pressed',
    'false',
  )
  await expect(review.getByTestId('overlay-handle')).toHaveCount(0)
  await expect(review.locator('[data-testid="overlay-box"][data-kind="placement"]')).toHaveCount(0)
  await role.click()
  await page.getByRole('option', { name: '오브젝트', exact: true }).click()
  await expect(pick).toHaveAttribute('aria-pressed', 'false')
  await role.click()
  await page.getByRole('option', { name: '반복 타일', exact: true }).click()
  await review.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect.poll(() => records.filter((r) => r.path.endsWith('/plan')).length).toBe(1)
  // Fake 도 서버처럼 오브젝트 외 자리 거부, 저장 성공 확인 먼저
  await expect(review.getByRole('button', { name: '계획 저장', exact: true })).toBeDisabled()
  await expect(review.getByRole('alert').filter({ hasText: '자리' })).toHaveCount(0)
  const plans = records.find((r) => r.path.endsWith('/plan'))!.body.assets as SavedPlan[]
  expect(plans[2]!.role).toBe('tile')
  expect(plans[2]!.placements).toBeNull()
})

test('unsaved placement edits survive refresh and conflict', async ({ page }) => {
  const records: Records = []
  await installFakeApi(page, {
    sprites: { seed: 'baseReview', settings, records, conflictOnce: true },
  })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = reviewOf(page)
  await editBoxes(review)
  const boxes = review.locator('[data-testid="overlay-box"][data-kind="placement"]')
  const pick = (k: number) => review.getByRole('button', { name: `자리 ${k} 선택 3`, exact: true })
  const save = review.getByRole('button', { name: '계획 저장', exact: true })
  const at = await frameOf(page)
  const start = at(0.625, 0.25)
  const end = at(0.525, 0.35)
  await page.mouse.move(start.x, start.y)
  await page.mouse.down()
  await page.mouse.move(end.x, end.y)
  await page.mouse.up()
  await review.getByText('정밀 조정 3', { exact: true }).click()
  const moved = { ...FIRST, x: 0.4, y: 0.1 }
  await expectBounds(page, '자리 1 ROI', moved)
  await review.getByRole('button', { name: '자리 추가 3', exact: true }).click()
  await expect(pick(3)).toHaveAttribute('aria-pressed', 'true')
  await page.getByRole('button', { name: '서버 상태 새로고침', exact: true }).click()
  await expect(boxes).toHaveCount(3)
  await expect(pick(3)).toHaveAttribute('aria-pressed', 'true')
  await save.click()
  await expect(review.getByRole('button', { name: '서버 계획으로 다시 불러오기' })).toBeVisible()
  expect(records.filter((record) => record.path.endsWith('/plan'))).toHaveLength(1)
  // 미저장 draft 와 그 선택 유지
  await expect(boxes).toHaveCount(3)
  await expect(pick(3)).toHaveAttribute('aria-pressed', 'true')
  await pick(1).click()
  await expectBounds(page, '자리 1 ROI', moved)
  await pick(3).click()
  await review.getByRole('button', { name: '서버 계획으로 다시 불러오기' }).click()
  // 서버 계획에 없는 자리 선택 해제
  await expect(boxes).toHaveCount(2)
  await expect(pick(3)).toHaveCount(0)
  await expect(pick(1)).toHaveAttribute('aria-pressed', 'false')
  await expect(pick(2)).toHaveAttribute('aria-pressed', 'false')
  await expect(
    review.getByRole('button', { name: '선택한 자리 삭제 3', exact: true }),
  ).toBeDisabled()
  await expect(save).toBeDisabled()
  await pick(1).click()
  await expectBounds(page, '자리 1 ROI', FIRST)
})

test('deleting a placement or the target during a drag leaves no late commit or selection', async ({
  page,
}) => {
  const errors: string[] = []
  page.on('pageerror', (error) => errors.push(error.message))
  // 대상 삭제가 있는 planReview
  await installFakeApi(page, { sprites: { seed: 'planReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = reviewOf(page)
  const at = await frameOf(page)
  const drag = async (from: { x: number; y: number }, to: { x: number; y: number }) => {
    await page.mouse.move(from.x, from.y)
    await page.mouse.down()
    await page.mouse.move(to.x, to.y)
  }
  await drag(at(0.625, 0.25), at(0.525, 0.35))
  await review
    .getByRole('button', { name: '선택한 자리 삭제 3', exact: true })
    .dispatchEvent('click')
  await page.mouse.up()
  await expect(review.locator('[data-testid="overlay-box"][data-kind="placement"]')).toHaveCount(1)
  await expect(review.getByRole('button', { name: '자리 1 선택 3', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await review.getByText('정밀 조정 3', { exact: true }).click()
  // 남은 자리 2 는 끌던 좌표를 받지 않음
  await expectBounds(page, '자리 1 ROI', SECOND)

  const frame = await frameOf(page)
  await drag(frame(0.25, 0.25), frame(0.35, 0.35))
  await review.getByRole('button', { name: '대상 3 삭제', exact: true }).dispatchEvent('click')
  await page.mouse.up()
  await expect(review.getByTestId('sprite-plan-card')).toHaveCount(2)
  await expect(review.locator('[data-part="오브젝트 바위"]')).toHaveCount(0)
  await expect(review.getByTestId('overlay-handle')).toHaveCount(0)
  await expect(review.locator('[data-testid="overlay-chip"][aria-pressed="true"]')).toHaveCount(0)
  expect(errors).toEqual([])
})
