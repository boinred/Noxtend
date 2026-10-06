/**
 * Design Ref: §3.6 · §4.2 #16~26 — 튜닝 도메인.
 *
 * 프롬프트 · 내역 · 골든 세트 · 판정. 순수 TS 이며 외부 라이브러리를 쓰지 않는다.
 *
 * 파이프라인 타입(`domain/job`)과 나누는 이유는 백엔드와 같다 — 튜닝은 파이프라인을
 * 관찰하고 조정하는 별개 관심사다.
 */
import type { AssetCategory, TaskKind } from '@/domain/job/types'

/**
 * 프롬프트 슬롯 축 (background-similarity-tuning §7.1) — pipeline TaskKind 에서 분리됐다.
 * 유사도 평가처럼 공정이 아닌 호출의 슬롯이 여기에만 산다. reconstruct 는 LLM 이 없다.
 */
export type PromptKind = Exclude<TaskKind, 'reconstruct' | 'synthesize'> | 'similarityEvaluate'

const PROMPT_KIND_LABELS: Record<PromptKind, string> = {
  analyze: '장면 분석',
  extract: '파츠 식별',
  decompose: '파츠 분해',
  rewriteDescriptions: '서술 재작성',
  generate: '파츠 생성',
  similarityEvaluate: '유사도 평가',
  analyzeSprites: '2D 배경 분석',
}

export function promptKindLabel(kind: PromptKind): string {
  return PROMPT_KIND_LABELS[kind]
}

/**
 * 슬롯별 허용 카테고리 — 유사도 평가는 Background 전용이다 (§7.1, 서버도 저장을
 * 거절한다). 화면이 이것을 모르면 만들 수 없는 슬롯이 "없음 — 실행 불가" 로 보여
 * 프롬프트가 없다는 오해를 만든다 (실측 피드백).
 */
export function promptKindCategories(kind: PromptKind): (AssetCategory | null)[] {
  return kind === 'similarityEvaluate' || kind === 'analyzeSprites'
    ? ['background']
    : [null, 'character', 'object', 'background']
}

/**
 * 프롬프트 한 버전 — **불변**.
 *
 * 수정은 편집이 아니라 새 행이다. 내역이 버전 id 를 가리키므로 본문이 나중에 바뀌면
 * "이 결과가 어느 프롬프트에서 나왔나" 의 답이 시간에 따라 달라진다.
 */
export interface PromptVersion {
  id: string
  kind: PromptKind
  /** null 이면 기본 슬롯. 카테고리 전용 프롬프트는 값을 가진다 (prompt-category-axis §7.2) */
  category: AssetCategory | null
  version: number
  system: string
  user: string
  jsonSchema: string
  note: string | null
  isActive: boolean
  /** 편집 화면이 보여준다 — 오타로 저장이 거부되는 일을 줄인다. */
  allowedVariables: string[]
  createdAt: string
}

export interface PromptDraft {
  system: string
  user: string
  jsonSchema: string
  note?: string
  /** 생략/undefined 면 기본 슬롯에 저장한다 */
  category?: AssetCategory
}

/** 격자 한 칸의 유효 활성 상태 (prompt-category-axis §12.1). */
export type PromptCellStatus = 'dedicated' | 'fallback' | 'unavailable'

/**
 * (단계 × 카테고리) 격자의 한 칸 — **폴백을 서버가 반영한 값**.
 *
 * <c>version</c>·<c>versionId</c> 는 실행 불가 칸에서 null. fallback 이면 폴백 대상인
 * 기본 버전을 가리킨다. 화면은 이 값을 표로 펼치기만 하고 폴백을 재구현하지 않는다.
 */
export interface PromptGridCell {
  category: AssetCategory | null
  status: PromptCellStatus
  version: number | null
  versionId: string | null
}

export interface PromptGridRow {
  kind: PromptKind
  cells: PromptGridCell[]
}

/**
 * LLM 호출 한 건.
 *
 * **요청·응답 전문이 그대로 온다.** 이 화면의 목적이 "무엇을 보내 무엇을 받았나" 이므로
 * 요약하면 쓸모가 없다.
 */
