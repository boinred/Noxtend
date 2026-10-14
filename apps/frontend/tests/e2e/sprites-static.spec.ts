import { test, expect, type Page } from '@playwright/test'
import { readFile } from 'node:fs/promises'
import { crc32 } from 'node:zlib'
import { installFakeApi } from './fakeApi'
import { SPRITE_IDS, SPRITE_SOURCE_PNG, type SpriteFakeOptions } from './spriteFakeApi'
import { spriteBackgroundWithSourcePath, readSpriteSource } from '../../src/routes/paths'

const viewLabels: Record<string, string> = {
  sideView: '횡스크롤',
  topDown: '탑다운',
  isometric: '아이소메트릭',
}
async function chooseSetting(page: Page, label: string, value: string) {
  await page.getByRole('combobox', { name: label, exact: true }).click()
  await page.getByRole('option', { name: value, exact: true }).click()
  await expect(page.getByRole('listbox')).toHaveCount(0)
}
const enhanceButton = (page: Page) => page.getByRole('button', { name: '보완하기', exact: true })
async function start(page: Page, view = 'sideView', kind = 'layers') {
  await page.goto('/2d/background')
  await page.getByTestId('mode-tab-image').click()
  await page
    .getByTestId('image-input')
    .setInputFiles({ name: 'source.png', mimeType: 'image/png', buffer: SPRITE_SOURCE_PNG })
  await chooseSetting(page, '시점', viewLabels[view]!)
  await chooseSetting(page, '결과 유형', kind === 'layers' ? '배경 레이어' : '반복 타일')
  await expect(page.getByTestId('sprite-image-model')).toContainText('Sprite Image Fake')
  await page.getByRole('button', { name: '2D 분석 시작', exact: true }).click()
}

async function approvePlan(page: Page) {
  await expect(page.getByRole('heading', { name: '제작 계획 검수' })).toBeVisible()
  await expect(page.getByText('비용 미확인', { exact: false })).toBeVisible()
  await page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' }).click()
  await expect(page.getByRole('heading', { name: '기준 이미지 검수' })).toBeVisible()
}
async function approveBases(page: Page, partial = false) {
  await page.getByLabel('후경 지면 기준 승인 선택', { exact: true }).check()
  if (!partial) await page.getByLabel('전경 나무 기준 승인 선택', { exact: true }).check()
  await page.getByRole('button', { name: '선택한 기준 이미지 승인' }).click()
  await expect(page.getByText('최종 승인 완료 · 1프레임', { exact: true })).toHaveCount(
    partial ? 1 : 2,
  )
}
async function decoded(page: Page, bytes: Buffer) {
  return page.evaluate(async (base64) => {
    const image = new Image()
    image.src = `data:image/png;base64,${base64}`
    await image.decode()
    const canvas = document.createElement('canvas')
    canvas.width = image.width
    canvas.height = image.height
    const ctx = canvas.getContext('2d')!
    ctx.drawImage(image, 0, 0)
    const pixels = ctx.getImageData(0, 0, image.width, image.height).data
    let clear = 0,
      opaque = 0
    for (let i = 3; i < pixels.length; i += 4) {
      if (pixels[i] === 0) clear++
      if (pixels[i] === 255) opaque++
    }
    return { width: image.width, height: image.height, clear, opaque }
  }, bytes.toString('base64'))
}
function storedZip(bytes: Buffer) {
  const files = new Map<string, Buffer>()
  let offset = 0
  while (bytes.readUInt32LE(offset) === 0x04034b50) {
    expect(bytes.readUInt16LE(offset + 8)).toBe(0)
    const length = bytes.readUInt32LE(offset + 18),
      nameLength = bytes.readUInt16LE(offset + 26),
      extra = bytes.readUInt16LE(offset + 28)
    const name = bytes.subarray(offset + 30, offset + 30 + nameLength).toString()
    const begin = offset + 30 + nameLength + extra,
      body = bytes.subarray(begin, begin + length)
    expect(crc32(body)).toBe(bytes.readUInt32LE(offset + 14))
    files.set(name, body)
    offset = begin + length
  }
  expect(bytes.readUInt32LE(offset)).toBe(0x02014b50)
  expect(bytes.readUInt32LE(bytes.length - 22)).toBe(0x06054b50)
  expect(bytes.readUInt16LE(bytes.length - 12)).toBe(files.size)
  return files
}

test('sprite status summarizes current backgrounds and marks the review step', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview' } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const panel = page.getByRole('region', { name: '작업 상태', exact: true })
  const steps = panel.getByRole('navigation', { name: '2D 제작 단계' })
  await expect(steps.getByRole('listitem')).toHaveCount(5)
  await expect(steps.locator('[aria-current="step"]')).toContainText('기준 검수')
  await expect(panel.getByLabel('원본 분석 성공')).toHaveText('1 / 1')
  await expect(panel.getByLabel('배경 생성 성공')).toHaveText('2 / 2')
  await expect(panel.getByText('시도 1', { exact: false })).toHaveCount(0)
  await expect(panel.getByRole('button', { name: '서버 상태 새로고침', exact: true })).toHaveText(
    '',
  )
  await expect(panel.getByRole('button', { name: '작업 취소', exact: true })).toHaveText('')
  const progress = panel.getByRole('progressbar', { name: '완료된 제작 단계' })
  await expect(progress).toHaveAttribute('aria-valuenow', '3')
  await expect(progress).toHaveAttribute('aria-valuemax', '5')
  const collapse = panel.getByRole('button', { name: '상세 접기', exact: true })
  await expect(collapse).toHaveAttribute('aria-expanded', 'true')
  await collapse.press('Enter')
  await expect(panel.getByRole('button', { name: '상세 보기', exact: true })).toHaveAttribute(
    'aria-expanded',
    'false',
  )
  await expect(steps.getByRole('listitem')).toHaveCount(0)
  await expect(progress).toBeVisible()
  await expect(panel.getByLabel('배경 생성 성공')).toHaveText('2 / 2')
  await panel.screenshot({
    path: '/tmp/noxtend-sprite-status-collapsed.png',
    animations: 'disabled',
  })
  await panel.getByRole('button', { name: '상세 보기', exact: true }).press('Space')
  await expect(steps.getByRole('listitem')).toHaveCount(5)
  const theme = page.getByTestId('theme-toggle')
  if ((await theme.getAttribute('data-theme-state')) === 'light') await theme.click()
  await expect(theme).toHaveAttribute('data-theme-state', 'dark')
  await panel.screenshot({ path: '/tmp/noxtend-sprite-status-desktop.png', animations: 'disabled' })
  await page.setViewportSize({ width: 390, height: 844 })
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await expect(steps.locator('[aria-current="step"]')).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
  await panel.screenshot({ path: '/tmp/noxtend-sprite-status-mobile.png', animations: 'disabled' })
  await theme.click()
  await expect(theme).toHaveAttribute('data-theme-state', 'light')
  await panel.screenshot({ path: '/tmp/noxtend-sprite-status-light.png', animations: 'disabled' })
})

test('static metadata review keeps five steps without animation stages', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'completed' } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  await expect(page.getByRole('progressbar', { name: '완료된 제작 단계' })).toHaveAttribute(
    'aria-valuenow',
    '5',
  )
  await page
    .getByRole('region', { name: '작업 상태', exact: true })
    .screenshot({ path: '/tmp/noxtend-sprite-status-success.png', animations: 'disabled' })
  await page.getByLabel('대상 이름 1', { exact: true }).fill('잔디 지면')
  await page.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect(page.getByRole('button', { name: '잔디 지면 최종 승인', exact: true })).toBeEnabled()
  const panel = page.getByRole('region', { name: '작업 상태', exact: true })
  const steps = panel.getByRole('navigation', { name: '2D 제작 단계' })
  await expect(steps.getByRole('listitem')).toHaveCount(5)
  await expect(steps.locator('[aria-current="step"]')).toContainText('최종 검수')
  await expect(panel.getByRole('status')).toContainText('최종 이미지 검수')
  await expect(panel.getByLabel('프레임 생성 성공')).toHaveCount(0)
  await expect(panel.getByLabel('배경 생성 성공')).toHaveText('2 / 2')
  await page.getByLabel('전경 나무 내보내기 선택', { exact: false }).check()
  await page.getByRole('button', { name: '내보내기', exact: true }).click()
  await expect(steps.getByRole('listitem').filter({ hasText: '기준 검수' })).toContainText('미완료')
})

