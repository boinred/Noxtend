import { expect, test } from '@playwright/test'
import { installFakeApi } from './fakeApi'

test('홈은 작업 시작과 최근 결과를 한 흐름으로 제공한다', async ({ page }) => {
  await installFakeApi(page, { seedTerminalJobs: 2 })
  await page.goto('/')

  await expect(page.getByTestId('home-cta-studio')).toBeVisible()
  await expect(page.getByTestId('section-recent').getByTestId('job-item')).toHaveCount(2)
  await expect(page.getByTestId('nav-canvas')).toHaveCount(0)

  await page.getByTestId('home-cta-studio').click()
  await expect(page).toHaveURL(/\/background$/)
  await expect(page.getByTestId('background-studio')).toBeVisible()
})

test('제거된 캔버스 주소는 홈으로 안전하게 돌아온다', async ({ page }) => {
  await installFakeApi(page)
  await page.goto('/canvas')

  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByTestId('home-screen')).toBeVisible()
})

test('모바일에서는 주요 탐색이 하단 레일로 유지된다', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await installFakeApi(page)
  await page.goto('/')

  const sidebar = page.getByTestId('sidebar')
  const box = await sidebar.boundingBox()
  expect(box).not.toBeNull()
  expect(Math.abs(box!.y + box!.height - 844)).toBeLessThanOrEqual(1)

  await expect(page.getByTestId('nav-home').getByText('홈')).toBeVisible()
  await expect(page.getByTestId('nav-background').getByText('배경')).toBeVisible()
  await expect(page.getByTestId('nav-admin').getByText('관리자')).toBeVisible()

  await page.getByTestId('nav-background').click()
  await expect(page.getByTestId('background-studio')).toBeVisible()
})

test('데스크톱 사이드바 접힘과 테마 선택이 화면 이동 후에도 유지된다', async ({ page }) => {
  await installFakeApi(page)
  await page.goto('/')

  await page.getByTestId('sidebar-toggle').click()
  await expect(page.getByTestId('sidebar')).toHaveAttribute('data-collapsed', 'true')

  await page.getByTestId('theme-toggle').click()
  const theme = await page.locator('html').getAttribute('data-theme')
  await page.getByTestId('nav-background').click()

  await expect(page.getByTestId('sidebar')).toHaveAttribute('data-collapsed', 'true')
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme!)
})
