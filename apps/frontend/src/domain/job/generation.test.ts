import { describe, expect, it } from 'vitest'
import { generationTally, groupGenerationStages, partCards } from './types'
import type { AssetPart, Job, JobTask } from './types'

/**
 * Design Ref: §5.2 · §5.3 — 팬아웃 결과를 화면 숫자로 옮기는 규칙.
 *
 * 결과 화면과 진행 표시가 같은 계산을 각자 하면 사용자가 본 숫자가 화면마다 달라진다.
 */
function part(ordinal: number, overrides: Partial<AssetPart> = {}): AssetPart {
  return {
    id: `p${ordinal}`,
    name: `파츠${ordinal}`,
    ordinal,
    description: null,
    category: null,
    placements: [],
    depthOrder: null,
    occludedBy: [],
    generatedImageId: null,
    generatedImages: [],
    generatedMesh: null,
    ...overrides,
  }
}

function task(overrides: Partial<JobTask> & { ordinal: number }): JobTask {
  return {
    id: `t${overrides.ordinal}`,
    kind: 'generate',
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

function job(parts: AssetPart[], tasks: JobTask[]): Job {
  return {
    id: 'j1',
    category: 'background',
    sourceImageId: 's1',
    status: 'running',
    scene: null,
    // 이 파일의 관심사는 파츠·방향 집계다 — 모델 선택은 비워 둔다
    models: { text: null, image: null, mesh: null },
    tasks,
    parts,
    failureReason: null,
    createdAt: '2026-08-07T00:00:00Z',
    completedAt: null,
    gender: null,
    partHints: [],
  }
}

describe('파츠 카드', () => {
  it('파츠 순서를 따르고 공정을 이어 붙인다', () => {
    const cards = partCards(
      job([part(1), part(0)], [task({ ordinal: 3, partId: 'p0', status: 'succeeded' })]),
    )

    // C-2 — 사용자가 본 파츠 순서와 어긋나지 않아야 한다
    expect(cards.map((c) => c.part.id)).toEqual(['p0', 'p1'])
    expect(cards[0]!.status).toBe('succeeded')
  })

  it('생성 공정이 아직 없는 파츠는 대기와 구분된다', () => {
    const cards = partCards(job([part(0)], []))

    // '곧 그려진다' 와 '그릴 계획이 없다' 는 사용자가 할 일이 다르다
    expect(cards[0]!.status).toBe('unplanned')
  })

  it('재생성하면 나중 공정을 가리킨다', () => {
    const cards = partCards(
      job(
        [part(0)],
        [
          task({ ordinal: 3, partId: 'p0', status: 'failed', failureReason: '옛 실패' }),
          task({ ordinal: 4, partId: 'p0', status: 'succeeded' }),
        ],
      ),
    )

    // 파츠는 하나이고 공정은 시도마다 늘어난다 — 옛 실패가 화면에 남으면 안 된다
    expect(cards[0]!.status).toBe('succeeded')
    expect(cards[0]!.failureReason).toBeNull()
  })

  it('앞 단계 공정은 파츠에 붙지 않는다', () => {
    const cards = partCards(
      job([part(0)], [task({ ordinal: 0, kind: 'analyze', status: 'succeeded' })]),
    )

    expect(cards[0]!.task).toBeNull()
  })

  it('파츠 하나에 네 방향 이미지와 공정을 고정 순서로 붙인다', () => {
    const cards = partCards(
      job(
        [
          part(0, {
            generatedImages: [
              { id: 'right-image', viewDirection: 'right' },
              { id: 'front-image', viewDirection: 'front' },
            ],
          }),
        ],
        [
          task({ ordinal: 3, partId: 'p0', viewDirection: 'front', status: 'succeeded' }),
          task({ ordinal: 4, partId: 'p0', viewDirection: 'right', status: 'succeeded' }),
          task({ ordinal: 5, partId: 'p0', viewDirection: 'back', status: 'pending' }),
          task({ ordinal: 6, partId: 'p0', viewDirection: 'left', status: 'failed' }),
        ],
      ),
    )

    expect(cards[0]!.views.map((view) => view.viewDirection)).toEqual([
      'front',
      'right',
      'back',
      'left',
    ])
    expect(cards[0]!.views.map((view) => view.imageId)).toEqual([
      'front-image',
      'right-image',
      null,
      null,
    ])
  })
})

describe('결과 머리말 숫자', () => {
  it('파츠 · 생성 · 실패를 센다', () => {
    const tally = generationTally(
      job(
        [part(0, { generatedImageId: 'i0' }), part(1, { generatedImageId: 'i1' }), part(2)],
        [
          task({ ordinal: 3, partId: 'p0', status: 'succeeded' }),
          task({ ordinal: 4, partId: 'p1', status: 'succeeded' }),
          task({ ordinal: 5, partId: 'p2', status: 'failed' }),
        ],
      ),
    )

    expect(tally).toEqual({ parts: 3, generated: 2, total: 3, failed: 1 })
  })

  it('이미지가 실제로 붙은 것만 생성으로 센다', () => {
    // 공정 성공과 이미지 존재가 어긋나면 리스트와 숫자가 달라진다
    const tally = generationTally(
      job([part(0)], [task({ ordinal: 3, partId: 'p0', status: 'succeeded' })]),
    )

    expect(tally.generated).toBe(0)
    expect(tally.total).toBe(1)
  })
})

describe('진행 표시 — 생성 단계 묶기', () => {
  const textStages: JobTask[] = [
    task({ ordinal: 0, kind: 'analyze', status: 'succeeded' }),
    task({ ordinal: 1, kind: 'extract', status: 'succeeded' }),
    task({ ordinal: 2, kind: 'decompose', status: 'succeeded' }),
  ]

  it('파츠가 20개여도 칸은 넷이다', () => {
    const generation = Array.from({ length: 20 }, (_, i) =>
      task({ ordinal: 3 + i, partId: `p${i}`, status: 'succeeded' }),
    )

    const rows = groupGenerationStages([...textStages, ...generation])

    // 칸 20개를 그리면 읽을 수 없다 (§5.3)
    expect(rows).toHaveLength(4)
    expect(rows[3]!.note).toBe('20/20')
    expect(rows[3]!.kind).toBe('generate')
  })

  it('진척을 분자/분모로 보여준다', () => {
    const rows = groupGenerationStages([
      ...textStages,
      task({ ordinal: 3, partId: 'p0', status: 'succeeded' }),
      task({ ordinal: 4, partId: 'p1', status: 'running' }),
      task({ ordinal: 5, partId: 'p2', status: 'pending' }),
    ])

    expect(rows[3]!.note).toBe('1/3')
    expect(rows[3]!.status).toBe('running')
  })

  it('실패가 섞이면 접힌 칸이 실패로 보인다', () => {
    const rows = groupGenerationStages([
      ...textStages,
      task({ ordinal: 3, partId: 'p0', status: 'succeeded' }),
      task({ ordinal: 4, partId: 'p1', status: 'failed' }),
    ])

    // 성공으로 보이면 사용자가 리스트를 열어 볼 이유가 없어진다
    expect(rows[3]!.status).toBe('failed')
  })

  it('생성 공정이 없으면 칸도 없다', () => {
    expect(groupGenerationStages(textStages)).toHaveLength(3)
  })
})

/**
 * 3D 도 한 줄로 접힌다 (§11.4).
 *
 * **개수가 고정이 아닌 단계가 둘이 됐다.** 둘 다 파츠 수에 따라 늘어나므로 그대로 그리면
 * 앞 세 단계가 화면 밖으로 밀려난다.
 */
describe('groupGenerationStages — 3D', () => {
  it('3D 공정을 한 줄로 접고 진행을 숫자로 낸다', () => {
    const rows = groupGenerationStages([
      task({ ordinal: 0, kind: 'analyze', status: 'succeeded' }),
      task({ ordinal: 1, kind: 'generate', status: 'succeeded' }),
      task({ ordinal: 2, kind: 'generate', status: 'succeeded' }),
      task({ ordinal: 3, kind: 'reconstruct', status: 'succeeded' }),
      task({ ordinal: 4, kind: 'reconstruct', status: 'running' }),
    ])

    const mesh = rows.find((row) => row.kind === 'reconstruct')

    expect(rows).toHaveLength(3)
    expect(mesh).toMatchObject({ note: '1/2', status: 'running' })
  })

  it('3D 공정이 없으면 그 줄도 없다', () => {
    const rows = groupGenerationStages([
      task({ ordinal: 0, kind: 'analyze', status: 'succeeded' }),
      task({ ordinal: 1, kind: 'generate', status: 'succeeded' }),
    ])

    // 3D 를 고르지 않은 작업에 빈 줄을 그리면 사용자는 멈춘 단계로 읽는다
    expect(rows.some((row) => row.kind === 'reconstruct')).toBe(false)
  })
})
