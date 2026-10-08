import { test, expect, type Page } from '@playwright/test'
import { readFile } from 'node:fs/promises'
import { installFakeApi } from './fakeApi'
import { SPRITE_IDS, SPRITE_SOURCE_PNG, type SpriteFakeOptions } from './spriteFakeApi'

declare global {
  interface Window {
    spriteRaf: { requested: number; canceled: number }
  }
}

async function choose(page: Page, label: string, value: string) {
  await page.getByRole('combobox', { name: label, exact: true }).click()
  await page.getByRole('option', { name: value, exact: true }).click()
}
async function loopPlan(page: Page) {
  await page.goto('/2d/background')
  await page.getByTestId('mode-tab-image').click()
  await page
    .getByTestId('image-input')
    .setInputFiles({ name: 'source.png', mimeType: 'image/png', buffer: SPRITE_SOURCE_PNG })
  await choose(page, '시점', '횡스크롤')
  await choose(page, '결과 유형', '배경 레이어')
  await page.getByRole('button', { name: '2D 분석 시작', exact: true }).click()
  await page.getByLabel('루프 애니메이션 1', { exact: true }).check()
  await choose(page, '프레임 수 1', '4프레임')
  await page.getByLabel('FPS 1', { exact: true }).fill('4')
  await page.getByLabel('동작 설명 1', { exact: true }).fill('바람에 천천히 흔들림')
  await page.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect(page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' })).toBeEnabled()
  await page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' }).click()
  await expect(page.getByRole('heading', { name: '기준 이미지 검수', exact: true })).toBeVisible()
}
async function bases(page: Page) {
  await page.getByLabel('후경 지면 기준 승인 선택', { exact: true }).check()
  await page.getByLabel('전경 나무 기준 승인 선택', { exact: true }).check()
  await page.getByRole('button', { name: '선택한 기준 이미지 승인', exact: true }).click()
  await expect(
    page.getByRole('button', { name: '후경 지면 애니메이션 승인', exact: true }),
  ).toBeEnabled()
}

test('regenerating one frame invalidates approval and keeps other assets usable', async ({
  page,
}) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { records } })
  await loopPlan(page)
  await expect(
    page.getByRole('button', { name: '후경 지면 프레임 2 재생성', exact: true }),
  ).toBeDisabled()
  await bases(page)
  await page.getByRole('button', { name: '후경 지면 애니메이션 승인', exact: true }).click()
  await expect(page.getByText('최종 승인 완료 · 4프레임', { exact: true })).toBeVisible()
  const other = await page
    .getByRole('img', { name: '전경 나무 기준 이미지', exact: true })
    .getAttribute('src')
  const original = await page
    .getByRole('img', { name: '후경 지면 프레임 2', exact: true })
    .getAttribute('src')
  const before = records.length
  await page.getByRole('button', { name: '후경 지면 프레임 2 재생성', exact: true }).click()
  await expect(
    page.getByRole('img', { name: '후경 지면 프레임 2', exact: true }),
  ).not.toHaveAttribute('src', original!)
  await expect(page.getByText('최종 승인 완료 · 4프레임', { exact: true })).toHaveCount(0)
  await expect(page.getByText('최종 승인 완료 · 1프레임', { exact: true })).toBeVisible()
  await expect(
    page.getByRole('img', { name: '전경 나무 기준 이미지', exact: true }),
  ).toHaveAttribute('src', other!)
  expect(records.slice(before)).toHaveLength(1)
  expect(records.at(-1)!.path).toContain('/frames/1/regenerate')
  expect(Object.keys(records.at(-1)!.body).sort()).toEqual(['expectedRevision', 'requestId'])
  await page.reload()
  await expect(
    page.getByRole('img', { name: '후경 지면 프레임 2', exact: true }),
  ).not.toHaveAttribute('src', original!)
  await page.getByRole('button', { name: '후경 지면 기준 재생성', exact: true }).click()
  await expect(
    page.getByText(
      '기준 재생성은 후속 프레임과 최종 승인을 무효화합니다. 기준 이미지를 다시 검수해 주세요.',
      { exact: true },
    ),
  ).toBeVisible()
  await expect(page.getByLabel('후경 지면 기준 승인 선택', { exact: true })).toBeEnabled()
  await expect(page.getByText('프레임 2 · 누락', { exact: true })).toBeVisible()
  await expect(
    page.getByRole('button', { name: '후경 지면 프레임 2 재생성', exact: true }),
  ).toBeDisabled()
  await expect(
    page.getByRole('img', { name: '전경 나무 기준 이미지', exact: true }),
  ).toHaveAttribute('src', other!)
})

