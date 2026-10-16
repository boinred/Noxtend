import { test, expect } from '@playwright/test'
import { installFakeApi } from './fakeApi'
import { SPRITE_IDS, type SpriteFakeOptions } from './spriteFakeApi'
const settings = { view: 'isometric', outputKind: 'tiles', tileWidth: 128, repeat: 'both' } as const

test('roles expose per asset output and validation with single image preview', async ({
  page,
}, info) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  await expect(review.getByRole('combobox', { name: /에셋 역할/ })).toHaveCount(3)
  await expect(review.getByText('출력 128×72px')).toBeVisible()
  await expect(review.getByText('출력 64×36px')).toBeVisible()
  await expect(page.getByTestId('sprite-analysis-assets')).toContainText(
    '반복 타일 1 · 오브젝트 1 · 원경 배경 1',
  )
  const preview = page.getByRole('region', { name: '배경 미리보기' })
  await expect(preview.getByRole('img', { name: '단일 에셋 검수' }).locator('image')).toHaveCount(1)
  await preview.getByRole('combobox').click()
  await page.getByRole('option', { name: '오브젝트 바위', exact: true }).click()
  const objectPreview = preview.getByRole('img', { name: '단일 에셋 검수' })
  await expect(objectPreview).toHaveAttribute('viewBox', '0 0 64 36')
  await expect(objectPreview.locator('image')).toHaveCount(1)
  await expect(objectPreview.locator('image')).toHaveAttribute('width', '64')
  await expect(objectPreview.locator('image')).toHaveAttribute('height', '36')
  await preview.getByRole('combobox').click()
  await page.getByRole('option', { name: '전경 나무', exact: true }).click()
  await expect(preview.getByRole('img', { name: '반복 경계 검수' }).locator('image')).toHaveCount(9)
  await review.getByLabel('격자 칸 너비 (원본 px)', { exact: true }).fill('10')
  await expect(review.getByRole('alert').filter({ hasText: '오브젝트 출력 크기' })).toContainText(
    '오브젝트 출력 크기는 1~1024픽셀',
  )
  await expect(review.getByRole('button', { name: '계획 저장', exact: true })).toBeDisabled()
  await page.screenshot({ path: info.outputPath('roles-1440.png'), fullPage: true })
  await page.setViewportSize({ width: 390, height: 844 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
  await page.screenshot({ path: info.outputPath('roles-390.png'), fullPage: true })
})

test('source comparison overlays current base images on their source regions', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings, partialBases: true } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const compare = page
    .getByRole('region', { name: '원본 비교' })
    .getByRole('img', { name: '원본 위 재배치' })
  await expect(compare).toHaveAttribute('viewBox', '0 0 320 180')
  const images = compare.locator('image')
  await expect(images).toHaveCount(3)
  await expect(images.nth(0)).toHaveAttribute(
    'href',
    new RegExp(`/api/uploads/${SPRITE_IDS.upload}/`),
  )
  // 원경 배경은 원본 전체, 오브젝트는 ROI
  for (const [index, x, y, width, height] of [
    [1, 0, 0, 320, 180],
    [2, 0, 0, 160, 90],
  ])
    for (const [name, value] of Object.entries({ x, y, width, height }))
      await expect(images.nth(index!)).toHaveAttribute(name, String(value))
  // 기준 이미지 없는 타일은 ROI 중심의 한 칸 윤곽
  const missing = compare.locator('rect')
  await expect(missing).toHaveCount(1)
  for (const [name, value] of Object.entries({ x: 0, y: 10, width: 320, height: 160 }))
    await expect(missing).toHaveAttribute(name, String(value))
  const opacity = page.getByRole('slider', { name: '재배치 투명도' })
  await expect(opacity).toHaveValue('50')
  await expect(compare.locator('g')).toHaveAttribute('opacity', '0.5')
  await opacity.fill('80')
  await expect(opacity).toHaveAttribute('aria-valuetext', '80%')
  await expect(compare.locator('g')).toHaveAttribute('opacity', '0.8')
})

test('grid draft survives refresh and conflict then reloads both and saves reordered HTTP properties', async ({
  page,
}) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, {
    sprites: {
      seed: 'completed',
      settings,
      tileOnly: true,
      reorderedPlans: true,
      conflictOnce: true,
      records,
    },
  })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const grid = page.getByLabel('격자 칸 너비 (원본 px)', { exact: true })
  const save = page.getByRole('button', { name: '계획 저장', exact: true })
  await expect(save).toBeDisabled()
  await grid.fill('160')
  await page.getByRole('button', { name: '서버 상태 새로고침', exact: true }).click()
  await expect(grid).toHaveValue('160')
  await save.click()
  await expect(page.getByRole('button', { name: '서버 계획으로 다시 불러오기' })).toBeVisible()
  await expect(grid).toHaveValue('160')
  await expect(save).toBeDisabled()
  expect(records.filter((record) => record.path.endsWith('/plan'))).toHaveLength(1)
  await page.getByRole('button', { name: '서버 계획으로 다시 불러오기' }).click()
  await expect(grid).toHaveValue('')
  await grid.fill('160')
  await save.click()
  await expect(save).toBeDisabled()
  await expect(page.getByText('변경한 계획을 저장한 뒤 승인해 주세요.')).toHaveCount(0)
  expect(records.filter((record) => record.path.endsWith('/plan')).at(-1)!.body.grid).toEqual({
    cellWidth: 0.5,
  })
  await expect(page.getByRole('link', { name: '이전 ZIP 다운로드', exact: false })).toBeVisible()
  await expect(page.getByText('최종 승인 완료 · 1프레임', { exact: true })).toHaveCount(3)
})

test('changing one role only invalidates that asset and layers have no role select', async ({
  page,
}) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { seed: 'completed', settings, records } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  await review.getByLabel('격자 칸 너비 (원본 px)', { exact: true }).fill('320')
  await review.getByRole('combobox', { name: '에셋 역할 3', exact: true }).click()
  await page.getByRole('option', { name: '반복 타일', exact: true }).click()
  await review.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect(review.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' })).toBeEnabled()
  expect(records.filter((record) => record.path.endsWith('/plan'))).toHaveLength(1)
  await review.getByRole('combobox', { name: '에셋 역할 3', exact: true }).click()
  await page.getByRole('option', { name: '오브젝트', exact: true }).click()
  await review.getByLabel('격자 칸 너비 (원본 px)', { exact: true }).fill('10')
  await expect(review.getByRole('alert').filter({ hasText: '오브젝트 출력 크기' })).toBeVisible()
  await expect(review.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' })).toBeDisabled()
  await review.getByRole('combobox', { name: '에셋 역할 3', exact: true }).click()
  await page.getByRole('option', { name: '반복 타일', exact: true }).click()
  await review.getByLabel('격자 칸 너비 (원본 px)', { exact: true }).fill('320')
  await review.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' }).click()
  await expect(page.getByText('최종 승인 완료 · 1프레임', { exact: true })).toHaveCount(2)
  await expect(
    page.getByRole('img', { name: '오브젝트 바위 기준 이미지', exact: true }),
  ).toBeVisible()
  await page.unrouteAll({ behavior: 'wait' })
  await installFakeApi(page, { sprites: { seed: 'baseReview' } })
  await page.reload()
  await expect(page.getByRole('combobox', { name: /에셋 역할/ })).toHaveCount(0)
})
