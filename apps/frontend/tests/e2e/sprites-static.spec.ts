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
async function start(page: Page, view = 'sideView', kind = 'layers') {
  await page.goto('/2d/background')
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
      else
        await expect(
          page.getByRole('img', { name: '반복 경계 검수' }).locator('image'),
        ).toHaveCount(9)
      await approveBases(page)
      await expect(page.getByRole('button', { name: '승인', exact: true })).toHaveCount(0)
      await page.getByLabel('후경 지면 내보내기 선택', { exact: false }).check()
      await page.getByLabel('전경 나무 내보내기 선택', { exact: false }).check()
      await expect(page.getByText('포함: 2개 · 제외 대상: 없음')).toBeVisible()
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
        schemaVersion: 1,
        productionMode: 'twoD',
        coordinateOrigin: 'topLeft',
        coordinateUnits: 'pixels',
        input: { excludedAssetIds: [] },
      })
      expect(manifest.assets).toHaveLength(2)
      for (const asset of manifest.assets) {
        expect(asset.frames).toHaveLength(1)
        expect(files.get(asset.basePath)).toEqual(files.get(asset.frames[0].path))
        const pixels = await decoded(page, files.get(asset.basePath)!)
        expect(pixels.width).toBe(actual.width)
        expect(pixels.height).toBe(actual.height)
        expect(await decoded(page, files.get(asset.frames[0].sheetPath)!)).toMatchObject({
          width: actual.width + 4,
          height: actual.height + 4,
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
  await expect(page.getByText('Fake 이미지 생성 실패')).toBeVisible()
  await page.getByLabel('후경 지면 내보내기 선택', { exact: false }).check()
  await expect(page.getByText('포함: 1개 · 제외 대상: 전경 나무')).toBeVisible()
  await page.getByRole('button', { name: '내보내기', exact: true }).click()
  await expect(page.getByRole('status').first()).toContainText('부분 성공')
  await expect(page.getByRole('link', { name: '현재 ZIP 다운로드', exact: false })).toBeVisible()
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
  await page.getByRole('button', { name: '실패 공정 재시도' }).first().click()
  await expect(page.getByRole('img', { name: '후경 지면 기준 이미지' })).toBeVisible()
  await page.getByRole('button', { name: '작업 취소' }).click()
  await expect(page.getByRole('status').first()).toContainText('취소')
  await expect(page.getByRole('button', { name: '내보내기', exact: true })).toBeDisabled()
  await page.reload()
  await expect(page.getByRole('status').first()).toContainText('취소')
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
