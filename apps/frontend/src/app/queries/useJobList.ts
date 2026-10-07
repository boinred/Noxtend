import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { deleteJob, listJobs } from '@/infra/api/jobApi'
import { queryKeys } from './keys'
import type { ProductionMode } from '@/domain/sprites/types'
import type { JobSummary } from '@/domain/job/types'
import type { JobListFilter } from '@/infra/api/jobApi'

export interface UseJobListResult {
  jobs: JobSummary[]
  /** 조건에 맞는 전체 건수. 목록은 limit 까지만 오므로 이보다 적을 수 있다 */
  total?: number
  isLoading: boolean
  error: Error | null
  isError: boolean
  refetch: () => Promise<unknown>
}

/** 진행 중 목록만 주기적으로 새로 받는다. 완료 이력은 스스로 바뀌지 않는다. */
const ACTIVE_REFETCH_MS = 5_000

export function useJobList(
  filter: JobListFilter,
  limit = 10,
  productionMode?: ProductionMode,
): UseJobListResult {
  const query = useQuery({
    queryKey: queryKeys.jobList(filter, limit, productionMode),
    queryFn: ({ signal }) => listJobs(filter, limit, signal, productionMode),
    refetchInterval: filter === 'active' ? ACTIVE_REFETCH_MS : false,
    retry: false,
  })

  return {
    jobs: query.data?.items ?? [],
    total: query.data?.total,
    isLoading: query.isLoading,
    error: query.error,
    isError: query.isError,
    refetch: query.refetch,
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
