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