test('sprite plan uses the shared analysis overlay and reflects draft edits', async ({
  page,
}, testInfo) => {
  await installFakeApi(page, { sprites: {} })
  await start(page)
  const overlay = page.getByRole('region', { name: '제작 계획 검수' }).getByTestId('parts-overlay')
  await expect(overlay).toBeVisible()
  await expect(overlay.getByTestId('overlay-chip')).toHaveText(['1. 후경 지면', '2. 전경 나무'])
  await expect(overlay.getByTestId('overlay-anchor')).toHaveCount(2)
  await expect(overlay.locator('[data-testid="overlay-box"][data-shown="true"]')).toHaveCount(2)
  const colors = await overlay
    .getByTestId('overlay-box')
    .evaluateAll((nodes) => nodes.map((node) => getComputedStyle(node).borderColor))
  expect(new Set(colors).size).toBe(2)

  await page.getByLabel('대상 이름 1', { exact: true }).fill('고대 석조 교량')
  await page.getByLabel('ROI w 1', { exact: true }).fill('0.6')
  await page.getByLabel('ROI x 1', { exact: true }).fill('0.1')
  const chip = overlay.getByRole('button', { name: '1. 고대 석조 교량', exact: true })
  await expect(chip).toBeVisible()
  const image = (await overlay.getByTestId('overlay-image').boundingBox())!
  const box = (await overlay
    .locator('[data-testid="overlay-box"][data-part="고대 석조 교량"]')
    .boundingBox())!
  expect(box.width / image.width).toBeCloseTo(0.6, 2)
  expect((box.x - image.x) / image.width).toBeCloseTo(0.1, 2)
  await chip.focus()
  await expect(overlay.getByTestId('overlay-chip').nth(1)).toHaveAttribute('data-dimmed', 'true')
  await page.keyboard.press('Enter')
  await expect(chip).toHaveAttribute('data-stuck', 'true')
  await page.keyboard.press('Enter')
  await expect(chip).toHaveAttribute('data-stuck', 'false')
  await chip.blur()
  await overlay.screenshot({ path: testInfo.outputPath('sprite-overlay-desktop.png') })

  await page.setViewportSize({ width: 390, height: 844 })
  await page.emulateMedia({ reducedMotion: 'reduce', colorScheme: 'dark' })
  const longName = '절벽 가장자리의 고대 석조 기둥과 오벨리스크'
  await page.getByLabel('대상 이름 1', { exact: true }).fill(longName)
  const mobileChip = overlay.getByRole('button', { name: `1. ${longName}`, exact: true })
  await expect(mobileChip).toBeVisible()
  const mobileImage = (await overlay.getByTestId('overlay-image').boundingBox())!
  const mobileLabel = (await mobileChip.boundingBox())!
  expect(mobileLabel.x + mobileLabel.width).toBeLessThanOrEqual(
    mobileImage.x + mobileImage.width + 1,
  )
  const otherLabel = (await overlay
    .getByRole('button', { name: '2. 전경 나무', exact: true })
    .boundingBox())!
  const overlapping =
    mobileLabel.x < otherLabel.x + otherLabel.width &&
    mobileLabel.x + mobileLabel.width > otherLabel.x &&
    mobileLabel.y < otherLabel.y + otherLabel.height &&
    mobileLabel.y + mobileLabel.height > otherLabel.y
  expect(overlapping).toBe(false)
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
  await overlay.screenshot({ path: testInfo.outputPath('sprite-overlay-mobile.png') })
})

for (const view of ['sideView', 'topDown', 'isometric'])
  for (const kind of ['layers', 'tiles']) {
    test(`static ${view} ${kind} requires plan and base review before export`, async ({
      page,
    }, testInfo) => {
      const records: NonNullable<SpriteFakeOptions['records']> = []
      await installFakeApi(page, {
        sprites: { records, priceError: view === 'isometric' && kind === 'tiles' },
      })
      await start(page, view, kind)
      await expect(page.getByRole('heading', { name: '제작 계획 검수' })).toBeVisible()
      expect(records[0]!.body.settings).toMatchObject({
        view,
        outputKind: kind,
        tileWidth: 128,
        repeat: 'both',
      })
      await expect(page.getByLabel('ROI x 1', { exact: true })).toHaveValue('0')
      await page.getByLabel('ROI w 1', { exact: true }).fill('0.7')
      await expect(
        page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' }),
      ).toBeDisabled()
      await page.getByRole('button', { name: '계획 저장' }).click()
      await expect(page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' })).toBeEnabled()
      await page.reload()
      await expect(page.getByLabel('ROI w 1', { exact: true })).toHaveValue('0.7')
      await approvePlan(page)
      const image = page.getByRole('img', { name: '전경 나무 기준 이미지', exact: true })
      await expect(image).toBeVisible()
      const pngDownload = page.waitForEvent('download')
      await page.getByRole('link', { name: 'PNG 다운로드 · 전경 나무' }).click()
      const pngFile = await pngDownload
      expect(pngFile.suggestedFilename()).toMatch(/^sprite-[\da-f-]{36}\.png$/)
      const actual = await decoded(page, await readFile((await pngFile.path())!))
      expect(actual).toMatchObject({
        width: kind === 'layers' ? 320 : 128,
        height: kind === 'layers' ? 180 : view === 'isometric' ? 64 : 128,
      })
      expect(actual.opaque).toBeGreaterThan(0)
      if (kind === 'layers' || view === 'isometric') expect(actual.clear).toBeGreaterThan(0)
      if (kind === 'layers') await page.getByLabel('전경 나무 표시', { exact: true }).uncheck()
      else {
        await chooseSetting(page, '미리볼 타일', '전경 나무')
        await expect(
          page.getByRole('img', { name: '반복 경계 검수' }).locator('image'),
        ).toHaveCount(9)
      }
      await approveBases(page)
      if (kind === 'tiles') {
        await page.getByLabel('오브젝트 바위 기준 승인 선택', { exact: true }).check()
        await page.getByRole('button', { name: '선택한 기준 이미지 승인', exact: true }).click()
      }
      await expect(page.getByRole('button', { name: '승인', exact: true })).toHaveCount(0)
      await page.getByLabel('후경 지면 내보내기 선택', { exact: false }).check()
      await page.getByLabel('전경 나무 내보내기 선택', { exact: false }).check()
      if (kind === 'tiles')
        await page.getByLabel('오브젝트 바위 내보내기 선택', { exact: false }).check()
      await expect(
        page.getByText(`포함: ${kind === 'tiles' ? 3 : 2}개 · 제외 대상: 없음`),
      ).toBeVisible()
      await expect(page.getByRole('button', { name: '내보내기', exact: true })).toBeEnabled()
      await page.getByRole('button', { name: '내보내기', exact: true }).click()
      await expect(
        page.getByRole('link', { name: '현재 ZIP 다운로드', exact: false }),
      ).toBeVisible()
      await page.reload()
      const zipDownload = page.waitForEvent('download')
      await page.getByRole('link', { name: '현재 ZIP 다운로드', exact: false }).click()
      const zipped = await zipDownload
      expect(zipped.suggestedFilename()).toMatch(/^sprites-[\da-f-]{36}\.zip$/)
      const files = storedZip(await readFile((await zipped.path())!))
      const manifest = JSON.parse(files.get('manifest.json')!.toString())
      expect(manifest).toMatchObject({
        schemaVersion: 2,
        productionMode: 'twoD',
        coordinateOrigin: 'topLeft',
        coordinateUnits: 'pixels',
        input: { excludedAssetIds: [] },
      })
      expect(manifest.assets).toHaveLength(kind === 'tiles' ? 3 : 2)
      for (const asset of manifest.assets) {
        expect(asset.frames).toHaveLength(1)
        expect(files.get(asset.basePath)).toEqual(files.get(asset.frames[0].path))
        const detail = manifest.input.assetDetails.find(
          (detail: { id: string }) => detail.id === asset.asset.id,
        )
        const pixels = await decoded(page, files.get(asset.basePath)!)
        expect(pixels.width).toBe(detail.canvas.width)
        expect(pixels.height).toBe(detail.canvas.height)
        expect(await decoded(page, files.get(asset.frames[0].sheetPath)!)).toMatchObject({
          width: detail.canvas.width + 4,
          height: detail.canvas.height + 4,
        })
      }
      if (view === 'isometric' && kind === 'tiles') {
        await page.screenshot({ path: testInfo.outputPath('desktop.png'), fullPage: true })
        await page.setViewportSize({ width: 390, height: 844 })
        expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(
          390,
        )
        expect(
          await page.getByTestId('page-inner').evaluate((el) => el.scrollWidth <= el.clientWidth),
        ).toBe(true)
        await page.screenshot({ path: testInfo.outputPath('mobile.png'), fullPage: true })
        const refresh = page.getByRole('button', { name: '서버 상태 새로고침' })
        await refresh.focus()
        await expect(refresh).toBeFocused()
        await page.keyboard.press('Enter')
      }
    })
  }

test('plan add remove and ROI/depth validation preserves an explicit static draft', async ({
  page,
}) => {
  await installFakeApi(page, { sprites: {} })
  await start(page)
  await page.getByLabel('ROI w 1', { exact: true }).fill('0')
  await expect(page.getByRole('button', { name: '계획 저장' })).toBeDisabled()
  await expect(page.getByRole('alert')).toContainText('양수 ROI')
  await page.getByLabel('ROI w 1', { exact: true }).fill('1')
  await page.getByLabel('깊이 순서 2', { exact: true }).fill('0')
  await expect(page.getByRole('alert')).toContainText('중복 없는 정수 순서')
  await page.getByLabel('깊이 순서 2', { exact: true }).fill('1')
  for (let i = 0; i < 10; i++)
    await page.getByRole('button', { name: '대상 추가', exact: true }).click()
  await expect(page.getByRole('button', { name: '대상 추가', exact: true })).toBeDisabled()
  await expect(page.getByText('12/12개', { exact: false })).toBeVisible()
  for (let i = 12; i >= 2; i--)
    await page.getByRole('button', { name: `대상 ${i} 삭제`, exact: true }).click()
  await expect(page.getByRole('button', { name: '대상 1 삭제', exact: true })).toBeDisabled()
  await page.getByLabel('대상 이름 1', { exact: true }).fill('긴 대상 이름을 수정한 뒤 저장합니다')
  await page.getByRole('button', { name: '계획 저장' }).click()
  await page.reload()
  await expect(page.getByLabel('대상 이름 1', { exact: true })).toHaveValue(
    '긴 대상 이름을 수정한 뒤 저장합니다',
  )
})

test('provider failure differs from empty registration and retries the lookup', async ({
  page,
}) => {
  await installFakeApi(page, { sprites: { providerErrorOnce: true } })
  await page.goto('/2d/background')
  await expect(page.getByRole('alert')).toContainText('공급자 연결 실패')
  await expect(page.getByTestId('provider-empty-notice')).toHaveCount(0)
  await page.getByRole('button', { name: '공급자 다시 조회' }).click()
  await expect(page.getByTestId('sprite-image-model')).toContainText('Sprite Image Fake')
})
for (const options of [
  { providersEmpty: true },
  { unsupportedModel: true },
  { imageModelsError: true },
])
  test(`invalid model/provider blocks start ${JSON.stringify(options)}`, async ({ page }) => {
    await installFakeApi(page, { sprites: options })
    await page.goto('/2d/background')
    await expect(enhanceButton(page)).toBeDisabled()
    await page.getByTestId('mode-tab-image').click()
    await expect(page.getByRole('button', { name: '2D 분석 시작', exact: true })).toBeDisabled()
    if (options.providersEmpty)
      await expect(page.getByTestId('provider-empty-notice').first()).toBeVisible()
    else
      await expect(page.getByRole('alert')).toContainText(
        options.unsupportedModel ? '투명 지원' : '이미지 모델 조회 실패',
      )
  })

test('detail HTTP failure is retryable and an actual 404 stays missing', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', detailErrorOnce: true } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  await expect(page.getByRole('alert')).toContainText('작업 연결 실패')
  await page.getByRole('button', { name: '작업 다시 조회' }).click()
  await expect(page.getByRole('heading', { name: '기준 이미지 검수' })).toBeVisible()
  await page.goto('/2d/background/10000000-0000-4000-8000-999999999999')
  await expect(page.getByRole('alert')).toContainText('작업을 찾을 수 없습니다')
  await expect(page.getByRole('button', { name: '작업 다시 조회' })).toHaveCount(0)
})

