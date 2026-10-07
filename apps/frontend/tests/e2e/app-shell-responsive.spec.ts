import { expect, test } from '@playwright/test'
import { installFakeApi } from './fakeApi'

test('홈은 작업 시작과 최근 결과를 한 흐름으로 제공한다', async ({ page }) => {
  await installFakeApi(page, { seedTerminalJobs: 2 })
  await page.goto('/')

  await expect(page.getByTestId('home-cta-studio')).toBeVisible()
  await expect(page.getByTestId('section-recent').getByTestId('job-item')).toHaveCount(2)
  await expect(page.getByTestId('nav-canvas')).toHaveCount(0)
  await page.screenshot({
    path: '../../.superpowers/sdd/2026-10-06-2d-background-sprites/task-14-desktop.png',
  })

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

  await expect(page.getByTestId('nav-mobile-home').getByText('홈')).toBeVisible()
  await expect(page.getByRole('button', { name: '3D 제작 메뉴' })).toBeVisible()
  await expect(page.getByRole('button', { name: '2D 제작 메뉴' })).toBeVisible()
  await expect(page.getByTestId('nav-admin').getByText('관리자')).toBeVisible()
  await page.screenshot({
    path: '../../.superpowers/sdd/2026-10-06-2d-background-sprites/task-14-mobile.png',
  })

  await page.getByRole('button', { name: '3D 제작 메뉴' }).click()
  await page.getByRole('menuitem', { name: '3D 배경' }).click()
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

test('MobileGroups_HaveDistinctAccessibleNames', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await installFakeApi(page, { sprites: { seed: 'baseReview' } })
  await page.goto('/2d/background/10000000-0000-4000-8000-000000000001')
  const menu = page.getByRole('button', { name: '2D 제작 메뉴' })
  await expect(menu).toHaveAttribute('aria-current', 'page')
  await expect(page.getByRole('button', { name: '3D 제작 메뉴' })).not.toHaveAttribute(
    'aria-current',
  )
  await menu.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('menuitem', { name: '2D 배경', exact: true })).toHaveAttribute(
    'aria-current',
    'page',
  )
  await expect(page.getByRole('menuitem', { name: '2D 캐릭터 준비 중' })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(menu).toBeFocused()
  await page.keyboard.press('Enter')
  await page.getByRole('menuitem', { name: '2D 캐릭터 준비 중' }).click()
  await expect(page).toHaveURL(/\/2d\/character$/)
  await expect(page.getByTestId('coming-soon-screen')).toBeVisible()
  await expect(menu).toBeFocused()
  await page.getByRole('button', { name: '3D 제작 메뉴' }).click()
  await page.getByRole('menuitem', { name: '3D 오브젝트 준비 중' }).click()
  await expect(page).toHaveURL(/\/object$/)
  await expect(page.getByTestId('coming-soon-screen')).toBeVisible()
  await expect(page.getByRole('button', { name: '3D 제작 메뉴' })).toHaveAttribute(
    'aria-current',
    'page',
  )
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})

test('detail url activates its parent group and restores menu focus', async ({ page }) => {
  const menuRequests: string[] = []
  page.on('request', (request) => {
    if (request.url().includes('MobileStudioMenu-')) menuRequests.push(request.url())
  })
  await installFakeApi(page, { seedRunningJob: true, runningPolls: 20 })
  await page.goto('/background/seed-running')
  await expect(page.getByTestId('nav-background')).toHaveAttribute('aria-current', 'page')
  await expect(page.getByTestId('nav-home')).not.toHaveAttribute('aria-current')
  await expect(page.getByRole('group', { name: '3D 제작' })).toBeVisible()
  await expect(page.getByRole('group', { name: '2D 제작' })).toBeVisible()
  await page.getByTestId('sidebar-toggle').click()
  const background = page.getByTestId('nav-background')
  await expect(background).toHaveAccessibleName('3D 배경')
  await expect(page.getByTestId('nav-spriteBackground')).toHaveAccessibleName('2D 배경')
  await background.hover()
  await expect(page.getByTestId('stage-progress')).toBeVisible()
  await expect(background.getByText('3D 배경')).toHaveCSS('opacity', '1')
  await expect(background.getByText('3D 배경')).toBeVisible()
  await page.screenshot({
    path: '../../.superpowers/sdd/2026-10-06-2d-background-sprites/task-14-rail.png',
  })
  expect(menuRequests).toHaveLength(0)
  await page.goto('/admin/calls')
  await expect(page.getByTestId('nav-admin')).toHaveAttribute('aria-current', 'page')
  await page.setViewportSize({ width: 390, height: 844 })
  const menu = page.getByRole('button', { name: '3D 제작 메뉴' })
  await expect(menu).toBeVisible()
  expect(menuRequests).toHaveLength(1)
  await menu.focus()
  await page.keyboard.press('Enter')
  await page.keyboard.press('Escape')
  await expect(menu).toBeFocused()
})

test('모바일 메뉴 lazy fallback 뒤 키보드 탐색과 Escape가 유지된다', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await installFakeApi(page)
  let release!: () => void
  const ready = new Promise<void>((resolve) => {
    release = resolve
  })
  await page.route('**/assets/MobileStudioMenu-*.js', async (route) => {
    await ready
    await route.continue()
  })
  await page.goto('/2d/object')
  await expect(page.getByRole('status').filter({ hasText: '2D 메뉴 불러오는 중' })).toBeVisible()
  release()
  const menu = page.getByRole('button', { name: '2D 제작 메뉴' })
  await expect(menu).toBeVisible()
  await expect(menu).toHaveAttribute('aria-current', 'page')
  await menu.focus()
  await page.keyboard.press('Enter')
  await page.keyboard.press('ArrowDown')
  await expect(page.getByRole('menuitem', { name: '2D 오브젝트 준비 중' })).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(menu).toBeFocused()
})
