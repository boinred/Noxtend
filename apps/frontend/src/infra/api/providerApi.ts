/**
 * Design Ref: §4.2 #9~#13 — 공급자 엔드포인트.
 *
 * **응답에 평문 키가 없다.** 요청에는 있을 수 있지만 (등록·교체) 되돌아오지 않는다 (§7).
 */
import { apiRequest, apiRequestVoid } from './client'
import type {
  Provider,
  ProviderInput,
  ProviderKindCapabilities,
  ProviderModel,
} from '@/domain/provider/types'

/**
 * **서버 값을 그대로 쓴다.** 종류 표기 보정도, 능력 재구성도 여기서 하지 않는다 —
 * 클라이언트가 서버의 사실을 다시 만들면 백엔드가 바뀔 때 조용히 어긋나고,
 * 계약이 깨진 것을 아무도 모르게 된다.
 */
export function listProviders(signal?: AbortSignal): Promise<Provider[]> {
  return apiRequest<Provider[]>('/api/providers', { signal })
}

export function createProvider(input: ProviderInput, signal?: AbortSignal): Promise<Provider> {
  return apiRequest<Provider>('/api/providers', { method: 'POST', body: input, signal })
}

/**
 * Design Ref: §4.2 #11 — `apiKey` 를 보내지 않으면 기존 키가 유지된다.
 * 빈 문자열도 보내지 않는다 — 서버가 "바꾸지 않음" 으로 다루지만, 의도를 여기서도 분명히 한다.
 */
export function updateProvider(
  id: string,
  input: ProviderInput,
  signal?: AbortSignal,
): Promise<Provider> {
  const body = {
    ...input,
    apiKey: input.apiKey?.trim() ? input.apiKey : undefined,
  }

  return apiRequest<Provider>(`/api/providers/${id}`, { method: 'PUT', body, signal })
}

export function deleteProvider(id: string, signal?: AbortSignal): Promise<void> {
  return apiRequestVoid(`/api/providers/${id}`, { method: 'DELETE', signal })
}

/**
 * Design Ref: §4.2 #14 — 고를 수 있는 모델 목록.
 *
 * 실패하면 던진다. 빈 배열로 흡수하지 않는다 — 화면이 "연결 확인부터 하세요" 를
 * 안내해야 하고, 그 판단은 오류 코드에 달렸다 (`PROVIDER_NO_VISION_MODELS` 인가
 * `PROVIDER_CALL_FAILED` 인가).
 */
export function listProviderModels(id: string, signal?: AbortSignal): Promise<ProviderModel[]> {
  return apiRequest<ProviderModel[]>(`/api/providers/${id}/models`, { signal })
}

/**
 * 이미지 생성 모델 목록 (사이클 #7 §4.2 #7).
 *
 * **텍스트 목록과 경로가 다르다.** 한 목록에 섞으면 사용자가 텍스트 단계에 이미지
 * 모델을 고를 수 있고, 그 오류는 접수가 아니라 실행 시점에야 드러난다.
 */
export function listProviderImageModels(
  id: string,
  signal?: AbortSignal,
): Promise<ProviderModel[]> {
  return apiRequest<ProviderModel[]>(`/api/providers/${id}/image-models`, { signal })
}

/**
 * 3D 생성 모델 목록 (사이클 #10).
 *
 * 목록은 검증된 스냅숏 고정이지만 호출은 실제로 나간다 — 키가 살아 있는지 확인하는
 * 것이 목적이고, 잔액 조회라 크레딧을 쓰지 않는다.
 */
export function listProviderMeshModels(id: string, signal?: AbortSignal): Promise<ProviderModel[]> {
  return apiRequest<ProviderModel[]>(`/api/providers/${id}/mesh-models`, { signal })
}

/**
 * 종류별 사용 용도 (등록 전에 읽는다).
 *
 * 등록된 공급자의 능력은 `Provider.capabilities` 에 이미 실려 온다. 이 조회는
 * **아직 만들지 않은 종류**를 고르는 관리자 폼을 위한 것이다.
 */
export function listProviderCapabilities(
  signal?: AbortSignal,
): Promise<ProviderKindCapabilities[]> {
  return apiRequest<ProviderKindCapabilities[]>('/api/providers/capabilities', { signal })
}

export interface ProviderTestResult {
  ok: boolean
  latencyMs: number
  /** 공급자가 지원하지 않는 기능은 null이다. */
  textModelCount: number | null
  imageModelCount: number | null
  meshModelCount: number | null
  /**
   * 3D 공급자의 남은 크레딧.
   *
   * **못 읽었으면 null 이다 — 0 과 다르다.** 0 은 "다 썼다" 이고 null 은 "모른다" 다.
   */
  meshCreditBalance: number | null
}

export function testProvider(id: string, signal?: AbortSignal): Promise<ProviderTestResult> {
  return apiRequest<ProviderTestResult>(`/api/providers/${id}/test`, { method: 'POST', signal })
}