test('cached detail failure preserves dirty name and ROI through successful retry', async ({
  page,
}) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { records } })
  await start(page)
  await expect(page.getByRole('heading', { name: '제작 계획 검수' })).toBeVisible()
  await page.getByLabel('대상 이름 1', { exact: true }).fill('아직 저장하지 않은 이름')
  await page.getByLabel('ROI w 1', { exact: true }).fill('0.6')
  let failing = true
  await page.route(`**/api/jobs/${SPRITE_IDS.job}`, (route) =>
    failing
      ? route.fulfill({
          status: 503,
          contentType: 'application/json',
          json: { data: null, error: { code: 'FAKE_ERROR', message: '상세 재조회 연결 실패' } },
        })
      : route.fallback(),
  )
  await page.getByRole('button', { name: '서버 상태 새로고침' }).click()
  await expect(page.getByText('상세 재조회 연결 실패', { exact: false })).toBeVisible()
  await expect(page.getByLabel('대상 이름 1', { exact: true })).toHaveValue(
    '아직 저장하지 않은 이름',
  )
  await expect(page.getByLabel('ROI w 1', { exact: true })).toHaveValue('0.6')
  await expect(page.getByRole('button', { name: '계획 저장', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' })).toBeDisabled()
  failing = false
  await page.getByRole('button', { name: '작업 다시 조회', exact: true }).click()
  await expect(page.getByText('상세 재조회 연결 실패', { exact: false })).toHaveCount(0)
  await expect(page.getByLabel('대상 이름 1', { exact: true })).toHaveValue(
    '아직 저장하지 않은 이름',
  )
  await expect(page.getByLabel('ROI w 1', { exact: true })).toHaveValue('0.6')
  await expect(page.getByRole('button', { name: '계획 저장', exact: true })).toBeEnabled()
  await expect(page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' })).toBeDisabled()
  expect(records).toHaveLength(1)
})

test('lost start and HTTP approval retry keep the exact saved UUID/body', async ({ page }) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { records, lostStartOnce: true, approveErrorOnce: true } })
  await start(page)
  await page.getByRole('button', { name: '같은 접수 요청 재전송' }).click()
  await expect(page.getByRole('heading', { name: '제작 계획 검수' })).toBeVisible()
  expect(records[0]).toEqual(records[1])
  await page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' }).click()
  await page.getByRole('button', { name: '같은 승인 요청 재전송' }).click()
  await expect(page.getByRole('heading', { name: '기준 이미지 검수' })).toBeVisible()
  expect(records[2]).toEqual(records[3])
  expect(records[2]!.body.requestId).not.toBe(records[0]!.body.requestId)
})

test('409 refreshes revision and exposes conflict until a new user approval', async ({ page }) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { records, conflictOnce: true } })
  await start(page)
  await page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' }).click()
  await expect(page.getByRole('alert')).toContainText('계획 충돌')
  await expect(page.getByText('revision 2', { exact: false })).toBeVisible()
  expect(records).toHaveLength(2)
  await expect(page.getByRole('button', { name: '같은 승인 요청 재전송' })).toHaveCount(0)
  await page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성' }).click()
  await expect(page.getByRole('heading', { name: '기준 이미지 검수' })).toBeVisible()
  expect(records[2]!.body.expectedRevision).toBe(2)
  expect(records[2]!.body.requestId).not.toBe(records[1]!.body.requestId)
})

test('partial bases export an explicit approved subset and report exclusions', async ({ page }) => {
  await installFakeApi(page, { sprites: { partialBases: true } })
  await start(page)
  await approvePlan(page)
  await approveBases(page, true)
  const panel = page.getByRole('region', { name: '작업 상태', exact: true })
  await expect(panel.getByLabel('배경 생성 성공')).toHaveText('1 / 2')
  await expect(page.getByText('Fake 이미지 생성 실패')).toBeVisible()
  await page.getByLabel('후경 지면 내보내기 선택', { exact: false }).check()
  await expect(page.getByText('포함: 1개 · 제외 대상: 전경 나무')).toBeVisible()
  await page.getByRole('button', { name: '내보내기', exact: true }).click()
  await expect(page.getByRole('status').first()).toContainText('부분 성공')
  await expect(panel.getByRole('listitem').filter({ hasText: '배경 생성' })).toContainText('미완료')
  await expect(panel.getByRole('listitem').filter({ hasText: '내보내기' })).toContainText(
    '부분 성공',
  )
  await expect(panel.getByRole('progressbar', { name: '완료된 제작 단계' })).toHaveAttribute(
    'aria-valuenow',
    '3',
  )
  await expect(page.getByRole('link', { name: '현재 ZIP 다운로드', exact: false })).toBeVisible()
  await panel.screenshot({ path: '/tmp/noxtend-sprite-status-partial.png', animations: 'disabled' })
})

