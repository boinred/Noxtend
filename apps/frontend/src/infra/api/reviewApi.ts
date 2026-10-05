/**
 * Design Ref: docs/specs/2026-08-28-review-gate.md §입력→출력 — 검수 게이트 엔드포인트.
 */
import { apiRequest, apiRequestVoid } from './client'
import type { Bounds, PaletteEntry, ReviewState } from '@/domain/job/types'
import type { JobAccepted } from './jobApi'

export function getReview(jobId: string, signal?: AbortSignal): Promise<ReviewState> {
  return apiRequest<ReviewState>(`/api/jobs/${jobId}/review`, { signal })
}

export interface AddReviewPartInput {
  name: string
  /** 비우면 승인 시 재작성 공정이 원본 이미지를 보고 채운다. */
  category?: string
  bounds: Bounds
  description?: string
  /**
   * 이 파츠가 가리는 기존 파츠 이름.
   *
   * **생략과 빈 배열의 뜻이 다르다** (occludedby-recompute §입력→출력 1). 생략하면
   * 서버가 기본 추정 — 겹치는 파츠 전부를 가린다. 빈 배열이면 검수자가 체크를 전부
   * 해제했다는 뜻이라 아무 관계도 기록되지 않는다.
   */
  occludes?: string[]
}

export interface ReviewPartAccepted {
  id: string
  partRef: string
  name: string
  category: string | null
  description: string | null
  placements: Bounds[]
  source: 'detected' | 'manual'
}

/**
 * 사각형 하나와 겹치는 파츠 이름 (occludedby-recompute §입력→출력 1).
 *
 * 화면이 "무엇을 가리는가" 체크박스를 그리려면 추가 전에 겹침을 알아야 한다.
 * 순수 조회라 상태를 바꾸지 않는다.
 */
export function findReviewOverlaps(
  jobId: string,
  bounds: Bounds,
  signal?: AbortSignal,
): Promise<{ overlapping: string[] }> {
  return apiRequest<{ overlapping: string[] }>(`/api/jobs/${jobId}/review/parts/overlaps`, {
    method: 'POST',
    body: { bounds },
    signal,
  })
}

/** 겹쳐도 추가된다 — 가림 관계는 `occludes` 가 정한다. */
export function addReviewPart(
  jobId: string,
  input: AddReviewPartInput,
  signal?: AbortSignal,
): Promise<ReviewPartAccepted> {
  return apiRequest<ReviewPartAccepted>(`/api/jobs/${jobId}/review/parts`, {
    method: 'POST',
    body: input,
    signal,
  })
}

export function removeReviewPart(
  jobId: string,
  partId: string,
  signal?: AbortSignal,
): Promise<void> {
  return apiRequestVoid(`/api/jobs/${jobId}/review/parts/${partId}`, { method: 'DELETE', signal })
}

/** 검수 화면에서 상자 하나를 끌어 옮기거나 크기를 바꾼다. 응답은 검수 상태 전체다. */
export function moveReviewPlacement(
  jobId: string,
  partId: string,
  ordinal: number,
  bounds: Bounds,
  signal?: AbortSignal,
): Promise<ReviewState> {
  return apiRequest<ReviewState>(
    `/api/jobs/${jobId}/review/parts/${partId}/placements/${ordinal}`,
    { method: 'PATCH', body: bounds, signal },
  )
}

/** 승인된 파츠 수만큼 Generate 공정이 한 번에 팬아웃 계획된다 (§목표). */
export function approveReview(jobId: string, signal?: AbortSignal): Promise<JobAccepted> {
  return apiRequest<JobAccepted>(`/api/jobs/${jobId}/review/approve`, { method: 'POST', signal })
}

// ─── review-gate-staged 사이클 1 — 서술 확인 단계 ───

/** 서술 확정 — 보류됐던 Generate 팬아웃 계획. 빈 서술 파츠가 있으면 400. */
export function confirmDescriptions(jobId: string, signal?: AbortSignal): Promise<JobAccepted> {
  return apiRequest<JobAccepted>(`/api/jobs/${jobId}/review/confirm-descriptions`, {
    method: 'POST',
    signal,
  })
}

/** 서술 단계에서 상자 단계로. 서술은 유지된다. */
export function returnToBoxes(jobId: string, signal?: AbortSignal): Promise<ReviewState> {
  return apiRequest<ReviewState>(`/api/jobs/${jobId}/review/return-to-boxes`, {
    method: 'POST',
    signal,
  })
}

/** 파츠 서술 하나 교체. 응답은 검수 상태 전체. */
export function editReviewDescription(
  jobId: string,
  partId: string,
  description: string,
  signal?: AbortSignal,
): Promise<ReviewState> {
  return apiRequest<ReviewState>(`/api/jobs/${jobId}/review/parts/${partId}/description`, {
    method: 'PUT',
    body: { description },
    signal,
  })
}

/** 팔레트 통째 교체. 개수 3~8·대문자 #RRGGBB 위반은 400. */
export function editReviewPalette(
  jobId: string,
  palette: PaletteEntry[],
  signal?: AbortSignal,
): Promise<ReviewState> {
  return apiRequest<ReviewState>(`/api/jobs/${jobId}/review/palette`, {
    method: 'PUT',
    body: { palette },
    signal,
  })
}
