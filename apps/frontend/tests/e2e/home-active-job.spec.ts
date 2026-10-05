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
import { installFakeApi } from './fakeApi'

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