test('failed pack preserves PNGs without inventing a ZIP and supports task retry/history', async ({
  page,
}) => {
  await installFakeApi(page, { sprites: { seed: 'packFailed' } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  await expect(page.getByText('ZIP 크기 상한 초과')).toBeVisible()
  await expect(page.getByRole('link', { name: 'PNG 다운로드', exact: false })).toHaveCount(2)
  await expect(page.getByRole('link', { name: 'ZIP 다운로드', exact: false })).toHaveCount(0)
  await expect(page.getByText('ZIP 패키징 중입니다.', { exact: false })).toHaveCount(0)
  await page.getByRole('button', { name: '실패 공정 재시도' }).click()
  await expect(page.getByRole('link', { name: '현재 ZIP 다운로드', exact: false })).toBeVisible()
  await page.getByLabel('후경 지면 내보내기 선택', { exact: false }).check()
  await page.getByRole('button', { name: '내보내기', exact: true }).click()
  await expect(page.getByRole('link', { name: '이전 ZIP 다운로드', exact: false })).toBeVisible()
})

test('failed generation can retry and cancellation is authoritative', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'failed' } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  await expect(page.getByRole('status').first()).toContainText('실패')
  const panel = page.getByRole('region', { name: '작업 상태', exact: true })
  await expect(panel.getByLabel('배경 생성 성공')).toHaveText('0 / 2')
  await panel.screenshot({ path: '/tmp/noxtend-sprite-status-failed.png', animations: 'disabled' })
  await page.getByRole('button', { name: '실패 공정 재시도' }).first().click()
  await expect(page.getByRole('img', { name: '후경 지면 기준 이미지' })).toBeVisible()
  await expect(panel.getByLabel('배경 생성 성공')).toHaveText('2 / 2')
  let cancelRequests = 0
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === `/api/jobs/${SPRITE_IDS.job}/cancel`) cancelRequests++
  })
  page.once('dialog', async (dialog) => {
    expect(dialog.type()).toBe('confirm')
    expect(dialog.message()).toContain('작업을 취소할까요?')
    expect(dialog.message()).toContain('PNG와 ZIP')
    await dialog.dismiss()
  })
  await page.getByRole('button', { name: '작업 취소' }).click()
  expect(cancelRequests).toBe(0)
  await expect(page.getByRole('status').first()).not.toContainText('취소')
  await expect(page.getByRole('button', { name: '작업 취소' })).toBeVisible()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: '작업 취소' }).click()
  await expect(page.getByRole('status').first()).toContainText('취소')
  expect(cancelRequests).toBe(1)
  await expect(page.getByRole('link', { name: 'PNG 다운로드', exact: false })).toHaveCount(2)
  await expect(page.getByRole('button', { name: '내보내기', exact: true })).toBeDisabled()
  await expect(
    page.getByRole('button', { name: '후경 지면 기준 재생성', exact: true }),
  ).toBeDisabled()
  await page.reload()
  await expect(page.getByRole('status').first()).toContainText('취소')
  await panel.screenshot({
    path: '/tmp/noxtend-sprite-status-canceled.png',
    animations: 'disabled',
  })
})

test('owned source URL sends only the GUID pair and rejects external/blob identity', async ({
  page,
}) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { records } })
  const url = spriteBackgroundWithSourcePath(SPRITE_IDS.sourceJob, SPRITE_IDS.sourceImage)
  expect(
    readSpriteSource(
      new URLSearchParams('sourceJobId=https://external/image&sourceGeneratedImageId=blob-key'),
    ),
  ).toBeNull()
  expect(readSpriteSource(new URLSearchParams(`sourceJobId=${SPRITE_IDS.sourceJob}`))).toBeNull()
  await page.goto(url)
  await expect(page.getByTestId('mode-tab-image')).toHaveAttribute('aria-selected', 'true')
  await chooseSetting(page, '시점', '탑다운')
  await chooseSetting(page, '결과 유형', '반복 타일')
  await expect(page.getByTestId('sprite-image-model')).toContainText('Sprite Image Fake')
  await page.getByRole('button', { name: '2D 분석 시작', exact: true }).click()
  await expect(page.getByRole('heading', { name: '제작 계획 검수' })).toBeVisible()
  expect(records[0]!.body).toMatchObject({
    sourceJobId: SPRITE_IDS.sourceJob,
    sourceGeneratedImageId: SPRITE_IDS.sourceImage,
  })
  expect(records[0]!.body).not.toHaveProperty('uploadId')
})

test('repeat options follow the chosen view on the input screen', async ({ page }) => {
  await installFakeApi(page, { sprites: {} })
  await page.goto('/2d/background')
  await chooseSetting(page, '결과 유형', '반복 타일')
  const repeat = page.getByRole('combobox', { name: '반복 방향', exact: true })
  const optionNames = async () => {
    await repeat.click()
    const names = await page.getByRole('option').allTextContents()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('listbox')).toHaveCount(0)
    return names
  }
  expect(await optionNames()).toEqual(['가로', '세로', '양쪽'])
  await chooseSetting(page, '시점', '아이소메트릭')
  expect(await optionNames()).toEqual(['격자 X축', '격자 Y축', '양쪽'])
})

test('external revision preserves unsaved draft until explicit reload', async ({ page }) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { records } })
  await start(page)
  await page.getByLabel('대상 이름 1', { exact: true }).fill('아직 저장하지 않은 이름')
  const current = await page.evaluate(
    async (id) => await (await fetch(`/api/jobs/${id}`)).json(),
    SPRITE_IDS.job,
  )
  await page.route(`**/api/jobs/${SPRITE_IDS.job}`, async (route) => {
    const body = structuredClone(current)
    body.data.sprite.reviewRevision = 2
    body.data.sprite.assets[0].plan.name = '다른 검수자의 이름'
    await route.fulfill({ status: 200, contentType: 'application/json', json: body })
  })
  await page.getByRole('button', { name: '서버 상태 새로고침' }).click()
  await expect(page.getByText('미저장 편집을 보존했습니다.', { exact: false })).toBeVisible()
  await expect(page.getByLabel('대상 이름 1', { exact: true })).toHaveValue(
    '아직 저장하지 않은 이름',
  )
  await expect(page.getByRole('button', { name: '계획 저장' })).toBeDisabled()
  expect(records).toHaveLength(1)
  await page.getByRole('button', { name: '서버 계획으로 다시 불러오기' }).click()
  await expect(page.getByLabel('대상 이름 1', { exact: true })).toHaveValue('다른 검수자의 이름')
})

test('390px input plan base review stay bounded with keyboard controls', async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await installFakeApi(page, { sprites: {} })
  await page.goto('/2d/background')
  const view = page.getByRole('combobox', { name: '시점', exact: true })
  await view.focus()
  await expect(view).toBeFocused()
  await page.keyboard.press('Space')
  await expect(page.getByRole('listbox')).toBeVisible()
  await page.keyboard.press('Enter')
  await expect(view).toContainText('횡스크롤')
  await expect(page.getByRole('listbox')).toBeHidden()
  await expect(view).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('combobox', { name: '결과 유형', exact: true })).toBeFocused()
  await chooseSetting(page, '결과 유형', '배경 레이어')
  await page.getByTestId('mode-tab-image').click()
  await page
    .getByTestId('image-input')
    .setInputFiles({ name: 'source.png', mimeType: 'image/png', buffer: SPRITE_SOURCE_PNG })
  await expect(page.getByTestId('sprite-image-model')).toContainText('Sprite Image Fake')
  await page.screenshot({ path: testInfo.outputPath('mobile-input.png'), fullPage: true })
  await page.getByRole('button', { name: '2D 분석 시작', exact: true }).click()
  await expect(page.getByRole('heading', { name: '제작 계획 검수' })).toBeVisible()
  expect(
    await page.getByTestId('page-inner').evaluate((el) => el.scrollWidth <= el.clientWidth),
  ).toBe(true)
  await page.screenshot({ path: testInfo.outputPath('mobile-plan.png'), fullPage: true })
  await approvePlan(page)
  expect(
    await page.getByTestId('page-inner').evaluate((el) => el.scrollWidth <= el.clientWidth),
  ).toBe(true)
  const baseChoice = page.getByLabel('후경 지면 기준 승인 선택', { exact: true })
  await baseChoice.focus()
  await page.keyboard.press('Space')
  await expect(baseChoice).toBeChecked()
  await page.getByTestId('sprite-studio').evaluate((el) => {
    el.scrollTop = 0
  })
  await page.screenshot({ path: testInfo.outputPath('mobile-bases.png'), fullPage: true })
})

