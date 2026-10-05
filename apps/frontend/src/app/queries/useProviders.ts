/**
 * Design Ref: §5.3 · §4.2 #9~#13 — 공급자 목록과 변이.
 *
 * 스튜디오와 관리자가 같은 목록을 본다. 훅이 하나라 스튜디오의 "공급자 0개 안내" 와
 * 관리자의 목록이 어긋나지 않는다.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  createProvider,
  deleteProvider,
  listProviderCapabilities,
  listProviderImageModels,
  listProviderMeshModels,
  listProviderModels,
  listProviders,
  testProvider,
  updateProvider,
} from '@/infra/api/providerApi'
import { apiErrorMessage } from './errors'
import { queryKeys } from './keys'
import type {
  Provider,
  ProviderInput,
  ProviderKindCapabilities,
  ProviderModel,
} from '@/domain/provider/types'
import { providerSupports } from '@/domain/provider/types'
import type { ProviderTestResult } from '@/infra/api/providerApi'

// 연결 확인 결과는 화면이 표시해야 하므로 이 층을 통해 내보낸다 (§9.2)
export type { ProviderTestResult }

export interface UseProvidersResult {
  providers: Provider[]
  /** 실행에 고를 수 있는 것만. 사용 중지된 공급자는 스튜디오 목록에 뜨지 않는다 */
  enabledProviders: Provider[]
  textProviders: Provider[]
  imageProviders: Provider[]
  /** 3D 를 만들 수 있는 공급자 (사이클 #10). 없으면 3D 없이 접수한다. */
  meshProviders: Provider[]
  isLoading: boolean
}

export function useProviders(): UseProvidersResult {
  const query = useQuery({
    queryKey: queryKeys.providers(),
    queryFn: async ({ signal }) => {
      try {
        return await listProviders(signal)
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') throw error

        // API 부재 시 빈 목록 — 스튜디오는 "공급자 0개" 안내를 띄우면 된다 (§6)
        return []
      }
    },
    retry: false,
  })

  const providers = query.data ?? []
  const enabledProviders = providers.filter((provider) => provider.isEnabled)

  return {
    providers,
    enabledProviders,
    // Provider roles are filtered before rendering so unsupported choices never enter form state.
    textProviders: enabledProviders.filter((provider) =>
      providerSupports(provider, 'textAnalysis'),
    ),
    imageProviders: enabledProviders.filter((provider) =>
      providerSupports(provider, 'imageGeneration'),
    ),
    meshProviders: enabledProviders.filter((provider) =>
      providerSupports(provider, 'meshGeneration'),
    ),
    isLoading: query.isLoading,
  }
}

export interface UseProviderModelsResult {
  models: ProviderModel[]
  isLoading: boolean
  /**
   * 목록을 못 가져왔을 때 보여줄 한 줄. 성공이면 `null`.
   *
   * **빈 배열로 흡수하지 않는다.** 공급자 목록(`useProviders`)은 API 부재를 빈 배열로
   * 삼켜 홈이 깨지지 않게 하지만 (§8.6), 모델 목록은 반대다 — 여기가 비면 실행이
   * 막히므로 사용자가 무엇을 해야 하는지 알아야 한다.
   */
  errorMessage: string | null
}

/**
 * Design Ref: §4.2 #14 — 선택한 공급자가 지원하는 모델.
 *
 * `providerId` 가 없으면 조회하지 않는다 — 공급자를 아직 안 골랐으면 물어볼 대상이 없다.
 * 목록은 몇 주 단위로 바뀌므로 서버가 캐시하고 (§3.2), 여기서도 오래 신선하게 둔다.
 */
/**
 * 종류별 사용 용도 — 관리자 폼이 **등록 전에** 읽는다.
 *
 * 프론트에 같은 표를 두지 않는 이유는 백엔드 `ProviderCapabilities.For` 가 유일한
 * 출처여야 하기 때문이다. 배포 사이에 바뀌지 않으므로 오래 캐시한다.
 */
export function useProviderCapabilities(): ProviderKindCapabilities[] {
  const query = useQuery({
    queryKey: queryKeys.providerCapabilities(),
    queryFn: ({ signal }) => listProviderCapabilities(signal),
    staleTime: Infinity,
    retry: false,
  })

  return query.data ?? []
}

export function useProviderModels(providerId: string | null): UseProviderModelsResult {
  const query = useQuery({
    queryKey: queryKeys.providerModels(providerId ?? ''),
    queryFn: ({ signal }) => listProviderModels(providerId!, signal),
    enabled: providerId !== null,
    staleTime: 5 * 60 * 1000,
    retry: false,
  })

  return {
    models: query.data ?? [],
    isLoading: query.isLoading,
    errorMessage: query.error
      ? apiErrorMessage(query.error, '모델 목록을 가져올 수 없습니다')
      : null,
  }
}

/**
 * 이미지 생성 모델 목록 (사이클 #7).
 *
 * 텍스트 목록과 훅이 나뉘어 있어야 화면이 어느 쪽을 물었는지가 호출에 드러난다.
 * **빈 목록이 오류가 아니다** — 이미지 생성이 아예 없는 공급자가 있고, 그것은 사실이다.
 */
export function useProviderImageModels(providerId: string | null): UseProviderModelsResult {
  const query = useQuery({
    queryKey: queryKeys.providerImageModels(providerId ?? ''),
    queryFn: ({ signal }) => listProviderImageModels(providerId!, signal),
    enabled: providerId !== null,
    staleTime: 5 * 60 * 1000,
    retry: false,
  })

  return {
    models: query.data ?? [],
    isLoading: query.isLoading,
    errorMessage: query.error
      ? apiErrorMessage(query.error, '이미지 모델 목록을 가져올 수 없습니다')
      : null,
  }
}

/**
 * 3D 생성 모델 목록 (사이클 #10).
 *
 * 이미지와 훅을 나눈 이유는 같다 — 한 목록에 섞으면 사용자가 이미지 단계에 3D 모델을
 * 고를 수 있고, 그 오류는 접수가 아니라 실행 시점에야 드러난다.
 */
export function useProviderMeshModels(providerId: string | null): UseProviderModelsResult {
  const query = useQuery({
    queryKey: queryKeys.providerMeshModels(providerId ?? ''),
    queryFn: ({ signal }) => listProviderMeshModels(providerId!, signal),
    enabled: providerId !== null,
    staleTime: 5 * 60 * 1000,
    retry: false,
  })

  return {
    models: query.data ?? [],
    isLoading: query.isLoading,
    errorMessage: query.error
      ? apiErrorMessage(query.error, '3D 모델 목록을 가져올 수 없습니다')
      : null,
  }
}

export function useProviderMutations() {
  const queryClient = useQueryClient()

  // 세 변이가 모두 같은 목록을 바꾼다. 무효화를 한 곳에 모아 빠뜨리지 않게 한다
  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.providers() })
  }

  const create = useMutation({
    mutationFn: (input: ProviderInput) => createProvider(input),
    onSuccess: invalidate,
  })

  const update = useMutation({
    mutationFn: ({ id, input }: { id: string; input: ProviderInput }) => updateProvider(id, input),
    onSuccess: invalidate,
  })

  const remove = useMutation({
    mutationFn: (id: string) => deleteProvider(id),
    onSuccess: invalidate,
  })

  // 연결 확인은 아무것도 바꾸지 않으므로 무효화하지 않는다
  const test = useMutation({
    mutationFn: (id: string) => testProvider(id),
  })

  return { create, update, remove, test }
}
