/**
 * Design Ref: §8.3 L1-F #3·#4 — 종료 판정과 폴링 백오프.
 */
import { describe, expect, it } from 'vitest'
import { isActive, isTerminal, modelBadges, nextPollDelayMs } from './types'
import type { JobModels, JobStatus } from './types'

describe('isTerminal', () => {
  // #3 — 종료 상태 판정
  it.each<JobStatus>(['succeeded', 'failed', 'canceled'])('종료로 판정한다: %s', (status) => {
    expect(isTerminal(status)).toBe(true)
    expect(isActive(status)).toBe(false)
  })

  it.each<JobStatus>(['pending', 'running'])('진행 중으로 판정한다: %s', (status) => {
    expect(isTerminal(status)).toBe(false)
    expect(isActive(status)).toBe(true)
  })
})

describe('nextPollDelayMs', () => {
  // #4 — 2s → 5s → 15s 상한
  it('첫 폴링은 2초 뒤다', () => {
    expect(nextPollDelayMs(0)).toBe(2_000)
  })

  it('두 번째는 5초 뒤다', () => {
    expect(nextPollDelayMs(1)).toBe(5_000)
  })

  it('세 번째부터 15초로 고정된다', () => {
    expect(nextPollDelayMs(2)).toBe(15_000)
    expect(nextPollDelayMs(3)).toBe(15_000)
    expect(nextPollDelayMs(100)).toBe(15_000)
  })

  it('간격이 줄어들지 않는다', () => {
    // 단조 증가가 깨지면 긴 작업에서 요청 수가 예측 불가능해진다
    const delays = [0, 1, 2, 3, 4, 5].map(nextPollDelayMs)
    const sorted = [...delays].sort((a, b) => a - b)
    expect(delays).toEqual(sorted)
  })

  it('10분 작업에서 요청이 50회를 넘지 않는다', () => {
    // 2초 고정이면 300회다 (§3.4). 상한을 두는 이유가 이 숫자다
    let elapsed = 0
    let attempts = 0
    while (elapsed < 10 * 60 * 1000) {
      elapsed += nextPollDelayMs(attempts)
      attempts += 1
    }

    expect(attempts).toBeLessThan(50)
  })
})

/**
 * 진행·결과 화면 상단의 "무엇으로 돌고 있는가" 한 줄.
 *
 * 공급자 표시명은 `domain/job` 이 알 수 없다 (계층 규칙상 import 가 없다) — 화면이
 * 이미 받아 둔 공급자 목록을 이름표로 넘긴다.
 */
describe('modelBadges', () => {
  const models: JobModels = {
    text: { providerConfigId: 'p-text', model: 'gpt-5.6-luna' },
    image: { providerConfigId: 'p-image', model: 'gemini-3-image' },
    mesh: null,
  }
  const names = { 'p-text': 'OpenAI 운영', 'p-image': 'Google 운영' }

  it('텍스트·이미지 순서로 낸다', () => {
    // 순서는 파이프라인 순서다 — 텍스트 세 공정이 돌고 나서 이미지가 돈다
    expect(modelBadges(models, names)).toEqual([
      { role: 'text', label: '텍스트', providerName: 'OpenAI 운영', model: 'gpt-5.6-luna' },
      { role: 'image', label: '이미지', providerName: 'Google 운영', model: 'gemini-3-image' },
    ])
  })

  it('삭제된 공급자는 이름 없이 모델만 남긴다', () => {
    // 이름을 못 찾았다고 배지를 통째로 버리면 "무엇으로 돌았는지" 를 잃는다.
    // 모델 id 가 그 중 가장 정보량이 큰 부분이다
    const badges = modelBadges(models, {})

    expect(badges.map((badge) => badge.providerName)).toEqual([null, null])
    expect(badges.map((badge) => badge.model)).toEqual(['gpt-5.6-luna', 'gemini-3-image'])
  })

  it('모델 칸이 없는 이전 API 응답에도 죽지 않는다', () => {
    // 서버가 이 필드를 내려보내기 전에 배포된 화면이 기존 작업을 열면 값이 통째로 없다.
    // 방향 없는 이전 응답을 정면으로 간주하는 것과 같은 자리다 (partCards)
    expect(modelBadges(undefined, names)).toEqual([])
  })

  it('고르지 않은 쪽은 배지를 만들지 않는다', () => {
    // 이미지 생성 없이 접수된 옛 작업 — 빈 배지가 그려지면 안 된다
    expect(modelBadges({ text: models.text, image: null, mesh: null }, names)).toHaveLength(1)
    expect(modelBadges({ text: null, image: null, mesh: null }, names)).toEqual([])
  })
})