test('approved subset can package while another asset is generating', async ({ page }) => {
  const records: NonNullable<SpriteFakeOptions['records']> = []
  await installFakeApi(page, { sprites: { records, runningBase: true } })
  await start(page)
  await approvePlan(page)
  await approveBases(page, true)
  await expect(page.getByText('기준 이미지 생성 중', { exact: true })).toBeVisible()
  await page.getByLabel('후경 지면 내보내기 선택', { exact: false }).check()
  await expect(page.getByText('포함: 1개 · 제외 대상: 전경 나무')).toBeVisible()
  await expect(page.getByRole('button', { name: '내보내기', exact: true })).toBeEnabled()
  await page.getByRole('button', { name: '내보내기', exact: true }).click()
  await expect(page.getByRole('link', { name: '현재 ZIP 다운로드', exact: false })).toBeVisible()
  await expect(page.getByRole('button', { name: '작업 취소', exact: true })).toBeVisible()
  const panel = page.getByRole('region', { name: '작업 상태', exact: true })
  await expect(panel.getByRole('status')).toContainText('기준 이미지 생성')
  await expect(panel.getByRole('status')).toContainText('실행 중')
  await expect(panel.getByLabel('배경 생성 성공')).toHaveText('1 / 2')
  await expect(
    page.getByRole('button', { name: '전경 나무 기준 재생성', exact: true }),
  ).toBeDisabled()
  const exports = records.filter((r) => r.path.endsWith('/exports'))
  expect(exports).toHaveLength(1)
  expect(exports[0]!.body.assetIds).toHaveLength(1)
})

test('failed packaging does not become a spinner for unrelated generation', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'packFailed', runningBase: true } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  await expect(page.getByText('ZIP 크기 상한 초과')).toBeVisible()
  await expect(page.getByRole('link', { name: 'PNG 다운로드', exact: false })).toHaveCount(1)
  await expect(page.getByText('ZIP 패키징 중입니다.', { exact: false })).toHaveCount(0)
  await page.getByLabel('후경 지면 내보내기 선택', { exact: false }).check()
  await expect(page.getByRole('button', { name: '내보내기', exact: true })).toBeEnabled()
})

for (const partial of [false, true])
  test(`static ${partial ? 'partial' : 'full'} ZIP allows base regeneration and preserves other assets`, async ({
    page,
  }) => {
    const records: NonNullable<SpriteFakeOptions['records']> = []
    await installFakeApi(page, { sprites: { records } })
    await start(page)
    await approvePlan(page)
    await approveBases(page)
    await expect(
      page.getByRole('button', { name: '후경 지면 기준 재생성', exact: true }),
    ).toBeEnabled()
    await page.getByLabel('후경 지면 내보내기 선택', { exact: false }).check()
    if (!partial) await page.getByLabel('전경 나무 내보내기 선택', { exact: false }).check()
    await page.getByRole('button', { name: '내보내기', exact: true }).click()
    await expect(page.getByRole('link', { name: '현재 ZIP 다운로드', exact: false })).toBeVisible()
    await page.reload()
    const other = await page
      .getByRole('img', { name: '전경 나무 기준 이미지', exact: true })
      .getAttribute('src')
    const original = await page
      .getByRole('img', { name: '후경 지면 기준 이미지', exact: true })
      .getAttribute('src')
    await page.getByRole('button', { name: '후경 지면 기준 재생성', exact: true }).click()
    await expect(page).toHaveURL(`/2d/background/${SPRITE_IDS.job}`)
    await expect(
      page.getByRole('img', { name: '후경 지면 기준 이미지', exact: true }),
    ).not.toHaveAttribute('src', original!)
    await expect(
      page.getByRole('img', { name: '전경 나무 기준 이미지', exact: true }),
    ).toHaveAttribute('src', other!)
    await expect(page.getByText('최종 승인 완료 · 1프레임', { exact: true })).toHaveCount(1)
    await expect(page.getByLabel('후경 지면 기준 승인 선택', { exact: true })).toBeEnabled()
    await expect(page.getByRole('link', { name: '현재 ZIP 다운로드', exact: false })).toHaveCount(0)
    await expect(page.getByRole('link', { name: '이전 ZIP 다운로드', exact: false })).toBeVisible()
    expect(records.at(-1)!.path).toContain('/frames/0/regenerate')
    await page.reload()
    await expect(page.getByLabel('후경 지면 기준 승인 선택', { exact: true })).toBeEnabled()
    await expect(
      page.getByRole('img', { name: '전경 나무 기준 이미지', exact: true }),
    ).toHaveAttribute('src', other!)
  })

test('sprite analysis card shows models and follows the saved plan', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'analyzing' } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  await expect(page.getByTestId('model-text')).toContainText('analysis')
  await expect(page.getByTestId('model-image')).toContainText('sprite-image')
  await expect(page.getByTestId('sprite-analysis-panel')).toHaveCount(0)
})

test('sprite analysis card reflects saved plan edits only', async ({ page }) => {
  await installFakeApi(page, { sprites: {} })
  await start(page)
  const card = page.getByTestId('sprite-analysis-panel')
  await expect(card.getByTestId('sprite-analysis-summary')).toHaveText('횡스크롤 · 레이어 2개')
  await expect(card.getByTestId('sprite-analysis-output')).toHaveText('배경 레이어')
  await expect(card.getByTestId('sprite-analysis-canvas')).toHaveText(
    '원본 320×180 → 요청 1024×1024 → 최종 320×180',
  )
  await expect(card.getByTestId('sprite-analysis-assets')).toHaveText('2개 · 총 2프레임')
  await expect(page.getByText('모델 요청', { exact: false })).toHaveCount(0)

  await page.getByLabel('루프 애니메이션 1', { exact: true }).check()
  await expect(card.getByTestId('sprite-analysis-assets')).toHaveText('2개 · 총 2프레임')
  await page.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect(card.getByTestId('sprite-analysis-assets')).toHaveText('2개 · 총 9프레임')

  await page.setViewportSize({ width: 390, height: 844 })
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect(card).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
})

test('sprite analysis card names isometric tiles', async ({ page }) => {
  await installFakeApi(page, { sprites: {} })
  await start(page, 'isometric', 'tiles')
  const card = page.getByTestId('sprite-analysis-panel')
  await expect(card.getByTestId('sprite-analysis-summary')).toHaveText('아이소메트릭 · 타일 3개')
  await expect(card.getByTestId('sprite-analysis-output')).toHaveText('반복 타일 · 128px · 양쪽')
})

