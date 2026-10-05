import { describe, expect, it } from 'vitest'
import { failedTask, jobProgress, taskDurationSeconds } from '@/domain/job/types'
import type { JobTask } from '@/domain/job/types'

/**
 * 진행률 — buffer 모드의 두 값.
 *
 * 사용자가 "진행 상태를 알 수 없다" 고 한 것에 대한 답이므로, **무엇이 확정이고
 * 무엇이 진행 중인지** 가르는 규칙이 핵심이다.
 */
function task(overrides: Partial<JobTask> & { ordinal: number }): JobTask {
  return {
    id: `t${overrides.ordinal}`,
    // 사이클 #7: 넷째 칸부터는 전부 생성이다 — 팬아웃이라 개수가 고정이 아니다
    kind: (['analyze', 'extract', 'decompose'] as const)[overrides.ordinal] ?? 'generate',
    status: 'pending',
    failureReason: null,
    attemptCount: 1,
    startedAt: null,
    completedAt: null,
    partId: null,
    progress: null,
    viewDirection: null,
    ...overrides,
  }
}

describe('jobProgress', () => {
  it('아무것도 안 끝났으면 둘 다 0이다', () => {
    const p = jobProgress([task({ ordinal: 0 }), task({ ordinal: 1 }), task({ ordinal: 2 })])

    expect(p.value).toBe(0)
    expect(p.buffer).toBe(0)
    expect(p.running).toBeNull()
  })

  it('진행 중인 단계는 buffer 에만 들어간다', () => {
    // 이것이 buffer 를 쓰는 이유다 — 확정과 진행 중을 가르지 않으면
    // 3분째 33% 에 멈춘 것처럼 보인다
    const p = jobProgress([
      task({ ordinal: 0, status: 'succeeded' }),
      task({ ordinal: 1, status: 'running' }),
      task({ ordinal: 2 }),
    ])

    expect(p.value).toBeCloseTo(1 / 3)
    expect(p.buffer).toBeCloseTo(2 / 3)
    expect(p.running?.kind).toBe('extract')
  })

  it('대기 중인 단계는 buffer 에 넣지 않는다', () => {
    // 아무 일도 안 하는데 진행 중으로 보이면 안 된다
    const p = jobProgress([
      task({ ordinal: 0, status: 'succeeded' }),
      task({ ordinal: 1 }),
      task({ ordinal: 2 }),
    ])

    expect(p.value).toBeCloseTo(1 / 3)
    expect(p.buffer).toBeCloseTo(1 / 3)
  })

  it('전부 끝나면 둘 다 1이다', () => {
    const p = jobProgress([
      task({ ordinal: 0, status: 'succeeded' }),
      task({ ordinal: 1, status: 'succeeded' }),
      task({ ordinal: 2, status: 'succeeded' }),
    ])

    expect(p.value).toBe(1)
    expect(p.buffer).toBe(1)
    expect(p.completed).toBe(3)
  })

  it('실패한 단계는 확정으로 세지 않는다', () => {
    const tasks = [
      task({ ordinal: 0, status: 'succeeded' }),
      task({ ordinal: 1, status: 'failed', failureReason: 'PART_DUPLICATE' }),
      task({ ordinal: 2 }),
    ]
    const p = jobProgress(tasks)

    expect(p.value).toBeCloseTo(1 / 3)
    expect(p.buffer).toBeCloseTo(1 / 3)
    expect(failedTask(tasks)?.failureReason).toBe('PART_DUPLICATE')
  })

  it('공정이 없으면 0으로 나누지 않는다', () => {
    expect(jobProgress([]).value).toBe(0)
  })

  it('18개 공정에서도 현재 위치와 전체 수를 안정적으로 계산한다', () => {
    // 홈과 상세 화면은 공정 수를 가정하지 않는다. 현재 공정의 ordinal 이 아니라
    // 정렬된 목록상 위치를 써야 중간 번호가 비어도 1부터 연속된 진행으로 읽힌다.
    const tasks = Array.from({ length: 18 }, (_, index) =>
      task({
        ordinal: index * 10,
        status: index < 6 ? 'succeeded' : index === 6 ? 'running' : 'pending',
      }),
    )

    const progress = jobProgress(tasks)

    expect(progress.current).toBe(7)
    expect(progress.completed).toBe(6)
    expect(progress.total).toBe(18)
    expect(progress.value).toBeCloseTo(6 / 18)
    expect(progress.buffer).toBeCloseTo(7 / 18)
  })
})

describe('taskDurationSeconds', () => {
  it('시작·완료가 있으면 초를 낸다', () => {
    const t = task({
      ordinal: 0,
      startedAt: '2026-08-01T00:00:00Z',
      completedAt: '2026-08-01T00:00:12Z',
    })

    expect(taskDurationSeconds(t)).toBe(12)
  })

  it('아직 안 끝났으면 null 이다', () => {
    expect(taskDurationSeconds(task({ ordinal: 0, startedAt: '2026-08-01T00:00:00Z' }))).toBeNull()
  })
})
