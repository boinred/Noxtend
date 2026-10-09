/**
 * Design Ref: §8.4 — L2 UI 액션 26건.
 *
 * §8.7 대응표가 이 파일의 번호를 참조한다. 번호를 바꾸면 대응표도 함께 고친다.
 */
import { expect, test } from '@playwright/test'
import {
  chooseOption,
  DEFAULT_PROVIDER,
  installFakeApi,
  installUnreachableApi,
  openOptions,
  selectImage,
} from './fakeApi'

test.describe('스튜디오 — 입력', () => {
  test('#1 /background 로드 — 스튜디오, 이미지 모드 선택됨', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')

    await expect(page.getByRole('tab')).toHaveText(['프롬프트 모드', '이미지 모드'])
    await expect(page.getByTestId('background-studio')).toBeVisible()
    await expect(page.getByTestId('mode-tab-image')).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByTestId('image-dropzone')).toBeVisible()
  })

  test('#2 프롬프트 모드 — 입력창 + 비활성 버튼', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')
    await page.getByTestId('mode-tab-prompt').click()

    await expect(page.getByTestId('prompt-input')).toBeVisible()
    await expect(page.getByTestId('prompt-generate')).toBeDisabled()
    await expect(page.getByTestId('prompt-coming-soon')).toContainText('준비 중')
  })

  test('#3 잘못된 형식 — 사유 표시', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')
    await selectImage(page, { name: 'doc.pdf', mimeType: 'application/pdf' })

    await expect(page.getByTestId('upload-rejection')).toContainText('PNG · JPG · WEBP')
    // 거부된 파일로는 시작할 수 없다
    await expect(page.getByTestId('start-analysis')).toBeDisabled()
  })

  test('#4 큰 파일 — 사유 표시', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')
    await selectImage(page, { size: 13 * 1024 * 1024 })

    await expect(page.getByTestId('upload-rejection')).toContainText('MB')
  })

  test('#5 정상 이미지 — 미리보기 + 버튼 활성', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')

    await expect(page.getByTestId('start-analysis')).toBeDisabled()
    await selectImage(page)

    await expect(page.getByTestId('image-preview')).toBeVisible()
    await expect(page.getByTestId('start-analysis')).toBeEnabled()
  })

  test('#6 공급자 0개 — /admin 안내', async ({ page }) => {
    await installFakeApi(page, { providers: [] })
    await page.goto('/background')

    await expect(page.getByTestId('provider-empty-notice')).toBeVisible()
    await page.getByTestId('provider-empty-link').click()

    // 사이클 #5: /admin 은 공급자 섹션으로 리다이렉트한다 (§2.3-6).
    // 기존 링크가 깨지지 않는 것이 이 단정의 요점이다
    await expect(page).toHaveURL(/\/admin\/providers$/)
    // 공급자가 0개라 표 대신 빈 상태가 뜬다. 도착 여부는 탭으로 본다
    await expect(page.getByTestId('admin-tabs')).toBeVisible()
  })

  // Check 단계 G-3: §5.4 "Dropzone: 드래그 앤 드롭" 이 미검증이었다.
  // 클릭 경로만 돌고 있어 onDrop 핸들러가 회귀해도 드러나지 않았다
  test('#5b 드래그 앤 드롭으로도 이미지를 받는다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')

    // DataTransfer 를 브라우저 안에서 만들어 실제 drop 이벤트를 발생시킨다.
    // setInputFiles 는 input 을 직접 건드리므로 드롭 경로를 지나지 않는다
    const dataTransfer = await page.evaluateHandle(() => {
      const transfer = new DataTransfer()
      const bytes = Uint8Array.from(
        atob(
          'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
        ),
        (c) => c.charCodeAt(0),
      )
      transfer.items.add(new File([bytes], 'dropped.png', { type: 'image/png' }))
      return transfer
    })

    const dropzone = page.getByTestId('image-dropzone')
    await dropzone.dispatchEvent('dragover', { dataTransfer })
    await expect(dropzone).toHaveAttribute('data-dragging', 'true')

    await dropzone.dispatchEvent('drop', { dataTransfer })

    await expect(page.getByTestId('image-preview')).toBeVisible()
    await expect(page.getByTestId('start-analysis')).toBeEnabled()
  })

  test('#A11y 드롭존을 키보드로 조작한다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')

    // button 이므로 포커스와 Enter 가 공짜로 온다 (§5.4 A11y)
    await page.getByTestId('image-dropzone').focus()
    await expect(page.getByTestId('image-dropzone')).toBeFocused()
  })
})

