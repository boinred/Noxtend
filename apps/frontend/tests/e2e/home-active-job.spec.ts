/**
 * 홈의 활성 작업 spotlight — "활성 작업 복귀" 여정.
 *
 * **Check 단계 G-2 로 추가됐다.** `product-ui-redesign` Design §6 이 E2E 목록에
 * "활성 작업 복귀" 를 명시했는데 실제로는 없었다.
 *
 * spotlight 는 홈 재구성의 중심(§2.2 항목 3)이고, **목록 전체가 아니라 이것만
 * 상세를 조회한다**(N+1 회피 설계). 그 두 성질 모두 깨져도 알 방법이 없었다.
 */
import { expect, test } from '@playwright/test'
import type { Job } from '../../src/domain/job/types'
import { SPRITE_IDS } from './spriteFakeApi'
import { installFakeApi, selectImage } from './fakeApi'

// 상세 핸들러는 폴링 횟수로 완료 전이를 재현한다. 유예를 크게 주어 검증 동안
// 작업이 "도는 중" 으로 남아 있게 한다 — 완료된 작업은 spotlight 의 대상이 아니다

test.describe('홈 활성 작업 spotlight', () => {
  test('#H1 진행 위치·현재 단계·확정 완료율을 한 줄로 보여준다', async ({ page }) => {
    await installFakeApi(page, { seedRunningJob: true, runningPolls: 20 })
    await page.goto('/')

    const spotlight = page.getByTestId('job-item').first()
    await expect(spotlight).toBeVisible()

    // 장면 완료 · 추출 진행 중 · 분해 대기 → 2/3 단계, 확정 완료율 33%
    await expect(spotlight).toContainText('2/3')
    await expect(spotlight).toContainText('파츠 식별')
    await expect(spotlight).toContainText('33% 완료')
  })

  /**
   * **확정된 것만 센다.** 진행 중인 단계를 완료로 세면 사용자가 실제보다 앞서
   * 있다고 믿게 되고, 그 단계가 실패하면 진행률이 뒤로 간다.
   */
  test('#H2 진행률이 실행 중인 단계를 완료로 세지 않는다', async ({ page }) => {
    await installFakeApi(page, { seedRunningJob: true, runningPolls: 20 })
    await page.goto('/')

    const bar = page.getByTestId('job-item').first().getByRole('progressbar')

    await expect(bar).toHaveAttribute('aria-valuenow', '33')
  })

  test('#H3 spotlight 를 눌러 작업으로 복귀한다', async ({ page }) => {
    await installFakeApi(page, { seedRunningJob: true, runningPolls: 20 })
    await page.goto('/')

    await page.getByTestId('job-item').first().click()

    // URL 이 작업의 주소다 — 탭을 닫았다 열어도 이어진다 (#4 FR-13)
    await expect(page).toHaveURL(/\/background\/seed-running$/)
    await expect(page.getByTestId('stage-progress')).toBeVisible()
  })

  /**
   * **N+1 회피가 설계 결정이다** (Design §2.2). 목록의 모든 작업이 상세를 조회하면
   * 작업이 스무 개일 때 요청이 스물한 번 난다.
   */
  test('#H4 상세 조회는 spotlight 하나만 한다', async ({ page }) => {
    const detailRequests: string[] = []
    await installFakeApi(page, { seedRunningJob: true, runningPolls: 20, seedTerminalJobs: 3 })

    page.on('request', (request) => {
      const match = /\/api\/jobs\/([^/?]+)$/.exec(new URL(request.url()).pathname)
      if (match) detailRequests.push(match[1]!)
    })

    await page.goto('/')
    await expect(page.getByTestId('job-item').first()).toContainText('2/3')

    expect([...new Set(detailRequests)]).toEqual(['seed-running'])
  })

  test('#H5 활성 작업이 없으면 spotlight 가 없다', async ({ page }) => {
    await installFakeApi(page, { seedTerminalJobs: 2 })
    await page.goto('/')

    // 최근 이력은 보이되 실행 중 강조는 없어야 한다
    await expect(page.getByTestId('section-recent')).toBeVisible()
    await expect(page.getByTestId('job-item').filter({ hasText: '실행 중인 작업' })).toHaveCount(0)
  })

  /**
   * **개수가 화면에 보이는 수와 같아야 한다.**
   *
   * "실행 중" 은 맨 앞 한 건을 크게 따로 그리고 나머지만 목록으로 넘긴다. 목록
   * 길이만 세면 카드가 셋 보이는데 "2" 가 뜬다 — 사용자가 세어 보면 바로 어긋난다.
   */
  test('#H6 섹션 제목이 실제로 그려진 작업 수를 보여준다', async ({ page }) => {
    await installFakeApi(page, { seedRunningJob: true, runningPolls: 20, seedTerminalJobs: 3 })
    await page.goto('/')

    await expect(page.getByTestId('section-recent-count')).toHaveText('3')

    // 크게 그린 한 건까지 세야 한다 — 실행 중은 그 한 건이 전부다
    await expect(page.getByTestId('section-running-count')).toHaveText('1')
  })

  /** 0건이면 배지를 감춘다 — 빈 상태 문구가 이미 그 말을 한다 */
  test('#H7 작업이 없으면 개수를 그리지 않는다', async ({ page }) => {
    await installFakeApi(page, {})
    await page.goto('/')

    await expect(page.getByTestId('section-recent-count')).toHaveCount(0)
    await expect(page.getByTestId('section-running-count')).toHaveCount(0)
  })

  /**
   * **줄 끝 세 요소가 한 축에 선다.**
   *
   * 상태 라벨이 이름 줄에만 걸려 있어 휴지통·꺾쇠보다 8.7px 위로 떠 있었다. 한 줄
   * 안에서 어긋난 축은 정렬 실수로 바로 읽힌다 — 실측으로 못박는다.
   */
  test('#H8 상태 라벨이 줄 끝 아이콘과 같은 높이에 선다', async ({ page }) => {
    await installFakeApi(page, { seedTerminalJobs: 2 })
    await page.goto('/')

    const section = page.getByTestId('section-recent')
    const row = section.getByTestId('job-item').first()
    await expect(row).toBeVisible()

    const status = await row.locator('[data-status]').first().boundingBox()
    const trash = await section.getByTestId('job-delete').first().boundingBox()

    const gap = Math.abs(status!.y + status!.height / 2 - (trash!.y + trash!.height / 2))

    // 1px 은 반올림 여유다. 그 이상은 눈에 보인다
    expect(gap).toBeLessThanOrEqual(1)
  })

  /**
   * **목록은 상한(10)까지만 온다.**
   *
   * 그래서 하나를 지우면 열한 번째가 올라와 보이는 수가 그대로다 — 그것만 보면
   * 삭제가 안 된 줄로 읽힌다. 전체가 더 많을 때 그 사실을 화면이 말해야 한다.
   */
  test('#H9 전체가 더 많으면 보이는 수와 전체를 함께 보여준다', async ({ page }) => {
    await installFakeApi(page, { seedTerminalJobs: 14 })
    await page.goto('/')

    await expect(page.getByTestId('section-recent-count')).toHaveText('10 / 14')
    await expect(page.getByTestId('section-recent-list').getByTestId('job-item')).toHaveCount(10)
  })

  /** 전부 보이면 숫자 하나면 된다 — 같은 수를 두 번 쓰면 차이를 찾게 된다 */
  test('#H10 전부 보이면 개수를 하나만 쓴다', async ({ page }) => {
    await installFakeApi(page, { seedTerminalJobs: 3 })
    await page.goto('/')

    await expect(page.getByTestId('section-recent-count')).toHaveText('3')
  })
})

