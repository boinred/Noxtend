import { describe, expect, it } from 'vitest'
import {
  costLabel,
  estimateMeshBatchCost,
  formatUsageTotal,
  formatUsd,
  summarizeJobUsage,
  summarizeUsage,
  usageAmount,
} from './usage'
import type { LlmCall, ModelPrice } from './types'

/**
 * Design Ref: §5.2 · FR-20 · SC-14 — **단가 미등록을 `$0` 으로 뭉개지 않는다.**
 *
 * 백엔드가 `null` 로 구분해 주는 것을 화면이 0 으로 더하면 합계가 실제보다 낮게 읽히고,
 * 사용자는 그 값을 실제 지출로 믿는다.
 */
function call(overrides: Partial<LlmCall> = {}): LlmCall {
  return {
    id: 'c1',
    taskId: 't1',
    kind: 'analyze',
    promptVersionId: 'p1',
    model: 'claude-opus-5',
    requestPayload: '',
    responsePayload: null,
    inputTokens: 1240,
    outputTokens: 380,
    latencyMs: 4200,
    succeeded: true,
    failureReason: null,
    at: '2026-08-07T00:00:00Z',
    estimatedCostUsd: 0.011,
    outputImages: null,
    ...overrides,
  }
}

function price(overrides: Partial<ModelPrice> = {}): ModelPrice {
  return {
    id: 'price-1',
    model: 'P1-20260311',
    inputPerMillion: 0,
    outputPerMillion: 0,
    longContextFrom: null,
    longInputPerMillion: null,
    longOutputPerMillion: null,
    effectiveFrom: '2026-08-01T00:00:00Z',
    note: 'Tripo P Series multiview 30크레딧 × $0.01',
    perImage: 0.3,
    ...overrides,
  }
}

describe('사용량 표기', () => {
  it('텍스트 공정은 입력 → 출력 토큰이다', () => {
    expect(usageAmount(call())).toBe('1,240 → 380 토큰')
  })

  it('이미지 공정은 장 수다 — 토큰 칸이 비어도 빈칸이 되지 않는다', () => {
    // D-16 — 라벨을 "토큰" 으로 박으면 파츠 공정 N칸이 전부 빈칸이 된다
    expect(
      usageAmount(
        call({ kind: 'generate', inputTokens: null, outputTokens: null, outputImages: 1 }),
      ),
    ).toBe('1장')
  })

  it('아무 값도 못 받은 호출은 대시다', () => {
    expect(usageAmount(call({ inputTokens: null, outputTokens: null }))).toBe('—')
  })
})

describe('금액 표기', () => {
  it('단가를 모르면 $0 이 아니라 낱말이다', () => {
    // SC-14 — `$0` 으로 보이면 "공짜로 썼다" 로 읽힌다
    expect(formatUsd(null)).toBe('미등록')
    expect(formatUsd(0)).toBe('$0.000')
  })

  it('소수 셋째 자리까지 — 파츠 하나가 $0.011 이다', () => {
    expect(formatUsd(0.011)).toBe('$0.011')
  })
})

describe('작업 합계', () => {
  it('아는 것만 더하고 모르는 것은 따로 센다', () => {
    const summary = summarizeUsage([
      call({ id: 'a', estimatedCostUsd: 0.011 }),
      call({ id: 'b', estimatedCostUsd: 0.04, outputImages: 1 }),
      call({ id: 'c', estimatedCostUsd: null }),
    ])

    expect(summary.totalUsd).toBeCloseTo(0.051)
    expect(summary.unpricedCount).toBe(1)
  })

  it('미등록이 있으면 합계에 그 사실이 함께 나온다', () => {
    // V-15 — 이것이 없으면 사용자가 낮은 합계를 실제 지출로 믿는다
    const summary = summarizeUsage([
      call({ id: 'a', estimatedCostUsd: 0.42 }),
      call({ id: 'b', estimatedCostUsd: null }),
    ])

    expect(formatUsageTotal(summary)).toBe('$0.420 (미등록 1건)')
  })

  it('미등록이 없으면 괄호를 붙이지 않는다', () => {
    const summary = summarizeUsage([call({ estimatedCostUsd: 0.42 })])

    expect(formatUsageTotal(summary)).toBe('$0.420')
  })

  it('빈 목록은 0 이고 미등록도 0 이다', () => {
    const summary = summarizeUsage([])

    expect(summary.totalUsd).toBe(0)
    expect(summary.unpricedCount).toBe(0)
  })
})