test.describe('스튜디오 — 실행', () => {
  test('#7 분석 시작 → 진행 → 결과', async ({ page }) => {
    await installFakeApi(page, { runningPolls: 1 })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()

    await expect(page.getByTestId('run-progress')).toBeVisible()
    await expect(page.getByTestId('run-progress')).toContainText('분석하는 중')
    await expect(page.getByTestId('run-result')).toBeVisible({ timeout: 15_000 })
  })

  // 사이클 #5: 자유 문장 프롬프트가 구조화된 장면 명세로 바뀌었다
  test('#8 결과 — 장면 명세 + 파츠 명세', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()

    // 조립을 좌우하는 셋이 화면에 있어야 한다 (Plan D-13)
    await expect(page.getByTestId('scene-camera')).toContainText('수평선')
    await expect(page.getByTestId('scene-light')).toContainText('좌측 후방')
    await expect(page.getByTestId('scene-scale')).toContainText('부두 기둥 — 높이 3m (구조화 기준)')

    await expect(page.getByTestId('part-row')).toHaveCount(4)

    // 파츠 글 영역에서 해당 행의 명세를 바로 확인
    await page.getByRole('button', { name: '등대 파츠 명세 보기' }).click()
    await expect(page.getByRole('dialog', { name: '등대 파츠 명세' })).toBeVisible()

    await expect(page.getByTestId('restart-analysis')).toBeEnabled()

    // 3D 공급자를 등록하지 않은 작업이다 — 그 자리는 "만들지 않음" 으로 남는다.
    // 실제로 만들어지는 경로는 `mesh.spec.ts` 가 본다
    await expect(page.getByTestId('part-3d-state').first()).toHaveAttribute(
      'data-state',
      'notRequested',
    )
  })

  /**
   * 좌표를 원본 위에 겹친다 (§2.3-7).
   *
   * 값만 보여주면 사람이 맞는지 판단할 수 없다 — 이 화면이 R-1 측정의 도구다.
   */
  test('#8b 파츠 좌표가 원본 위에 겹쳐 보인다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    await expect(page.getByTestId('overlay-image')).toBeVisible()
    // 파츠 4개 중 첫 것이 배치 셋이라 상자는 6개다 (사이클 #9)
    await expect(page.getByTestId('overlay-box')).toHaveCount(6)
    // 지면 기준선 — 좌표 검증에서 가장 먼저 보는 선이다
    await expect(page.getByTestId('overlay-horizon')).toContainText('0.55')

    const frame = await page.getByTestId('overlay-image').boundingBox()
    const box = await page.getByTestId('overlay-box').first().boundingBox()

    // 상자가 이미지 안에 들어간다 — 좌표계가 어긋나면 화면 밖으로 나간다
    expect(box!.x).toBeGreaterThanOrEqual(frame!.x - 1)
    expect(box!.x + box!.width).toBeLessThanOrEqual(frame!.x + frame!.width + 1)
  })

  /**
   * 사용자 지적으로 나왔다: "진행 상태를 알 수 없다".
   * 이전에는 "분석하는 중" 한 줄뿐이라 세 단계 중 어디인지 알 수 없었다.
   */
  test('#7b 진행 중 단계와 진행률이 보인다', async ({ page }) => {
    await installFakeApi(page, { runningPolls: 50 })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()

    await expect(page.getByTestId('stage-progress')).toBeVisible()
    // 세 단계가 전부 이름으로 보인다
    await expect(page.getByTestId('stage-item')).toHaveCount(3)
    await expect(page.getByTestId('stage-list')).toContainText('장면 분석')
    await expect(page.getByTestId('stage-list')).toContainText('파츠 분해')

    // buffer 가 value 보다 앞선다 — 진행 중인 단계까지 포함하기 때문이다
    const value = await page.getByTestId('progress-value').boundingBox()
    const buffer = await page.getByTestId('progress-buffer').boundingBox()
    expect(buffer!.width).toBeGreaterThan(value!.width)
  })

  /**
   * 사용자 지적: "2/3 구간 사이에는 애니메이션이 있어야 할 것 같은데".
   *
   * 정적인 반투명 채움은 "예약됨" 으로 읽히지 "일하는 중" 으로는 안 읽힌다.
   * 그 구간이 바로 지금 도는 단계이므로 흐르는 무늬로 활동을 표시한다.
   */
  test('#7d 진행 중 구간의 무늬가 흐른다', async ({ page }) => {
    await installFakeApi(page, { runningPolls: 50 })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('progress-bar')).toHaveAttribute('data-active', 'true')

    const bar = page.getByTestId('progress-buffer')

    // 두 시점의 무늬 위치가 달라야 흐르는 것이다
    const first = await bar.screenshot()
    await page.waitForTimeout(340)
    const second = await bar.screenshot()

    expect(Buffer.compare(first, second)).not.toBe(0)
  })

  test('#7e 끝난 막대는 흐르지 않는다', async ({ page }) => {
    // 다 끝났는데 계속 흐르면 아직 도는 것처럼 보인다
    await installFakeApi(page)
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    // 결과 화면에는 진행 막대가 없다 — 끝난 것을 계속 보여줄 이유가 없다
    await expect(page.getByTestId('progress-bar')).toHaveCount(0)
  })

  test('#7c 실패하면 어느 단계에서 왜인지 보인다', async ({ page }) => {
    // 코드만으로는 알 수 없다 — "PROVIDER_CALL_FAILED" 는 어느 단계인지 안 알려준다
    await installFakeApi(page, { outcome: 'failed' })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()

    await expect(page.getByTestId('run-failure')).toBeVisible()
    await expect(page.getByTestId('stage-progress')).toBeVisible()

    const failed = page.locator('[data-testid="stage-item"][data-status="failed"]')
    await expect(failed).toHaveCount(1)
    await expect(failed).toContainText('장면 분석')
    // 재시도 횟수가 보여야 "멈춘 것" 과 "다시 시도 중" 이 구분된다
    await expect(failed).toContainText('3회 시도')
  })

  test('#9 취소 — 입력 단계 복귀', async ({ page }) => {
    await installFakeApi(page, { runningPolls: 50 })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()

    await expect(page.getByTestId('run-progress')).toBeVisible()
    page.once('dialog', (dialog) => dialog.accept())
    await page.getByRole('button', { name: '작업 취소', exact: true }).click()

    // 이미지를 이어받아 돌아온다 — 설정만 바꿔 다시 돌리는 것이 흔한 다음 행동이다
    await expect(page).toHaveURL(/\/background\?image=/)
    await expect(page.getByTestId('image-dropzone')).toBeVisible()
  })

  test('#10 실패 주입 — 사유 + 다시 시도', async ({ page }) => {
    await installFakeApi(page, { outcome: 'failed' })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()

    await expect(page.getByTestId('run-failure')).toBeVisible()
    await expect(page.getByTestId('failure-reason')).toContainText(
      '외부 API와 보안 연결이 일시적으로 끊겼습니다. 잠시 후 다시 시도해 주세요.',
    )

    await page.getByTestId('back-to-input').click()
    await expect(page.getByTestId('image-dropzone')).toBeVisible()
  })
})