/** 호출·사용량 행의 축 — 프롬프트 슬롯 전부 + 3D(reconstruct, LLM 아님). */
export type CallKind = PromptKind | 'reconstruct'

export function callKindLabel(kind: CallKind): string {
  return kind === 'reconstruct' ? '3D 재구성' : promptKindLabel(kind)
}

export interface LlmCall {
  id: string
  taskId: string
  kind: CallKind
  promptVersionId: string
  model: string
  requestPayload: string
  responsePayload: string | null
  inputTokens: number | null
  outputTokens: number | null
  latencyMs: number
  /**
   * **공급자 호출**의 성패 — 그 단계의 성패가 아니다.
   *
   * 유효한 JSON 이 왔는데 내용이 기대와 달라 파싱에서 실패하면 호출은 성공이고
   * 공정은 실패다. 단계 성패는 작업의 `tasks[].status` 를 봐야 한다.
   * `succeeded === false` 로만 걸러 문제를 찾으면 그 경우를 통째로 놓친다 (G-5).
   */
  succeeded: boolean
  failureReason: string | null
  at: string
  /**
   * USD. 단가를 모르는 모델이면 null — **0 이 아니다** (FR-20).
   *
   * 서버는 사이클 #5 부터 이 값을 내려주고 있었는데 프론트 타입에만 빠져 있었다.
   * 화면이 null 을 0 으로 뭉개면 합계가 실제보다 낮게 읽힌다.
   */
  estimatedCostUsd: number | null
  /** 이미지 호출이면 장 수, 텍스트면 null. 장당 과금의 곱수다 (사이클 #7). */
  outputImages: number | null
}

/** 내역이 커지는 것을 알아채기 위한 숫자 (§2.3-8). */
export interface CallStats {
  count: number
  approximateBytes: number
}

/** 평가의 기준선. `expectedNote` 는 사람이 적고 사람이 읽는다. */
export interface GoldenSample {
  id: string
  storedImageId: string
  name: string
  expectedNote: string
  createdAt: string
}

/**
 * 골든 샘플로 돌린 실행 하나.
 *
 * `promptVersions` 가 단계별 버전 번호다 — 이것이 없으면 무엇을 비교하는지 알 수 없다.
 */
export interface GoldenRun {
  jobId: string
  status: string
  model: string | null
  promptVersions: Record<string, number>
  partCount: number
  verdict: Verdict | null
  createdAt: string
}

/** 사람의 판정. 합불 + 메모, 재판정은 덮어쓴다. */
export interface Verdict {
  isPass: boolean
  memo: string
  at: string
}

/**
 * 두 실행이 비교 가능한가 — 같은 골든 샘플의 실행이면 언제나 참이다.
 *
 * **같은 버전끼리도 비교 대상이다** (FR-13). LLM 출력은 비결정적이라 같은 프롬프트도
 * 결과가 다르고, 그 흔들림의 폭을 눈으로 익혀야 "이건 프롬프트 효과인가 우연인가" 를
 * 판단할 수 있다.
 */
export function describeRun(run: GoldenRun): string {
  const versions = Object.entries(run.promptVersions)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([, v]) => `v${v}`)
    .join('/')

  return versions.length > 0 ? versions : '버전 미상'
}

/**
 * 모델 단가 — **모델당 시행일별로 한 행**.
 *
 * 같은 모델에 여러 행이 쌓인다. 공급자가 단가를 올리면 새 행을 추가하고, 오타를
 * 고칠 때는 해당 행을 수정한다. 이 구분이 이 표가 존재하는 이유다 — 행이 하나뿐이면
 * 인상분이 과거 지출까지 소급되어 지난달 비용이 조용히 부풀어 오른다.
 */
