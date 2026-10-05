/**
 * Design Ref: character-studio §7 #10 · §6.1 · §6.3 — 캐릭터 스튜디오 E2E.
 *
 * 배경 스튜디오 여정을 캐릭터 고유값(성별·파츠 힌트)으로 미러한다. 캐릭터만의 검증은
 * 셋이다: ① 성별 필수 게이팅 ② 힌트·성별이 접수 페이로드에 실림 ③ /character 스위치
 * 세 곳이 켜져 실제 화면이 열림(준비 중 아님).
 */
import { expect, test } from '@playwright/test'
import {
  DEFAULT_PROVIDER,
  MESH_PROVIDER,
  chooseOption,
  installFakeApi,
  selectImage,
} from './fakeApi'

const WITH_MESH = { providers: [DEFAULT_PROVIDER, MESH_PROVIDER] }

test('#1 업로드 → 성별 → 파츠 힌트 → 접수 → 4면 결과 완주', async ({ page }) => {
  await installFakeApi(page)
  await page.goto('/character')

  await selectImage(page)
  await page.getByTestId('gender-female').click()

  // 악세사리 그룹을 펼쳐 팔찌를 포함한다
  await page.getByTestId('hint-group-accessory').click()
  await page.getByRole('checkbox', { name: '팔찌' }).check()

  await page.getByTestId('start-generation').click()

  await expect(page.getByTestId('run-result')).toBeVisible()
  // 파츠 4개 × 4면 = 이미지 16장. part-row 만 세면 "4면" 을 확인한 것이 아니다(리뷰 #5)
  await expect(page.getByTestId('part-row')).toHaveCount(4)
  await expect(page.getByTestId('part-image')).toHaveCount(16)
})

/**
 * 성별은 파츠 힌트가 아니라 **바디의 속성**이다 — 도메인이 "성별은 베이스바디 구성을
 * 가른다" 고 못박는다. 그 관계를 자리로 읽히게 두었으므로, 자리가 흐트러지면 잡는다.
 *
 * 접이식 **밖**이어야 하는 것이 핵심이다. 필수값이라 섹션이 접혀 있으면 안 고른
 * 사용자가 왜 시작이 막히는지 알 수 없다.
 */
test('#5 성별이 바디 그룹 바로 위에, 접이식 밖에 있다', async ({ page }) => {
  await installFakeApi(page)
  await page.goto('/character')

  const gender = page.getByTestId('gender-field')
  const body = page.getByTestId('hint-group-base')

  // 그룹을 펼치지 않아도 보인다 — 접이식 안이면 이 단언이 깨진다
  await expect(gender).toBeVisible()
  await expect(body).toHaveText('바디')

  // 바디 그룹보다 위에 있다
  const genderBox = await gender.boundingBox()
  const bodyBox = await body.boundingBox()
  expect(genderBox!.y + genderBox!.height).toBeLessThanOrEqual(bodyBox!.y)

  // 칩은 가로로 나란하다 — 라벨과 두 선택지가 같은 줄에 온다
  const female = await page.getByTestId('gender-female').boundingBox()
  const male = await page.getByTestId('gender-male').boundingBox()
  expect(Math.abs(female!.y - male!.y)).toBeLessThan(2)
  expect(male!.x).toBeGreaterThan(female!.x)

  // 색 외 상태 표현 — 고르면 aria-pressed 가 켜진다
  await expect(page.getByTestId('gender-female')).toHaveAttribute('aria-pressed', 'false')
  await page.getByTestId('gender-female').click()
  await expect(page.getByTestId('gender-female')).toHaveAttribute('aria-pressed', 'true')
  await expect(page.getByTestId('gender-male')).toHaveAttribute('aria-pressed', 'false')
})

test('#2 성별을 고르기 전에는 시작이 막힌다 (캐릭터 필수)', async ({ page }) => {
  await installFakeApi(page)
  await page.goto('/character')

  await selectImage(page)
  // 이미지가 있어도 성별을 안 고르면 접수를 막는다 (§D-01)
  await expect(page.getByTestId('start-generation')).toBeDisabled()

  await page.getByTestId('gender-male').click()
  await expect(page.getByTestId('start-generation')).toBeEnabled()
})