/**
 * 사용자 지적: "다시 분석을 누르면 최근 올린 이미지를 재사용하게 해줘".
 *
 * 같은 이미지로 프롬프트만 바꿔 돌리는 것이 튜닝의 기본 동작인데, 매번 파일을 다시
 * 고르게 하면 흐름이 끊기고 저장소에 같은 이미지의 사본이 쌓인다.
 */
test.describe('이미지 이어받기', () => {
  test('#R1 다시 분석 — 직전 이미지가 이미 들어 있다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    await page.getByTestId('restart-analysis').click()

    // URL 이 이미지를 나른다 — 새로고침해도 유지된다 (FR-13 과 같은 이유)
    await expect(page).toHaveURL(/\/background\?image=/)
    await expect(page.getByTestId('image-preview')).toBeVisible()
    await expect(page.getByTestId('dropzone-caption')).toContainText('직전 이미지')
    // 파일을 새로 고르지 않아도 바로 시작할 수 있다
    await expect(page.getByTestId('start-analysis')).toBeEnabled()
  })

  test('#R2 새로고침해도 이어받은 이미지가 남는다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await page.getByTestId('restart-analysis').click()

    await page.reload()

    await expect(page.getByTestId('image-preview')).toBeVisible()
    await expect(page.getByTestId('start-analysis')).toBeEnabled()
  })

  test('#R3 이어받은 이미지는 다시 올리지 않는다', async ({ page }) => {
    await installFakeApi(page)

    const uploads: string[] = []
    page.on('request', (r) => {
      if (r.url().endsWith('/api/uploads') && r.method() === 'POST') uploads.push(r.url())
    })

    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    expect(uploads).toHaveLength(1)

    await page.getByTestId('restart-analysis').click()
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    // 같은 이미지를 다시 올리면 저장소에 사본이 쌓이고 골든 세트와도 어긋난다
    expect(uploads).toHaveLength(1)
  })

  test('#R4 이어받은 상태에서 다른 이미지를 고르면 그쪽이 이긴다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await page.getByTestId('restart-analysis').click()
    await expect(page.getByTestId('dropzone-caption')).toContainText('직전 이미지')

    await selectImage(page, { name: 'other.png' })

    // 이어받은 이미지를 두고 다른 것을 고르는 것이 자연스러운 순서다
    await expect(page.getByTestId('dropzone-caption')).toContainText('other.png')
  })

  test('#R5 실패·취소에서도 이어받는다', async ({ page }) => {
    await installFakeApi(page, { outcome: 'failed' })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-failure')).toBeVisible()

    await page.getByTestId('back-to-input').click()

    // 실패했을 때야말로 같은 이미지로 다시 돌려보고 싶다
    await expect(page).toHaveURL(/\/background\?image=/)
    await expect(page.getByTestId('image-preview')).toBeVisible()
  })
})

