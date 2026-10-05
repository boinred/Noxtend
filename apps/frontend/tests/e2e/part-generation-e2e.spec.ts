/**
 * Design Ref: §8.3 L3 — 전체 여정.
 *
 * **두 시나리오가 이 사이클의 핵심 흐름이다.** 특히 두 번째("방향 하나 강제 실패 →
 * 부분 성공 → 재시도 → 성공 승격")는 Plan D-3 과 FR-08 이 존재하는 이유 그 자체인데,
 * Do 단계까지 화면에서 한 번도 걸어본 적이 없었다.
 */
import { expect, test } from '@playwright/test'
import { installFakeApi, selectImage } from './fakeApi'

test.describe('L3 — 업로드에서 파츠 이미지까지', () => {
  test('#1 업로드 → 4단계 완주 → 파츠 이미지 N장 → 사용량 확인', async ({ page }) => {
    await installFakeApi(page, { generation: 'all' })

    await page.goto('/background')
    await selectImage(page)

    // 텍스트와 이미지 공급자를 모두 고를 수 있어야 접수가 통과한다
    await expect(page.getByTestId('model-select')).toBeVisible()
    await expect(page.getByTestId('image-model-select')).toBeVisible()

    await page.getByTestId('start-analysis').click()

    // URL 이 작업의 주소다 — 여기서 새로고침해도 이어진다
    await expect(page).toHaveURL(/\/background\/[^/]+$/)
    await expect(page.getByTestId('run-result')).toBeVisible()

    // 파이프라인이 처음으로 픽셀을 냈다 — 이 사이클의 목적
    await expect(page.getByTestId('part-image')).toHaveCount(16)
    await expect(page.getByTestId('job-status-badge')).toHaveText('성공')

    // 장면 명세는 남아 있다 — 생성이 명세를 대체한 것이 아니라 소비한 것이다
    await expect(page.getByTestId('scene-panel')).toBeVisible()

    await page.getByTestId('usage-summary').click()
    await expect(page.getByTestId('usage-row')).toHaveCount(4)
  })

  test('#2 새로고침해도 결과가 이어진다 (FR-13)', async ({ page }) => {
    await installFakeApi(page, { generation: 'all' })

    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('part-image')).toHaveCount(16)

    await page.reload()

    // 상태를 컴포넌트가 들고 있으면 새로고침 한 번에 10분짜리 작업을 잃는다
    await expect(page.getByTestId('part-image')).toHaveCount(16)
  })
})

test.describe('L3 — 부분 성공에서 승격까지', () => {
  test('#3 방향 하나 실패 → 부분 성공 → 재시도 → 성공 (SC-05 · V-7)', async ({ page }) => {
    await installFakeApi(page, { generation: 'partial' })

    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    // ① 부분 성공 — 성공분 15장이 남고 실패 방향 하나가 보인다
    await expect(page.getByTestId('job-status-badge')).toHaveText('부분 성공')
    await expect(page.getByTestId('part-image')).toHaveCount(15)
    await expect(page.getByTestId('part-failed')).toHaveCount(1)

    // ② 실패한 것만 다시 돌린다 — 비싼 성공분을 다시 만들지 않는 것이 목적이다
    await page.getByTestId('part-retry').click()

    // ③ 승격 — 남은 실패가 없으면 작업이 성공으로 올라간다
    await expect(page.getByTestId('job-status-badge')).toHaveText('성공')
    await expect(page.getByTestId('part-image')).toHaveCount(16)
    await expect(page.getByTestId('part-failed')).toHaveCount(0)
  })
})

test.describe('L3 — 관리자 내역 (FR-18·FR-19)', () => {
  test('#4 관리자는 사용량을 전부 편다 (D-14)', async ({ page }) => {
    await installFakeApi(page, { seedTerminalJobs: 1 })

    await page.goto('/admin/calls')

    // 지연 로드된 화면이다 (Check G-1) — 청크가 와야 표가 보인다
    await expect(page.getByTestId('calls-screen')).toBeVisible()
    await expect(page.getByTestId('calls-table')).toBeVisible()
    await expect(page.getByTestId('call-row')).toHaveCount(4)
  })

  test('#5 미등록이 $0 이 아니라 낱말이다 (FR-20 · C-7)', async ({ page }) => {
    await installFakeApi(page, { seedTerminalJobs: 1 })
    await page.goto('/admin/calls')

    // `$0` 으로 보이면 "공짜로 썼다" 로 읽히고, 그 오해가 단가 누락보다 나쁘다.
    // 넷째 칸은 사용량 자체가 없어 단가를 등록해도 값이 생기지 않는 호출이다
    await expect(page.getByTestId('call-cost')).toContainText([
      '$0.011',
      '$0.040',
      '미등록',
      '사용량 없음',
    ])
    await expect(page.getByTestId('calls-total')).toContainText('미등록 1건 · 사용량 없음 1건')
  })

  test('#6 단가 화면에 장당 열이 있다 (D-8)', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin/prices')

    await expect(page.getByTestId('price-table')).toBeVisible()
    // 이미지 모델만 값을 갖고 토큰 모델은 대시다 — 한눈에 구분돼야 한다
    await expect(page.getByTestId('price-per-image')).toContainText(['—', '—', '—', '$0.04'])
  })
})
