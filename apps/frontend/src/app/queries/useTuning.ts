/**
 * Design Ref: §5.1 · §9.2 — 튜닝 쿼리.
 *
 * 프롬프트와 골든 세트는 자주 바뀌지 않는다. 대신 **변경 직후 반영이 눈에 보여야**
 * 하므로 변이마다 정확히 무효화한다 — 활성 전환은 목록과 이력 둘 다 바뀐다.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  activatePromptVersion,
  createGoldenSample,
  createModelPrice,
  createPromptVersion,
  deleteGoldenSample,
  deleteModelPrice,
  getCallStats,
  getPromptGrid,
  listGoldenRuns,
  listGoldenSamples,
  listJobCalls,
  listModelPrices,
  listPromptVersions,
  recordVerdict,
  updateGoldenSample,
  updateModelPrice,
} from '@/infra/api/tuningApi'
import { queryKeys } from './keys'
import type { AssetCategory } from '@/domain/job/types'
import type { PromptKind } from '@/domain/tuning/types'
import type { ModelPriceDraft, PromptDraft } from '@/domain/tuning/types'

/**
 * [목적] 프롬프트 관리 화면 그리드가 쓸 (단계 × 카테고리) 격자 데이터를 불러오고 캐시한다.
 * [핵심 동작] 격자 전용 쿼리 키로 캐싱한다 — 활성 전환·저장 뒤 이 키를 무효화하면 화면이 다시 그려진다.
 * [반환] { rows(격자 행), isLoading }.
 */
export function usePromptGrid() {
  const query = useQuery({
    queryKey: queryKeys.promptGrid(),
    queryFn: ({ signal }) => getPromptGrid(signal),
    retry: false,
  })

  return { rows: query.data ?? [], isLoading: query.isLoading }
}

/**
 * [목적] 편집 화면에서 한 단계·카테고리의 버전 이력을 불러오고 캐시한다.
 * [핵심 동작] 쿼리 키에 category 를 포함한다 — 이게 없으면 캐릭터 이력을 본 뒤 기본 이력을 열 때 캐릭터 행이 남아
 * 캐시가 섞인다. 슬롯마다 키가 나뉘어 섞이지 않는다.
 * [반환] { versions(그 슬롯의 버전 목록), isLoading }.
 */
export function usePromptVersions(kind: PromptKind, category: AssetCategory | null) {
  const query = useQuery({
    queryKey: queryKeys.promptVersions(kind, category),
    queryFn: ({ signal }) => listPromptVersions(kind, category, signal),
    retry: false,
  })

  return { versions: query.data ?? [], isLoading: query.isLoading }
}

/**
 * [목적] 편집 화면의 "새 버전 저장"과 "이 버전 켜기/롤백" 동작을 제공한다.
 * [핵심 동작] 저장은 현재 화면의 카테고리를 초안에 실어 그 슬롯에 만든다. 두 동작 모두 성공하면 격자와
 * 이 카테고리 이력 캐시를 함께 무효화해 화면이 곧바로 최신으로 다시 그려진다.
 * [반환] { create, activate } (각각 react-query mutation).
 */
export function usePromptMutations(kind: PromptKind, category: AssetCategory | null) {
  const client = useQueryClient()

  // 활성 전환은 격자(어느 버전이 켜졌나·폴백)와 이 카테고리 이력(플래그)을 함께 바꾼다
  const invalidate = () => {
    void client.invalidateQueries({ queryKey: queryKeys.promptGrid() })
    void client.invalidateQueries({ queryKey: queryKeys.promptVersions(kind, category) })
  }

  return {
    create: useMutation({
      // 이 화면의 카테고리를 초안에 실어 그 슬롯에 저장한다 (§7.2)
      mutationFn: (draft: PromptDraft) =>
        createPromptVersion(kind, { ...draft, category: category ?? undefined }),
      onSuccess: invalidate,
    }),
    activate: useMutation({
      mutationFn: (versionId: string) => activatePromptVersion(versionId),
      onSuccess: invalidate,
    }),
  }
}