describe('3D 사용량과 비용', () => {
  it('실제 크레딧과 파츠당 단가를 기존 작업 합계에 더한다', () => {
    const summary = summarizeJobUsage(
      [call({ estimatedCostUsd: 0.04 })],
      [
        {
          id: 'mesh-1',
          taskId: 'task-mesh-1',
          partName: '나무',
          model: 'P1-20260311',
          creditsConsumed: 30,
          createdAt: '2026-08-20T00:00:00Z',
        },
        {
          id: 'mesh-2',
          taskId: 'task-mesh-2',
          partName: '바위',
          model: 'P1-20260311',
          creditsConsumed: 30,
          createdAt: '2026-08-20T00:00:00Z',
        },
      ],
      [price()],
    )

    expect(summary.rows.map((row) => row.amount)).toEqual([
      '1,240 → 380 토큰',
      '30 크레딧',
      '30 크레딧',
    ])
    expect(summary.totalUsd).toBeCloseTo(0.64)
    expect(summary.unpricedCount).toBe(0)
  })

  it('결과 생성 시점에 적용되던 단가를 사용한다', () => {
    const summary = summarizeJobUsage(
      [],
      [
        {
          id: 'mesh-1',
          taskId: 'task-mesh-1',
          partName: '나무',
          model: 'P1-20260311',
          creditsConsumed: 30,
          createdAt: '2026-08-10T00:00:00Z',
        },
      ],
      [
        price({ id: 'old', perImage: 0.3 }),
        price({ id: 'new', effectiveFrom: '2026-08-15T00:00:00Z', perImage: 0.45 }),
      ],
    )

    expect(summary.totalUsd).toBeCloseTo(0.3)
  })

  it('단가가 없으면 실제 크레딧을 보여주되 미등록으로 센다', () => {
    const summary = summarizeJobUsage(
      [],
      [
        {
          id: 'mesh-1',
          taskId: 'task-mesh-1',
          partName: '나무',
          model: 'unknown-mesh',
          creditsConsumed: 50,
          createdAt: '2026-08-20T00:00:00Z',
        },
      ],
      [],
    )

    expect(summary.rows[0]?.amount).toBe('50 크레딧')
    expect(summary.unpricedCount).toBe(1)
    expect(formatUsageTotal(summary)).toBe('$0.000 (미등록 1건)')
  })

  it('실행 전에는 파츠 수와 현재 파츠당 단가로 예상 비용을 계산한다', () => {
    expect(estimateMeshBatchCost([price()], 'P1-20260311', 3)).toBeCloseTo(0.9)
    expect(estimateMeshBatchCost([], 'P1-20260311', 3)).toBeNull()
  })
})

/**
 * **"단가를 모른다" 와 "쓴 것이 없다" 는 다른 일이다.**
 *
 * 둘 다 백엔드에서 `null` 로 오지만 사용자가 할 일이 다르다. 앞은 단가를 등록하면 값이
 * 채워지고 그때까지 합계가 실제보다 낮다. 뒤는 고칠 것이 없고 합계도 정확하다.
 * 뭉쳐 세면 고칠 게 없는 건수가 경고에 계속 남아 지표가 무뎌진다.
 *
 * 가르는 기준은 성패가 아니라 **사용량**이다. `succeeded` 는 공급자 호출의 성패라(G-5)
 * 실패해도 토큰이 청구된 호출이 있고, 그 비용은 세어야 한다.
 */
