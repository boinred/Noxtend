import { test, expect } from '@playwright/test'
import { installFakeApi } from './fakeApi'
import { SPRITE_IDS, editBoxes, type SpriteFakeOptions } from './spriteFakeApi'

test('ROI selects directly and commits local move, resize and precise fields only on save', async ({
  page,
}, testInfo) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { seed: 'baseReview', records } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const panel = page.getByRole('region', { name: '제작 계획 검수' })
  await editBoxes(panel)
  await panel.getByText('정밀 조정 1', { exact: true }).click()
  await page.getByLabel('ROI w 1', { exact: true }).fill('0.6')
  await page.getByLabel('ROI h 1', { exact: true }).fill('0.5')
  const image = panel.getByTestId('overlay-image')
  await expect(image).toBeVisible()
  await image.scrollIntoViewIfNeeded()
  const rect = (await image.boundingBox())!
  const before = records.length
  await page.mouse.click(rect.x + rect.width * 0.1, rect.y + rect.height * 0.1)
  await expect(panel.getByRole('button', { name: '대상 선택 1', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(panel.getByTestId('overlay-handle')).toHaveCount(8)
  await expect(page.getByLabel('ROI x 1', { exact: true })).toHaveValue('0')
  await page.mouse.move(rect.x + rect.width * 0.1, rect.y + rect.height * 0.1)
  await page.mouse.down()
  await page.mouse.move(rect.x + rect.width * 0.2, rect.y + rect.height * 0.2)
  await expect
    .poll(async () => Number(await page.getByLabel('ROI x 1', { exact: true }).inputValue()))
    .toBeCloseTo(0.1, 2)
  await page.mouse.up()
  expect(records.length).toBe(before)
  await expect(page.getByLabel('ROI w 1', { exact: true })).toHaveValue('0.6')
  const handle = panel.locator('[data-handle="se"]')
  const box = (await handle.boundingBox())!
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
  await page.mouse.down()
  await page.mouse.move(rect.x + rect.width + 40, rect.y + rect.height + 40)
  await page.mouse.up()
  await expect
    .poll(async () => Number(await page.getByLabel('ROI w 1', { exact: true }).inputValue()))
    .toBeCloseTo(0.9, 2)
  await panel.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect.poll(() => records.filter((r) => r.path.endsWith('/plan')).length).toBe(1)
  const payload = records.find((record) => record.path.endsWith('/plan'))!.body
  const assets = payload.assets as {
    sourceBounds: { x: number; y: number; w: number; h: number }
  }[]
  expect(assets[0]!.sourceBounds.x).toBeCloseTo(0.1, 2)
  expect(assets[0]!.sourceBounds.w).toBeCloseTo(0.9, 2)
  await page.emulateMedia({ colorScheme: 'dark' })
  await panel
    .getByTestId('parts-overlay')
    .screenshot({ path: testInfo.outputPath('roi-overlay-1440-dark.png') })
  await page.setViewportSize({ width: 390, height: 844 })
  await panel.getByRole('button', { name: '대상 선택 2', exact: true }).click()
  await panel
    .locator('[data-testid="sprite-plan-card"]')
    .nth(1)
    .screenshot({ path: testInfo.outputPath('roi-card-390-dark.png') })
  await panel
    .getByTestId('parts-overlay')
    .screenshot({ path: testInfo.outputPath('roi-overlay-390-dark.png') })
})

for (const reason of ['Escape', 'pointercancel', 'blur']) {
  test(`ROI ${reason} cancels preview and outside release cannot commit`, async ({ page }) => {
    await installFakeApi(page, { sprites: { seed: 'baseReview' } })
    await page.goto(`/2d/background/${SPRITE_IDS.job}`)
    const panel = page.getByRole('region', { name: '제작 계획 검수' })
    await editBoxes(panel)
    await page.getByText('정밀 조정 1', { exact: true }).click()
    await page.getByLabel('ROI w 1', { exact: true }).fill('0.6')
    await page.getByLabel('ROI h 1', { exact: true }).fill('0.5')
    const image = page.getByTestId('overlay-image')
    await image.scrollIntoViewIfNeeded()
    const rect = (await image.boundingBox())!
    await page.mouse.move(rect.x + rect.width * 0.1, rect.y + rect.height * 0.1)
    await page.mouse.down()
    await page.mouse.move(rect.x + rect.width * 0.2, rect.y + rect.height * 0.2)
    if (reason === 'Escape') await page.keyboard.press('Escape')
    else
      await page.evaluate(
        (event) =>
          event === 'blur'
            ? window.dispatchEvent(new Event('blur'))
            : document.dispatchEvent(new PointerEvent('pointercancel')),
        reason,
      )
    await page.mouse.move(rect.x + rect.width + 20, rect.y + rect.height + 20)
    await page.mouse.up()
    await expect(page.getByLabel('ROI x 1', { exact: true })).toHaveValue('0')
    await expect(page.getByLabel('ROI y 1', { exact: true })).toHaveValue('0')
  })
}

test('ROI locked target rejects drag and supports label selection without local changes', async ({
  page,
}) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', runningBase: true } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const panel = page.getByRole('region', { name: '제작 계획 검수' })
  await editBoxes(panel)
  await page.getByTestId('overlay-chip').nth(1).click()
  await expect(page.getByRole('button', { name: '대상 선택 2', exact: true })).toBeDisabled()
  await expect(page.getByTestId('overlay-chip').nth(1)).toHaveAttribute('aria-pressed', 'true')
  const box = page.getByTestId('overlay-box').nth(1)
  await box.dispatchEvent('pointerdown', {
    pointerId: 1,
    isPrimary: true,
    button: 0,
    clientX: 200,
    clientY: 200,
  })
  await page.evaluate(() => {
    document.dispatchEvent(
      new PointerEvent('pointermove', { pointerId: 1, clientX: 400, clientY: 400 }),
    )
    document.dispatchEvent(
      new PointerEvent('pointerup', { pointerId: 1, clientX: 400, clientY: 400 }),
    )
  })
  await expect(page.getByRole('button', { name: '계획 저장', exact: true })).toBeDisabled()
})

test('ROI rejects secondary input, preserves invalid precision and cancels on server reload', async ({
  page,
}) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview' } })
  const detail = page.waitForResponse((response) =>
    response.url().endsWith(`/api/jobs/${SPRITE_IDS.job}`),
  )
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const panel = page.getByRole('region', { name: '제작 계획 검수' })
  await editBoxes(panel)
  const envelope = await (await detail).json()
  await page.getByText('정밀 조정 1', { exact: true }).click()
  const width = page.getByLabel('ROI w 1', { exact: true })
  await width.fill('')
  const box = page.getByTestId('overlay-box').first()
  await box.dispatchEvent('pointerdown', { pointerId: 1, isPrimary: true, button: 0 })
  await page.evaluate(() => document.dispatchEvent(new PointerEvent('pointerup', { pointerId: 1 })))
  await expect(width).toHaveValue('')
  await width.fill('0.6')
  for (const input of [
    { button: 2, isPrimary: true },
    { button: 0, isPrimary: false },
  ]) {
    await box.dispatchEvent('pointerdown', { pointerId: 4, clientX: 100, clientY: 100, ...input })
    await page.evaluate(() => {
      document.dispatchEvent(
        new PointerEvent('pointermove', { pointerId: 4, clientX: 300, clientY: 300 }),
      )
      document.dispatchEvent(new PointerEvent('pointerup', { pointerId: 4 }))
    })
    await expect(page.getByLabel('ROI x 1', { exact: true })).toHaveValue('0')
  }
  const image = page.getByTestId('overlay-image')
  await image.scrollIntoViewIfNeeded()
  const rect = (await image.boundingBox())!
  await page.mouse.move(rect.x + rect.width * 0.1, rect.y + rect.height * 0.1)
  await page.mouse.down()
  await page.mouse.move(rect.x + rect.width * 0.2, rect.y + rect.height * 0.1)
  envelope.data.sprite.reviewRevision++
  await page.route(`**/api/jobs/${SPRITE_IDS.job}`, (route) => route.fulfill({ json: envelope }))
  await page.getByRole('button', { name: '서버 상태 새로고침', exact: true }).dispatchEvent('click')
  await expect(page.getByText('서버 계획이 변경되었습니다.', { exact: false })).toBeVisible()
  await page.getByRole('button', { name: '서버 계획으로 다시 불러오기' }).dispatchEvent('click')
  await page.mouse.up()
  await expect(width).toHaveValue('1')
  await expect(page.getByLabel('ROI x 1', { exact: true })).toHaveValue('0')
  await expect(page.getByRole('button', { name: '계획 저장', exact: true })).toBeDisabled()
})

