/**
 * Design Ref: §4.2 #16~26 — 튜닝 엔드포인트.
 *
 * 파이프라인 API 와 같은 봉투·같은 오류 정규화를 쓴다 (`client.ts`).
 */
import { apiRequest, apiRequestVoid } from './client'
import type { AssetCategory } from '@/domain/job/types'
import type { PromptKind } from '@/domain/tuning/types'
import type {
  CallStats,
  ModelPrice,
  ModelPriceDraft,
  GoldenRun,
  GoldenSample,
  LlmCall,
  PromptDraft,
  PromptGridRow,
  PromptVersion,
  Verdict,
} from '@/domain/tuning/types'

// ─── 프롬프트 ───

/**
 * [목적] 프롬프트 관리 화면의 그리드 뷰에 쓸 (단계 × 카테고리) 현황을 서버에서 가져온다.
 * [핵심 동작] 폴백(전용 없으면 기본)은 서버가 이미 계산해 넣어준다 — 프론트는 다시 판정하지 않고 응답 봉투의
 * rows 만 꺼내 그대로 표로 펼친다.
 * [반환] 단계별 격자 행 목록(PromptGridRow[]).
 *
 * Ref: prompt-category-axis §12.1.
 */
export function getPromptGrid(signal?: AbortSignal): Promise<PromptGridRow[]> {
  return apiRequest<{ rows: PromptGridRow[] }>('/api/prompts/grid', { signal }).then(
    (response) => response.rows,
  )
}

/**
 * [목적] 편집 화면에서 한 단계의 버전 이력을 가져온다.
 * [핵심 동작] category 가 있으면 `?category=` 로 그 카테고리 전용 이력만, 없으면(null) 기본 슬롯 이력을 요청한다 —
 * 슬롯을 나눠야 이력이 뒤섞이지 않는다.
 * [반환] 그 슬롯의 버전 목록(PromptVersion[]).
 */
export function listPromptVersions(
  kind: PromptKind,
  category: AssetCategory | null,
  signal?: AbortSignal,
): Promise<PromptVersion[]> {
  const query = category ? `?category=${category}` : ''
  return apiRequest<PromptVersion[]>(`/api/prompts/${kind}/versions${query}`, { signal })
}

/**
 * 새 버전. **항상 비활성으로 만들어진다** — 저장이 활성화가 아니다 (FR-09).
 *
 * 변수 오타는 여기서 400 으로 거부된다. 활성화한 뒤 실행이 전부 실패하는 것보다 낫다.
 */
export function createPromptVersion(
  kind: PromptKind,
  draft: PromptDraft,
  signal?: AbortSignal,
): Promise<PromptVersion> {
  return apiRequest<PromptVersion>(`/api/prompts/${kind}/versions`, {
    method: 'POST',
    body: draft,
    signal,
  })
}

/** 활성 전환. 롤백도 이것이다 — 이전 버전을 켜면 된다. */
export function activatePromptVersion(
  versionId: string,
  signal?: AbortSignal,
): Promise<PromptVersion> {
  return apiRequest<PromptVersion>(`/api/prompts/versions/${versionId}/activate`, {
    method: 'POST',
    signal,
  })
}

// ─── 골든 세트 ───

export function listGoldenSamples(signal?: AbortSignal): Promise<GoldenSample[]> {
  return apiRequest<GoldenSample[]>('/api/golden', { signal })
}

export function createGoldenSample(
  input: { storedImageId: string; name: string; expectedNote: string },
  signal?: AbortSignal,
): Promise<GoldenSample> {
  return apiRequest<GoldenSample>('/api/golden', { method: 'POST', body: input, signal })
}

export function updateGoldenSample(
  id: string,
  input: { name: string; expectedNote: string },
  signal?: AbortSignal,
): Promise<GoldenSample> {
  return apiRequest<GoldenSample>(`/api/golden/${id}`, { method: 'PUT', body: input, signal })
}

export function deleteGoldenSample(id: string, signal?: AbortSignal): Promise<void> {
  return apiRequestVoid(`/api/golden/${id}`, { method: 'DELETE', signal })
}

/** 이 이미지로 돌린 실행들 — 비교 화면의 목록. */
export function listGoldenRuns(id: string, signal?: AbortSignal): Promise<GoldenRun[]> {
  return apiRequest<GoldenRun[]>(`/api/golden/${id}/runs`, { signal })
}

// ─── 내역 · 판정 ───

export function listJobCalls(jobId: string, signal?: AbortSignal): Promise<LlmCall[]> {
  return apiRequest<LlmCall[]>(`/api/jobs/${jobId}/calls`, { signal })
}

export function getCallStats(signal?: AbortSignal): Promise<CallStats> {
  return apiRequest<CallStats>('/api/calls/stats', { signal })
}

/** 사람의 판정. 다시 부르면 덮어쓴다. */
export function recordVerdict(
  jobId: string,
  input: { isPass: boolean; memo: string },
  signal?: AbortSignal,
): Promise<Verdict> {
  return apiRequest<Verdict>(`/api/jobs/${jobId}/verdict`, {
    method: 'POST',
    body: input,
    signal,
  })
}

// ─── 단가 ───

/**
 * 모델 단가 목록.
 *
 * 전에는 코드 안의 상수 표라 단가 하나를 고치려면 재배포해야 했다.
 */
export function listModelPrices(signal?: AbortSignal): Promise<ModelPrice[]> {
  return apiRequest<ModelPrice[]>('/api/prices', { signal }).then((prices) =>
    prices.map(normalizeModelPrice),
  )
}

/** 새 단가 행 — **공급자가 단가를 바꿨을 때**. 과거 호출의 비용은 그대로 남는다. */
export function createModelPrice(
  draft: ModelPriceDraft,
  signal?: AbortSignal,
): Promise<ModelPrice> {
  return apiRequest<ModelPrice>('/api/prices', { method: 'POST', body: draft, signal }).then(
    normalizeModelPrice,
  )
}

/** 기존 행 수정 — **오타 정정**. 과거 호출의 비용도 함께 다시 계산된다. */
export function updateModelPrice(
  id: string,
  draft: ModelPriceDraft,
  signal?: AbortSignal,
): Promise<ModelPrice> {
  return apiRequest<ModelPrice>(`/api/prices/${id}`, { method: 'PUT', body: draft, signal }).then(
    normalizeModelPrice,
  )
}

export function deleteModelPrice(id: string, signal?: AbortSignal): Promise<void> {
  return apiRequestVoid(`/api/prices/${id}`, { method: 'DELETE', signal })
}

function normalizeModelPrice(price: ModelPrice): ModelPrice {
  return {
    ...price,
    provider: price.provider ?? null,
    allowHistoricalFallback: price.allowHistoricalFallback ?? true,
    sourceEvidenceJson: price.sourceEvidenceJson ?? null,
  }
}
