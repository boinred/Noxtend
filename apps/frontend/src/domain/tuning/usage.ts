/**
 * 공정별 사용량·비용 집계 (사이클 #7 FR-18~FR-20).
 *
 * Design Ref: §5.2 · C-6 · C-7
 *
 * **핵심 규칙은 "구분되는 것을 뭉개지 않는다" 이다.** 백엔드는 단가를 모르는 호출에
 * `null` 을 주는데, 화면이 그것을 `0` 으로 더하면 합계가 실제보다 낮게 읽히고
 * 사용자는 그 값을 실제 지출로 믿는다 (FR-20).
 */
import { groupPricesByModel } from './types'
import type { LlmCall, ModelPrice } from './types'

/** 표 한 줄 — 공정 하나의 사용량. */
export interface UsageRow {
  call: LlmCall
  /** "1,240 → 380 토큰" 또는 "1장". 라벨이 "토큰" 이 아니라 "사용량" 이다 (C-6) */
  amount: string
  latencyMs: number
  /** null 이면 값을 못 낸 것 — `$0` 과 구분되는 낱말로 보여야 한다 (C-7) */
  costUsd: number | null
  /**
   * 공급자가 사용량을 하나라도 적어 줬는가.
   *
   * `costUsd` 가 null 인 이유를 가른다 — 사용량이 있는데 값이 없으면 단가를 모르는 것이고,
   * 사용량 자체가 없으면 매길 것이 없는 것이다.
   */
  measured: boolean
}

export interface UsageSummary {
  rows: UsageRow[]
  /** 단가를 아는 호출들의 합계 */
  totalUsd: number
  /** 단가를 모르는 호출 수. 0 이 아니면 합계가 실제보다 낮다 */
  unpricedCount: number
  /**
   * 사용량 자체가 없어 값을 매길 수 없는 호출 수.
   *
   * **합계를 낮추지 않는다** — 쓴 것이 없으니 뺄 것도 없다. 미등록과 같이 세면 운영자가
   * 고칠 수 없는 건수가 경고에 계속 남아, 정작 단가를 등록해야 할 때 신호가 안 보인다.
   */
  unmeasuredCount: number
}

/** 작업 결과에 저장된 3D 생성 사용량. */
export interface MeshUsage {
  id: string
  taskId: string
  partName: string
  model: string
  creditsConsumed: number | null
  createdAt: string
}

/** 스튜디오 사용량 표의 공통 행 — LLM 호출과 3D 결과를 같은 열로 표시한다. */
export interface JobUsageRow {
  id: string
  taskId: string
  kind: LlmCall['kind']
  partName: string | null
  amount: string
  latencyMs: number | null
  costUsd: number | null
  measured: boolean
}

export interface JobUsageSummary {
  rows: JobUsageRow[]
  totalUsd: number
  unpricedCount: number
  unmeasuredCount: number
}

/** 공급자가 사용량을 하나라도 적어 줬는가 — 백엔드 `ModelPriceBook.Estimate` 의 첫 분기와 같은 규칙. */
function isMeasured(call: LlmCall): boolean {
  return call.inputTokens !== null || call.outputTokens !== null || call.outputImages !== null
}

/**
 * 사용량 표기 (C-6 · D-16).
 *
 * **"토큰" 으로 박지 않는 이유**: 생성 공정은 장당 과금이라 토큰이 비고, 라벨을
 * 토큰으로 두면 파츠 공정 N칸이 전부 빈칸이 된다.
 */
export function usageAmount(call: LlmCall): string {
  if (call.outputImages !== null) {
    return `${call.outputImages}장`
  }

  if (call.inputTokens === null && call.outputTokens === null) {
    return '—'
  }

  const input = (call.inputTokens ?? 0).toLocaleString('ko-KR')
  const output = (call.outputTokens ?? 0).toLocaleString('ko-KR')

  return `${input} → ${output} 토큰`
}

export function summarizeUsage(calls: LlmCall[]): UsageSummary {
  const rows = calls.map((call) => ({
    call,
    amount: usageAmount(call),
    latencyMs: call.latencyMs,
    costUsd: call.estimatedCostUsd,
    measured: isMeasured(call),
  }))

  return {
    rows,
    // null 을 0 으로 더하지 않는다 — 아는 것만 더하고 모르는 것은 따로 센다
    totalUsd: rows.reduce((sum, row) => sum + (row.costUsd ?? 0), 0),
    unpricedCount: rows.filter((row) => row.costUsd === null && row.measured).length,
    unmeasuredCount: rows.filter((row) => row.costUsd === null && !row.measured).length,
  }
}

/**
 * 스튜디오 작업의 LLM·이미지·3D 비용 합계.
 *
 * 3D 단가는 결과 생성 시점을 기준으로 고른다. 관리자가 단가를 바꿔도 지난 결과 비용이
 * 새 단가로 소급되어 바뀌지 않아야 하기 때문이다.
 */
export function summarizeJobUsage(
  calls: LlmCall[],
  meshes: MeshUsage[],
  prices: ModelPrice[],
): JobUsageSummary {
  const callRows: JobUsageRow[] = summarizeUsage(calls).rows.map((row) => ({
    id: row.call.id,
    taskId: row.call.taskId,
    kind: row.call.kind,
    partName: null,
    amount: row.amount,
    latencyMs: row.latencyMs,
    costUsd: row.costUsd,
    measured: row.measured,
  }))

  // 3D 결과 하나가 유료 제출 한 건이다. 크레딧 응답이 없더라도 결과 자체는 사용량 1건이다.
  const meshRows: JobUsageRow[] = meshes.map((mesh) => ({
    id: mesh.id,
    taskId: mesh.taskId,
    kind: 'reconstruct',
    partName: mesh.partName,
    amount: mesh.creditsConsumed === null ? '1개' : `${mesh.creditsConsumed} 크레딧`,
    latencyMs: null,
    costUsd: meshPriceAt(prices, mesh.model, mesh.createdAt)?.perImage ?? null,
    measured: true,
  }))

  const rows = [...callRows, ...meshRows]

  return {
    rows,
    // 미등록을 0원으로 해석하지 않고 경고 건수에 남기는 기존 합계 규칙 유지
    totalUsd: rows.reduce((sum, row) => sum + (row.costUsd ?? 0), 0),
    unpricedCount: rows.filter((row) => row.costUsd === null && row.measured).length,
    unmeasuredCount: rows.filter((row) => row.costUsd === null && !row.measured).length,
  }
}