test('sprite analysis card stays on a canceled job after analysis', async ({ page }) => {
  // canceled seed — 기준 이미지 생성 뒤 취소된 작업
  await installFakeApi(page, { sprites: { seed: 'canceled' } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  await expect(page.getByTestId('sprite-analysis-panel')).toBeVisible()
  await expect(page.getByTestId('model-summary')).toBeVisible()
})

async function promptInput(page: Page) {
  await page.goto('/2d/background')
  await expect(page.getByTestId('mode-tab-prompt')).toHaveAttribute('aria-selected', 'true')
  await expect(page.getByTestId('sprite-image-model')).toContainText('Sprite Image Fake')
  await page.getByTestId('sprite-prompt-input').fill('  항구 마을  ')
}
async function chooseOutput(page: Page) {
  await chooseSetting(page, '시점', '횡스크롤')
  await chooseSetting(page, '결과 유형', '배경 레이어')
}
function countFileUploads(page: Page) {
  let count = 0
  page.on('request', (request) => {
    if (request.method() === 'POST' && new URL(request.url()).pathname === '/api/uploads') count++
  })
  return () => count
}
function sourceDraft(page: Page) {
  const panel = page.getByRole('region', { name: '원본 준비', exact: true })
  const status = page.getByRole('region', { name: '작업 상태', exact: true })
  return {
    panel,
    status,
    steps: status.getByRole('navigation', { name: '2D 제작 단계' }),
    candidates: panel.getByTestId('sprite-source-candidate'),
    prompt: panel.getByTestId('sprite-source-prompt'),
    save: panel.getByRole('button', { name: '저장', exact: true }),
    generate: panel.getByRole('button', { name: '원본 후보 생성', exact: true }),
    analyze: panel.getByRole('button', { name: '이 원본으로 분석', exact: true }),
    reenhance: panel.getByRole('button', { name: '다시 보완', exact: true }),
  }
}
const jobPath = `/2d/background/${SPRITE_IDS.job}`
const uuid = /^[\da-f]{8}-[\da-f]{4}-4[\da-f]{3}-[89ab][\da-f]{3}-[\da-f]{12}$/i
type Records = NonNullable<SpriteFakeOptions['records']>
const sourceRecords = (records: Records, command: string) =>
  records.filter((record) => record.path.endsWith(`/sprites/source/${command}`))

test('prompt draft enhances, generates the edited prompt, restores after reload and analyzes', async ({
  page,
}) => {
  const records: Records = []
  await installFakeApi(page, { sprites: { records } })
  const uploaded = countFileUploads(page)
  await promptInput(page)
  await expect(enhanceButton(page)).toBeDisabled()
  await chooseOutput(page)
  await enhanceButton(page).click()
  await expect(page).toHaveURL(new RegExp(`${jobPath}$`))
  const ui = sourceDraft(page)
  await expect(ui.panel.getByTestId('sprite-source-settings')).toHaveText('횡스크롤 · 배경 레이어')
  await expect(ui.panel.getByTestId('sprite-source-description')).toHaveText('항구 마을')
  await expect(ui.prompt).toHaveValue('항구 마을 · 보완 프롬프트')
  await expect(ui.prompt).toHaveAttribute('aria-describedby', /.+/)
  await expect(ui.panel.getByText('1000자', { exact: false })).toHaveCount(0)
  await expect(ui.panel.getByText(`${'항구 마을 · 보완 프롬프트'.length}/4000`)).toBeVisible()
  await expect(ui.steps.getByRole('listitem')).toHaveCount(6)
  await expect(ui.steps.locator('[aria-current="step"]')).toContainText('원본 준비')
  await expect(ui.status.getByLabel('원본 후보', { exact: true })).toHaveText('0 / 8')
  // 원본 준비 단계에는 분석·배경 요약 없음
  for (const label of ['원본 분석 성공', '배경 생성 성공'])
    await expect(ui.status.getByLabel(label, { exact: true })).toHaveCount(0)
  await expect(ui.generate).toHaveAttribute('data-variant', 'default')
  await expect(ui.analyze).toHaveAttribute('data-variant', 'outline')
  await expect(ui.analyze).toBeDisabled()
  await ui.generate.click()
  await expect(ui.candidates).toHaveCount(1)
  // 저장 없이 고친 프롬프트로 바로 생성
  await ui.prompt.fill('수정한 항구 프롬프트')
  await ui.generate.click()
  await expect(ui.candidates).toHaveCount(2)
  await expect(ui.prompt).toHaveValue('수정한 항구 프롬프트')
  expect(sourceRecords(records, 'generate').map((record) => record.body)).toEqual([
    {
      requestId: expect.stringMatching(uuid),
      expectedRevision: 2,
      prompt: '항구 마을 · 보완 프롬프트',
    },
    { requestId: expect.stringMatching(uuid), expectedRevision: 4, prompt: '수정한 항구 프롬프트' },
  ])
  expect(sourceRecords(records, 'prompt')).toHaveLength(0)

  await page.reload()
  await expect(ui.candidates).toHaveCount(2)
  await expect(ui.prompt).toHaveValue('수정한 항구 프롬프트')
  await expect(ui.status.getByLabel('원본 후보', { exact: true })).toHaveText('2 / 8')
  const ids = await ui.candidates
    .locator('img')
    .evaluateAll((images) =>
      images.map((image) => new URL((image as HTMLImageElement).src).pathname.split('/')),
    )
  expect(ids.map((parts) => parts.slice(0, 7).join('/'))).toEqual(
    Array(2).fill(`/api/jobs/${SPRITE_IDS.job}/sprites/source/candidates`),
  )
  await ui.panel.getByText('사용한 프롬프트').nth(1).click()
  await expect(ui.panel.locator('details p').nth(1)).toHaveText('수정한 항구 프롬프트')
  await expect(ui.panel.locator('details p').nth(1)).toBeVisible()
  await ui.candidates.first().focus()
  await ui.candidates.first().press('Enter')
  await expect(ui.candidates.first()).toHaveAttribute('aria-pressed', 'true')
  await expect(ui.analyze).toHaveAttribute('data-variant', 'default')
  await expect(ui.generate).toHaveAttribute('data-variant', 'outline')
  await ui.analyze.click()
  await expect(page.getByRole('heading', { name: '제작 계획 검수' })).toBeVisible()
  await expect(ui.panel).toHaveCount(0)
  await expect(ui.steps.getByRole('listitem')).toHaveCount(6)
  // 선택 뒤에는 기존 작업 요약 유지
  await expect(ui.status.getByLabel('원본 분석 성공', { exact: true })).toBeVisible()
  await expect(ui.steps.getByRole('listitem').first()).toHaveAttribute('data-state', 'succeeded')
  expect(records.find((record) => record.path === '/api/jobs/sprites/drafts')!.body).toEqual({
    requestId: expect.stringMatching(uuid),
    description: '항구 마을',
    providerConfigId: SPRITE_IDS.provider,
    model: 'analysis',
    imageProviderConfigId: SPRITE_IDS.provider,
    imageModel: 'sprite-image',
    settings: { view: 'sideView', outputKind: 'layers', tileWidth: 128, repeat: 'both' },
  })
  expect(sourceRecords(records, 'select')[0]!.body.candidateId).toBe(ids[0]![7])
  expect(records.some((record) => record.path === '/api/jobs/sprites')).toBe(false)
  expect(uploaded()).toBe(0)
})

test('source generation failure keeps candidates and retry clears the failed row', async ({
  page,
}) => {
  const records: Records = []
  await installFakeApi(page, {
    sprites: {
      records,
      seed: 'sourceDraft',
      sourceCandidates: 1,
      sourceFailures: ['generateSpriteSource'],
    },
  })
  await page.goto(jobPath)
  const ui = sourceDraft(page)
  await ui.candidates.first().click()
  await ui.generate.click()
  await expect(ui.status.getByText('원본 후보 생성 · 실패', { exact: false })).toBeVisible()
  await expect(ui.status.getByText('Fake 원본 생성 실패', { exact: true })).toBeVisible()
  await expect(ui.candidates).toHaveCount(1)
  await expect(ui.candidates.first()).toHaveAttribute('aria-pressed', 'true')
  await ui.status.getByRole('button', { name: '실패 공정 재시도', exact: true }).click()
  await expect(ui.candidates).toHaveCount(2)
  await expect(ui.status.getByRole('button', { name: '실패 공정 재시도' })).toHaveCount(0)
  await expect(ui.status.getByText('원본 후보 생성 ·', { exact: false })).toHaveCount(0)
  expect(sourceRecords(records, 'generate')).toHaveLength(1)
})

test('busy source command keeps the draft and refreshes from the server', async ({ page }) => {
  const records: Records = []
  const sprites: SpriteFakeOptions = {
    records,
    seed: 'sourceDraft',
    sourceCandidates: 1,
    sourceBusyOnce: true,
    sourceTaskGets: Infinity,
  }
  await installFakeApi(page, { sprites })
  await page.goto(jobPath)
  const ui = sourceDraft(page)
  await ui.generate.click()
  await expect(ui.panel.getByRole('alert')).toHaveText('원본 준비 공정이 실행 중입니다')
  await expect(ui.generate).toBeDisabled()
  await expect(ui.status.getByLabel('원본 공정', { exact: true })).toHaveText('원본 후보 생성 중')
  sprites.sourceTaskGets = 1
  await expect(ui.candidates).toHaveCount(2)
  await expect(ui.generate).toBeEnabled()
  expect(sourceRecords(records, 'generate')).toHaveLength(1)
})

test('prompt state survives tabs while image mode starts the selected file', async ({ page }) => {
  const records: Records = []
  await installFakeApi(page, { sprites: { records } })
  const uploaded = countFileUploads(page)
  await promptInput(page)
  await chooseOutput(page)
  await page.getByTestId('mode-tab-image').press('Enter')
  await page
    .getByTestId('image-input')
    .setInputFiles({ name: 'source.png', mimeType: 'image/png', buffer: SPRITE_SOURCE_PNG })
  await page.getByTestId('mode-tab-prompt').press('Space')
  await expect(page.getByTestId('sprite-prompt-input')).toHaveValue('  항구 마을  ')
  await expect(enhanceButton(page)).toBeEnabled()
  await page.getByTestId('mode-tab-image').click()
  await page.getByRole('button', { name: '2D 분석 시작', exact: true }).click()
  await expect(page.getByRole('heading', { name: '제작 계획 검수' })).toBeVisible()
  expect(records.find((record) => record.path === '/api/jobs/sprites')!.body.uploadId).toBe(
    SPRITE_IDS.upload,
  )
  expect(records.some((record) => record.path === '/api/jobs/sprites/drafts')).toBe(false)
  expect(uploaded()).toBe(1)
})

test('source query presence selects image mode even for an invalid pair', async ({ page }) => {
  await installFakeApi(page, { sprites: {} })
  await page.goto('/2d/background?sourceJobId=invalid')
  await expect(page.getByTestId('mode-tab-image')).toHaveAttribute('aria-selected', 'true')
  await expect(page.getByRole('alert')).toContainText('원본 작업·결과 ID 쌍이 유효하지 않습니다')
})

test('pending enhance blocks duplicate submit across tabs', async ({ page }) => {
  const records: Records = []
  await installFakeApi(page, { sprites: { records } })
  await promptInput(page)
  await chooseOutput(page)
  let release!: () => void
  const pending = new Promise<void>((resolve) => {
    release = resolve
  })
  await page.route('**/api/jobs/sprites/drafts', async (route) => {
    await pending
    await route.fallback()
  })
  await enhanceButton(page).click()
  const busy = page.getByRole('button', { name: '보완 중…', exact: true })
  await expect(busy).toBeDisabled()
  await expect(page.getByText('이 화면을 유지해 주세요', { exact: false })).toHaveCount(0)
  await page.getByTestId('mode-tab-image').click()
  await expect(page.getByRole('button', { name: '접수 중…', exact: true })).toBeDisabled()
  await page.getByTestId('mode-tab-prompt').click()
  await expect(busy).toBeDisabled()
  release()
  await expect(page).toHaveURL(new RegExp(`${jobPath}$`))
  await expect(sourceDraft(page).prompt).toHaveValue('항구 마을 · 보완 프롬프트')
  expect(records.filter((record) => record.path === '/api/jobs/sprites/drafts')).toHaveLength(1)
})

for (const [width, height, columns] of [
  [1440, 900, 3],
  [390, 844, 2],
] as const)
  test(`source draft panel locks while a task runs and stays bounded at ${width}px dark`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height })
    await page.emulateMedia({ reducedMotion: 'reduce' })
    const sprites: SpriteFakeOptions = {
      seed: 'sourceDraft',
      sourceCandidates: 3,
      sourceDescription: '해질녘 항구 마을. 낮은 채도의 청록과 주황. '.repeat(30) + 'x'.repeat(240),
      sourcePrompt: '부두와 등대가 보이는 긴 프롬프트 '.repeat(120) + 'y'.repeat(240),
      sourceTaskGets: Infinity,
    }
    await installFakeApi(page, { sprites })
    await page.goto(jobPath)
    const toggle = page.getByTestId('theme-toggle')
    if ((await toggle.getAttribute('data-theme-state')) !== 'dark') await toggle.click()
    await expect(toggle).toHaveAttribute('data-theme-state', 'dark')
    const ui = sourceDraft(page)
    await expect(ui.candidates).toHaveCount(3)
    await expect
      .poll(() =>
        ui.candidates
          .first()
          .locator('img')
          .evaluate((image) => (image as HTMLImageElement).naturalWidth),
      )
      .toBe(320)
    expect(
      await ui.panel
        .getByTestId('sprite-source-candidates')
        .evaluate((node) => getComputedStyle(node).gridTemplateColumns.split(' ').length),
    ).toBe(columns)
    await ui.panel.getByText('사용한 프롬프트').first().click()
    const bounded = async () => {
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(
        width,
      )
      expect(
        await page
          .getByTestId('page-inner')
          .evaluate((node) => node.scrollWidth <= node.clientWidth),
      ).toBe(true)
    }
    await bounded()
    const big = ui.panel.getByTestId('sprite-source-selected')
    await expect(big).toHaveCount(0)
    await ui.candidates.nth(1).click()
    await expect(big).toBeVisible()
    await expect(big.getByRole('img', { name: '선택한 원본 후보 2' })).toBeVisible()
    await expect(big.getByText('사용한 프롬프트', { exact: true })).toBeVisible()
    await expect(big).toContainText('#2')
    await bounded()
    await page.screenshot({
      path: test.info().outputPath(`source-draft-${width}.png`),
      fullPage: true,
    })
    await ui.candidates.nth(1).click()
    await expect(big).toHaveCount(0)

    await ui.generate.click()
    await expect(ui.status.getByLabel('원본 공정', { exact: true })).toHaveText('원본 후보 생성 중')
    await expect(ui.steps.locator('[aria-current="step"]')).toContainText('원본 준비')
    for (const control of [ui.prompt, ui.save, ui.generate, ui.analyze, ui.reenhance])
      await expect(control).toBeDisabled()
    for (const item of await ui.candidates.all()) await expect(item).toBeDisabled()
    await expect(ui.panel.getByRole('progressbar')).toHaveCount(0)
    await bounded()
    sprites.sourceTaskGets = 1
    await expect(ui.candidates).toHaveCount(4)
    await expect(ui.generate).toBeEnabled()
    await expect(ui.prompt).toBeEnabled()
  })