test('#3 성별·파츠 힌트가 접수 페이로드에 실린다', async ({ page }) => {
  await installFakeApi(page)
  await page.goto('/character')

  await selectImage(page)
  await page.getByTestId('gender-female').click()
  await page.getByTestId('hint-group-accessory').click()
  await page.getByRole('checkbox', { name: '팔찌' }).check()
  // 개수 스테퍼로 2로 올린다 (팔찌는 다중 파츠)
  await page.getByRole('group', { name: '팔찌 개수' }).getByLabel('개수 늘리기').click()

  const request = page.waitForRequest((r) => r.url().endsWith('/api/jobs') && r.method() === 'POST')
  await page.getByTestId('start-generation').click()
  const body = (await request).postDataJSON()

  expect(body.category).toBe('character')
  expect(body.gender).toBe('female')
  expect(body.partHints).toContainEqual({ type: '팔찌', count: 2 })
})

test('#4 변형이 있는 파츠는 변형을 함께 싣는다', async ({ page }) => {
  await installFakeApi(page)
  await page.goto('/character')

  await selectImage(page)
  await page.getByTestId('gender-female').click()
  await page.getByTestId('hint-group-shoes').click()
  await page.getByRole('checkbox', { name: '신발' }).check()

  const request = page.waitForRequest((r) => r.url().endsWith('/api/jobs') && r.method() === 'POST')
  await page.getByTestId('start-generation').click()
  const body = (await request).postDataJSON()

  // 신발은 단일 파츠라 개수 1 고정, 변형은 첫 값(발목형)으로 실린다
  expect(body.partHints).toContainEqual({ type: '신발', count: 1, variant: '발목형' })
})

test('#5 사이드바 캐릭터가 실제 화면으로 열린다 — 준비 중 아님 (스위치 세 곳)', async ({
  page,
}) => {
  await installFakeApi(page)
  await page.goto('/')

  await page.getByTestId('nav-character').click()

  await expect(page).toHaveURL(/\/character$/)
  await expect(page.getByTestId('character-studio')).toBeVisible()
  await expect(page.getByTestId('coming-soon-screen')).toHaveCount(0)
  // 준비 중 배지가 붙지 않는다
  await expect(page.getByTestId('nav-character').locator('[title="준비 중"]')).toHaveCount(0)
})

test('#6 홈에서 캐릭터 작업이 캐릭터 스튜디오로 열린다 (독립 리뷰 #2 회귀 가드)', async ({
  page,
}) => {
  await installFakeApi(page, { runningPolls: 50 })
  await page.goto('/character')

  await selectImage(page)
  await page.getByTestId('gender-female').click()
  await page.getByTestId('start-generation').click()
  await expect(page.getByTestId('run-progress')).toBeVisible()

  // 홈의 "실행 중" 스포트라이트가 캐릭터 작업을 가리킨다 — 배경 경로로 새지 않는다
  await page.getByTestId('nav-home').click()
  const running = page.getByTestId('section-running').getByTestId('job-item').first()
  await expect(running).toContainText('캐릭터 작업')
  await running.click()

  await expect(page).toHaveURL(/\/character\/[^/]+$/)
  await expect(page.getByTestId('character-studio')).toBeVisible()
})

test('#7 진행 중 새로고침 — 이어서 완료 (FR-13, /character/:jobId 이어받기)', async ({ page }) => {
  await installFakeApi(page, { runningPolls: 3 })
  await page.goto('/character')

  await selectImage(page)
  await page.getByTestId('gender-female').click()
  await page.getByTestId('start-generation').click()
  await expect(page.getByTestId('run-progress')).toBeVisible()

  await expect(page).toHaveURL(/\/character\/[^/]+$/)
  await page.reload()
  await expect(page.getByTestId('run-progress')).toBeVisible()

  // 새로고침이 작업을 끊지 않는다 — URL 이 주소이고 서버가 정본이다
  await expect(page.getByTestId('run-result')).toBeVisible({ timeout: 20_000 })
})

