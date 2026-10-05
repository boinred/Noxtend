/**
 * 유사도 실행 API (background-similarity-tuning §10).
 *
 * 시작은 multipart — 기준 렌더 PNG 가 요청에 실린다. `Idempotency-Key` 는 화면이
 * 실행 세션마다 하나 만들어 재시도에 같은 값을 보낸다 (§10.1) — 더블클릭·네트워크
 * 재전송이 유료 호출을 두 번 만들지 않는다.
 */
import { apiRequest } from './client'
import type {
  SceneRevision,
  SimilarityCandidate,
  SimilarityCostEstimate,
  SimilarityRun,
  SimilarityStatus,
} from '@/domain/similarity/types'

export function getSimilarityStatus(
  jobId: string,
  signal?: AbortSignal,
): Promise<SimilarityStatus> {
  return apiRequest<SimilarityStatus>(`/api/jobs/${jobId}/similarity`, { signal })
}

export function getSimilarityEstimate(
  jobId: string,
  model: string,
  maxIterations: number,
  signal?: AbortSignal,
): Promise<SimilarityCostEstimate> {
  const query = new URLSearchParams({ model, maxIterations: String(maxIterations) })
  return apiRequest<SimilarityCostEstimate>(`/api/jobs/${jobId}/similarity/estimate?${query}`, {
    signal,
  })
}

export function getSimilarityRun(
  jobId: string,
  runId: string,
  signal?: AbortSignal,
): Promise<SimilarityRun> {
  return apiRequest<SimilarityRun>(`/api/jobs/${jobId}/similarity-runs/${runId}`, { signal })
}

export interface StartSimilarityRunInput {
  jobId: string
  providerConfigId: string
  model: string
  maxIterations: number
  layoutId: string
  idempotencyKey: string
  render: Blob
}

export function startSimilarityRun(
  input: StartSimilarityRunInput,
  signal?: AbortSignal,
): Promise<SimilarityRun> {
  const form = new FormData()
  form.set('providerConfigId', input.providerConfigId)
  form.set('model', input.model)
  form.set('maxIterations', String(input.maxIterations))
  form.set('layoutId', input.layoutId)
  form.set('render', input.render, 'render.png')

  return apiRequest<SimilarityRun>(`/api/jobs/${input.jobId}/similarity-runs`, {
    method: 'POST',
    body: form,
    headers: { 'Idempotency-Key': input.idempotencyKey },
    signal,
  })
}

/** 선택한 보정 id 로 후보 revision 생성 (§9.2) — 값은 서버 저장본이 정본이다. */
export function createSimilarityCandidate(
  jobId: string,
  runId: string,
  adjustmentIds: string[],
  signal?: AbortSignal,
): Promise<SimilarityCandidate> {
  return apiRequest<SimilarityCandidate>(`/api/jobs/${jobId}/similarity-runs/${runId}/candidates`, {
    method: 'POST',
    body: { adjustmentIds },
    signal,
  })
}

/** 후보 렌더 업로드 — 같은 SHA 재전송은 성공으로 (§10.1). 업로드 뒤에는 서버가 끝낸다. */
export function uploadCandidateRender(
  jobId: string,
  runId: string,
  evaluationId: string,
  render: Blob,
  signal?: AbortSignal,
): Promise<void> {
  const form = new FormData()
  form.set('render', render, 'render.png')
  return apiRequest<unknown>(
    `/api/jobs/${jobId}/similarity-runs/${runId}/evaluations/${evaluationId}/render`,
    { method: 'PUT', body: form, signal },
  ).then(() => undefined)
}

/** 실패한 run 의 저장 렌더 재시도 (§9.3) — 새 캡처 없이 유료 재호출이다. */
export function retrySimilarityRun(jobId: string, runId: string): Promise<SimilarityRun> {
  return apiRequest<SimilarityRun>(`/api/jobs/${jobId}/similarity-runs/${runId}/retry`, {
    method: 'POST',
  })
}

/** 향후 반복·미시작 평가 중단 (§9.3) — 이미 나간 호출의 비용은 남는다. */
export function cancelSimilarityRun(jobId: string, runId: string): Promise<void> {
  return apiRequest<unknown>(`/api/jobs/${jobId}/similarity-runs/${runId}/cancel`, {
    method: 'POST',
  }).then(() => undefined)
}

/** 현재 결과로 실행 종료 (§9.3). */
export function completeSimilarityRun(jobId: string, runId: string): Promise<void> {
  return apiRequest<unknown>(`/api/jobs/${jobId}/similarity-runs/${runId}/complete`, {
    method: 'POST',
  }).then(() => undefined)
}

/** revision 이력 — 최신부터 (§10). */
export function listSceneRevisions(jobId: string): Promise<SceneRevision[]> {
  return apiRequest<SceneRevision[]>(`/api/jobs/${jobId}/scene-layout/revisions`)
}

/** 복원 = 값 복사 (§4.3) — 새 활성 revision 이 만들어진다. */
export function restoreSceneRevision(jobId: string, layoutId: string): Promise<SceneRevision> {
  return apiRequest<SceneRevision>(
    `/api/jobs/${jobId}/scene-layout/revisions/${layoutId}/restore`,
    { method: 'POST' },
  )
}
