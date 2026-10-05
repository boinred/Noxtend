/**
 * Design Ref: §4.2 #9 — 공급자 설정. 순수 TS.
 *
 * **평문 키를 담는 필드가 없다.** 서버가 마스킹만 내려보내므로 (§7) 타입에도 그 자리가
 * 없어야 한다. 필드를 만들어 두면 언젠가 채우려는 코드가 생긴다.
 */

export type ProviderKind = 'openai' | 'anthropic' | 'google' | 'tripo' | 'meshy'
export type ProviderCapability =
  | 'textAnalysis'
  | 'imageGeneration'
  /** 4방향 이미지에서 3D mesh 를 만든다 (사이클 #10). */
  | 'meshGeneration'
  /** 원본·렌더 두 이미지를 비교해 구조화 점수를 낸다 (background-similarity-tuning §7.2). */
  | 'similarityEvaluation'

/**
 * **모델 필드가 없다.** 공급자는 접속 수단이고, 어떤 모델을 부를지는 실행할 때 고른다
 * (`PipelineTask.Model`). 여기에 두면 "공급자 하나 = 모델 하나" 가 되어 같은 키로
 * 두 모델을 쓰려면 공급자를 두 번 등록해야 한다.
 */
export interface Provider {
  id: string
  displayName: string
  kind: ProviderKind
  /** App features backed by this connection, distinct from the vendor's full catalog. */
  capabilities: ProviderCapability[]
  /** `••••••••4f2c` — 되읽을 수 없다. */
  apiKeyMasked: string
  isEnabled: boolean
}

/** 등록·수정 입력. 수정 시 `apiKey` 를 비우면 기존 키가 유지된다 (§4.2 #11). */
export interface ProviderInput {
  displayName: string
  kind: ProviderKind
  apiKey?: string
  isEnabled?: boolean
}

/**
 * 스튜디오가 고를 수 있는 모델 하나 (§4.2 #14).
 *
 * 서버가 이미 걸러서 준다 — 추출이 요구하는 이미지 입력·구조화 출력을 갖춘 모델만 온다.
 * `id` 를 보내고 `displayName` 을 보여준다.
 */
export interface ProviderModel {
  id: string
  displayName: string
}

export const PROVIDER_KINDS: readonly ProviderKind[] = [
  'openai',
  'anthropic',
  'google',
  'tripo',
  'meshy',
]

const KIND_LABELS: Record<ProviderKind, string> = {
  anthropic: 'Anthropic',
  openai: 'OpenAI',
  google: 'Google Gemini',
  tripo: 'Tripo',
  meshy: 'Meshy',
}

/** 종류별 사용 용도 — 서버가 준다. 프론트가 같은 표를 들면 백엔드와 조용히 어긋난다. */
export interface ProviderKindCapabilities {
  kind: ProviderKind
  capabilities: ProviderCapability[]
}

const CAPABILITY_LABELS: Record<ProviderCapability, string> = {
  textAnalysis: '텍스트 분석',
  imageGeneration: '이미지 생성',
  meshGeneration: '3D 제작',
  similarityEvaluation: '유사도 평가',
}

export function providerKindLabel(kind: ProviderKind): string {
  return KIND_LABELS[kind]
}

/**
 * 종류별 능력 목록에서 하나를 고른다.
 *
 * **표를 여기 두지 않는 이유**: `ProviderCapabilities.For` 가 백엔드의 유일한 출처인데
 * 프론트가 사본을 들면 둘이 어긋난다. Google 이 텍스트·이미지 둘 다 하게 된 것도
 * (gemini-text-provider, 2026-08-28) 이 원칙 덕에 프론트 변경 없이 반영됐다.
 */
export function providerCapabilitiesFor(
  table: readonly ProviderKindCapabilities[],
  kind: ProviderKind,
): readonly ProviderCapability[] {
  return table.find((entry) => entry.kind === kind)?.capabilities ?? []
}

export function providerCapabilityLabel(capability: ProviderCapability): string {
  return CAPABILITY_LABELS[capability]
}

export function providerSupports(provider: Provider, capability: ProviderCapability): boolean {
  return provider.capabilities.includes(capability)
}
