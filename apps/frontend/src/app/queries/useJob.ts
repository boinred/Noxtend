/**
 * Design Ref: §5.3 · §3.4 · R-9 — 작업 폴링.
 *
 * **화면은 `infra/api` 를 직접 부르지 않는다** (§9.2). 폴링·캐시·무효화가 이 층에
 * 모여 있어야 화면마다 다른 주기로 서버를 두드리는 일이 생기지 않는다.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  addMeshProduction,
  cancelJob,
  generateSelectedViews,
  getJob,
  replanPartMesh,
  retryTask,
  returnToDescriptions,
  startJob,
} from '@/infra/api/jobApi'
import { isTerminal, nextPollDelayMs } from '@/domain/job/types'
import { queryKeys } from './keys'
import type { Job, ViewDirection } from '@/domain/job/types'
import type { BackPlan, LeftRightPlan, StartJobInput } from '@/infra/api/jobApi'

// 화면이 전송 계층(`infra/api`)의 타입을 직접 알지 않도록 이 계층을 거쳐 내보낸다 (§9.2)
export type { BackPlan, LeftRightPlan }

export interface UseJobResult {
  job: Job | undefined
  isLoading: boolean
  isNotFound: boolean
}

/**
 * 지수 백오프 폴링. 종료 상태가 되면 멈춘다.
 *
 * `refetchInterval` 이 함수를 받으므로 쿼리 자신의 상태로 다음 간격을 정할 수 있다.
 * 시도 횟수는 `dataUpdateCount` 를 쓴다 — 별도 카운터를 두면 리렌더마다 초기화된다.
 */
export function useJob(jobId: string | undefined): UseJobResult {
  const query = useQuery({
    queryKey: queryKeys.job(jobId ?? ''),
    queryFn: ({ signal }) => getJob(jobId!, signal),
    enabled: Boolean(jobId),
    refetchInterval: (query) => {
      const status = query.state.data?.status
      // 종료됐거나 아직 못 받았으면 폴링을 멈춘다.
      // 못 받은 경우까지 멈추는 이유는 404 를 무한히 두드리지 않기 위해서다
      if (!status || isTerminal(status)) return false

      return nextPollDelayMs(query.state.dataUpdateCount - 1)
    },
    // 404 를 재시도하면 없는 작업을 세 번 더 묻는다
    retry: false,
  })

  return {
    job: query.data,
    isLoading: query.isLoading,
    isNotFound: query.isError,
  }
}

/**
 * 작업 접수 → `/background/{jobId}` 로 이동할 id 를 돌려준다 (FR-13 · §5.2).
 *
 * 목록을 무효화하는 이유는 홈의 "실행 중" 이 즉시 새 작업을 보여야 하기 때문이다.
 */
export function useStartJob() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (input: StartJobInput) => startJob(input),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
    },
  })
}

/**
 * 실패한 파츠 방향 하나를 다시 돌린다 (사이클 #7 FR-08).
 *
 * **작업 상세와 내역을 함께 무효화한다.** 상태가 부분 성공 → 실행 중으로 되돌아가면
 * 폴링이 다시 시작되고, 재시도가 성공하면 내역에 호출이 한 건 는다.
 */
export function useRetryTask(jobId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (taskId: string) => retryTask(jobId!, taskId),
    onSuccess: () => {
      if (jobId === undefined) return

      void queryClient.invalidateQueries({ queryKey: queryKeys.job(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobCalls(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
    },
  })
}

/**
 * 끝난 작업에 3D 를 붙인다 (사이클 #11).
 *
 * 재시도와 같은 무효화를 한다 — 상태가 종료에서 실행 중으로 되돌아가면 폴링이 다시
 * 시작되고, 3D 가 끝나면 내역에 호출이 는다.
 */
export function useAddMeshProduction(jobId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (selection: { providerConfigId: string; model: string }) =>
      addMeshProduction(jobId!, selection.providerConfigId, selection.model),
    onSuccess: () => {
      if (jobId === undefined) return

      void queryClient.invalidateQueries({ queryKey: queryKeys.job(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobCalls(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
    },
  })
}

/** 파츠별 "3D 전송 뷰 자유 선택 + 대칭" (spec 20260917). */
export function useReplanPartMesh(jobId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (selection: {
      partId: string
      providerConfigId: string
      model: string
      leftRight: LeftRightPlan
      back: BackPlan
    }) =>
      replanPartMesh(
        jobId!,
        selection.partId,
        selection.providerConfigId,
        selection.model,
        selection.leftRight,
        selection.back,
      ),
    onSuccess: () => {
      if (jobId === undefined) return

      void queryClient.invalidateQueries({ queryKey: queryKeys.job(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobCalls(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
    },
  })
}

export function useCancelJob() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (jobId: string) => cancelJob(jobId),
    onSuccess: (_result, jobId) => {
      // 작업 상세와 두 목록이 함께 바뀐다 — active 에서 빠지고 terminal 로 들어간다
      void queryClient.invalidateQueries({ queryKey: queryKeys.job(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
    },
  })
}

export function useGenerateSelectedViews(jobId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (directions: ViewDirection[]) => generateSelectedViews(jobId!, directions),
    onSuccess: () => {
      if (jobId === undefined) return

      void queryClient.invalidateQueries({ queryKey: queryKeys.job(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobCalls(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
    },
  })
}

export function useReturnToDescriptions(jobId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (partId: string) => returnToDescriptions(jobId!, partId),
    onSuccess: () => {
      if (jobId === undefined) return

      void queryClient.invalidateQueries({ queryKey: queryKeys.job(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobCalls(jobId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
    },
  })
}