for (const [count, columns] of [
  [1, 1],
  [2, 2],
] as const)
  for (const width of [1440, 390])
    test(`source draft with ${count} candidate(s) uses ${columns} column(s) at ${width}px`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 900 })
      await installFakeApi(page, { sprites: { seed: 'sourceDraft', sourceCandidates: count } })
      await page.goto(jobPath)
      const ui = sourceDraft(page)
      await expect(ui.candidates).toHaveCount(count)
      expect(
        await ui.panel
          .getByTestId('sprite-source-candidates')
          .evaluate((node) => getComputedStyle(node).gridTemplateColumns.split(' ').length),
      ).toBe(columns)
      const prompts = ui.panel.getByTestId('sprite-source-candidates').getByText('#1')
      if (count === 1) {
        await expect(prompts).toBeVisible()
        await ui.candidates.first().click()
        await expect(ui.panel.getByTestId('sprite-source-selected')).toHaveCount(0)
      } else {
        await expect(prompts).toBeHidden()
      }
    })

for (const options of [
  {},
  { unsupportedModel: true },
  { imageModelsError: true },
  { textModelsEmpty: true },
])
  test(`prompt enhance requires description, settings and both models ${JSON.stringify(options)}`, async ({
    page,
  }) => {
    await installFakeApi(page, { sprites: options })
    await page.goto('/2d/background')
    const button = enhanceButton(page)
    const reason = page.getByTestId('sprite-submit-reason')
    await expect(button).toBeDisabled()
    await page.getByTestId('sprite-prompt-input').fill('항구')
    await expect(button).toBeDisabled()
    await expect(reason).toHaveText('시점과 결과 유형을 선택해 주세요')
    await chooseOutput(page)
    await page.getByTestId('sprite-prompt-input').fill('   ')
    await expect(button).toBeDisabled()
    await expect(reason).toHaveText('장면 설명을 1~1000자로 입력해 주세요')
    await page.getByTestId('sprite-prompt-input').fill('가'.repeat(1001))
    await expect(button).toBeDisabled()
    await page.getByTestId('sprite-prompt-input').fill('항구')
    if (Object.keys(options).length) {
      await expect(button).toBeDisabled()
      await expect(reason).not.toHaveText('')
    } else {
      await expect(button).toBeEnabled()
      await expect(reason).toHaveCount(0)
    }
  })

for (const catalog of ['providers', 'image-models'])
  test(`cached ${catalog} failure preserves the description but blocks enhance`, async ({
    page,
  }) => {
    const records: Records = []
    await page.clock.install()
    await installFakeApi(page, { sprites: { records } })
    await promptInput(page)
    await chooseOutput(page)
    await expect(enhanceButton(page)).toBeEnabled()
    const path =
      catalog === 'providers'
        ? '/api/providers'
        : `/api/providers/${SPRITE_IDS.provider}/image-models`
    await page.route(`**${path}`, (route) =>
      route.fulfill({
        status: 503,
        json: { data: null, error: { code: 'FAKE_ERROR', message: '캐시 이후 조회 실패' } },
      }),
    )
    await page.clock.fastForward(300001)
    const failed = page.waitForResponse(
      (response) => new URL(response.url()).pathname === path && response.status() === 503,
    )
    await page.evaluate(() => {
      window.dispatchEvent(new Event('offline'))
      window.dispatchEvent(new Event('online'))
    })
    await failed
    await expect(page.getByRole('alert')).toContainText('캐시 이후 조회 실패')
    await expect(
      page.getByText('이전 이미지 모델 선택: Sprite Image Fake', { exact: true }),
    ).toBeVisible()
    await expect(page.getByTestId('sprite-prompt-input')).toHaveValue('  항구 마을  ')
    await expect(enhanceButton(page)).toBeDisabled()
    expect(records).toHaveLength(0)
  })