test.describe('관리자', () => {
  test('#11 /admin 로드 — 목록', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin')

    await expect(page.getByTestId('admin-screen')).toBeVisible()
    await expect(page.getByTestId('provider-row')).toHaveCount(1)
  })

  test('#11b 목록이 0개일 때 안내한다', async ({ page }) => {
    await installFakeApi(page, { providers: [] })
    await page.goto('/admin')

    await expect(page.getByTestId('provider-list-empty')).toBeVisible()
  })

  test('#12 등록 — 목록에 추가', async ({ page }) => {
    await installFakeApi(page, { providers: [] })
    await page.goto('/admin')
    await page.getByTestId('provider-add').click()

    await page.getByTestId('provider-name-input').fill('GPT 실험')
    await chooseOption(page, 'provider-kind-select', 'OpenAI')
    await page.getByTestId('provider-key-input').fill('sk-test-abcd')
    await page.getByTestId('provider-submit').click()

    await expect(page.getByTestId('provider-row')).toHaveCount(1)
    await expect(page.getByTestId('provider-row')).toContainText('GPT 실험')
  })

  test('#12b Google 이미지 키를 등록하고 용도를 확인한다', async ({ page }) => {
    await installFakeApi(page, { providers: [] })
    await page.goto('/admin')
    await page.getByTestId('provider-add').click()

    await chooseOption(page, 'provider-kind-select', 'Google Gemini')
    await expect(page.getByTestId('provider-capabilities')).toContainText('이미지 생성')
    await expect(page.getByTestId('provider-capabilities')).not.toContainText('텍스트 분석')

    await page.getByTestId('provider-name-input').fill('Gemini 이미지 운영')
    await page.getByTestId('provider-key-input').fill('AIza-test-abcd')
    await page.getByTestId('provider-submit').click()

    const row = page.getByTestId('provider-row')
    await expect(row).toContainText('Google Gemini')
    await expect(row).toContainText('이미지 생성')
  })

  test('#12c 공급자 목록에 앱이 사용하는 용도를 보여준다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin')

    const row = page.getByTestId('provider-row')
    await expect(row.getByTestId('provider-capability')).toHaveText([
      'T텍스트 분석',
      '◈이미지 생성',
      '≈유사도 평가',
    ])
  })

  test('#13 키 마스킹 — 화면·응답에 평문 없음', async ({ page }) => {
    const plainKey = 'sk-test-supersecret-9999'
    const bodies: string[] = []

    await installFakeApi(page, { providers: [] })
    page.on('response', async (response) => {
      if (!response.url().includes('/api/providers')) return
      bodies.push(await response.text().catch(() => ''))
    })

    await page.goto('/admin')
    await page.getByTestId('provider-add').click()
    await page.getByTestId('provider-name-input').fill('비밀 공급자')
    await page.getByTestId('provider-key-input').fill(plainKey)
    await page.getByTestId('provider-submit').click()

    await expect(page.getByTestId('provider-key-masked')).toContainText('9999')

    // 화면 어디에도 평문이 없다 (§5.4 Security)
    expect(await page.locator('body').innerText()).not.toContain(plainKey)
    // 응답에도 없다 — 요청에는 있지만 되돌아오지 않는다 (§7)
    expect(bodies.join('\n')).not.toContain(plainKey)
  })

  test('#14 수정 (키 비움) — 기존 유지', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin')
    await page.getByTestId('provider-edit').click()

    // 화면이 그 사실을 말해야 한다 — 빈 입력창은 "지워졌다" 로 읽힌다
    await expect(page.getByTestId('provider-key-hint')).toContainText('유지')

    // 키 말고 다른 것을 바꾼다. 아무것도 안 바꾸면 "유지됐다" 가 "수정이 안 됐다" 와
    // 구별되지 않는다 — 이름이 바뀌었는데 마스킹이 그대로여야 계약이 증명된다
    await page.getByTestId('provider-name-input').fill('Claude 운영 (수정)')
    await page.getByTestId('provider-submit').click()

    await expect(page.getByTestId('provider-row')).toContainText('Claude 운영 (수정)')
    await expect(page.getByTestId('provider-key-masked')).toHaveText(DEFAULT_PROVIDER.apiKeyMasked)
  })

  test('#15 삭제 — 제거', async ({ page }) => {
    await installFakeApi(page)
    page.on('dialog', (dialog) => dialog.accept())

    await page.goto('/admin')
    await page.getByTestId('provider-delete').click()

    await expect(page.getByTestId('provider-list-empty')).toBeVisible()
  })

  test('#16 연결 확인 — 결과 표시', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin')
    await page.getByTestId('provider-test').click()

    await expect(page.getByTestId('provider-test-result')).toContainText('정상')
  })
})

test.describe('사이드바', () => {
  test('#17 관리자 항목이 하단 영역에 있다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/')

    await expect(page.getByTestId('sidebar-footer-divider')).toBeVisible()
    await page.getByTestId('nav-admin').click()
    // 사이클 #5: /admin 은 공급자 섹션으로 리다이렉트한다 (§2.3-6)
    await expect(page).toHaveURL(/\/admin\/providers$/)
  })

  test('#17b 접힘 상태에서도 하단 영역이 동작한다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/')
    await page.getByTestId('sidebar-toggle').click()

    await expect(page.getByTestId('sidebar')).toHaveAttribute('data-collapsed', 'true')
    await page.getByTestId('nav-admin').click()
    // 사이클 #5: /admin 은 공급자 섹션으로 리다이렉트한다 (§2.3-6)
    await expect(page).toHaveURL(/\/admin\/providers$/)
  })

  test('#18 배경에 준비 중 표시가 없다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/')

    await expect(page.getByTestId('nav-background').locator('[title="준비 중"]')).toHaveCount(0)
  })
})

