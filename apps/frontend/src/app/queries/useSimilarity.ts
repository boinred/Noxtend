/**
 * 유사도 실행 상태와 시작 (background-similarity-tuning §10 · §14).
 *
 * **폴링은 진행 중일 때만** — run 이 종료 상태로 바뀌면 자동으로 멈춘다.
 * 유료 호출의 진행 상황이라 화면이 실제 워커 상태와 붙어 있어야 한다 (§14.2).
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getSimilarityEstimate,
  getSimilarityStatus,
  startSimilarityRun,
} from '@/infra/api/similarityApi'
import type { StartSimilarityRunInput } from '@/infra/api/similarityApi'
import { queryKeys } from './keys'
import { isRunActive } from '@/domain/similarity/types'
import type { SimilarityCostEstimate, SimilarityStatus } from '@/domain/similarity/types'

export interface UseSimilarityStatusResult {
  status: SimilarityStatus | null
  isLoading: boolean
  isError: boolean
}

export function useSimilarityStatus(
  jobId: string | null,
  enabled: boolean,
): UseSimilarityStatusResult {
  const query = useQuery({
    queryKey: queryKeys.similarityStatus(jobId ?? ''),
    queryFn: ({ signal }) => getSimilarityStatus(jobId!, signal),
    enabled: enabled && jobId != null,
    // 평가는 수 초~수십 초 — 진행 중일 때만 2.5초 폴링, 끝나면 멈춘다
    refetchInterval: (query) => (isRunActive(query.state.data?.openRun ?? null) ? 2_500 : false),
    retry: 1,
  })

  return { status: query.data ?? null, isLoading: query.isLoading, isError: query.isError }
}

export function useSimilarityEstimate(
  jobId: string | null,
  model: string | null,
  maxIterations: number,
  enabled: boolean,
): SimilarityCostEstimate | null {
  const query = useQuery({
    queryKey: queryKeys.similarityEstimate(jobId ?? '', model ?? '', maxIterations),
    queryFn: ({ signal }) => getSimilarityEstimate(jobId!, model!, maxIterations, signal),
    enabled: enabled && jobId != null && model != null,
    // 단가표는 관리자가 바꿀 때만 변한다
    staleTime: 60_000,
  })

  return query.data ?? null
}

export function useStartSimilarityRun(jobId: string | null) {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (input: StartSimilarityRunInput) => startSimilarityRun(input),
    // 시작 성공 — 현황을 즉시 다시 받아 폴링이 붙는다
    onSuccess: () => {
      if (jobId) void client.invalidateQueries({ queryKey: queryKeys.similarityStatus(jobId) })
    },
  })
}