test('목록 실패는 정상 빈 결과와 구분하고 다시 조회할 수 있다', async ({ page }) => {
  await installFakeApi(page)
  let failed = true
  await page.route('**/api/jobs?*', (route) =>
    failed
      ? route.fulfill({
          status: 503,
          json: { data: null, error: { code: 'unavailable', message: '목록 연결 실패' } },
        })
      : route.fallback(),
  )
  await page.goto('/')
  await expect(page.getByTestId('section-running')).toContainText('목록 연결 실패')
  await expect(page.getByText('진행 중인 작업이 없습니다')).toHaveCount(0)
  await expect(page.getByText('아직 완료된 작업이 없습니다')).toHaveCount(0)
  failed = false
  await page.getByTestId('section-running').getByRole('button', { name: '목록 다시 조회' }).click()
  await expect(page.getByText('진행 중인 작업이 없습니다')).toBeVisible()
  await page.goto('/admin/calls')
  failed = true
  await page.reload()
  await expect(page.getByTestId('calls-screen')).toContainText('목록 연결 실패')
  await expect(page.getByTestId('calls-empty')).toHaveCount(0)
})

test('스튜디오 상세 연결 실패는 실제 404와 구분한다', async ({ page }) => {
  await installFakeApi(page)
  for (const category of ['background', 'character']) {
    await page.route('**/api/jobs/missing', (route) =>
      route.fulfill({
        status: 503,
        json: { data: null, error: { code: 'unavailable', message: '작업 연결 실패' } },
      }),
    )
    await page.goto(`/${category}/missing`)
    await expect(page.getByTestId('job-not-found')).toHaveCount(0)
    await expect(page.getByRole('button', { name: '작업 다시 조회' })).toBeVisible()
    await page.unroute('**/api/jobs/missing')
    await page.getByRole('button', { name: '작업 다시 조회' }).click()
    await expect(page.getByTestId('job-not-found')).toBeVisible()
  }
})