test('ROI target becoming active cancels a pending drag', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview' } })
  const detail = page.waitForResponse((response) =>
    response.url().endsWith(`/api/jobs/${SPRITE_IDS.job}`),
  )
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const panel = page.getByRole('region', { name: '제작 계획 검수' })
  await editBoxes(panel)
  const envelope = await (await detail).json()
  await page.getByText('정밀 조정 1', { exact: true }).click()
  await page.getByLabel('ROI w 1', { exact: true }).fill('0.6')
  const image = page.getByTestId('overlay-image')
  await image.scrollIntoViewIfNeeded()
  const rect = (await image.boundingBox())!
  await page.mouse.move(rect.x + rect.width * 0.1, rect.y + rect.height * 0.1)
  await page.mouse.down()
  await page.mouse.move(rect.x + rect.width * 0.2, rect.y + rect.height * 0.1)
  const taskId = envelope.data.sprite.assets[0].frames[0].currentTaskId
  const task = envelope.data.tasks.find((candidate: { id: string }) => candidate.id === taskId)
  task.status = 'running'
  await page.route(`**/api/jobs/${SPRITE_IDS.job}`, (route) => route.fulfill({ json: envelope }))
  await page.getByRole('button', { name: '서버 상태 새로고침', exact: true }).dispatchEvent('click')
  await expect(page.getByLabel('ROI x 1', { exact: true })).toBeDisabled()
  await page.mouse.up()
  await expect(page.getByLabel('ROI x 1', { exact: true })).toHaveValue('0')
  await expect(page.getByLabel('ROI w 1', { exact: true })).toHaveValue('0.6')
})

