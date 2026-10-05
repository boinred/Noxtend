/**
 * Design Ref: docs/specs/2026-08-28-review-gate.md — 검수 게이트 폴링·편집.
 *
 * **화면은 `infra/api` 를 직접 부르지 않는다** (§9.2) — `useJob.ts` 와 같은 규칙.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  addReviewPart,
  approveReview,
  confirmDescriptions,
  editReviewDescription,
  editReviewPalette,
  findReviewOverlaps,
  getReview,
  moveReviewPlacement,
  removeReviewPart,
  returnToBoxes,
} from '@/infra/api/reviewApi'
import { queryKeys } from './keys'
import type { AddReviewPartInput } from '@/infra/api/reviewApi'
import type { Bounds, PaletteEntry, ReviewState } from '@/domain/job/types'

/**
 * 검수 대기 상태 조회.
 *
 * `enabled` 는 호출부가 `job.status === 'pendingReview'` 일 때만 켠다 — 다른 상태의
 * 작업에서 이 쿼리를 미리 돌릴 이유가 없다.
 */
export function useReview(jobId: string | undefined, enabled: boolean) {
  const query = useQuery({
    queryKey: queryKeys.review(jobId ?? ''),
    queryFn: ({ signal }) => getReview(jobId!, signal),
    enabled: Boolean(jobId) && enabled,
  })

  return { review: query.data, isLoading: query.isLoading }
}

/**
 * 그린 사각형과 겹치는 파츠 이름을 조회한다 (occludedby-recompute §입력→출력 1).
 *
 * **쿼리가 아니라 뮤테이션이다.** 드래그가 끝난 시점에 한 번만 부르기 위해서다 —
 * 쿼리로 두고 `draftBounds` 를 키에 넣으면 `onMouseMove` 마다 새 키가 생겨 드래그 한 번에
 * 수십 번 요청이 나가고 체크박스가 깜빡인다.
 */
export function useFindOverlaps(jobId: string | undefined) {
  return useMutation({
    mutationFn: (bounds: Bounds) => findReviewOverlaps(jobId!, bounds),
  })
}

/** 검수 화면에서 사각형으로 파츠를 추가한다. 겹쳐도 추가되고, 가림 관계는 `occludes` 가 정한다. */
export function useAddReviewPart(jobId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (input: AddReviewPartInput) => addReviewPart(jobId!, input),
    onSuccess: () => {
      if (jobId === undefined) return
      void queryClient.invalidateQueries({ queryKey: queryKeys.review(jobId) })
    },
  })
}

/** 검수 화면에서 잘못 탐지된 파츠를 제외한다. */
export function useRemoveReviewPart(jobId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (partId: string) => removeReviewPart(jobId!, partId),
    onSuccess: () => {
      if (jobId === undefined) return
      void queryClient.invalidateQueries({ queryKey: queryKeys.review(jobId) })
    },
  })
}

/**
 * 상자를 끌어 옮기거나 크기를 바꾼다.
 *
 * **응답을 캐시에 직접 심는다.** 무효화만 하면 재조회가 돌아올 때까지 상자가 옛 자리에
 * 남아, 놓은 손 아래에서 한 번 되돌아갔다가 다시 오는 것처럼 보인다. 서버가 이미 검수
 * 상태 전체를 돌려주므로 그대로 쓴다.
 */
export function useMoveReviewPlacement(jobId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ partId, ordinal, bounds }: MovePlacementInput) =>
      moveReviewPlacement(jobId!, partId, ordinal, bounds),
    onSuccess: (review) => {
      if (jobId === undefined) return
      queryClient.setQueryData(queryKeys.review(jobId), review)
    },
  })
}

export type MovePlacementInput = { partId: string; ordinal: number; bounds: Bounds }

/**
 * 상자 확정 — 서술 단계로 넘어간다 (review-gate-staged 사이클 1).
 *
 * 작업 상세를 무효화해야 재작성이 도는 동안 `running` 진행 표시로, 끝나면 다시
 * `pendingReview` 의 서술 단계로 화면이 따라간다.
 */
export function useApproveReview(jobId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: () => approveReview(jobId!),
    onSuccess: () => {
      if (jobId === undefined) return
      void queryClient.invalidateQueries({ queryKey: queryKeys.job(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.review(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
    },
  })
}

/**
 * 서술 확정 — 보류됐던 Generate 팬아웃 (review-gate-staged 사이클 1).
 *
 * 상자 확정과 같은 무효화 — `job.status` 가 `running` 으로 바뀌어 진행 표시로 넘어간다.
 */
export function useConfirmDescriptions(jobId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: () => confirmDescriptions(jobId!),
    onSuccess: () => {
      if (jobId === undefined) return
      void queryClient.invalidateQueries({ queryKey: queryKeys.job(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.review(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
    },
  })
}

// 검수 상태 전체 응답을 캐시에 심는 공통 onSuccess — 상자 이동과 같은 이유
function useSetReview(jobId: string | undefined) {
  const queryClient = useQueryClient()
  return (review: ReviewState) => {
    if (jobId === undefined) return
    queryClient.setQueryData(queryKeys.review(jobId), review)
  }
}

/** 서술 단계 → 상자 단계. */
export function useReturnToBoxes(jobId: string | undefined) {
  const setReview = useSetReview(jobId)
  return useMutation({ mutationFn: () => returnToBoxes(jobId!), onSuccess: setReview })
}

/** 파츠 서술 하나 저장. 응답으로 캐시 교체 — 편집 중인 다른 행은 행별 draft 가 지킨다. */
export function useEditReviewDescription(jobId: string | undefined) {
  const setReview = useSetReview(jobId)
  return useMutation({
    mutationFn: ({ partId, description }: { partId: string; description: string }) =>
      editReviewDescription(jobId!, partId, description),
    onSuccess: setReview,
  })
}

/** 팔레트 통째 저장. */
export function useEditReviewPalette(jobId: string | undefined) {
  const setReview = useSetReview(jobId)
  return useMutation({
    mutationFn: (palette: PaletteEntry[]) => editReviewPalette(jobId!, palette),
    onSuccess: setReview,
  })
}
