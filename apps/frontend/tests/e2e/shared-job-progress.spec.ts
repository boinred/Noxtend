import { expect, test, type Page } from '@playwright/test'
import type { Job } from '../../src/domain/job/types'
import {
  chooseOption,
  DEFAULT_PROVIDER,
  MESH_PROVIDER,
  installFakeApi,
  selectImage,
  type FakeApiOptions,
} from './fakeApi'

const artifactDir = process.env.JOB_PROGRESS_ARTIFACT_DIR

async function openJob(
  page: Page,
  category: 'background' | 'character',
  options: FakeApiOptions = {},
) {
  await installFakeApi(page, { seedTerminalJobs: 1, ...options })
  if (category === 'background' && !options.runningPolls) {
    await page.goto('/background/seed-job-0')
  } else {
    await page.goto(`/${category}`)
    await selectImage(page)
    if (category === 'character') {
      await page.getByTestId('gender-female').click()
      if (options.providers?.some((provider) => provider.id === MESH_PROVIDER.id)) {
        await page.getByTestId('hint-group-accessory').click()
        await page.getByRole('checkbox', { name: '팔찌', exact: true }).check()
      }
    }
    if (options.providers?.some((provider) => provider.id === MESH_PROVIDER.id)) {
      await page.getByTestId('produces-meshes-checkbox').check()
      await chooseOption(page, 'mesh-provider-select', 'Tripo 운영')
    }
    await page.getByTestId(category === 'character' ? 'start-generation' : 'start-analysis').click()
    await expect(page).toHaveURL(new RegExp(`/${category}/job-\\d+$`))
  }
  const panel = page.getByTestId(`${category}-pipeline-stepper`)
  await expect(panel).toBeVisible()
  return panel
}

async function readJob(page: Page): Promise<Job> {
  const id = new URL(page.url()).pathname.split('/').at(-1)!
  return page.evaluate(async (id) => {
    const response = await fetch(`/api/jobs/${id}`)
    return (await response.json()).data
  }, id)
}

async function replaceJob(page: Page, job: Job) {
  await page.route(`**/api/jobs/${job.id}`, (route) =>
    route.fulfill({ json: { data: job, error: null } }),
  )
  await page.getByRole('button', { name: '서버 상태 새로고침', exact: true }).click()
}

