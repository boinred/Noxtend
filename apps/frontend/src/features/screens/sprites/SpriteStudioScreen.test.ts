import { describe, expect, it } from 'vitest'
import { taskGroupSummary } from './SpriteStudioScreen'
import type { TaskStatusGroup } from '@/domain/job/types'

const group = (overrides: Partial<TaskStatusGroup>): TaskStatusGroup => ({
  kind: 'generateSprite',
  total: 0,
  running: 0,
  pending: 0,
  succeeded: 0,
  failed: 0,
  canceled: 0,
  retrying: 0,
  ...overrides,
})

describe('taskGroupSummary', () => {
  it('0인 상태는 숨기고 상태 순서대로 한 줄로 요약한다', () => {
    const text = taskGroupSummary(group({ total: 6, running: 1, pending: 2, succeeded: 3 }))
    expect(text).toMatch(/ · 전체 6 · 실행 중 1 · 대기 2 · 성공 3$/)
    expect(text).not.toContain('실패')
  })

  it('재시도 중은 상태 요약 뒤에 붙는다', () => {
    const text = taskGroupSummary(group({ total: 3, pending: 1, failed: 1, retrying: 1 }))
    expect(text).toMatch(/ · 전체 3 · 대기 1 · 실패 1 · 재시도 중 1$/)
  })

  it('재시도가 없으면 재시도 중을 표시하지 않는다', () => {
    expect(taskGroupSummary(group({ total: 1, running: 1 }))).not.toContain('재시도 중')
  })
})
