/**
 * Design Ref: §5.3 · §6 — 홈 두 섹션.
 *
 * **API 가 없어도 화면이 깨지지 않는다.** 이것은 친절이 아니라 회귀 방어 장치다 (§8.6) —
 * 홈을 건드리는 기존 테스트가 백엔드 없이 계속 돌아야 한다.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { deleteJob, listJobs } from '@/infra/api/jobApi'
import { queryKeys } from './keys'
import type { JobSummary } from '@/domain/job/types'
import type { JobListFilter } from '@/infra/api/jobApi'

export interface UseJobListResult {
  jobs: JobSummary[]
  /** 조건에 맞는 전체 건수. 목록은 limit 까지만 오므로 이보다 적을 수 있다 */
  total?: number
  isLoading: boolean
}

/** 진행 중 목록만 주기적으로 새로 받는다. 완료 이력은 스스로 바뀌지 않는다. */
const ACTIVE_REFETCH_MS = 5_000

export function useJobList(filter: JobListFilter, limit = 10): UseJobListResult {
  const query = useQuery({
    queryKey: queryKeys.jobList(filter),
    queryFn: async ({ signal }) => {
      try {
        return await listJobs(filter, limit, signal)
      } catch (error) {
        // 취소는 그대로 올려야 TanStack Query 가 상태를 덮지 않는다
        if (error instanceof DOMException && error.name === 'AbortError') throw error

        // 서버가 없거나 실패하면 빈 목록이다. 던지면 홈이 오류 화면이 되고,
        // 백엔드 없이 도는 기존 테스트가 전부 무너진다 (§8.6)
        return { items: [], total: undefined }
      }
    },
    refetchInterval: filter === 'active' ? ACTIVE_REFETCH_MS : false,
    retry: false,
  })

  return {
    jobs: query.data?.items ?? [],
    total: query.data?.total,
    isLoading: query.isLoading,
  }
}

/** 종료된 작업 삭제 및 목록 갱신 훅 */
export function useDeleteJob() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (jobId: string) => deleteJob(jobId),
    onSuccess: () => {
      // 작업 목록 쿼리 전체 갱신
      queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
    },
    onError: (error) => {
      console.error('작업 삭제 실패:', error)
    },
  })
}