test('fps edit updates metadata without generation requests', async ({ page }) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await page.addInitScript(() => {
    const stats = { requested: 0, canceled: 0 }
    window.spriteRaf = stats
    const request = window.requestAnimationFrame.bind(window)
    const cancel = window.cancelAnimationFrame.bind(window)
    window.requestAnimationFrame = (callback) => {
      stats.requested++
      return request(callback)
    }
    window.cancelAnimationFrame = (id) => {
      stats.canceled++
      cancel(id)
    }
  })
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await installFakeApi(page, { sprites: { records } })
  await loopPlan(page)
  await bases(page)
  const other = await page
    .getByRole('img', { name: '전경 나무 기준 이미지', exact: true })
    .getAttribute('src')
  await page.getByRole('button', { name: '후경 지면 애니메이션 승인', exact: true }).click()
  await expect(page.getByText('최종 승인 완료 · 4프레임', { exact: true })).toBeVisible()
  const detailBefore = page.waitForResponse(
    (response) => new URL(response.url()).pathname === `/api/jobs/${SPRITE_IDS.job}`,
  )
  await page.getByRole('button', { name: '서버 상태 새로고침', exact: true }).click()
  const initial = (await (await detailBefore).json()).data
  const before = records.length
  await page.getByLabel('FPS 1', { exact: true }).fill('8')
  await page.getByLabel('대상 이름 1', { exact: true }).fill('바람 지면')
  await page.getByLabel('깊이 순서 1', { exact: true }).fill('-1')
  const detailAfter = page.waitForResponse(
    (response) => new URL(response.url()).pathname === `/api/jobs/${SPRITE_IDS.job}`,
  )
  await page.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect(
    page.getByRole('button', { name: '바람 지면 애니메이션 승인', exact: true }),
  ).toBeEnabled()
  const final = (await (await detailAfter).json()).data
  expect(final.tasks).toEqual(initial.tasks)
  expect(final.sprite.assets[1]).toEqual(initial.sprite.assets[1])
  expect(final.sprite.assets[0].frames).toEqual(initial.sprite.assets[0].frames)
  const changes = records.slice(before)
  expect(changes).toHaveLength(1)
  expect(changes[0]!.path).toBe(`/api/jobs/${SPRITE_IDS.job}/sprites/plan`)
  expect(changes[0]!.body.assets).toMatchObject([
    { name: '바람 지면', fps: 8, order: -1 },
    { name: '전경 나무', loop: false },
  ])
  await page.reload()
  await expect(page.getByLabel('FPS 1', { exact: true })).toHaveValue('8')
  await expect(
    page.getByRole('img', { name: '전경 나무 기준 이미지', exact: true }),
  ).toHaveAttribute('src', other!)
  const scrub = page.getByRole('slider', { name: '바람 지면 프레임 탐색', exact: true })
  await expect(scrub).toHaveValue('0')
  expect(await page.evaluate(() => window.spriteRaf.requested)).toBe(0)
  await page.waitForTimeout(150)
  await expect(scrub).toHaveValue('0')
  await expect(page.getByRole('button', { name: '재생', exact: true })).toBeVisible()
  await scrub.focus()
  await page.keyboard.press('ArrowRight')
  await expect(scrub).toHaveValue('1')
  await page.getByRole('button', { name: '재생', exact: true }).click()
  await expect(page.getByRole('button', { name: '일시정지', exact: true })).toBeVisible()
  await expect.poll(() => scrub.inputValue()).not.toBe('1')
  await page.getByRole('button', { name: '일시정지', exact: true }).click()
  const paused = await scrub.inputValue()
  await page.waitForTimeout(300)
  await expect(scrub).toHaveValue(paused)
  expect(await page.evaluate(() => window.spriteRaf.canceled)).toBeGreaterThan(0)
  await page.getByRole('button', { name: '재생', exact: true }).click()
  await page.getByRole('link', { name: '홈', exact: true }).click()
  await expect(page.getByRole('region', { name: '배경 미리보기', exact: true })).toHaveCount(0)
  const requested = await page.evaluate(() => window.spriteRaf.requested)
  await page.waitForTimeout(150)
  expect(await page.evaluate(() => window.spriteRaf.requested)).toBe(requested)
  expect(records.slice(before)).toHaveLength(1)
})