test('2D 홈 요약은 현재 프레임·검수 단계와 모드 주소를 사용한다', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'frameReview', frameStates: true } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const job: Job = await page.evaluate(
    async (id) => (await (await fetch(`/api/jobs/${id}`)).json()).data,
    SPRITE_IDS.job,
  )
  const summary = {
    id: job.id,
    category: job.category,
    status: job.status,
    sourceImageId: job.sourceImageId,
    productionMode: job.productionMode,
    partCount: 0,
    createdAt: job.createdAt,
    sprite: {
      phase: job.sprite!.phase,
      assetCount: 2,
      approvedAssetCount: 0,
      imageCount: 99,
      exportCount: 0,
    },
  }
  await page.route('**/api/jobs?*', (route) =>
    route.fulfill({ json: { data: { items: [summary], total: 1 }, error: null } }),
  )
  await page.goto('/')
  const spotlight = page.getByTestId('section-running').getByTestId('job-item')
  await expect(spotlight).toContainText('2D 배경 작업')
  await expect(spotlight).toContainText('애니메이션 프레임 생성')
  await expect(spotlight).toContainText('현재 프레임 이미지 13/16')
  await expect(spotlight).toContainText('실행 1')
  await expect(spotlight.getByRole('progressbar')).toHaveCount(0)
  const recent = page.getByTestId('section-recent').getByTestId('job-item')
  await expect(recent).toContainText('에셋 2개 · 승인 0개 · 누적 이미지 99개')
  await expect(recent).not.toContainText('파츠')
  await spotlight.click()
  await expect(page).toHaveURL(new RegExp(`/2d/background/${SPRITE_IDS.job}$`))
})