test('ROI directly selects a small nested region above the later full-image region without editing', async ({
  page,
}) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { seed: 'baseReview', records } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const panel = page.getByRole('region', { name: '제작 계획 검수' })
  await editBoxes(panel)
  await panel.getByText('정밀 조정 1', { exact: true }).click()
  const bounds = { x: '0.32', y: '0.27', w: '0.08', h: '0.1' }
  for (const axis of ['w', 'h', 'x', 'y'] as const) {
    await page.getByLabel(`ROI ${axis} 1`, { exact: true }).fill(bounds[axis])
  }
  await panel.getByText('정밀 조정 2', { exact: true }).click()
  for (const [axis, value] of Object.entries({ x: '0', y: '0', w: '1', h: '1' })) {
    await expect(page.getByLabel(`ROI ${axis} 2`, { exact: true })).toHaveValue(value)
  }
  await expect(panel.getByTestId('overlay-handle')).toHaveCount(0)
  const image = panel.getByTestId('overlay-image')
  await image.scrollIntoViewIfNeeded()
  const rect = (await image.boundingBox())!
  const before = records.length
  await page.mouse.click(rect.x + rect.width * 0.34, rect.y + rect.height * 0.3)
  await expect(panel.getByRole('button', { name: '대상 선택 1', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(panel.getByRole('button', { name: '대상 선택 2', exact: true })).toHaveAttribute(
    'aria-pressed',
    'false',
  )
  await expect(panel.getByTestId('overlay-chip').first()).toHaveAttribute('aria-pressed', 'true')
  await expect(panel.getByTestId('overlay-handle')).toHaveCount(8)
  const smallBox = (await panel.getByTestId('overlay-box').first().boundingBox())!
  for (const handle of await panel.getByTestId('overlay-handle').all()) {
    const handleRect = (await handle.boundingBox())!
    const centerX = handleRect.x + handleRect.width / 2
    const centerY = handleRect.y + handleRect.height / 2
    expect(centerX).toBeGreaterThanOrEqual(smallBox.x - 1)
    expect(centerX).toBeLessThanOrEqual(smallBox.x + smallBox.width + 1)
    expect(centerY).toBeGreaterThanOrEqual(smallBox.y - 1)
    expect(centerY).toBeLessThanOrEqual(smallBox.y + smallBox.height + 1)
  }
  for (const [axis, value] of Object.entries(bounds)) {
    await expect(page.getByLabel(`ROI ${axis} 1`, { exact: true })).toHaveValue(value)
  }
  for (const [axis, value] of Object.entries({ x: '0', y: '0', w: '1', h: '1' })) {
    await expect(page.getByLabel(`ROI ${axis} 2`, { exact: true })).toHaveValue(value)
  }
  expect(records.length).toBe(before)
})