test.describe('홈', () => {
  test('#19 최근 작업 — 완료 이력', async ({ page }) => {
    await installFakeApi(page, { seedTerminalJobs: 2 })
    await page.goto('/')

    await expect(page.getByTestId('section-recent-list')).toBeVisible()
    await expect(page.getByTestId('section-recent').getByTestId('job-item')).toHaveCount(2)
  })

  // Check 단계 G-3: §5.4 "Item: 썸네일 · 시각 · 파츠 개수" 를 개수만 확인하고 있었다
  test('#19c 항목이 썸네일·시각·파츠 개수를 갖춘다', async ({ page }) => {
    await installFakeApi(page, { seedTerminalJobs: 1 })
    await page.goto('/')

    const item = page.getByTestId('section-recent').getByTestId('job-item').first()

    // 썸네일은 소스 이미지를 가리킨다 — 어느 작업인지 목록에서 알아보는 유일한 단서다
    await expect(item.locator('img')).toHaveAttribute('src', /\/api\/uploads\/.+\/content$/)
    await expect(item.locator('img')).toBeVisible()

    await expect(item).toContainText('완료')
    await expect(item).toContainText('파츠 4개')
    // 시각은 로케일 형식이라 문구를 고정하지 않고 존재만 본다
    await expect(item).toContainText(/\d+\. ?\d+\./)
  })

  test('#19b 0건이면 빈 상태를 유지한다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/')

    await expect(page.getByTestId('section-recent')).toContainText('아직 완료된 작업이 없습니다')
    await expect(page.getByTestId('section-running')).toContainText('진행 중인 작업이 없습니다')
  })

  test('#20 API 없음 — 화면이 깨지지 않는다', async ({ page }) => {
    // §8.6 회귀 방어 장치. 이것이 깨지면 홈을 건드리는 기존 테스트가 전부 무너진다
    await installUnreachableApi(page)
    await page.goto('/')

    await expect(page.getByTestId('home-screen')).toBeVisible()
    await expect(page.getByTestId('section-running')).toBeVisible()
    await expect(page.getByTestId('section-recent')).toBeVisible()
    await expect(page.getByTestId('sidebar')).toBeVisible()
  })

  test('#21 실행 중 — 진행 중 작업', async ({ page }) => {
    await installFakeApi(page, { runningPolls: 50 })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-progress')).toBeVisible()

    await page.getByTestId('nav-home').click()
    await expect(page.getByTestId('section-running').getByTestId('job-item')).toHaveCount(1)
  })

  test('#22 항목 클릭 → /background/{jobId}', async ({ page }) => {
    await installFakeApi(page, { seedTerminalJobs: 1 })
    await page.goto('/')

    await page.getByTestId('section-recent').getByTestId('job-item').first().click()

    await expect(page).toHaveURL(/\/background\/seed-job-0$/)
    await expect(page.getByTestId('run-result')).toBeVisible()
  })

  test('#22b 최근 작업 항목 삭제 — 삭제 버튼 클릭 시 확인 후 목록에서 제거된다', async ({
    page,
  }) => {
    await installFakeApi(page, { seedTerminalJobs: 2 })
    await page.goto('/')

    // confirm 다이얼로그 자동 수락
    page.on('dialog', (dialog) => dialog.accept())

    const recentSection = page.getByTestId('section-recent')
    await expect(recentSection.getByTestId('job-item')).toHaveCount(2)

    await recentSection.getByTestId('job-delete').first().click()

    await expect(recentSection.getByTestId('job-item')).toHaveCount(1)
  })

  test('#22c 삭제 버튼 클릭 시 상세 페이지로 이동하지 않는다', async ({ page }) => {
    await installFakeApi(page, { seedTerminalJobs: 1 })
    await page.goto('/')

    page.on('dialog', (dialog) => dialog.dismiss())

    const recentSection = page.getByTestId('section-recent')
    await recentSection.getByTestId('job-delete').first().click()

    // 홈 화면에 머무름
    await expect(page).toHaveURL(/\/$/)
    await expect(recentSection.getByTestId('job-item')).toHaveCount(1)
  })

  test('#22d 진행 중 작업에는 삭제 버튼이 없다', async ({ page }) => {
    await installFakeApi(page, { seedRunningJob: true, runningPolls: 50 })
    await page.goto('/')

    const running = page.getByTestId('section-running')
    await expect(running.getByTestId('job-item')).toHaveCount(1)

    // **진행 중에 지우면 유료 외부 작업이 돌고 있는데 추적할 기록이 사라진다.**
    // 서버도 409 로 막지만, 누를 수 있게 두면 사용자가 눌러 보고 오류를 받는다
    await expect(running.getByTestId('job-delete')).toHaveCount(0)
  })

  test('#22e 서버가 진행 중 삭제를 거절하면 목록이 그대로다', async ({ page }) => {
    await installFakeApi(page, { seedRunningJob: true, runningPolls: 50 })
    await page.goto('/')

    const jobId = await page
      .getByTestId('section-running')
      .getByTestId('job-item')
      .first()
      .getAttribute('href')

    // 화면을 거치지 않고 직접 부른다 — fake 가 서버와 같은 규칙인지 보는 자리다
    const status = await page.evaluate(async (href) => {
      const id = href!.split('/').pop()
      const response = await fetch(`/api/jobs/${id}`, { method: 'DELETE' })
      return response.status
    }, jobId)

    expect(status).toBe(409)
    await expect(page.getByTestId('section-running').getByTestId('job-item')).toHaveCount(1)
  })
})

test.describe('작업 이어받기 (FR-13)', () => {
  test('#23 분석 시작 — URL 이 바뀐다', async ({ page }) => {
    await installFakeApi(page, { runningPolls: 50 })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()

    // 상태를 컴포넌트가 아니라 URL 이 갖는다 (§5.2)
    await expect(page).toHaveURL(/\/background\/job-\d+$/)
  })

  test('#24 진행 중 새로고침 — 같은 작업이 이어진다', async ({ page }) => {
    await installFakeApi(page, { runningPolls: 50 })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-progress')).toBeVisible()

    const url = page.url()
    await page.reload()

    await expect(page).toHaveURL(url)
    await expect(page.getByTestId('run-progress')).toBeVisible()
  })

  test('#25 완료 후 새로고침 — 결과가 남는다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()
    await expect(page.getByTestId('run-result')).toBeVisible()

    await page.reload()

    await expect(page.getByTestId('scene-summary')).toContainText('해질녘')
    await expect(page.getByTestId('part-row')).toHaveCount(4)
  })

  test('#26 없는 jobId — 입력 단계 안내', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background/does-not-exist')

    await expect(page.getByTestId('job-not-found')).toBeVisible()
    await page.getByTestId('job-not-found-back').click()
    await expect(page.getByTestId('image-dropzone')).toBeVisible()
  })
})

