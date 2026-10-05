import { expect, test, type Page } from '@playwright/test'
import { installFakeApi } from './fakeApi'

// 계산된 색과 CSS 토큰의 브라우저 정규화 값 대조
async function expectTokenColor(
  page: Page,
  selector: string,
  property: 'background-color' | 'border-color' | 'color',
  token: string,
) {
  const result = await page.evaluate(
    ({ selector, property, token }) => {
      const element = document.querySelector(selector)
      if (!element) throw new Error(`요소를 찾을 수 없음: ${selector}`)

      const probe = document.createElement('span')
      probe.style.setProperty(property, `var(${token})`)
      document.body.append(probe)

      const actual = getComputedStyle(element).getPropertyValue(property)
      const expected = getComputedStyle(probe).getPropertyValue(property)
      probe.remove()
      return { actual, expected }
    },
    { selector, property, token },
  )

  expect(result.actual).toBe(result.expected)
}

test.describe('design-system 계산값 기준선', () => {
  test('V-1 화면 폭과 V-2 사이드바 폭이 승인 규약과 같다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/')

    await expect(page.getByTestId('page-inner')).toHaveCSS('width', '1176px')
    await expect(page.getByTestId('sidebar')).toHaveCSS('width', '200px')

    await page.goto('/admin/prices')
    await expect(page.getByTestId('page-inner')).toHaveCSS('width', '1176px')

    await page.getByTestId('sidebar-toggle').click()
    await expect(page.getByTestId('sidebar')).toHaveCSS('width', '56px')
  })

  test('V-3 다크·라이트의 화면 표면이 의미 토큰을 사용한다', async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('nextend.theme', 'dark'))
    await installFakeApi(page)
    await page.goto('/')

    await expectTokenColor(page, 'body', 'background-color', '--background')
    await expectTokenColor(page, 'body', 'color', '--foreground')
    await expectTokenColor(page, '[data-testid="sidebar"]', 'background-color', '--sidebar-bg')

    await page.getByTestId('theme-toggle').click()
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
    await expectTokenColor(page, 'body', 'background-color', '--background')
    await expectTokenColor(page, 'body', 'color', '--foreground')
    await expectTokenColor(page, '[data-testid="sidebar"]', 'background-color', '--sidebar-bg')
  })

  test('V-5 1440·1280 화면에 가로 스크롤이 없다', async ({ page }) => {
    await installFakeApi(page)

    for (const width of [1440, 1280]) {
      await page.setViewportSize({ width, height: 900 })
      await page.goto('/admin/prices')

      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
      expect(overflow, `${width}px 뷰포트 가로 초과`).toBeLessThanOrEqual(0)
    }
  })

  test('V-6 버튼 4종의 기본·hover 표현이 의미 토큰을 따른다', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await installFakeApi(page)
    await page.goto('/admin/providers')

    // 관리자 화면은 지연 로드다 (사이클 #7 Check G-1). `expectTokenColor` 는
    // `page.evaluate` 로 즉시 DOM 을 읽으므로 청크가 오기를 기다리지 않는다 —
    // 여기서 한 번 기다려야 그 뒤의 단정들이 성립한다
    await expect(page.getByTestId('provider-add')).toBeVisible()

    await expectTokenColor(page, '[data-testid="provider-add"]', 'background-color', '--primary')
    await expectTokenColor(page, '[data-testid="provider-add"]', 'color', '--primary-foreground')
    await expectTokenColor(page, '[data-testid="provider-test"]', 'color', '--foreground')
    await expectTokenColor(page, '[data-testid="provider-test"]', 'border-color', '--border')
    await expectTokenColor(page, '[data-testid="provider-delete"]', 'color', '--destructive')

    await page.getByTestId('provider-test').first().hover()
    await expectTokenColor(page, '[data-testid="provider-test"]', 'background-color', '--accent')
    await expectTokenColor(
      page,
      '[data-testid="provider-test"]',
      'border-color',
      '--muted-foreground',
    )

    await page.getByTestId('provider-delete').first().hover()
    await expect(page.getByTestId('provider-delete').first()).not.toHaveCSS(
      'background-color',
      'rgba(0, 0, 0, 0)',
    )
    await expectTokenColor(page, '[data-testid="provider-delete"]', 'border-color', '--destructive')

    await page.getByTestId('provider-add').click()
    await expectTokenColor(page, '[data-testid="provider-cancel"]', 'color', '--muted-foreground')
    await page.getByTestId('provider-cancel').hover()
    await expectTokenColor(page, '[data-testid="provider-cancel"]', 'background-color', '--accent')
    await expectTokenColor(page, '[data-testid="provider-cancel"]', 'color', '--foreground')
  })

  test('V-7 비활성 입력이 활성 입력과 계산값으로 구별된다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prices')
    await page.getByTestId('price-edit').first().click()

    const styles = await page.evaluate(() => {
      const disabled = document.querySelector('[data-testid="price-model-input"]')
      const active = document.querySelector('[data-testid="price-effective-input"]')
      if (!disabled || !active) throw new Error('단가 입력 요소 누락')

      return {
        disabledBackground: getComputedStyle(disabled).backgroundColor,
        disabledBorderStyle: getComputedStyle(disabled).borderStyle,
        activeBackground: getComputedStyle(active).backgroundColor,
        activeBorderStyle: getComputedStyle(active).borderStyle,
      }
    })

    expect(styles.disabledBackground).not.toBe(styles.activeBackground)
    expect(styles.disabledBorderStyle).toBe('dashed')
    expect(styles.activeBorderStyle).toBe('solid')
  })

  test('V-8 공급자 폼은 입력 폭, 프롬프트 편집은 관리자 공통 전체 폭이다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/providers')
    await page.getByTestId('provider-add').click()

    const providerInputWidth = await page
      .getByTestId('provider-name-input')
      .evaluate((element) => element.getBoundingClientRect().width)

    await page.goto('/admin/prompts/analyze')
    const promptInputWidth = await page
      .getByTestId('prompt-system-input')
      .evaluate((element) => element.getBoundingClientRect().width)

    // 짧은 입력 몇 개짜리 공급자 폼은 form 폭(720)을 지킨다
    expect(providerInputWidth).toBe(720)
    // 프롬프트 편집은 긴 본문·스키마를 다루는 작업면이라 그리드 등 다른 관리자
    // 화면과 같은 max 폭을 쓴다 — 화면 사이 폭 널뜀 제거가 목적이다
    expect(promptInputWidth).toBeGreaterThan(providerInputWidth)
  })

  test('V-9 공급자 사용 용도는 색과 표식이 모두 다르다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/providers')

    const textBadge = page
      .getByTestId('provider-capability')
      .filter({ hasText: '텍스트 분석' })
      .first()
    const imageBadge = page
      .getByTestId('provider-capability')
      .filter({ hasText: '이미지 생성' })
      .first()

    for (const theme of ['dark', 'light']) {
      await page.locator('html').evaluate((element, value) => {
        element.setAttribute('data-theme', value)
      }, theme)

      const [textStyle, imageStyle] = await Promise.all([
        textBadge.evaluate((element) => ({
          background: getComputedStyle(element).backgroundColor,
          border: getComputedStyle(element).borderColor,
          color: getComputedStyle(element).color,
        })),
        imageBadge.evaluate((element) => ({
          background: getComputedStyle(element).backgroundColor,
          border: getComputedStyle(element).borderColor,
          color: getComputedStyle(element).color,
        })),
      ])

      expect(textStyle, `${theme} 테마 용도 색상`).not.toEqual(imageStyle)
    }

    await expect(textBadge.locator('[data-capability-mark]')).toHaveText('T')
    await expect(imageBadge.locator('[data-capability-mark]')).toHaveText('◈')
  })
})