test('layers use distinct FPS with fixed canvas and keyboard scrub', async ({ page }, testInfo) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await installFakeApi(page, { sprites: { seed: 'frameReview', records } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const preview = page.getByRole('img', { name: '레이어 합성 검수', exact: true })
  await expect(preview).toHaveAttribute('viewBox', '0 0 320 180')
  const scrub = page.getByRole('slider', { name: '후경 지면 프레임 탐색', exact: true })
  await scrub.focus()
  await page.keyboard.press('ArrowRight')
  await page.keyboard.press('ArrowRight')
  await expect(scrub).toHaveValue('2')
  await expect(preview.locator('image').nth(0)).toHaveAttribute('data-frame-index', '2')
  await expect(preview.locator('image').nth(1)).toHaveAttribute('data-frame-index', '4')
  await expect(preview.locator('image').nth(1)).toHaveAttribute('x', '0')
  await expect(preview.locator('image').nth(1)).toHaveAttribute('y', '0')
  await page.getByLabel('전경 나무 표시', { exact: true }).uncheck()
  await expect(preview.locator('image')).toHaveCount(1)
  await page.screenshot({ path: testInfo.outputPath('animation-desktop.png'), fullPage: true })
  await page.setViewportSize({ width: 390, height: 844 })
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    .toBe(true)
  await page.screenshot({ path: testInfo.outputPath('animation-390.png'), fullPage: true })
  expect(records).toHaveLength(0)
})

for (const view of ['topDown', 'isometric'] as const)
  for (const repeat of ['x', 'y', 'both'] as const)
    test(`animated ${view} tile repeats along ${repeat} axes`, async ({ page }) => {
      await installFakeApi(page, {
        sprites: {
          seed: 'frameReview',
          settings: { view, repeat, outputKind: 'tiles', tileWidth: 128 },
        },
      })
      await page.goto(`/2d/background/${SPRITE_IDS.job}`)
      const preview = page.getByRole('img', { name: '반복 경계 검수', exact: true })
      await expect(preview.locator('image')).toHaveCount(9)
      const scrub = page.getByRole('slider', { name: '후경 지면 프레임 탐색', exact: true })
      await scrub.focus()
      await page.keyboard.press('ArrowRight')
      await expect(preview.locator('image').nth(0)).toHaveAttribute('data-frame-index', '1')
      const coordinates = await preview
        .locator('image')
        .evaluateAll((images) =>
          images.map((img) => [Number(img.getAttribute('x')), Number(img.getAttribute('y'))]),
        )
      const height = view === 'isometric' ? 64 : 128
      expect(coordinates).toContainEqual([-64, -height / 2])
      if (view === 'isometric') {
        expect(coordinates).toContainEqual([-128, -64])
      } else {
        expect(new Set(coordinates.map((p) => p[1])).size).toBe(3)
        expect(new Set(coordinates.map((p) => p[0])).size).toBe(3)
      }
      await expect(
        page.getByText(`검수할 반복축: ${repeat === 'both' ? 'X·Y' : repeat.toUpperCase()}`, {
          exact: false,
        }),
      ).toBeVisible()
    })

test('current missing failed and running slots are shown without historical tasks', async ({
  page,
}) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { seed: 'frameReview', frameStates: true, records } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const panel = page.getByRole('region', { name: '작업 상태', exact: true })
  const steps = panel.getByRole('navigation', { name: '2D 제작 단계' })
  await expect(steps.getByRole('listitem')).toHaveCount(7)
  await expect(steps.locator('[aria-current="step"]')).toContainText('프레임 생성')
  await expect(panel.getByLabel('배경 생성 성공')).toHaveText('2 / 2')
  await expect(panel.getByLabel('프레임 생성 성공')).toHaveText('11 / 14')
  await expect(page.getByText('프레임 2 · 실패', { exact: true })).toBeVisible()
  await expect(page.getByText('프레임 3 · 생성 중', { exact: true })).toBeVisible()
  await expect(page.getByText('프레임 4 · 누락', { exact: true })).toBeVisible()
  await expect(page.getByRole('img', { name: '후경 지면 프레임 2', exact: true })).toHaveCount(0)
  await expect(
    page.getByRole('button', { name: '후경 지면 프레임 3 재생성', exact: true }),
  ).toBeDisabled()
  await expect(
    page.getByRole('button', { name: '후경 지면 기준 재생성', exact: true }),
  ).toBeDisabled()
  await expect(page.getByLabel('FPS 1', { exact: true })).toBeDisabled()
  await expect(page.getByLabel('대상 이름 1', { exact: true })).toBeDisabled()
  await expect(page.getByLabel('FPS 2', { exact: true })).toBeEnabled()
  await expect(
    page.getByRole('button', { name: '후경 지면 애니메이션 승인', exact: true }),
  ).toBeDisabled()
  await expect(
    page.getByRole('button', { name: '전경 나무 애니메이션 승인', exact: true }),
  ).toBeEnabled()
  await page.getByRole('button', { name: '후경 지면 프레임 2 재생성', exact: true }).click()
  await expect(panel.getByLabel('프레임 생성 성공')).toHaveText('12 / 14')
  await expect(page.getByText('프레임 2 · 완료', { exact: true })).toHaveCount(2)
  await expect(page.getByText('프레임 3 · 생성 중', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '실패 공정 재시도', exact: true })).toHaveCount(0)
  expect(records).toHaveLength(1)
  await page.setViewportSize({ width: 390, height: 844 })
  await panel.screenshot({
    path: '/tmp/noxtend-sprite-status-animation-mobile.png',
    animations: 'disabled',
  })
})

