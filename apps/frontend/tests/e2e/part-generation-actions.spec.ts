/**
 * Design Ref: §8.3 L2 — 결과 화면 체크리스트(§5.4) · 실패 방향 재시도 · 사용량 접기/펼치기.
 *
 * **이 파일이 검증하는 것은 "로직이 맞는가" 가 아니다** — 그것은 L1-B 가 이미 한다.
 * 여기서 보는 것은 **사용자가 그 로직의 결과를 실제로 보는가** 다. 사이클 #7 Check 에서
 * SC-05·SC-11·SC-13·SC-14 가 "부분" 에 머문 이유가 정확히 이 축의 공백이었다.
 */
import { expect, test } from '@playwright/test'
import { installFakeApi, openOptions, selectImage } from './fakeApi'

/** 결과 화면까지 간다 — 접수하면 Fake 가 즉시 완료 상태를 준다. */
async function gotoResult(page: import('@playwright/test').Page) {
  await page.goto('/background')
  await selectImage(page)
  await page.getByTestId('start-analysis').click()
  await expect(page.getByTestId('run-result')).toBeVisible()
}

test.describe('결과 — 파츠 제작 파이프라인 리스트 (§5.4)', () => {
  test('#1 파츠마다 한 행에 네 방향 이미지와 3D 에셋 목적지가 보인다', async ({ page }) => {
    await installFakeApi(page, { generation: 'all' })
    await gotoResult(page)

    await expect(page.getByTestId('part-pipeline-list')).toBeVisible()
    await expect(page.getByTestId('part-pipeline-header').locator('span')).toHaveText([
      '파츠',
      '4방향 이미지',
      '3D 에셋',
    ])
    await expect(page.getByTestId('part-row')).toHaveCount(4)
    await expect(page.getByTestId('part-image')).toHaveCount(16)
    await expect(page.getByTestId('part-view-label')).toHaveCount(16)
    await expect(page.getByTestId('part-view-label').first()).toHaveText('정면')
    await expect(page.getByTestId('part-3d-state')).toHaveCount(4)
  })

  test('#1b 누른 방향에서 4방향 Carousel을 열고 방향키로 순환한다', async ({ page }) => {
    await installFakeApi(page, { generation: 'all' })
    await gotoResult(page)

    const firstRow = page.getByTestId('part-row').first()
    const images = firstRow.getByTestId('part-image')
    const backSource = await images.nth(2).getAttribute('src')
    const leftSource = await images.nth(3).getAttribute('src')
    await images.nth(2).click()

    const preview = page.getByRole('dialog', { name: '등대 이미지 크게 보기' })
    await expect(preview).toBeVisible()
    await expect(preview.getByTestId('part-carousel-position')).toHaveText('후면 · 3/4')
    await expect(preview.getByTestId('part-carousel-thumb')).toHaveCount(4)
    await expect(preview.getByTestId('part-image-preview')).toHaveAttribute('src', backSource!)

    await page.keyboard.press('ArrowRight')
    await expect(preview.getByTestId('part-carousel-position')).toHaveText('좌측 · 4/4')
    await expect(preview.getByTestId('part-image-preview')).toHaveAttribute('src', leftSource!)

    // 네 방향은 수평 회전 순서라 좌측 다음이 다시 정면이다
    await page.keyboard.press('ArrowRight')
    await expect(preview.getByTestId('part-carousel-position')).toHaveText('정면 · 1/4')

    await page.keyboard.press('Escape')
    await expect(preview).not.toBeVisible()
  })

  test('#1c 파츠 정보를 누르면 명세 팝업을 열고 Esc로 닫는다', async ({ page }) => {
    await installFakeApi(page, { generation: 'all' })
    await gotoResult(page)

    const firstRow = page.getByTestId('part-row').first()
    const toggle = firstRow.getByRole('button', { name: '등대 파츠 명세 보기' })
    await expect(toggle).toHaveAttribute('aria-haspopup', 'dialog')

    await toggle.click()
    const dialog = page.getByRole('dialog', { name: '등대 파츠 명세' })
    const specification = dialog.getByTestId('part-specification')

    await expect(dialog).toBeVisible()
    await expect(specification).toBeVisible()
    await expect(specification).toContainText('등대 — 낡은 표면, 해수에 색이 바램')
    // 배치 목록에도 번호 1 이 있으므로 정의 목록의 값으로 좁힌다 (사이클 #9)
    await expect(specification.getByText('깊이', { exact: true })).toBeVisible()
    await expect(specification.getByRole('definition').filter({ hasText: /^1$/ })).toBeVisible()
    // 첫 파츠는 배치가 셋이다 — 한 문단이 아니라 줄로 나뉘어야 읽힌다 (사이클 #9)
    await expect(specification.getByTestId('placement-row')).toHaveCount(3)
    await expect(specification.getByTestId('placement-row').first()).toContainText(
      'x 0.05 · y 0.10 · w 0.12 · h 0.20',
    )
    await expect(specification.getByTestId('placement-row').last()).toContainText(
      'x 0.55 · y 0.11 · w 0.11 · h 0.19',
    )

    await page.keyboard.press('Escape')
    await expect(dialog).not.toBeVisible()
    await expect(toggle).toBeFocused()
  })

  test('#2 행이 파츠 Ordinal 순서다 (C-2)', async ({ page }) => {
    await installFakeApi(page, { generation: 'all' })
    await gotoResult(page)

    // 사용자가 본 파츠 목록과 진행 표시가 어긋나면 어느 행이 어느 파츠인지 알 수 없다
    await expect(page.getByTestId('part-row')).toContainText([
      '등대',
      '목조 부두',
      '어선',
      '가로등',
    ])
  })

  test('#3 요약이 파츠 · 생성 · 실패를 센다', async ({ page }) => {
    await installFakeApi(page, { generation: 'partial' })
    await gotoResult(page)

    await expect(page.getByTestId('generation-tally')).toContainText('파츠 4')
    await expect(page.getByTestId('generation-tally')).toContainText('이미지 15/16')
    await expect(page.getByTestId('generation-tally')).toContainText('실패 1')
  })

  test('#4 부분 성공 배지가 성공과 다른 낱말이다 (C-3 · 원칙 ③)', async ({ page }) => {
    await installFakeApi(page, { generation: 'partial' })
    await gotoResult(page)

    const badge = page.getByTestId('job-status-badge')
    await expect(badge).toHaveText('부분 성공')
    // 색을 가르는 것은 data-status 다 — 뭉개면 사용자가 4장을 다 받았다고 믿는다
    await expect(badge).toHaveAttribute('data-status', 'partiallySucceeded')
  })

  test('#5 전부 성공하면 성공 배지다', async ({ page }) => {
    await installFakeApi(page, { generation: 'all' })
    await gotoResult(page)

    await expect(page.getByTestId('job-status-badge')).toHaveText('성공')
  })

  test('#6 실패한 방향이 사유와 [다시] 버튼을 보여준다', async ({ page }) => {
    await installFakeApi(page, { generation: 'partial' })
    await gotoResult(page)

    const failed = page.getByTestId('part-failed')
    await expect(failed).toHaveCount(1)
    await expect(failed).toContainText('GENERATION_EMPTY_RESPONSE')
    await expect(page.getByTestId('part-retry')).toBeVisible()
  })

  test('#7 실패해도 성공분이 남는다 (Plan D-3)', async ({ page }) => {
    await installFakeApi(page, { generation: 'partial' })
    await gotoResult(page)

    // 이것이 부분 성공을 도입한 이유다 — 9장 중 7장을 버리지 않는다
    await expect(page.getByTestId('part-image')).toHaveCount(15)
  })
})

