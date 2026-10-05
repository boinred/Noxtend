/**
 * Design Ref: §11.1 · §11.3 · §11.4 — 4방향 이미지에서 3D 에셋까지.
 *
 * **이 스펙만 3D 공급자를 등록한다.** 기본 fake 에 넣으면 공급자 하나를 전제하는
 * 관리자 시나리오가 전부 흔들린다 — 3D 는 선택이라는 사실이 그대로 드러나는 편이 낫다.
 */
import { expect, test } from '@playwright/test'
import {
  DEFAULT_PROVIDER,
  MESHY_PROVIDER,
  MESH_PROVIDER,
  chooseOption,
  installFakeApi,
  openOptions,
  selectImage,
} from './fakeApi'

const WITH_MESH = { providers: [DEFAULT_PROVIDER, MESH_PROVIDER] }

/** 두 3D 공급자가 함께 있는 상태 — 사용자가 크레딧 있는 쪽을 고른다 (사이클 #12). */
const WITH_BOTH_MESH = { providers: [DEFAULT_PROVIDER, MESH_PROVIDER, MESHY_PROVIDER] }

test.describe('3D 제작', () => {
  test('3D 공급자가 없으면 그 줄이 아예 없다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')

    // 고를 수 없는 것을 비활성으로 보여 주면 "왜 못 고르지" 를 묻게 된다
    await expect(page.getByTestId('mesh-settings')).toHaveCount(0)
  })

  test('3D 공급자가 있으면 공급자와 모델을 고른다', async ({ page }) => {
    await installFakeApi(page, WITH_MESH)
    await page.goto('/background')

    // 3D 는 기본이 꺼짐이라 켜야 공급자·모델 줄이 뜬다
    await page.getByTestId('produces-meshes-checkbox').check()

    await expect(page.getByTestId('mesh-provider-select')).toBeVisible()
    await expect(page.getByTestId('mesh-model-select')).toBeVisible()
  })

  test('완료된 파츠는 GLB 를 내려받을 수 있다', async ({ page }) => {
    await installFakeApi(page, WITH_MESH)
    await page.goto('/background')
    await selectImage(page)
    // 3D 팬아웃 opt-in — 기본이 꺼짐이라 켜야 접수에 3D 선택이 실린다
    await page.getByTestId('produces-meshes-checkbox').check()
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    // 내려받기는 뷰어 안에 있다 (사이클 #13) — 받기 전에 결과를 보는 순서다
    await page.getByTestId('mesh-tile').first().click()
    await page.getByTestId('mesh-download-menu').click()

    const download = page.getByTestId('mesh-download-glb')
    await expect(download).toBeVisible()

    // **공급자 링크가 아니다.** 그쪽 URL 은 5분이면 만료되므로 우리 endpoint 여야 한다
    await expect(download).toHaveAttribute('href', /\/api\/generated-meshes\//)
    await expect(download).toContainText('MB')
  })

  test('상태마다 다른 것을 보여준다', async ({ page }) => {
    await installFakeApi(page, { ...WITH_MESH, meshOutcome: 'mixed' })
    await page.goto('/background')
    await selectImage(page)
    // 3D 팬아웃 opt-in — 기본이 꺼짐이라 켜야 접수에 3D 선택이 실린다
    await page.getByTestId('produces-meshes-checkbox').check()
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    const cells = page.getByTestId('part-3d-state')

    // 완료 · 진행 · 실패 · 대기가 한 줄씩 — 갈래가 일곱이라 하나만 두면 나머지를 못 본다
    await expect(cells.nth(0)).toHaveAttribute('data-state', 'ready')
    await expect(cells.nth(1)).toHaveAttribute('data-state', 'running')
    await expect(cells.nth(2)).toHaveAttribute('data-state', 'failed')
    await expect(cells.nth(3)).toHaveAttribute('data-state', 'queued')
  })

  test('진행 중이면 진행률을 막대와 숫자로 보여준다', async ({ page }) => {
    await installFakeApi(page, { ...WITH_MESH, meshOutcome: 'mixed' })
    await page.goto('/background')
    await selectImage(page)
    // 3D 팬아웃 opt-in — 기본이 꺼짐이라 켜야 접수에 3D 선택이 실린다
    await page.getByTestId('produces-meshes-checkbox').check()
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    const running = page.getByTestId('part-3d-state').nth(1)

    await expect(running).toContainText('3D 제작 64%')
    await expect(running.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '64')
  })

  test('실패는 코드가 아니라 사람이 읽는 말이고 다시 시도할 수 있다', async ({ page }) => {
    await installFakeApi(page, { ...WITH_MESH, meshOutcome: 'mixed' })
    await page.goto('/background')
    await selectImage(page)
    // 3D 팬아웃 opt-in — 기본이 꺼짐이라 켜야 접수에 3D 선택이 실린다
    await page.getByTestId('produces-meshes-checkbox').check()
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    const failed = page.getByTestId('part-3d-state').nth(2)

    // 공급자 원문이 그대로 나오면 사용자가 무엇을 해야 할지 알 수 없다 (§11.5)
    await expect(failed).toContainText('3D 제작이 실패했습니다')
    await expect(failed).not.toContainText('MESH_TASK_FAILED')
    await expect(failed.getByTestId('mesh-retry')).toBeEnabled()
  })

  // ─── 두 번째 공급자 (사이클 #12) ───

  test('3D 공급자가 둘이면 둘 다 고를 수 있다', async ({ page }) => {
    await installFakeApi(page, WITH_BOTH_MESH)
    await page.goto('/background')

    // 3D 는 기본이 꺼짐이라 켜야 공급자·모델 줄이 뜬다
    await page.getByTestId('produces-meshes-checkbox').check()

    await expect(page.getByTestId('mesh-provider-select')).toBeVisible()

    // 둘이 나란히 서는 것이 "크레딧 있는 쪽을 고른다" 의 전부다
    await expect(await openOptions(page, 'mesh-provider-select')).toHaveText([
      'Tripo 운영',
      'Meshy 운영',
    ])
  })

  test('Meshy 를 고르면 그쪽 모델이 온다', async ({ page }) => {
    await installFakeApi(page, WITH_BOTH_MESH)
    await page.goto('/background')

    // 3D 는 기본이 꺼짐이라 켜야 공급자·모델 줄이 뜬다
    await page.getByTestId('produces-meshes-checkbox').check()

    await chooseOption(page, 'mesh-provider-select', 'Meshy 운영')

    // 모델 이름과 현재 설정 단가를 함께 보여 사용자가 공급자 선택 전에 비용을 비교한다.
    await expect(await openOptions(page, 'mesh-model-select')).toHaveText([
      'Meshy 7 (Multi-Image)장당 $0.400',
    ])
  })
})