export interface ModelPrice {
  id: string
  model: string
  inputPerMillion: number
  outputPerMillion: number
  /** 장문 단가가 시작되는 입력 토큰 수. null 이면 구간 구분이 없다 */
  longContextFrom: number | null
  longInputPerMillion: number | null
  longOutputPerMillion: number | null
  effectiveFrom: string
  note: string
  /**
   * 이미지 1장당 USD. 토큰 과금 모델이면 null (사이클 #7 · Plan D-8).
   *
   * **토큰 칸에 환산해 넣지 않는다.** 이미지 모델은 장·해상도·품질당 과금이라
   * "100만 토큰당" 이라는 단위 자체가 성립하지 않고, 억지로 넣으면 표를 읽는 사람이
   * 그 값을 토큰 단가로 오해한다.
   */
  perImage: number | null
}

export type ModelPriceDraft = Omit<ModelPrice, 'id'>

/**
 * 모델별로 묶고, 묶음 안에서 **적용 중인 행을 맨 위**에 둔다.
 *
 * 목록을 시행일 순으로 늘어놓으면 같은 모델의 행이 흩어져 "지금 적용되는 단가가
 * 무엇인가" 를 읽을 수 없다. 모델이 묶음의 단위다.
 *
 * **단순 내림차순은 쓰지 않는다.** 인상을 미리 등록해 두면 미래 행이 맨 위에 오고,
 * 모델명도 거기 붙어 훑어볼 때 그 값이 현재 단가처럼 읽힌다. 비용을 잘못 읽게 하지
 * 않는 것이 이 표의 목적이므로, 묶음의 첫 행은 언제나 실제로 쓰이는 행이어야 한다.
 */
export function groupPricesByModel(prices: ModelPrice[]): ModelPriceGroup[] {
  const byModel = new Map<string, ModelPrice[]>()

  for (const price of prices) {
    const rows = byModel.get(price.model)
    if (rows) rows.push(price)
    else byModel.set(price.model, [price])
  }

  return [...byModel.entries()]
    .flatMap(([model, rows]) => {
      const desc = [...rows].sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom))
      const earliest = desc.at(-1)

      // 묶음은 행이 있어야 만들어진다 — 빈 묶음은 생길 수 없지만 타입으로 못 박는다
      if (!earliest) return []

      const current = currentPrice(desc, earliest)

      return [{ model, rows: orderForDisplay(desc, current), current }]
    })
    .sort((a, b) => a.model.localeCompare(b.model))
}

export interface ModelPriceGroup {
  model: string
  /** 적용 중인 행 → 예정(빠른 순) → 지난 행(최신 순) */
  rows: ModelPrice[]
  /** 지금 적용되는 행. 미래 시행 행만 있으면 그중 가장 이른 것 */
  current: ModelPrice
}

/**
 * 지금 적용되는 행.
 *
 * 미래 시행일만 있는 경우 — 인상을 미리 등록해 둔 상태 — 에는 가장 이른 행을 준다.
 * 백엔드가 "모든 행보다 오래된 호출은 가장 오래된 행으로 소급" 하는 것과 같은 규칙이라
 * 화면과 계산이 어긋나지 않는다.
 */
function currentPrice(sortedDesc: ModelPrice[], earliest: ModelPrice): ModelPrice {
  const now = new Date().toISOString()
  return sortedDesc.find((p) => p.effectiveFrom <= now) ?? earliest
}

/**
 * 표시 순서: 적용 중 → 예정 → 지난 행.
 *
 * 예정은 **빠른 순**이다 — 다음에 무엇이 적용될지가 먼 미래보다 궁금하다.
 * 지난 행은 최신 순 — 최근 이력이 오래된 것보다 자주 참조된다.
 */
function orderForDisplay(sortedDesc: ModelPrice[], current: ModelPrice): ModelPrice[] {
  const rest = sortedDesc.filter((p) => p.id !== current.id)

  const scheduled = rest
    .filter((p) => p.effectiveFrom > current.effectiveFrom)
    .sort((a, b) => a.effectiveFrom.localeCompare(b.effectiveFrom))

  const past = rest.filter((p) => p.effectiveFrom <= current.effectiveFrom)

  return [current, ...scheduled, ...past]
}

/** 미래 시행 행인가 — 화면이 "예정" 으로 구분해 보여준다 */
export function isScheduled(price: ModelPrice): boolean {
  return price.effectiveFrom > new Date().toISOString()
}