/**
 * 모델 선택 (§4.2 #14).
 *
 * 이전에는 관리자가 모델을 문자열로 등록했고, 오타나 부적합 모델은 실행해 봐야 드러났다.
 * 이 묶음이 검증하는 것은 **고를 수 있는 것만 보이는가**와 **고를 수 없으면 막히는가**다.
 */
test.describe('모델 선택 (§4.2 #14)', () => {
  test('#M1 공급자를 고르면 그 공급자가 지원하는 모델이 목록에 뜬다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')

    const select = page.getByTestId('model-select')
    // 첫 항목이 기본값이다 — 아무것도 안 골라도 실행할 수 있다.
    // id 가 아니라 사람이 읽는 이름을 보여준다
    await expect(select).toContainText('Claude Opus 5')

    const options = await openOptions(page, 'model-select')
    await expect(options).toHaveCount(2)
    // 항목에 단가 한 줄이 따라붙으므로 이름은 부분 일치로 본다 (사이클 #8 #M9)
    await expect(options.first()).toContainText('Claude Opus 5')
  })

  /**
   * 사이클 #8 — 단가를 관리자 표까지 가야 알 수 있었다.
   *
   * 고르는 순간에 근거가 없으면 사람은 그냥 첫 항목을 고른다. `<option>` 이 텍스트만
   * 담을 수 있어 붙이지 못하던 것을, Select 프리미티브로 바뀌면서 붙인다.
   */
  test('#M9 모델 목록이 지금 적용되는 단가를 함께 보여준다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')

    const options = await openOptions(page, 'model-select')
    await expect(options.filter({ hasText: 'Claude Opus 5' })).toContainText(
      '100만 토큰당 입력 $5.000 · 출력 $25.000',
    )
    // 미등록은 `$0` 과 구분되는 낱말이다 (C-7 · FR-20)
    await expect(options.filter({ hasText: 'Claude Sonnet 5' })).toContainText('단가 미등록')
  })

  test('#M2 고른 모델이 접수 요청에 실린다', async ({ page }) => {
    await installFakeApi(page)

    const bodies: string[] = []
    page.on('request', (request) => {
      if (request.url().endsWith('/api/jobs') && request.method() === 'POST') {
        bodies.push(request.postData() ?? '')
      }
    })

    await page.goto('/background')
    await chooseOption(page, 'model-select', 'Claude Sonnet 5')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()

    await expect(page.getByTestId('scene-panel')).toBeVisible()
    expect(bodies.join('\n')).toContain('claude-sonnet-5')
  })

  test('#M3 쓸 모델이 없으면 실행이 막힌다 — 자유 입력 폴백은 없다', async ({ page }) => {
    await installFakeApi(page, { models: [] })
    await page.goto('/background')
    await selectImage(page)

    await expect(page.getByTestId('model-unavailable')).toBeVisible()
    // 드롭다운도, 대신 쓸 입력창도 없다. 목록이 유일한 경로다
    await expect(page.getByTestId('model-select')).toHaveCount(0)
    await expect(page.getByTestId('start-analysis')).toBeDisabled()
  })

  test('#M4 목록 조회가 실패하면 이유를 보여주고 관리자로 안내한다', async ({ page }) => {
    await installFakeApi(page, {
      modelsError: {
        status: 502,
        code: 'PROVIDER_CALL_FAILED',
        message: '모델 목록을 가져올 수 없습니다: HTTP 401',
      },
    })
    await page.goto('/background')
    await selectImage(page)

    // 서버가 준 문구를 그대로 보여준다 — "HTTP 401" 이 키 문제를 가리킨다
    await expect(page.getByTestId('model-unavailable')).toContainText('HTTP 401')
    await expect(page.getByTestId('start-analysis')).toBeDisabled()

    await page.getByTestId('model-admin-link').click()
    await expect(page.getByTestId('provider-table')).toBeVisible()
  })

  test('#M4b 모델 로딩도 공급자와 같은 폼 필드 구조를 유지한다', async ({ page }) => {
    await installFakeApi(page)
    await page.route('**/api/providers/*/models', async (route) => {
      // 응답 대기 중의 실제 레이아웃을 측정할 수 있는 고정 관찰 구간
      await new Promise((resolve) => setTimeout(resolve, 1_000))
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: [{ id: 'model-1', displayName: '모델 1' }], error: null }),
      })
    })
    await page.goto('/background')

    const provider = page.getByTestId('provider-select')
    const textSettings = provider.locator('..').locator('..')
    const loading = textSettings.getByTestId('model-loading')
    await expect(loading).toBeVisible()
    await expect(loading).toBeDisabled()
    await expect(page.getByLabel('모델', { exact: true })).toHaveAttribute(
      'data-testid',
      'model-loading',
    )

    const providerBox = await provider.boundingBox()
    const loadingBox = await loading.boundingBox()
    expect(Math.abs(providerBox!.y - loadingBox!.y)).toBeLessThanOrEqual(1)
    expect(Math.abs(providerBox!.height - loadingBox!.height)).toBeLessThanOrEqual(1)
  })

  test('#M5 관리자 등록 폼에 모델 입력이 없다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin')
    await page.getByTestId('provider-add').click()

    await expect(page.getByTestId('provider-model-input')).toHaveCount(0)

    // 이름 + 키만으로 등록이 완료된다
    await page.getByTestId('provider-name-input').fill('GPT 실험')
    await chooseOption(page, 'provider-kind-select', 'OpenAI')
    await page.getByTestId('provider-key-input').fill('sk-test-abcd')
    await page.getByTestId('provider-submit').click()

    await expect(page.getByTestId('provider-row')).toHaveCount(2)
  })

  test('#M6 연결 확인이 모델 개수를 근거로 보여준다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/admin')
    await page.getByTestId('provider-test').click()

    // 확인 방법이 추출 호출에서 목록 조회로 바뀌었다 — 근거가 개수다
    const result = page.getByTestId('provider-test-result')
    await expect(result).toContainText('텍스트 모델 2개')
    await expect(result).toContainText('이미지 모델 2개')
  })

  test('#M6b Google 연결 확인은 이미지 모델만 근거로 보여준다', async ({ page }) => {
    await installFakeApi(page, {
      providers: [
        {
          id: 'p-google',
          displayName: 'Gemini 이미지',
          kind: 'google',
          capabilities: ['imageGeneration'],
          apiKeyMasked: '••••••••1234',
          isEnabled: true,
        },
      ],
    })
    await page.goto('/admin')
    await page.getByTestId('provider-test').click()

    const result = page.getByTestId('provider-test-result')
    await expect(result).toContainText('이미지 모델 2개')
    await expect(result).not.toContainText('텍스트 모델')
  })

  test('#M6c 3D 공급자는 모델 수와 남은 크레딧을 함께 보여준다', async ({ page }) => {
    await installFakeApi(page, {
      providers: [
        {
          id: 'p-meshy',
          displayName: 'Meshy 운영',
          kind: 'meshy',
          capabilities: ['meshGeneration'],
          apiKeyMasked: '••••••••7c3d',
          isEnabled: true,
        },
      ],
      meshCreditBalance: 2_400,
    })
    await page.goto('/admin')
    await page.getByTestId('provider-test').click()

    const result = page.getByTestId('provider-test-result')

    // 3D 공급자는 이 자리가 비어 있었다 — 붙었는데 증거가 없었다 (사이클 #12 후속)
    await expect(result).toContainText('3D 모델 1개')

    // 파츠 하나가 크레딧 30 이라, 남은 양을 모르면 작업 도중에 멈춘다
    await expect(result).toContainText('크레딧 2,400')
  })

  test('#M6d 잔액을 못 읽으면 아무 말도 하지 않는다', async ({ page }) => {
    await installFakeApi(page, {
      providers: [
        {
          id: 'p-meshy',
          displayName: 'Meshy 운영',
          kind: 'meshy',
          capabilities: ['meshGeneration'],
          apiKeyMasked: '••••••••7c3d',
          isEnabled: true,
        },
      ],
      meshCreditBalance: null,
    })
    await page.goto('/admin')
    await page.getByTestId('provider-test').click()

    const result = page.getByTestId('provider-test-result')

    // **모르는 것을 0 으로 적으면 사용자가 다 썼다고 읽는다**
    await expect(result).toContainText('3D 모델 1개')
    await expect(result).not.toContainText('크레딧')
  })

  test('#M7 진행 표시가 버튼 자체에 나타난다', async ({ page }) => {
    await installFakeApi(page)
    // 클릭 직후 상태를 볼 수 있도록 응답을 늦춘다
    await page.route('**/api/providers/*/test', async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 900))
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: { ok: true, latencyMs: 12, textModelCount: 2, imageModelCount: 2 },
          error: null,
        }),
      })
    })

    await page.goto('/admin')
    const button = page.getByTestId('provider-test')
    await button.click()

    // 별도 문구가 아니라 버튼이 스스로 상태를 말한다
    await expect(button).toHaveText('확인 중…')
    await expect(button).toBeDisabled()

    await expect(button).toHaveText('연결 확인')
    await expect(page.getByTestId('provider-test-result')).toContainText('정상')
  })

  test('#M8 긴 오류 메시지가 열을 침범하지 않는다', async ({ page }) => {
    // 사용자 지적으로 나왔다: 결과가 버튼과 같은 flex 줄에 있어 길어지면
    // 셀 밖으로 40px 넘쳐 상태 열을 침범했다
    await installFakeApi(page)
    await page.route('**/api/providers/*/test', (route) =>
      route.fulfill({
        status: 502,
        contentType: 'application/json',
        body: JSON.stringify({
          data: null,
          error: {
            code: 'PROVIDER_CALL_FAILED',
            message: '아주 긴 오류 메시지'.repeat(6),
            fields: null,
          },
        }),
      }),
    )

    await page.goto('/admin')
    await page.getByTestId('provider-test').click()
    await expect(page.getByTestId('provider-test-result')).toBeVisible()

    const cell = await page.locator('[data-testid="provider-row"] td:last-child').boundingBox()
    const result = await page.getByTestId('provider-test-result').boundingBox()

    // 결과는 자기 셀 안에 머문다
    expect(result!.x).toBeGreaterThanOrEqual(cell!.x)
  })
})