/**
 * Design Ref: §7.1~7.2 — 끝난 작업에 3D 를 뒤늦게 붙인다 (사이클 #11).
 *
 * **이 줄이 있는 이유는 비용이다.** 전체 재실행은 $1.35 를 다시 내는데 그 99.5% 가
 * 이미 갖고 있는 이미지다. 여기서는 3D 크레딧만 나간다.
 */
test.describe('3D 이어서 만들기', () => {
  const NO_MESH = { ...WITH_MESH, meshAtIntake: false }

  async function finished(page: import('@playwright/test').Page, options: object) {
    await installFakeApi(page, options)
    await page.goto('/background')
    await selectImage(page)
    // 3D 팬아웃 opt-in — 기본이 꺼짐이라 켜야 접수에 3D 선택이 실린다.
    // 3D 공급자가 아예 없는 시나리오에서는 체크박스 자체가 없으므로 있을 때만 켠다
    const meshOptIn = page.getByTestId('produces-meshes-checkbox')
    if (await meshOptIn.count()) await meshOptIn.check()
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()
  }

  test('3D 없이 끝난 작업은 이어서 만들 수 있다', async ({ page }) => {
    await finished(page, NO_MESH)

    const row = page.getByTestId('mesh-backfill')

    await expect(row).toBeVisible()
    await expect(row).toContainText('이미지를 다시 만들지 않고')

    // 파츠당 유료 호출 한 건 — 버튼을 누르기 전에 설정 단가 기준 총액을 확인한다
    await expect(page.getByTestId('add-mesh')).toContainText('3D 4개 만들기 · 예상 $1.200')
  })

  test('완료된 3D 결과의 실제 크레딧과 비용이 작업 합계에 포함된다', async ({ page }) => {
    await finished(page, WITH_MESH)

    // LLM $0.051 + Tripo 결과 4건 × $0.300. 미등록·사용량 없음 경고는 그대로 남는다.
    await expect(page.getByTestId('usage-summary')).toContainText('$1.251')
    await page.getByTestId('usage-summary').click()

    await expect(page.getByTestId('usage-row')).toHaveCount(8)
    await expect(page.getByTestId('usage-table')).toContainText('50 크레딧')
    await expect(page.getByTestId('usage-table')).toContainText('$0.300')
  })

  test('이미 3D 를 고른 작업에는 줄이 없다', async ({ page }) => {
    await finished(page, WITH_MESH)

    // 한 번 붙이면 그 뒤는 파츠별 다시 시도가 맡는다
    await expect(page.getByTestId('mesh-backfill')).toHaveCount(0)
  })

  test('누르면 3D 가 붙고 줄이 사라진다', async ({ page }) => {
    await finished(page, NO_MESH)

    await page.getByTestId('add-mesh').click()

    // 작업이 다시 열려 3D 를 만든다 — 줄은 제 역할을 마치고 사라진다
    await expect(page.getByTestId('mesh-backfill')).toHaveCount(0)
  })

  /**
   * 모델을 못 불러오면 시작을 막는다 (§7.2).
   *
   * 서버도 거절하지만, 눌러서 오류를 보는 것보다 못 누르는 편이 낫다.
   */
  test('모델을 못 불러오면 버튼이 막힌다', async ({ page }) => {
    // **접수 화면을 거치지 않는다.** 모델을 못 불러오면 거기서도 시작이 막혀
    // 결과 화면까지 갈 수 없다 — 이미 끝나 있는 작업을 직접 연다
    await installFakeApi(page, {
      ...NO_MESH,
      seedTerminalJobs: 1,
      meshModelsError: { status: 502, code: 'PROVIDER_CALL_FAILED', message: '연결 실패' },
    })

    await page.goto('/background/seed-job-0')

    await expect(page.getByTestId('mesh-backfill')).toBeVisible()
    await expect(page.getByTestId('add-mesh')).toBeDisabled()
  })

  test('3D 공급자가 없으면 줄이 없다', async ({ page }) => {
    await finished(page, { meshAtIntake: false })

    // 고를 수 없는 것을 비활성으로 보여 주면 "왜 못 고르지" 를 묻게 된다
    await expect(page.getByTestId('mesh-backfill')).toHaveCount(0)
  })

  // ─── 두 번째 공급자 (사이클 #12) ───

  test('Meshy 결과에는 FBX 내려받기가 함께 있다', async ({ page }) => {
    await installFakeApi(page, { ...WITH_BOTH_MESH, meshProducesFbx: true })
    await page.goto('/background')
    await selectImage(page)
    // 3D 팬아웃 opt-in — 기본이 꺼짐이라 켜야 접수에 3D 선택이 실린다
    await page.getByTestId('produces-meshes-checkbox').check()
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    await page.getByTestId('mesh-tile').first().click()
    await page.getByTestId('mesh-download-menu').click()

    const fbx = page.getByTestId('mesh-download-fbx')

    await expect(fbx).toBeVisible()
    await expect(fbx).toHaveAttribute('href', /\/api\/generated-meshes\/[^/]+\/fbx$/)

    // GLB 는 그대로 있다 — FBX 가 그것을 대신하지 않는다
    await expect(page.getByTestId('mesh-download-glb')).toBeVisible()
  })

  test('Tripo 결과에는 FBX 버튼이 아예 없다', async ({ page }) => {
    await installFakeApi(page, WITH_MESH)
    await page.goto('/background')
    await selectImage(page)
    // 3D 팬아웃 opt-in — 기본이 꺼짐이라 켜야 접수에 3D 선택이 실린다
    await page.getByTestId('produces-meshes-checkbox').check()
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    await page.getByTestId('mesh-tile').first().click()
    await page.getByTestId('mesh-download-menu').click()

    await expect(page.getByTestId('mesh-download-glb')).toBeVisible()

    // **비활성이 아니라 없다** — 비활성 항목은 "곧 생긴다" 로 읽힌다
    await expect(page.getByTestId('mesh-download-fbx')).toHaveCount(0)
  })
})

