/**
 * Design Ref: §8.5 — L3 E2E 여정 7건.
 *
 * L2 가 화면 하나의 동작을 보는 것과 달리, 여기서는 **화면을 넘나드는 상태**를 본다.
 * #6·#7 이 FR-13(작업 이어받기)의 정면 검증이다 — R-9 가 이번 사이클에서 가장
 * 늦게 발견된 위험이었고, 그래서 가장 직접적으로 확인한다.
 */
import { expect, test } from '@playwright/test'
import { chooseOption, installFakeApi, openOptions, selectImage } from './fakeApi'

test('#1 공급자 등록 → 업로드 → 실행 → 결과 완주', async ({ page }) => {
  await installFakeApi(page, { providers: [] })

  // 공급자가 없으면 스튜디오가 다음 할 일을 알려준다
  await page.goto('/background')
  await page.getByTestId('provider-empty-link').click()

  await page.getByTestId('provider-add').click()
  await page.getByTestId('provider-name-input').fill('Claude 운영')
  await page.getByTestId('provider-key-input').fill('sk-ant-test-4f2c')
  await page.getByTestId('provider-submit').click()
  await expect(page.getByTestId('provider-row')).toHaveCount(1)

  await page.getByTestId('nav-background').click()
  await selectImage(page)
  await page.getByTestId('start-analysis').click()

  await expect(page.getByTestId('scene-summary')).toContainText('해질녘')
  await expect(page.getByTestId('part-row')).toHaveCount(4)
})

test('#2 결과 후 새로고침 → 홈 "최근 작업" → 다시 열기', async ({ page }) => {
  await installFakeApi(page)

  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('run-result')).toBeVisible()

  await page.reload()
  await expect(page.getByTestId('run-result')).toBeVisible()

  await page.getByTestId('nav-home').click()
  const item = page.getByTestId('section-recent').getByTestId('job-item').first()
  await expect(item).toBeVisible()
  await item.click()

  // 영속의 의미는 "같은 결과가 다시 열린다" 이다
  await expect(page.getByTestId('scene-summary')).toContainText('해질녘')
})

test('#3 실패 → 다시 시도 → 성공', async ({ page }) => {
  await installFakeApi(page, { outcome: 'failed' })

  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('run-failure')).toBeVisible()

  // 설정 바꿔서 다시 시작하면 입력 단계로 돌아간다. 이전 실패가 화면에 남지 않는다
  await page.getByTestId('back-to-input').click()
  await expect(page.getByTestId('run-failure')).toHaveCount(0)

  await installFakeApi(page, { outcome: 'succeeded' })
  await selectImage(page)
  await page.getByTestId('start-analysis').click()

  await expect(page.getByTestId('run-result')).toBeVisible()
})

test('#4 공급자 2종을 각각 선택할 수 있다', async ({ page }) => {
  await installFakeApi(page, {
    providers: [
      {
        id: 'p-anthropic',
        displayName: 'Claude 운영',
        kind: 'anthropic',
        capabilities: ['textAnalysis'],
        apiKeyMasked: '••••••••4f2c',
        isEnabled: true,
      },
      {
        id: 'p-openai',
        displayName: 'GPT 실험',
        kind: 'openai',
        capabilities: ['textAnalysis', 'imageGeneration'],
        apiKeyMasked: '••••••••abcd',
        isEnabled: true,
      },
    ],
  })

  await page.goto('/background')
  const select = page.getByTestId('provider-select')
  await expect(await openOptions(page, 'provider-select')).toHaveCount(2)
  await page.keyboard.press('Escape')

  await chooseOption(page, 'provider-select', 'GPT 실험')
  // 고른 것이 닫힌 칸에 남는다 — 사용자가 확인하는 것은 값이 아니라 이름이다
  await expect(select).toContainText('GPT 실험')

  await selectImage(page)
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('run-result')).toBeVisible()
})

test('#5 취소 후 재시작 — 상태 오염이 없다', async ({ page }) => {
  await installFakeApi(page, { runningPolls: 50 })

  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('start-analysis').click()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: '작업 취소', exact: true }).click()

  // 취소도 이미지를 이어받는다 — 취소는 대개 "이 이미지 말고" 가 아니라
  // "이 설정 말고" 이고, 바꾸고 싶으면 드롭존을 누르면 된다
  await expect(page).toHaveURL(/\/background\?image=/)

  // 취소된 **작업**의 흔적은 남으면 안 된다 — 이어받은 이미지는 흔적이 아니라 의도다
  await expect(page.getByTestId('run-progress')).toHaveCount(0)
  await expect(page.getByTestId('upload-rejection')).toHaveCount(0)

  await installFakeApi(page, { outcome: 'succeeded' })
  await selectImage(page)
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('run-result')).toBeVisible()
})

test('#6 진행 중 이탈 — 시작 → 홈 → "실행 중" 클릭 → 결과 (FR-13 정면 검증)', async ({ page }) => {
  await installFakeApi(page, { runningPolls: 2 })

  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('run-progress')).toBeVisible()

  const jobUrl = page.url()

  // 10분 이상 걸리는 작업에서 사용자가 페이지를 떠나는 것은 정상이다 (§5.2)
  await page.getByTestId('nav-home').click()
  const running = page.getByTestId('section-running').getByTestId('job-item').first()
  await expect(running).toBeVisible()
  await running.click()

  await expect(page).toHaveURL(jobUrl)
  await expect(page.getByTestId('run-result')).toBeVisible({ timeout: 20_000 })
})

test('#7 진행 중 새로고침 — 이어서 완료 (유실 없음)', async ({ page }) => {
  await installFakeApi(page, { runningPolls: 3 })

  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('run-progress')).toBeVisible()

  await page.reload()
  await expect(page.getByTestId('run-progress')).toBeVisible()

  // 새로고침이 작업을 끊지 않는다 — URL 이 주소이고 서버가 정본이다
  await expect(page.getByTestId('run-result')).toBeVisible({ timeout: 20_000 })
  await expect(page.getByTestId('part-row')).toHaveCount(4)
})

/**
 * 스튜디오를 지연 로드로 내리면서 생긴 대가를 홈 경로에서 되갚는다 (사이클 #8).
 *
 * 링크에 손이 닿는 순간과 실제로 누르는 순간 사이에는 늘 여유가 있다. 그 사이에
 * 청크를 받아두면 지연 로드의 깜빡임은 홈에서 들어오는 흐름에서 사라진다.
 * `/background` 직접 진입은 여전히 기다린다 — 거기엔 미리 받을 틈이 없다.
 */
test('#8 홈에서 스튜디오 링크에 손이 닿으면 화면 묶음을 미리 받는다', async ({ page }) => {
  await installFakeApi(page)
  await page.goto('/')

  const chunk = page.waitForRequest(/BackgroundStudioScreen/, { timeout: 7_000 })
  await page.getByTestId('home-cta-studio').hover()

  await chunk
})