test('lost animation approval replays the same request without creating tasks', async ({
  page,
}) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, {
    sprites: { seed: 'frameReview', records, lostAssetApprovalOnce: true },
  })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  await page.getByRole('button', { name: '후경 지면 애니메이션 승인', exact: true }).click()
  await expect(
    page.getByRole('button', { name: '같은 애니메이션 승인 재전송', exact: true }),
  ).toBeEnabled()
  await page.getByRole('button', { name: '같은 애니메이션 승인 재전송', exact: true }).click()
  await expect(page.getByText('최종 승인 완료 · 8프레임', { exact: true })).toBeVisible()
  expect(records).toHaveLength(2)
  expect(records[1]).toEqual(records[0])
})

test('animation validation blocks invalid FPS and retains static effective one frame', async ({
  page,
}) => {
  await installFakeApi(page, { sprites: {} })
  await loopPlan(page)
  await page.getByLabel('FPS 1', { exact: true }).fill('31')
  await expect(page.getByRole('button', { name: '계획 저장', exact: true })).toBeDisabled()
  await page.getByLabel('FPS 1', { exact: true }).fill('0')
  await expect(page.getByRole('button', { name: '계획 저장', exact: true })).toBeDisabled()
  await page.getByLabel('FPS 1', { exact: true }).fill('19')
  await expect(page.getByRole('button', { name: '계획 저장', exact: true })).toBeEnabled()
  await expect(page.getByRole('textbox', { name: '동작 설명 1', exact: true })).toHaveAttribute(
    'maxlength',
    '500',
  )
  await expect(page.getByText('총 5/64장', { exact: false })).toBeVisible()
  await page.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect(page.getByRole('button', { name: '계획 저장', exact: true })).toBeDisabled()
  await bases(page)
  const scrub = page.getByRole('slider', { name: '후경 지면 프레임 탐색', exact: true })
  await scrub.focus()
  await page.keyboard.press('ArrowRight')
  await expect(scrub).toHaveValue('1')
})