/** 선택한 3D 모델로 파츠를 일괄 생성할 때의 설정 단가 기준 예상 비용. */
export function estimateMeshBatchCost(
  prices: ModelPrice[],
  model: string,
  partCount: number,
): number | null {
  const perResult = meshPriceAt(prices, model, new Date().toISOString())?.perImage
  return perResult === null || perResult === undefined ? null : perResult * partCount
}

/** 백엔드 ModelPriceBook과 같은 시행일 선택 규칙의 3D 정액 단가 조회. */
function meshPriceAt(prices: ModelPrice[], model: string, at: string): ModelPrice | null {
  const eligible = prices
    .filter(
      (price) =>
        Date.parse(price.effectiveFrom) <= Date.parse(at) ||
        price.allowHistoricalFallback !== false,
    )
    .sort((a, b) => b.model.length - a.model.length)
  const matchedModel = eligible.find((price) => {
    if (!model.toLowerCase().startsWith(price.model.toLowerCase())) return false
    const rest = model.slice(price.model.length)
    return rest === '' || /^-[0-9]+$/.test(rest)
  })?.model
  const candidates = eligible.filter((price) => price.model === matchedModel)
  if (candidates.length === 0) return null

  // 수집 행의 시행 전 미등록과 레거시 소급 정책 보존
  return (
    candidates
      .filter((price) => Date.parse(price.effectiveFrom) <= Date.parse(at))
      .sort((a, b) => Date.parse(b.effectiveFrom) - Date.parse(a.effectiveFrom))[0] ??
    candidates
      .filter((price) => price.allowHistoricalFallback !== false)
      .sort((a, b) => Date.parse(a.effectiveFrom) - Date.parse(b.effectiveFrom))[0] ??
    null
  )
}

/**
 * 비용 칸 한 개의 낱말 (C-7).
 *
 * 값이 없는 두 경우를 다른 낱말로 낸다. 같은 "미등록" 으로 적으면 운영자가 단가표를
 * 뒤지다가 고칠 것이 없다는 걸 뒤늦게 안다.
 */
export function costLabel(row: Pick<UsageRow, 'costUsd' | 'measured'>): string {
  if (row.costUsd !== null) return formatUsd(row.costUsd)

  return row.measured ? '미등록' : '사용량 없음'
}

/**
 * 금액 표기. 미등록은 `$0` 이 아니라 낱말이다 (C-7).
 *
 * 소수 셋째 자리까지 — 파츠 하나가 $0.011 이라 둘째 자리에서 자르면 여러 건이
 * 전부 $0.01 로 보인다.
 */
export function formatUsd(value: number | null): string {
  return value === null ? '미등록' : `$${value.toFixed(3)}`
}

/**
 * 합계 한 줄 — `$0.42 (미등록 1건 · 사용량 없음 2건)`.
 *
 * 둘을 나눠 적는다. 앞은 "단가를 넣으면 합계가 올라간다", 뒤는 "고칠 것이 없다" 로
 * 읽혀야 하기 때문이다. 해당 없는 쪽은 적지 않고, 둘 다 없으면 괄호를 붙이지 않는다.
 */
export function formatUsageTotal(
  summary: Pick<UsageSummary, 'totalUsd' | 'unpricedCount' | 'unmeasuredCount'>,
): string {
  const total = `$${summary.totalUsd.toFixed(3)}`

  const notes = [
    summary.unpricedCount > 0 ? `미등록 ${summary.unpricedCount}건` : null,
    summary.unmeasuredCount > 0 ? `사용량 없음 ${summary.unmeasuredCount}건` : null,
  ].filter((note) => note !== null)

  return notes.length === 0 ? total : `${total} (${notes.join(' · ')})`
}

/**
 * 모델 하나의 지금 단가를 고르는 자리에서 읽을 한 줄로 (사이클 #8).
 *
 * **`groupPricesByModel` 을 그대로 쓴다.** "지금 적용되는 행" 규칙 — 미래 행만
 * 있으면 미등록, 아니면 오늘 이전 중 최신 — 을 여기서 다시 구현하면 관리자 표와
 * 스튜디오가 서로 다른 단가를 말하게 된다.
 *
 * 이미지 모델은 장당이다. 토큰 단위로 환산하지 않는 이유는 `ModelPrice.perImage` 가
 * 말하는 바와 같다 — 억지로 넣으면 읽는 사람이 그 값을 토큰 단가로 오해한다.
 */
export function modelPriceHint(prices: ModelPrice[], modelId: string): string {
  const group = groupPricesByModel(prices).find((g) => g.model === modelId)

  // 단가 미등록은 `$0` 과 구분되는 낱말이다 (C-7 · FR-20)
  if (!group?.current) return '단가 미등록'

  const { current } = group
  if (current.perImage !== null) return `장당 ${formatUsd(current.perImage)}`

  return `100만 토큰당 입력 ${formatUsd(current.inputPerMillion)} · 출력 ${formatUsd(current.outputPerMillion)}`
}