test.describe('결과 — 사용량 (FR-18·FR-19·FR-20)', () => {
  test('#8 사용량이 기본으로 접혀 있다 (D-14)', async ({ page }) => {
    await installFakeApi(page)
    await gotoResult(page)

    // 일반 사용자에게 공정마다 달러 금액을 상시 노출하는 것은 파이프라인 내부 노출이다
    await expect(page.getByTestId('usage-panel')).toBeVisible()
    await expect(page.getByTestId('usage-table')).not.toBeVisible()
  })

  test('#9 펼치면 공정별 표가 나온다', async ({ page }) => {
    await installFakeApi(page)
    await gotoResult(page)

    await page.getByTestId('usage-summary').click()

    await expect(page.getByTestId('usage-table')).toBeVisible()
    await expect(page.getByTestId('usage-row')).toHaveCount(4)
  })

  test('#10 합계가 미등록과 사용량 없음을 갈라 센다 (SC-14 · V-15)', async ({ page }) => {
    await installFakeApi(page)
    await gotoResult(page)

    // 앞은 "단가를 넣으면 합계가 올라간다", 뒤는 "고칠 것이 없다" 로 읽혀야 한다.
    // 뭉쳐 세면 고칠 수 없는 건수가 경고에 남아 정작 등록해야 할 때 신호가 안 보인다
    await expect(page.getByTestId('usage-summary')).toContainText('미등록 1건 · 사용량 없음 1건')
  })

  test('#11 이미지 공정은 토큰이 아니라 장 수다 (D-16 · C-6)', async ({ page }) => {
    await installFakeApi(page)
    await gotoResult(page)
    await page.getByTestId('usage-summary').click()

    // 라벨을 "토큰" 으로 박으면 파츠 공정 칸이 전부 빈칸이 된다
    await expect(page.getByTestId('usage-table')).toContainText('1장')
    await expect(page.getByTestId('usage-table')).toContainText('1,240 → 380 토큰')
  })
})