test('approved animation exports all current frames in a valid ZIP manifest', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'frameReview' } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  await page.getByRole('button', { name: '후경 지면 애니메이션 승인', exact: true }).click()
  await expect(page.getByText('최종 승인 완료 · 8프레임', { exact: true })).toHaveCount(1)
  await page.getByRole('button', { name: '전경 나무 애니메이션 승인', exact: true }).click()
  await expect(page.getByText('최종 승인 완료 · 8프레임', { exact: true })).toHaveCount(2)
  await page.getByLabel('후경 지면 내보내기 선택', { exact: false }).check()
  await page.getByLabel('전경 나무 내보내기 선택', { exact: false }).check()
  await page.getByRole('button', { name: '내보내기', exact: true }).click()
  const download = page.waitForEvent('download')
  await page.getByRole('link', { name: '현재 ZIP 다운로드', exact: false }).click()
  const bytes = await readFile((await (await download).path())!)
  const files = new Map<string, Buffer>()
  for (let offset = 0; bytes.readUInt32LE(offset) === 0x04034b50;) {
    const length = bytes.readUInt32LE(offset + 18),
      nameLength = bytes.readUInt16LE(offset + 26)
    const start = offset + 30 + nameLength
    files.set(bytes.subarray(offset + 30, start).toString(), bytes.subarray(start, start + length))
    offset = start + length
  }
  expect(bytes.readUInt32LE(bytes.length - 22)).toBe(0x06054b50)
  const manifest = JSON.parse(files.get('manifest.json')!.toString())
  expect(manifest.schemaVersion).toBe(1)
  expect(manifest.assets).toHaveLength(2)
  for (const asset of manifest.assets) {
    expect(asset.frames).toHaveLength(8)
    expect(asset.asset.imageIds).toEqual(
      asset.frames.map((frame: { imageId: string }) => frame.imageId),
    )
    for (const frame of asset.frames) {
      expect(files.get(frame.path)!.subarray(0, 8)).toEqual(
        Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
      )
      const sheet = files.get(frame.sheetPath)!
      expect(sheet.readUInt32BE(16)).toBe((320 + 4) * 8)
      expect(sheet.readUInt32BE(20)).toBe(180 + 4)
      expect(frame.rect).toEqual({ x: frame.index * 324 + 2, y: 2, width: 320, height: 180 })
    }
  }
  await page.getByRole('button', { name: '후경 지면 기준 재생성', exact: true }).click()
  await expect(page.getByText('프레임 2 · 누락', { exact: true })).toBeVisible()
  await expect(page.getByLabel('후경 지면 기준 승인 선택', { exact: true })).toBeEnabled()
  await expect(page.getByRole('link', { name: '현재 ZIP 다운로드', exact: false })).toHaveCount(0)
  await expect(page.getByRole('link', { name: '이전 ZIP 다운로드', exact: false })).toBeVisible()
})