test('re-enhance with an existing prompt asks before replacing it', async ({ page }) => {
  const records: Records = []
  await installFakeApi(page, { sprites: { records, seed: 'sourceDraft', sourceCandidates: 1 } })
  await page.goto(jobPath)
  const ui = sourceDraft(page)
  await expect(ui.reenhance).toHaveAttribute('data-variant', 'outline')
  await ui.reenhance.click()
  const editor = ui.panel.getByTestId('sprite-source-description-input')
  await expect(editor).toHaveValue('항구 마을')
  await expect(editor).toBeFocused()
  await editor.fill('비 오는 항구 마을')
  let message = ''
  page.once('dialog', (dialog) => {
    message = dialog.message()
    void dialog.dismiss()
  })
  const run = ui.panel.getByRole('button', { name: '보완 실행', exact: true })
  await run.click()
  await expect.poll(() => message).toBe('다시 보완하면 현재 프롬프트가 새 결과로 바뀝니다')
  await expect(editor).toHaveValue('비 오는 항구 마을')
  expect(records).toHaveLength(0)
  page.once('dialog', (dialog) => void dialog.accept())
  await run.click()
  await expect(ui.prompt).toHaveValue('비 오는 항구 마을 · 보완 프롬프트')
  await expect(ui.panel.getByTestId('sprite-source-description')).toHaveText('비 오는 항구 마을')
  await expect(editor).toHaveCount(0)
  expect(records.map((record) => record.body)).toEqual([
    {
      requestId: expect.stringMatching(uuid),
      expectedRevision: 1,
      description: '비 오는 항구 마을',
    },
  ])
})

test('running source generation survives reload with progress, candidates and locked controls', async ({
  page,
}) => {
  const sprites: SpriteFakeOptions = {
    seed: 'sourceDraft',
    sourceCandidates: 1,
    sourceTaskGets: Infinity,
  }
  await installFakeApi(page, { sprites })
  await page.goto(jobPath)
  const ui = sourceDraft(page)
  await ui.generate.click()
  await expect(ui.generate).toBeDisabled()
  await page.reload()
  await expect(ui.steps.locator('[aria-current="step"]')).toContainText('원본 준비')
  await expect(ui.status.getByLabel('원본 공정', { exact: true })).toHaveText('원본 후보 생성 중')
  await expect(ui.status.getByTestId('sprite-task-summary')).toHaveText(
    '원본 후보 생성 · 전체 1 · 실행 중 1',
  )
  await expect(ui.candidates).toHaveCount(1)
  await expect(ui.generate).toBeDisabled()
  await expect(ui.prompt).toBeDisabled()
  await expect(ui.candidates.first()).toBeDisabled()
  await expect(ui.status.getByTestId('progress-buffer')).toBeVisible()
  sprites.sourceTaskGets = 1
  await expect(ui.candidates).toHaveCount(2)
  await expect(ui.status.getByTestId('progress-buffer')).toHaveCount(0)
  await expect(ui.generate).toBeEnabled()
})

test('first enhance failure lets the user write a prompt and generate', async ({ page }) => {
  const records: Records = []
  await installFakeApi(page, {
    sprites: { records, sourceFailures: ['enhanceSpriteSource'] },
  })
  await promptInput(page)
  await chooseOutput(page)
  await enhanceButton(page).click()
  const ui = sourceDraft(page)
  await expect(ui.panel.getByText('프롬프트를 직접 입력해 생성할 수 있습니다')).toBeVisible()
  await expect(ui.status.getByText('원본 프롬프트 보완 · 실패', { exact: false })).toBeVisible()
  await expect(ui.prompt).toHaveValue('')
  await expect(ui.generate).toBeDisabled()
  await ui.prompt.fill('직접 쓴 항구 프롬프트')
  await ui.generate.click()
  await expect(ui.candidates).toHaveCount(1)
  await expect(ui.status.getByRole('button', { name: '실패 공정 재시도' })).toHaveCount(0)
  await expect(ui.status.getByText('원본 프롬프트 보완 ·', { exact: false })).toHaveCount(0)
  expect(sourceRecords(records, 'generate')[0]!.body.prompt).toBe('직접 쓴 항구 프롬프트')
})

test('eight candidates disable generation and cancel leaves candidates read-only', async ({
  page,
}) => {
  const records: Records = []
  await installFakeApi(page, { sprites: { records, seed: 'sourceDraft', sourceCandidates: 8 } })
  await page.goto(jobPath)
  const ui = sourceDraft(page)
  await expect(ui.candidates).toHaveCount(8)
  await expect(ui.generate).toBeDisabled()
  await expect(ui.panel.getByText('후보는 최대 8장입니다', { exact: true })).toBeVisible()
  await expect(ui.status.getByLabel('원본 후보', { exact: true })).toHaveText('8 / 8')
  let message = ''
  page.once('dialog', (dialog) => {
    message = dialog.message()
    void dialog.accept()
  })
  await ui.status.getByRole('button', { name: '작업 취소', exact: true }).click()
  await expect(ui.panel.getByRole('link', { name: '새 2D 작업', exact: true })).toHaveAttribute(
    'href',
    '/2d/background',
  )
  expect(message).toBe(
    '원본 준비를 취소하면 이 작업에서 더 생성할 수 없습니다. 만든 후보는 볼 수 있습니다.',
  )
  await expect(ui.candidates).toHaveCount(8)
  for (const control of [ui.candidates.first(), ui.prompt, ui.reenhance, ui.generate])
    await expect(control).toBeDisabled()
  expect(records.filter((record) => record.path.includes('/sprites/source/'))).toHaveLength(0)
})

test('canceled draft without a prompt hides the manual input hint', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'sourceDraft', sourcePrompt: null } })
  await page.goto(jobPath)
  const ui = sourceDraft(page)
  await expect(ui.panel.getByText('프롬프트를 직접 입력해 생성할 수 있습니다')).toBeVisible()
  page.once('dialog', (dialog) => void dialog.accept())
  await ui.status.getByRole('button', { name: '작업 취소', exact: true }).click()
  await expect(ui.panel.getByRole('link', { name: '새 2D 작업', exact: true })).toBeVisible()
  await expect(ui.prompt).toBeDisabled()
  await expect(ui.panel.getByText('프롬프트를 직접 입력해 생성할 수 있습니다')).toHaveCount(0)
})

test('mixed Fake roles keep approval on same canvas and old ZIP stays stale after grid revert', async ({
  page,
}) => {
  await installFakeApi(page, {
    sprites: {
      seed: 'completed',
      settings: { view: 'topDown', outputKind: 'tiles', tileWidth: 128, repeat: 'both' },
    },
  })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const result = await page.evaluate(async (id) => {
    const url = `/api/jobs/${id}`
    const read = async () => (await (await fetch(url)).json()).data.sprite
    const before = await read()
    const write = async (grid: { cellWidth: number } | null) => {
      const sprite = await read()
      const response = await fetch(`${url}/sprites/plan`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          requestId: crypto.randomUUID(),
          expectedRevision: sprite.reviewRevision,
          assets: sprite.assets.map((a: { plan: unknown }) => a.plan),
          grid,
        }),
      })
      return response.status
    }
    const invalidStatus = await write({ cellWidth: 0 })
    const afterInvalid = await read()
    await write({ cellWidth: 0.9999 })
    const changed = await read()
    await write(null)
    const restored = await read()
    return { before, invalidStatus, afterInvalid, changed, restored }
  }, SPRITE_IDS.job)
  expect(result.before.assets.map((a: { plan: { role: string } }) => a.plan.role)).toEqual([
    'backdrop',
    'tile',
    'object',
  ])
  expect(result.invalidStatus).toBe(400)
  expect(result.afterInvalid).toEqual(result.before)
  expect(result.changed.assets).toEqual(result.before.assets)
  expect(result.changed.exports[0].isCurrent).toBe(false)
  expect(result.changed.completedExportId).toBeNull()
  expect(result.restored.exports[0].isCurrent).toBe(false)
  expect(result.restored.completedExportId).toBeNull()
})

test('added target save synchronizes the draft without reloading', async ({ page }) => {
  await installFakeApi(page, { sprites: {} })
  await start(page)
  await page.getByRole('button', { name: '대상 추가', exact: true }).click()
  await expect(page.getByLabel('대상 이름 3', { exact: true })).toHaveValue('대상 3')
  await page.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect(page.getByRole('button', { name: '계획 저장', exact: true })).toBeDisabled()
  await expect(
    page.getByRole('button', { name: '계획 승인 · 기준 이미지 생성', exact: true }),
  ).toBeEnabled()
  await expect(page.getByRole('button', { name: '대상 추가', exact: true })).toBeEnabled()
  await expect(page.getByLabel('대상 이름 3', { exact: true })).toHaveValue('대상 3')
})