export function useGoldenSamples() {
  const query = useQuery({
    queryKey: queryKeys.golden(),
    queryFn: ({ signal }) => listGoldenSamples(signal),
    retry: false,
  })

  return { samples: query.data ?? [], isLoading: query.isLoading }
}

export function useGoldenMutations() {
  const client = useQueryClient()
  const invalidate = () => {
    void client.invalidateQueries({ queryKey: queryKeys.golden() })
  }

  return {
    create: useMutation({
      mutationFn: (input: { storedImageId: string; name: string; expectedNote: string }) =>
        createGoldenSample(input),
      onSuccess: invalidate,
    }),
    update: useMutation({
      mutationFn: ({ id, ...input }: { id: string; name: string; expectedNote: string }) =>
        updateGoldenSample(id, input),
      onSuccess: invalidate,
    }),
    remove: useMutation({
      mutationFn: (id: string) => deleteGoldenSample(id),
      onSuccess: invalidate,
    }),
  }
}

/**
 * 골든 샘플의 실행 이력.
 *
 * 판정을 남기면 이 목록의 해당 행이 바뀌므로 함께 무효화한다.
 */
export function useGoldenRuns(sampleId: string | null) {
  const query = useQuery({
    queryKey: queryKeys.goldenRuns(sampleId ?? ''),
    queryFn: ({ signal }) => listGoldenRuns(sampleId!, signal),
    enabled: sampleId !== null,
    retry: false,
  })

  return { runs: query.data ?? [], isLoading: query.isLoading }
}

export function useVerdictMutation(sampleId: string | null) {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ jobId, isPass, memo }: { jobId: string; isPass: boolean; memo: string }) =>
      recordVerdict(jobId, { isPass, memo }),
    onSuccess: () => {
      if (sampleId !== null) {
        void client.invalidateQueries({ queryKey: queryKeys.goldenRuns(sampleId) })
      }
    },
  })
}

/** 작업의 LLM 내역. 성공·실패 모두. */
export function useJobCalls(jobId: string | null) {
  const query = useQuery({
    queryKey: queryKeys.jobCalls(jobId ?? ''),
    queryFn: ({ signal }) => listJobCalls(jobId!, signal),
    enabled: jobId !== null,
    retry: false,
  })

  return { calls: query.data ?? [], isLoading: query.isLoading }
}

/** 내역 규모 (§2.3-8) — 보존 정책 대신 커지는 것을 보이게 한다. */
export function useCallStats() {
  const query = useQuery({
    queryKey: queryKeys.callStats(),
    queryFn: ({ signal }) => getCallStats(signal),
    retry: false,
  })

  return { stats: query.data ?? null }
}

export function useModelPrices() {
  const query = useQuery({
    queryKey: queryKeys.prices(),
    queryFn: ({ signal }) => listModelPrices(signal),
    retry: false,
  })

  return { prices: query.data ?? [], isLoading: query.isLoading }
}

/**
 * 단가 변이.
 *
 * **누적 비용도 함께 무효화한다.** 단가를 고치면 이미 쌓인 호출의 비용이 다시 계산되는데,
 * 목록만 새로 고치면 화면에 옛 합계가 남아 "고쳤는데 안 바뀐다" 로 읽힌다.
 */
export function usePriceMutations() {
  const client = useQueryClient()

  const invalidate = () => {
    void client.invalidateQueries({ queryKey: queryKeys.prices() })
    void client.invalidateQueries({ queryKey: queryKeys.callStats() })
  }

  return {
    create: useMutation({
      mutationFn: (draft: ModelPriceDraft) => createModelPrice(draft),
      onSuccess: invalidate,
    }),
    update: useMutation({
      mutationFn: ({ id, draft }: { id: string; draft: ModelPriceDraft }) =>
        updateModelPrice(id, draft),
      onSuccess: invalidate,
    }),
    remove: useMutation({
      mutationFn: (id: string) => deleteModelPrice(id),
      onSuccess: invalidate,
    }),
  }
}