test('idle generation input edits require renewed plan review and keep unrelated results', async ({
  page,
}) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { records } })
  await loopPlan(page)
  await bases(page)
  const other = await page
    .getByRole('img', { name: '전경 나무 기준 이미지', exact: true })
    .getAttribute('src')
  const before = records.length
  await page.getByRole('textbox', { name: '동작 설명 1', exact: true }).fill('더 천천히 흔들림')
  await page.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect(
    page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성', exact: true }),
  ).toBeEnabled()
  expect(records.slice(before)).toHaveLength(1)
  expect(records.at(-1)!.path).toBe(`/api/jobs/${SPRITE_IDS.job}/sprites/plan`)
  await expect(page.getByRole('img', { name: '후경 지면 기준 이미지', exact: true })).toHaveCount(0)
  await expect(
    page.getByRole('img', { name: '전경 나무 기준 이미지', exact: true }),
  ).toHaveAttribute('src', other!)
  await expect(
    page.getByRole('button', { name: '후경 지면 프레임 2 재생성', exact: true }),
  ).toBeDisabled()
  await page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성', exact: true }).click()
  await expect(page.getByRole('img', { name: '후경 지면 기준 이미지', exact: true })).toBeVisible()
  await expect(
    page.getByRole('img', { name: '전경 나무 기준 이미지', exact: true }),
  ).toHaveAttribute('src', other!)
})

test('removing the last loop stops RAF on the same page and restores paused playback', async ({
  page,
}) => {
  await page.addInitScript(() => {
    const stats = { requested: 0, canceled: 0 }
    window.spriteRaf = stats
    const request = window.requestAnimationFrame.bind(window)
    const cancel = window.cancelAnimationFrame.bind(window)
    window.requestAnimationFrame = (callback) => {
      stats.requested++
      return request(callback)
    }
    window.cancelAnimationFrame = (id) => {
      stats.canceled++
      cancel(id)
    }
  })
  await installFakeApi(page, { sprites: {} })
  await loopPlan(page)
  const preview = page.getByRole('region', { name: '배경 미리보기', exact: true })
  await preview.evaluate((element) => element.setAttribute('data-lifecycle-check', 'same-preview'))
  await page.getByRole('button', { name: '재생', exact: true }).click()
  await expect(page.getByRole('button', { name: '일시정지', exact: true })).toBeVisible()
  await expect.poll(() => page.evaluate(() => window.spriteRaf.requested)).toBeGreaterThan(0)
  const canceled = await page.evaluate(() => window.spriteRaf.canceled)
  await page.getByLabel('루프 애니메이션 1', { exact: true }).uncheck()
  await page.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect(
    page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성', exact: true }),
  ).toBeEnabled()
  await expect(preview).toHaveAttribute('data-lifecycle-check', 'same-preview')
  await expect(page.getByRole('button', { name: '일시정지', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '재생', exact: true })).toHaveCount(0)
  await expect.poll(() => page.evaluate(() => window.spriteRaf.canceled)).toBeGreaterThan(canceled)
  const requested = await page.evaluate(() => window.spriteRaf.requested)
  await page.waitForTimeout(150)
  expect(await page.evaluate(() => window.spriteRaf.requested)).toBe(requested)
  await page.getByLabel('루프 애니메이션 1', { exact: true }).check()
  await page.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect(page.getByRole('button', { name: '재생', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '일시정지', exact: true })).toHaveCount(0)
  await expect(preview).toHaveAttribute('data-lifecycle-check', 'same-preview')
  await page.waitForTimeout(150)
  expect(await page.evaluate(() => window.spriteRaf.requested)).toBe(requested)
  await page.getByRole('button', { name: '재생', exact: true }).click()
  await expect
    .poll(() => page.evaluate(() => window.spriteRaf.requested))
    .toBeGreaterThan(requested)
})