test.describe('입력 — 이미지 공급자·모델 (§5.4)', () => {
  test('#12 이미지 공급자와 모델을 따로 고른다 (D-4)', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')

    await expect(page.getByTestId('image-provider-select')).toBeVisible()
    await expect(page.getByTestId('image-model-select')).toBeVisible()
  })

  test('#13 이미지 모델 목록이 텍스트 목록과 다르다', async ({ page }) => {
    await installFakeApi(page)
    await page.goto('/background')

    // 한 목록에 섞으면 텍스트 단계에 이미지 모델을 고를 수 있다 (§4.2 #7)
    await expect(page.getByTestId('model-select')).toContainText('Claude Opus 5')
    await expect(page.getByTestId('image-model-select')).toContainText('GPT Image 2')

    // 닫힌 칸은 고른 것 하나만 보여준다 — 섞임 여부는 목록을 열어야 드러난다.
    // 항목마다 단가 한 줄이 따라붙으므로 이름은 부분 일치로 본다 (사이클 #8 #M9)
    const imageOptions = await openOptions(page, 'image-model-select')
    await expect(imageOptions).toHaveCount(2)
    await expect(imageOptions.nth(0)).toContainText('GPT Image 2')
    await expect(imageOptions.nth(1)).toContainText('Gemini 3.1 Flash Image')
    await expect(imageOptions.filter({ hasText: 'Claude' })).toHaveCount(0)
  })

  test('#14 이미지 모델이 없으면 시작이 막힌다', async ({ page }) => {
    await installFakeApi(page, { imageModels: [] })
    await page.goto('/background')
    await selectImage(page)

    // 서버도 거절하지만, 워커까지 갔다 오는 오류보다 여기서 막히는 편이 낫다
    await expect(page.getByTestId('start-analysis')).toBeDisabled()
  })

  test('#14b 텍스트와 이미지 선택란이 capability로 공급자를 가른다', async ({ page }) => {
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
          displayName: 'OpenAI 운영',
          kind: 'openai',
          capabilities: ['textAnalysis', 'imageGeneration'],
          apiKeyMasked: '••••••••abcd',
          isEnabled: true,
        },
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
    await page.goto('/background')

    await expect(await openOptions(page, 'provider-select')).toHaveText([
      'Claude 운영',
      'OpenAI 운영',
    ])
    await page.keyboard.press('Escape')

    await expect(await openOptions(page, 'image-provider-select')).toHaveText([
      'OpenAI 운영',
      'Gemini 이미지',
    ])
  })
})

test.describe('진행 — 생성 단계 묶기 (§5.3)', () => {
  test('#15 실패 화면의 단계 목록이 생성을 한 칸으로 묶는다', async ({ page }) => {
    // StageProgress 는 진행·실패 화면에 있다. 실패는 앞 단계에서 나므로 생성 공정이
    // 아직 없고, 여기서 보는 것은 **묶기가 기존 3단계를 깨지 않는가** 다
    await installFakeApi(page, { outcome: 'failed' })
    await page.goto('/background')
    await selectImage(page)
    await page.getByTestId('start-analysis').click()

    await expect(page.getByTestId('run-failure')).toBeVisible()
    await expect(page.getByTestId('stage-item')).toHaveCount(3)
  })
})