test.describe('레이아웃 폭 (§5.0)', () => {
  test('#W0 공급자와 모델은 데스크톱 한 행, 모바일 두 행이다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')

    const providerDesktop = await page.getByTestId('provider-select').boundingBox()
    const modelDesktop = await page.getByTestId('model-select').boundingBox()

    expect(Math.abs(providerDesktop!.y - modelDesktop!.y)).toBeLessThanOrEqual(1)
    expect(modelDesktop!.x).toBeGreaterThan(providerDesktop!.x)

    await page.setViewportSize({ width: 390, height: 844 })
    const providerMobile = await page.getByTestId('provider-select').boundingBox()
    const modelMobile = await page.getByTestId('model-select').boundingBox()

    expect(modelMobile!.y).toBeGreaterThan(providerMobile!.y)
  })

  test('#W1 스튜디오 입력이 1280px 바깥 폭을 모두 사용한다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')

    const pageBox = await page.getByTestId('page-inner').boundingBox()
    const contentBox = await page.getByTestId('studio-content').boundingBox()

    expect(pageBox!.width).toBeGreaterThan(960)
    expect(pageBox!.width).toBeLessThanOrEqual(1280)
    expect(contentBox!.width).toBe(pageBox!.width)
    expect(contentBox!.x).toBe(pageBox!.x)
  })

  test('#W2 결과도 1280px 바깥 폭을 모두 사용한다', async ({ page }) => {
    await installFakeApi(page, { seedTerminalJobs: 1 })
    await page.goto('/background/seed-job-0')

    const pageBox = await page.getByTestId('page-inner').boundingBox()
    const contentBox = await page.getByTestId('studio-content').boundingBox()

    expect(pageBox!.width).toBeGreaterThan(960)
    expect(pageBox!.width).toBeLessThanOrEqual(1280)
    expect(contentBox!.width).toBe(pageBox!.width)
    expect(contentBox!.x).toBe(pageBox!.x)
  })

  // 작업 시작·활성 작업·최근 이력을 한 화면에서 비교하므로 홈은 대시보드 폭을 쓴다
  test('#W4 홈은 1280px 작업 현황 폭을 사용한다', async ({ page }) => {
    await installFakeApi(page, { seedTerminalJobs: 1 })

    await page.goto('/')
    const home = await page.getByTestId('page-inner').boundingBox()

    await page.goto('/admin')
    const admin = await page.getByTestId('page-inner').boundingBox()

    expect(home!.width).toBeLessThanOrEqual(1280)
    expect(home!.width).toBeGreaterThan(960)

    // 두 화면 모두 넓은 작업 정보를 다루며 공통 max 폭을 따른다
    expect(Math.abs(admin!.width - home!.width)).toBeLessThanOrEqual(1)
  })

  test('#W3 관리자 표가 1280px 에서 멈춘다', async ({ page }) => {
    await installFakeApi(page)
    await page.setViewportSize({ width: 1920, height: 900 })
    await page.goto('/admin')

    // 1920 에서 사이드바를 빼도 1280 보다 넓다 → 상한이 실제로 걸린다
    const box = await page.getByTestId('page-inner').boundingBox()
    expect(box!.width).toBeLessThanOrEqual(1280)
  })
})