describe('미등록과 사용량 없음', () => {
  const unmeasured = { inputTokens: null, outputTokens: null, outputImages: null }

  it('사용량이 없는 호출은 미등록으로 세지 않는다', () => {
    const summary = summarizeUsage([
      call({ id: 'a', estimatedCostUsd: 0.42 }),
      call({ id: 'b', estimatedCostUsd: null, ...unmeasured, succeeded: false }),
    ])

    expect(summary.unpricedCount).toBe(0)
    expect(summary.unmeasuredCount).toBe(1)
  })

  it('사용량이 있는데 값이 없으면 그것이 미등록이다', () => {
    const summary = summarizeUsage([
      call({ id: 'a', estimatedCostUsd: null, inputTokens: 2075, outputTokens: 1377 }),
    ])

    expect(summary.unpricedCount).toBe(1)
    expect(summary.unmeasuredCount).toBe(0)
  })

  it('실패했어도 토큰이 붙었으면 비용을 센다', () => {
    // 공급자가 청구한 뒤 끊긴 호출 — 성패로 가르면 이 돈이 합계에서 사라진다
    const summary = summarizeUsage([call({ id: 'a', estimatedCostUsd: 0.02, succeeded: false })])

    expect(summary.totalUsd).toBeCloseTo(0.02)
    expect(summary.unmeasuredCount).toBe(0)
  })

  it('합계는 둘을 따로 낸다', () => {
    const summary = summarizeUsage([
      call({ id: 'a', estimatedCostUsd: 0.42 }),
      call({ id: 'b', estimatedCostUsd: null, inputTokens: 2075, outputTokens: 1377 }),
      call({ id: 'c', estimatedCostUsd: null, ...unmeasured, succeeded: false }),
    ])

    expect(formatUsageTotal(summary)).toBe('$0.420 (미등록 1건 · 사용량 없음 1건)')
  })

  it('한쪽만 있으면 그쪽만 적는다', () => {
    const summary = summarizeUsage([
      call({ id: 'a', estimatedCostUsd: 0.42 }),
      call({ id: 'b', estimatedCostUsd: null, ...unmeasured, succeeded: false }),
    ])

    expect(formatUsageTotal(summary)).toBe('$0.420 (사용량 없음 1건)')
  })

  it('비용 칸의 낱말이 둘을 구분한다', () => {
    const { rows } = summarizeUsage([
      call({ id: 'a', estimatedCostUsd: 0.011 }),
      call({ id: 'b', estimatedCostUsd: null, inputTokens: 2075, outputTokens: 1377 }),
      call({ id: 'c', estimatedCostUsd: null, ...unmeasured, succeeded: false }),
    ])

    expect(rows.map(costLabel)).toEqual(['$0.011', '미등록', '사용량 없음'])
  })
})

it('비소급 신규 3D 단가는 과거 미등록을 보존한다', () => {
  const future = {
    ...price({ effectiveFrom: '2099-01-01T00:00:00Z' }),
    allowHistoricalFallback: false,
  }
  expect(estimateMeshBatchCost([future], future.model, 1)).toBeNull()
})

it('미래 날짜 버전이 기존 3D alias의 과거 단가를 가리지 않는다', () => {
  const legacy = price({ model: 'P1', effectiveFrom: '2020-01-01T00:00:00Z' })
  const future = {
    ...price({ model: 'P1-20260311', effectiveFrom: '2099-01-01T00:00:00Z', perImage: 5 }),
    allowHistoricalFallback: false,
  }
  expect(estimateMeshBatchCost([future, legacy], future.model, 1)).toBe(legacy.perImage)
})

it('수집 시행일은 timezone 문자열 순서 대신 같은 순간으로 비교한다', () => {
  const collected = {
    ...price({ effectiveFrom: '2026-08-02T00:00:00+09:00' }),
    allowHistoricalFallback: false,
  }
  const summary = summarizeJobUsage(
    [],
    [
      {
        id: 'mesh',
        taskId: 'task',
        partName: 'part',
        model: collected.model,
        createdAt: '2026-08-01T16:00:00Z',
        creditsConsumed: null,
      },
    ],
    [collected],
  )
  expect(summary.rows[0]?.costUsd).toBe(collected.perImage)
})