test('기존 결과의 현재 방향은 소유 작업·생성 이미지 ID 쌍으로 2D에 진입한다', async ({ page }) => {
  const records: { path: string; body: Record<string, unknown> }[] = []
  await installFakeApi(page, { seedTerminalJobs: 1, generation: 'all', sprites: { records } })
  await page.goto('/background/seed-job-0')
  const job: Job = await page.evaluate(
    async () => (await (await fetch('/api/jobs/seed-job-0')).json()).data,
  )
  job.id = SPRITE_IDS.sourceJob
  const images = job.parts[0]!.generatedImages
  images.forEach(
    (image, index) =>
      (image.id = `40000000-0000-4000-8000-${String(index + 90).padStart(12, '0')}`),
  )
  job.parts[0]!.generatedImageId = images.find((image) => image.viewDirection === 'front')!.id
  await page.route(`**/api/jobs/${job.id}`, (route) =>
    route.fulfill({ json: { data: job, error: null } }),
  )
  await page.goto(`/character/${job.id}`)
  await page.getByTestId('part-image-open').nth(2).click()
  const entry = page.getByTestId('part-image-to-sprite')
  const back = images.find((image) => image.viewDirection === 'back')!.id
  await expect(entry).toHaveAttribute(
    'href',
    `/2d/background?sourceJobId=${job.id}&sourceGeneratedImageId=${back}`,
  )
  await page.keyboard.press('ArrowRight')
  const left = images.find((image) => image.viewDirection === 'left')!.id
  await expect(entry).toHaveAttribute(
    'href',
    `/2d/background?sourceJobId=${job.id}&sourceGeneratedImageId=${left}`,
  )
  await entry.click()
  await expect(
    page.getByText('기존 작업 결과를 원본으로 사용합니다.', { exact: false }),
  ).toBeVisible()
  await page.getByRole('combobox', { name: '시점', exact: true }).click()
  await page.getByRole('option', { name: '횡스크롤' }).click()
  await page.getByRole('combobox', { name: '결과 유형', exact: true }).click()
  await page.getByRole('option', { name: '배경 레이어' }).click()
  await page.getByRole('button', { name: '2D 분석 시작', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`/2d/background/${SPRITE_IDS.job}$`))
  const body = records.find((record) => record.path === '/api/jobs/sprites')!.body
  expect(body.sourceJobId).toBe(job.id)
  expect(body.sourceGeneratedImageId).toBe(left)
  expect(body).not.toHaveProperty('uploadId')
  expect(body).not.toHaveProperty('url')
  expect(body).not.toHaveProperty('blobKey')
})

for (const category of ['background', 'character']) {
  test(`${category} 공급자 조회 실패와 성공한 빈 목록을 구분한다`, async ({ page }) => {
    await installFakeApi(page, { providers: [] })
    await page.route('**/api/providers', (route) =>
      route.fulfill({
        status: 503,
        json: { data: null, error: { code: 'unavailable', message: '공급자 연결 실패' } },
      }),
    )
    await page.goto(`/${category}`)
    await expect(page.getByRole('alert')).toContainText('공급자 연결 실패')
    await expect(page.getByTestId('provider-empty-notice')).toHaveCount(0)
    await page.unroute('**/api/providers')
    await page.getByRole('button', { name: '공급자 다시 조회' }).click()
    await expect(page.getByTestId('provider-empty-notice')).toBeVisible()
  })
  test(`${category} 캐시된 작업의 재조회 오류는 검수 draft를 보존한다`, async ({ page }) => {
    await installFakeApi(page, { reviewGate: true })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('requires-review-checkbox').check()
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('review-gate')).toBeVisible()
    const id = new URL(page.url()).pathname.split('/').at(-1)!
    if (category === 'character') {
      await page.goto(`/character/${id}`)
      await expect(page.getByTestId('review-gate')).toBeVisible()
    }
    const canvas = page.getByTestId('review-canvas')
    await page.waitForFunction(
      () =>
        (document.querySelector('[data-testid="overlay-image"]') as HTMLImageElement)
          ?.naturalWidth > 0,
    )
    await canvas.evaluate((element) => element.scrollIntoView({ block: 'start' }))
    const box = (await canvas.boundingBox())!
    await page.mouse.move(box.x + box.width * 0.05, box.y + box.height * 0.08)
    await page.mouse.down()
    await page.mouse.move(box.x + box.width * 0.2, box.y + box.height * 0.2)
    await page.mouse.up()
    await page.getByTestId('review-add-name').fill('미저장 파츠')
    await page.route(`**/api/jobs/${id}`, (route) =>
      route.fulfill({
        status: 503,
        json: { data: null, error: { code: 'unavailable', message: '상세 재조회 실패' } },
      }),
    )
    await expect(page.getByText('상세 재조회 실패', { exact: true })).toBeVisible()
    await expect(page.getByTestId('review-add-name')).toHaveValue('미저장 파츠')
    await expect(page.getByTestId('job-not-found')).toHaveCount(0)
    await page.unroute(`**/api/jobs/${id}`)
    await page.getByRole('button', { name: '작업 다시 조회' }).click()
    await expect(page.getByText('상세 재조회 실패', { exact: true })).toHaveCount(0)
    await expect(page.getByTestId('review-add-name')).toHaveValue('미저장 파츠')
  })
}

