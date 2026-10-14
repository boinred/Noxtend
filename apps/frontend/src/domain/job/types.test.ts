/**
 * Design Ref: §8.3 L1-F #3·#4 — 종료 판정과 폴링 백오프.
 */
import { describe, expect, it } from 'vitest'
import {
  isActive,
  isTerminal,
  jobPollDelayMs,
  modelBadges,
  nextPollDelayMs,
  taskStatusGroups,
} from './types'
import type { JobModels, JobStatus, JobTask, TaskKind } from './types'
import type { SpritePhase } from '../sprites/types'

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

describe('jobPollDelayMs', () => {
  const job = (phase: SpritePhase | null, ...tasks: [TaskKind, JobStatus][]) => ({
    sprite: phase ? { phase } : null,
    tasks: tasks.map(([kind, status]) => ({ kind, status })),
  })

  it.each<TaskKind>(['enhanceSpriteSource', 'generateSpriteSource'])(
    '원본 준비 공정 %s 이 진행 중이면 시도 횟수와 무관하게 2초 고정이다',
    (kind) => {
      for (const status of ['pending', 'running'] as const)
        for (const attempt of [0, 1, 2, 50])
          expect(jobPollDelayMs(job('sourceDraft', [kind, status]), attempt)).toBe(2_000)
    },
  )

  it('원본 공정이 끝났거나 다른 공정이면 기존 백오프를 따른다', () => {
    expect(jobPollDelayMs(job('sourceDraft', ['enhanceSpriteSource', 'succeeded']), 5)).toBe(15_000)
    expect(jobPollDelayMs(job('sourceDraft', ['analyzeSprites', 'running']), 5)).toBe(15_000)
    expect(jobPollDelayMs(job('sourceDraft'), 1)).toBe(5_000)
  })

  it('sourceDraft 가 아닌 단계나 3D 작업은 원본 공정이 남아 있어도 기존 백오프를 따른다', () => {
    expect(jobPollDelayMs(job('analyzing', ['generateSpriteSource', 'running']), 4)).toBe(15_000)
    expect(jobPollDelayMs(job(null, ['generateSpriteSource', 'running']), 4)).toBe(15_000)
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

describe('taskStatusGroups', () => {
  const task = (kind: TaskKind, status: JobStatus, attemptCount = 1) => ({
    kind,
    status,
    attemptCount,
  })

  it('빈 입력은 빈 목록이다', () => {
    expect(taskStatusGroups([])).toEqual([])
  })

  it('종류별 상태 수를 첫 등장 순서로 센다', () => {
    const tasks: Pick<JobTask, 'kind' | 'status' | 'attemptCount'>[] = [
      task('generateSprite', 'running'),
      task('analyzeSprites', 'succeeded'),
      task('generateSprite', 'pending', 0),
      task('generateSprite', 'succeeded'),
      task('generateSprite', 'failed'),
      task('generateSprite', 'canceled'),
    ]
    expect(taskStatusGroups(tasks)).toEqual([
      {
        kind: 'generateSprite',
        total: 5,
        running: 1,
        pending: 1,
        succeeded: 1,
        failed: 1,
        canceled: 1,
        retrying: 0,
      },
      {
        kind: 'analyzeSprites',
        total: 1,
        running: 0,
        pending: 0,
        succeeded: 1,
        failed: 0,
        canceled: 0,
        retrying: 0,
      },
    ])
  })

  it('재시도 중은 시도 2회 이상 실행과 시도 1회 이상 대기만 센다', () => {
    const [group] = taskStatusGroups([
      task('generateSprite', 'running', 2),
      task('generateSprite', 'pending', 3),
      task('generateSprite', 'running', 1),
      task('generateSprite', 'failed', 2),
    ])
    expect(group).toMatchObject({ total: 4, running: 2, pending: 1, failed: 1, retrying: 2 })
  })

  it.each([
    ['pending', 1, 1],
    ['pending', 0, 0],
    ['running', 1, 0],
    ['running', 2, 1],
  ] as const)('%s 시도 %i회의 재시도 중 수는 %i이다', (status, attemptCount, retrying) => {
    const [group] = taskStatusGroups([task('generateSprite', status, attemptCount)])
    expect(group?.retrying).toBe(retrying)
  })

  it('집계 상태가 아닌 공정은 전체에만 센다', () => {
    const [group] = taskStatusGroups([
      task('generateSprite', 'pendingReview'),
      task('generateSprite', 'partiallySucceeded'),
    ])
    expect(group).toMatchObject({ total: 2, running: 0, pending: 0, succeeded: 0, failed: 0 })
  })
})