/**
 * Design Ref: docs/specs/20260917-selective-view-mirror.md — 파츠별 3D 전송 뷰
 * 자유 선택 + 대칭. 공급자는 파츠 전체에 공유되고, 체크박스는 파츠마다 하나씩이다.
 */
test.describe('파츠별 3D 전송 뷰 자유 선택 + 대칭 (spec 20260917)', () => {
  async function toResult(page: import('@playwright/test').Page) {
    await installFakeApi(page, WITH_MESH)
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()
  }

  test('공급자가 있으면 배치 섹션이 뜨고 체크박스는 기본이 전부 켜짐이다', async ({ page }) => {
    await toResult(page)

    await expect(page.getByTestId('part-mesh-batch')).toBeVisible()
    await expect(page.getByTestId('replan-mesh-submit')).toContainText('3D 4개 만들기')
    await expect(page.getByTestId('axis-front-used').first()).toBeChecked()
    await expect(page.getByTestId('axis-left-used').first()).toBeChecked()
    await expect(page.getByTestId('axis-right-used').first()).toBeChecked()
    await expect(page.getByTestId('axis-back-used').first()).toBeChecked()
  })

  test('좌측→우측 대칭을 켜면 우측 사용이 자동으로 꺼지고 비활성화된다', async ({ page }) => {
    await toResult(page)

    const rightUsed = page.getByTestId('axis-right-used').first()
    await page.getByTestId('axis-mirror-left').first().check()

    await expect(rightUsed).not.toBeChecked()
    await expect(rightUsed).toBeDisabled()
  })

  test('정면 사용을 끄면 나머지가 켜진 채로 남아 있을 때 오류가 뜨고 제출이 막힌다', async ({
    page,
  }) => {
    await toResult(page)

    await page.getByTestId('axis-front-used').first().uncheck()

    await expect(page.getByTestId('axis-selection-error').first()).toBeVisible()
    await expect(page.getByTestId('replan-mesh-selection-error')).toBeVisible()
    await expect(page.getByTestId('replan-mesh-submit')).toBeDisabled()
  })

  test('3D 생성을 누르면 정면 사용이 켜진 파츠 수만큼 replan-mesh 요청이 나간다', async ({
    page,
  }) => {
    await toResult(page)

    const requests: string[] = []
    page.on('request', (request) => {
      if (request.method() === 'POST' && /\/replan-mesh$/.test(request.url())) {
        requests.push(request.url())
      }
    })

    await page.getByTestId('replan-mesh-submit').click()

    await expect.poll(() => requests.length).toBe(4)
  })
})