/**
 * 작업이 무엇으로 돌고 있는가 — 표기와 이어받기.
 *
 * 모델 선택은 저장돼 있었지만 응답에 없어서 화면이 알 수 없었다. 두 결함이 함께
 * 있었다: 진행 중 모델을 볼 수 없었고, 다시 시도가 선택을 목록 첫 항목으로 되돌렸다.
 */
test.describe('스튜디오 — 모델 표기와 이어받기', () => {
  test('#M1 진행 화면이 고른 텍스트·이미지 모델을 보여준다', async ({ page }) => {
    await installFakeApi(page, { runningPolls: 50 })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()

    await expect(page.getByTestId('run-progress')).toBeVisible()

    // 공급자 표시명과 모델 id 를 함께 — 이름만으로는 어느 모델인지 알 수 없다
    await expect(page.getByTestId('model-text')).toContainText(DEFAULT_PROVIDER.displayName)
    await expect(page.getByTestId('model-text')).toContainText('claude-opus-5')
    await expect(page.getByTestId('model-image')).toContainText('gpt-image-2')
  })

  test('#M2 실패 화면에도 남는다', async ({ page }) => {
    // 모델은 접수 시점에 고정되므로 상태와 무관하다. 여기서 빠지면 "무엇이 실패했나" 를 잃는다
    await installFakeApi(page, { outcome: 'failed' })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()

    await expect(page.getByTestId('run-failure')).toBeVisible()
    await expect(page.getByTestId('model-summary')).toBeVisible()
  })

  test('#M3 다시 시도가 고른 모델을 이어받는다', async ({ page }) => {
    await installFakeApi(page, { outcome: 'failed' })
    await page.goto('/background')
    await selectImage(page)

    // 목록 첫 항목이 아닌 것을 고른다 — 이어받지 않으면 첫 항목으로 되돌아간다
    await chooseOption(page, 'model-select', 'Claude Sonnet 5')
    await chooseOption(page, 'image-model-select', 'Gemini 3.1 Flash Image')
    await page.getByTestId('start-analysis').click()

    await expect(page.getByTestId('run-failure')).toBeVisible()
    await page.getByTestId('back-to-input').click()

    await expect(page.getByTestId('model-select')).toContainText('Claude Sonnet 5')
    await expect(page.getByTestId('image-model-select')).toContainText('Gemini 3.1 Flash Image')
  })
})