/**
 * Design Ref: character-mesh-ui §FR-01~08 — 캐릭터 스튜디오의 3D 선택·이어서 만들기·
 * "다시 시도" 이어받기. 배경(§11.1·§7.1~7.2·§9.1)과 같은 백본을 캐릭터 화면에 켠다.
 */
test.describe('캐릭터 3D', () => {
  test('#8 3D 공급자가 없으면 그 줄이 아예 없다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/character')

    await expect(page.getByTestId('mesh-settings')).toHaveCount(0)
  })

  test('#9 3D 공급자가 있으면 입력 화면에 공급자를 고르는 줄이 뜨고, 고르면 모델도 뜬다', async ({
    page,
  }) => {
    await installFakeApi(page, WITH_MESH)
    await page.goto('/character')

    // 3D 는 기본이 꺼짐이라 켜야 공급자·모델 줄이 뜬다
    await page.getByTestId('produces-meshes-checkbox').check()

    await expect(page.getByTestId('mesh-provider-select')).toBeVisible()
    // D-02 — 기본은 꺼짐이라 공급자를 고르기 전엔 모델 select 가 없다
    await expect(page.getByTestId('mesh-model-select')).toHaveCount(0)

    await chooseOption(page, 'mesh-provider-select', 'Tripo 운영')
    await expect(page.getByTestId('mesh-model-select')).toBeVisible()
  })

  test('#10 3D 를 안 만지면 접수 페이로드에 mesh 필드가 없다 (D-02 — 기본 꺼짐)', async ({
    page,
  }) => {
    await installFakeApi(page, WITH_MESH)
    await page.goto('/character')

    await selectImage(page)
    await page.getByTestId('gender-female').click()

    const request = page.waitForRequest(
      (r) => r.url().endsWith('/api/jobs') && r.method() === 'POST',
    )
    await page.getByTestId('start-generation').click()
    const body = (await request).postDataJSON()

    // 배경은 첫 3D 공급자를 자동으로 고르지만(§D-02), 캐릭터는 만지지 않으면 계속 꺼져 있다
    expect(body.meshProviderConfigId).toBeUndefined()
    expect(body.meshModel).toBeUndefined()
  })

  test('#11 3D 를 고르면 접수 페이로드에 mesh 필드가 실린다', async ({ page }) => {
    await installFakeApi(page, WITH_MESH)
    await page.goto('/character')

    // 3D 는 기본이 꺼짐이라 켜야 공급자·모델 줄이 뜬다
    await page.getByTestId('produces-meshes-checkbox').check()

    await selectImage(page)
    await page.getByTestId('gender-female').click()
    await page.getByTestId('hint-group-accessory').click()
    await page.getByRole('checkbox', { name: '팔찌' }).check()
    await chooseOption(page, 'mesh-provider-select', 'Tripo 운영')

    const request = page.waitForRequest(
      (r) => r.url().endsWith('/api/jobs') && r.method() === 'POST',
    )
    await page.getByTestId('start-generation').click()
    const body = (await request).postDataJSON()

    // mesh 필드가 gender·partHints 와 같은 요청 본문에 함께 실린다 (V-4)
    expect(body.meshProviderConfigId).toBe(MESH_PROVIDER.id)
    expect(body.meshModel).toBeTruthy()
    expect(body.gender).toBe('female')
    expect(body.partHints).toEqual(
      expect.arrayContaining([expect.objectContaining({ type: '팔찌' })]),
    )
  })

  test('#12 3D 없이 끝난 캐릭터 작업은 이어서 만들 수 있다 (사후 추가)', async ({ page }) => {
    await installFakeApi(page, { ...WITH_MESH, meshAtIntake: false })
    await page.goto('/character')

    await selectImage(page)
    await page.getByTestId('gender-female').click()
    await page.getByTestId('start-generation').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    const row = page.getByTestId('mesh-backfill')
    await expect(row).toBeVisible()
    // 4면이 갖춰진 파츠만 대상이라는 근거 문구(T-06)
    await expect(row).toContainText('4면이 갖춰진 파츠만')
    // 파츠 4개(fake 고정 명단) 만큼 유료 호출이 나간다는 사전 안내(FR-06)
    await expect(page.getByTestId('add-mesh')).toContainText('3D 4개 만들기')

    // 클릭 한 번 = POST 한 번 (T-05 연타 가드가 실제로 막는지는 별도지만, 정상
    // 클릭 경로에서 중복 호출이 없는지는 여기서 고정한다)
    const meshRequests: string[] = []
    page.on('request', (r) => {
      if (r.url().includes('/mesh') && r.method() === 'POST') {
        meshRequests.push(r.url())
      }
    })

    await page.getByTestId('add-mesh').click()
    await expect(page.getByTestId('mesh-backfill')).toHaveCount(0)
    expect(meshRequests).toHaveLength(1)
  })

  test('#13 "다시 시도" 가 이미지·성별·파츠 힌트·모델 선택을 이어받는다 (FR-08)', async ({
    page,
  }) => {
    await installFakeApi(page, { ...WITH_MESH, outcome: 'failed' })
    await page.goto('/character')

    // 3D 는 기본이 꺼짐이라 켜야 공급자·모델 줄이 뜬다
    await page.getByTestId('produces-meshes-checkbox').check()

    await selectImage(page)
    await page.getByTestId('gender-female').click()
    await page.getByTestId('hint-group-accessory').click()
    await page.getByRole('checkbox', { name: '팔찌' }).check()
    await chooseOption(page, 'mesh-provider-select', 'Tripo 운영')

    await page.getByTestId('start-generation').click()
    await expect(page.getByTestId('run-failure')).toBeVisible()

    await page.getByTestId('back-to-input').click()

    // 입력 화면으로 돌아왔고, 성별·힌트·3D 선택이 전부 이전 값으로 채워져 있다
    await expect(page.getByTestId('gender-female')).toHaveAttribute('aria-pressed', 'true')
    // 접이식 그룹은 닫힌 채로 시작한다 — 접혀 있어도 선택 수 배지로 이어받힘을 먼저 확인한다
    await expect(page.getByTestId('hint-group-accessory')).toContainText('1개 선택')
    await page.getByTestId('hint-group-accessory').click()
    await expect(page.getByRole('checkbox', { name: '팔찌' })).toBeChecked()
    await expect(page.getByTestId('mesh-provider-select')).toContainText('Tripo 운영')

    // 이미지도 이어받아서 다시 올릴 필요 없이 바로 시작할 수 있다
    await expect(page.getByTestId('start-generation')).toBeEnabled()
  })

  test('#14 3D 결과가 파츠 카드 안에 타일로 보이고, 실패 파츠는 재시도할 수 있다 (V-6)', async ({
    page,
  }) => {
    await installFakeApi(page, { ...WITH_MESH, meshOutcome: 'mixed' })
    await page.goto('/character')

    // 3D 는 기본이 꺼짐이라 켜야 공급자·모델 줄이 뜬다
    await page.getByTestId('produces-meshes-checkbox').check()

    await selectImage(page)
    await page.getByTestId('gender-female').click()
    await chooseOption(page, 'mesh-provider-select', 'Tripo 운영')

    await page.getByTestId('start-generation').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    // 캐릭터도 배경과 같은 PartGallery/MeshTile 을 공유한다(FR-07) — 카테고리 조건이
    // 없으므로 파츠 카드 안에 3D 타일이 그대로 보여야 한다
    await expect(page.getByTestId('mesh-tile').first()).toBeVisible()

    // meshOutcome: 'mixed' 는 파츠마다 완료·진행·실패·대기를 한 줄씩 섞는다
    const failed = page.getByTestId('part-3d-state').nth(2)
    await expect(failed.getByTestId('mesh-retry')).toBeEnabled()
  })
})