for (const category of ['background', 'character'] as const) {
  const imageLabel = category === 'background' ? '파츠 이미지 생성 성공' : '방향 이미지 생성 성공'

  test(`${category}: header, seven stages and actual success summaries`, async ({ page }) => {
    const panel = await openJob(page, category)
    await expect(panel.getByRole('heading', { name: '파이프라인 진행 상태' })).toBeVisible()
    await expect(panel.getByTestId(/^stepper-step-/)).toHaveCount(7)
    await expect(panel.getByLabel('원본 분석 성공')).toHaveText('1 / 1')
    await expect(panel.getByLabel(imageLabel)).toHaveText('16 / 16')
    await expect(panel.getByLabel('3D 모델 생성 성공')).toHaveText('4 / 4')
    await expect(panel.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '6')
    await expect(panel.getByRole('progressbar')).toHaveAttribute('aria-valuemax', '7')
    await expect(panel.getByRole('button', { name: '작업 취소', exact: true })).toHaveCount(0)
    if (artifactDir) {
      await page.emulateMedia({ reducedMotion: 'reduce' })
      await page.evaluate(() => document.fonts.ready)
      await panel.screenshot({
        path: `${artifactDir}/${category}-desktop.png`,
        animations: 'disabled',
        caret: 'hide',
      })
    }
  })

  test(`${category}: partial results keep actual denominators`, async ({ page }) => {
    const panel = await openJob(page, category, { generation: 'partial', meshOutcome: 'mixed' })
    await expect(panel.getByLabel(imageLabel)).toHaveText('15 / 16')
    await expect(panel.getByLabel('3D 모델 생성 성공')).toHaveText('1 / 4')
    await expect(panel.getByTestId('stepper-step-generate')).toHaveAttribute(
      'data-state',
      'partiallySucceeded',
    )
    await expect(panel.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '4')
  })

  test(`${category}: unplanned results stay unknown`, async ({ page }) => {
    const panel = await openJob(page, category)
    const job = await readJob(page)
    job.models.mesh = null
    job.tasks = job.tasks.filter((task) => !['generate', 'reconstruct'].includes(task.kind))
    job.parts = job.parts.map((part) => ({
      ...part,
      generatedImageId: null,
      generatedImages: [],
      generatedMesh: null,
    }))
    await replaceJob(page, job)
    await expect(panel.getByLabel(imageLabel)).toHaveText('대상 확인 중')
    await expect(panel.getByLabel('3D 모델 생성 성공')).toHaveCount(0)
    await expect(panel).not.toContainText('0 / 0')
    job.models.mesh = { providerConfigId: 'provider-mesh', model: 'P1-20260311' }
    await replaceJob(page, job)
    await expect(panel.getByLabel('3D 모델 생성 성공')).toHaveText('대상 확인 중')
  })

  test(`${category}: mesh results remain visible after later generation`, async ({ page }) => {
    const panel = await openJob(page, category, { meshAtIntake: false })
    const job = await readJob(page)
    expect(job.models.mesh).toBeNull()
    expect(job.tasks.some((task) => task.kind === 'reconstruct')).toBe(true)
    await expect(panel.getByLabel('3D 모델 생성 성공')).toHaveText('4 / 4')
    // 실제 제작 대상 두 개만 남긴 후속 제작 응답
    job.tasks = job.tasks.filter(
      (task) =>
        task.kind !== 'reconstruct' ||
        job.parts.slice(0, 2).some((part) => part.id === task.partId),
    )
    job.parts = job.parts.map((part, index) => ({
      ...part,
      generatedMesh: index < 2 ? part.generatedMesh : null,
    }))
    await replaceJob(page, job)
    await expect(panel.getByLabel('3D 모델 생성 성공')).toHaveText('2 / 2')
    job.tasks = job.tasks.filter((task) => task.kind !== 'reconstruct')
    job.parts = job.parts.map((part) => ({ ...part, generatedMesh: null }))
    await replaceJob(page, job)
    await expect(panel.getByLabel('3D 모델 생성 성공')).toHaveCount(0)
  })

  test(`${category}: collapsed progress remains usable on mobile`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.emulateMedia({ reducedMotion: 'reduce' })
    const panel = await openJob(page, category)
    const toggle = panel.getByRole('button', { name: '상세 접기', exact: true })
    await toggle.focus()
    await page.keyboard.press('Enter')
    await expect(panel.getByTestId('stepper-step-analyze')).toBeHidden()
    await expect(panel.getByLabel(imageLabel)).toHaveText('16 / 16')
    await expect(panel.getByRole('button', { name: '서버 상태 새로고침' })).toBeVisible()
    await page.keyboard.press('Space')
    await expect(panel.getByTestId('stepper-step-analyze')).toBeVisible()
    for (const theme of ['light', 'dark']) {
      const themeToggle = page.getByTestId('theme-toggle')
      if ((await themeToggle.getAttribute('data-theme-state')) !== theme) await themeToggle.click()
      await expect(themeToggle).toHaveAttribute('data-theme-state', theme)
      await expect(panel.getByLabel('3D 모델 생성 성공')).toBeVisible()
      expect(
        await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
      ).toBe(true)
      if (artifactDir) {
        await page.evaluate(() => document.fonts.ready)
        await panel.screenshot({
          path: `${artifactDir}/${category}-mobile-${theme}.png`,
          animations: 'disabled',
          caret: 'hide',
        })
      }
    }
    await panel.getByRole('button', { name: '상세 접기' }).click()
    if (artifactDir)
      await panel.screenshot({
        path: `${artifactDir}/${category}-mobile-collapsed.png`,
        animations: 'disabled',
        caret: 'hide',
      })
  })

  test(`${category}: cancel requires acceptance and preserves input`, async ({ page }) => {
    const panel = await openJob(page, category, {
      runningPolls: 100,
      providers: [DEFAULT_PROVIDER, MESH_PROVIDER],
    })
    const job = await readJob(page)
    // 캐릭터 Fake 완료 전이의 입력 보존과 별개로 현재 접수 입력을 검증
    let requests = 0
    let releaseCancel!: () => void
    const pending = new Promise<void>((resolve) => {
      releaseCancel = resolve
    })
    await page.route('**/api/jobs/*/cancel', async (route) => {
      requests++
      await pending
      await route.fallback()
    })
    const cancel = panel.getByRole('button', { name: '작업 취소', exact: true })
    await expect(page.getByRole('button', { name: '작업 취소', exact: true })).toHaveCount(1)
    page.once('dialog', async (dialog) => {
      expect(dialog.message()).toBe('작업을 취소할까요?')
      await dialog.dismiss()
    })
    await cancel.click()
    expect(requests).toBe(0)
    await expect(panel).toBeVisible()
    page.once('dialog', (dialog) => dialog.accept())
    await cancel.click()
    await expect(cancel).toBeDisabled()
    await expect.poll(() => requests).toBe(1)
    releaseCancel()
    await expect(page.getByTestId('image-dropzone')).toBeVisible()
    expect(requests).toBe(1)
    const url = new URL(page.url())
    expect(url.searchParams.get('image')).toBe(job.sourceImageId)
    await expect(page.getByTestId('image-dropzone').locator('img')).toBeVisible()
    if (category === 'character') {
      await expect(page.getByTestId('gender-female')).toHaveAttribute('aria-pressed', 'true')
      expect(JSON.parse(url.searchParams.get('partHints')!)).toEqual(job.partHints)
    }
    expect(url.searchParams.get('meshModel')).toBe(job.models.mesh!.model)
    expect(url.searchParams.toString()).toContain(job.models.text!.model)
    expect(url.searchParams.toString()).toContain(job.models.image!.model)
  })

  test(`${category}: cached query failure disables cancellation`, async ({ page }) => {
    const panel = await openJob(page, category, { runningPolls: 100 })
    const job = await readJob(page)
    let failing = true
    await page.route(`**/api/jobs/${job.id}`, (route) =>
      failing
        ? route.fulfill({
            status: 503,
            json: {
              data: null,
              error: { code: 'TEMPORARY_FAILURE', message: '진행 조회 실패', fields: null },
            },
          })
        : route.fallback(),
    )
    await panel.getByRole('button', { name: '서버 상태 새로고침' }).click()
    await expect(page.getByRole('alert')).toContainText('진행 조회 실패')
    await expect(panel).toBeVisible()
    await expect(panel.getByRole('button', { name: '작업 취소', exact: true })).toBeDisabled()
    failing = false
    await panel.getByRole('button', { name: '서버 상태 새로고침' }).click()
    await expect(page.getByRole('alert')).toHaveCount(0)
    await expect(panel.getByRole('button', { name: '작업 취소', exact: true })).toBeEnabled()
  })

  test(`${category}: succeeded generation attempts remain in details`, async ({ page }) => {
    await openJob(page, category)
    const job = await readJob(page)
    const task = job.tasks.find((task) => task.kind === 'generate')!
    task.attemptCount = 3
    job.tasks.filter((task) => task.kind === 'generate')[1]!.attemptCount = 2
    await replaceJob(page, job)
    const details = page.getByLabel('공정 시도 정보')
    await expect(details).toContainText('3회 시도')
    await expect(details.getByRole('listitem')).toHaveCount(1)
  })

  test(`${category}: retry details remain visible`, async ({ page }) => {
    await openJob(page, category, { outcome: 'failed' })
    // seeded 종료 작업은 항상 성공이므로 실제 실패 응답으로 덮어쓰기
    const job = await readJob(page)
    job.status = 'failed'
    job.failureReason = 'PROVIDER_CALL_FAILED'
    job.tasks[0] = {
      ...job.tasks[0]!,
      status: 'failed',
      attemptCount: 3,
      failureReason: 'PROVIDER_CALL_FAILED',
    }
    await replaceJob(page, job)
    await expect(page.getByTestId('failure-reason')).not.toBeEmpty()
    await expect(page.getByTestId('stage-progress')).toContainText('3회 시도')
    await expect(page.getByTestId('retry-analysis')).toBeVisible()
    const retry = page.waitForRequest(
      (request) => request.url().includes('/retry') && request.method() === 'POST',
    )
    await page.getByTestId('retry-analysis').click()
    await retry
  })
}