test('홈 상세 오류는 실제 404와 구분하고 링크를 유지한다', async ({ page }) => {
  await installFakeApi(page, { seedRunningJob: true, runningPolls: 20 })
  await page.route('**/api/jobs/seed-running', (route) =>
    route.fulfill({
      status: 503,
      json: { data: null, error: { code: 'unavailable', message: '진행 조회 실패' } },
    }),
  )
  await page.goto('/')
  await expect(page.getByRole('alert')).toContainText('진행 조회 실패')
  await expect(page.getByTestId('section-running').getByTestId('job-item')).toHaveAttribute(
    'href',
    '/background/seed-running',
  )
  await expect(page.getByRole('progressbar')).toHaveCount(0)
  await page.unroute('**/api/jobs/seed-running')
  await page.route('**/api/jobs/seed-running', (route) =>
    route.fulfill({
      status: 404,
      json: { data: null, error: { code: 'not_found', message: '삭제된 작업' } },
    }),
  )
  await page.getByRole('button', { name: '작업 다시 조회' }).click()
  await expect(page.getByRole('alert')).toContainText('해당 작업을 찾을 수 없습니다.')
})

test('목록 로딩은 정상 빈 목록으로 표시하지 않는다', async ({ page }) => {
  await installFakeApi(page)
  let release!: () => void
  const ready = new Promise<void>((resolve) => {
    release = resolve
  })
  await page.route('**/api/jobs?*', async (route) => {
    await ready
    await route.fallback()
  })
  await page.goto('/')
  await expect(page.getByText('작업 목록을 불러오는 중…')).toHaveCount(2)
  await expect(page.getByText('진행 중인 작업이 없습니다')).toHaveCount(0)
  release()
  await expect(page.getByText('진행 중인 작업이 없습니다')).toBeVisible()
})

test('2D 분석·패키징 단계는 현재 공정만 실행·대기 수에 포함한다', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'analyzing' } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const job: Job = await page.evaluate(
    async (id) => (await (await fetch(`/api/jobs/${id}`)).json()).data,
    SPRITE_IDS.job,
  )
  const summary = {
    id: job.id,
    category: job.category,
    status: job.status,
    sourceImageId: job.sourceImageId,
    productionMode: job.productionMode,
    partCount: 0,
    createdAt: job.createdAt,
    sprite: {
      phase: job.sprite!.phase,
      assetCount: 2,
      approvedAssetCount: 0,
      imageCount: 0,
      exportCount: 0,
    },
  }
  await page.route('**/api/jobs?*', (route) =>
    route.fulfill({ json: { data: { items: [summary], total: 1 }, error: null } }),
  )
  await page.goto('/')
  const spotlight = page.getByTestId('section-running').getByTestId('job-item')
  await expect(spotlight).toContainText('제작 대상 분석')
  await expect(spotlight).toContainText('실행 1')
  await expect(spotlight).not.toContainText('100% 완료')
  job.sprite!.phase = 'packaging'
  job.tasks[0]!.status = 'succeeded'
  job.tasks.push({
    ...job.tasks[0]!,
    id: 'old-pack',
    kind: 'packSprites',
    ordinal: 20,
    status: 'running',
  })
  job.tasks.push({
    ...job.tasks[0]!,
    id: 'current-pack',
    kind: 'packSprites',
    ordinal: 21,
    status: 'pending',
  })
  summary.sprite.phase = 'packaging'
  await page.route(`**/api/jobs/${job.id}`, (route) =>
    route.fulfill({ json: { data: job, error: null } }),
  )
  await page.reload()
  await expect(spotlight).toContainText('패키징')
  await expect(spotlight).toContainText('실행 0 · 대기 1')
})
