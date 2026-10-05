/**
 * Design Ref: §9.1, §10.2 — 경로 문자열의 유일한 정의처.
 *
 * 직전 사이클에서는 `app/routes.tsx` 안에 상수·트리·화면 매핑이 함께 있었다.
 * 화면이 늘수록 그 파일이 모든 변경의 교차점이 되므로 상수만 떼어낸다.
 * 이 파일은 아무것도 import 하지 않는다 — 그래서 누구나 참조해도 순환이 생기지 않는다.
 */
export const ROUTES = {
  home: '/',
  character: '/character',
  object: '/object',
  background: '/background',
  /**
   * Design Ref: §5.2 · FR-13 — **URL 이 작업의 주소가 된다.**
   *
   * 10분 이상 걸리는 작업에서 사용자가 페이지를 떠나는 것은 예외가 아니라 정상이다.
   * 상태를 컴포넌트가 들고 있으면 새로고침 한 번에 진행 중이던 작업을 잃는다 (R-9).
   */
  backgroundJob: '/background/:jobId',
  /** character-studio §6.3 · FR-13 — 배경과 같은 이유로 URL 이 작업의 주소가 된다. */
  characterJob: '/character/:jobId',
  /**
   * 관리자.
   *
   * 사이클 #5 에서 화면이 1개 → 4개로 늘었다. 사이드바를 늘리지 않고 **섹션 탭**으로
   * 나눈다 (§2.3-6) — 사이드바는 제작 흐름(캐릭터·오브젝트·배경)의 자리이고
   * 관리자는 그 밖이다.
   *
   * `/admin` 은 공급자로 리다이렉트한다 — 기존 북마크가 깨지지 않게.
   */
  admin: '/admin',
  adminProviders: '/admin/providers',
  adminPrompts: '/admin/prompts',
  adminPromptEdit: '/admin/prompts/:kind',
  adminGolden: '/admin/golden',
  adminPrices: '/admin/prices',
  /** 사이클 #7 — `useJobCalls` 의 첫 소비자 (FR-18) */
  adminCalls: '/admin/calls',
  adminGoldenRuns: '/admin/golden/:sampleId',
} as const

/** 파라미터를 채운 실제 경로. 리터럴 조합을 화면에 두면 라우트를 바꿀 때 누락된다. */
export function backgroundJobPath(jobId: string): string {
  return `/background/${jobId}`
}

/** 캐릭터 작업의 주소 (character-studio §6.3 · FR-13). */
export function characterJobPath(jobId: string): string {
  return `/character/${jobId}`
}

/**
 * 작업의 주소를 카테고리로 고른다 (character-studio 독립 리뷰 #2).
 *
 * 홈 목록은 카테고리 무관하게 작업을 받으므로, 링크를 배경 경로로 고정하면 캐릭터 작업이
 * 배경 스튜디오로 열린다. 오브젝트는 아직 전용 화면이 없어 배경으로 폴백한다.
 */
export function jobPath(category: string, jobId: string): string {
  return category === 'character' ? characterJobPath(jobId) : backgroundJobPath(jobId)
}

/**
 * 공급자 설정 참조와 모델 id 한 쌍.
 *
 * 구조 타입으로 받는다 — 이 파일은 아무것도 import 하지 않으므로 `domain/job` 의
 * `ModelSelection` 을 끌어올 수 없다. 모양이 같으면 그대로 들어온다.
 */
interface SelectionParam {
  providerConfigId: string
  model: string
}

/** 입력 화면이 이어받을 선택 — 고르지 않은 쪽은 `null`. */
export interface CarriedSelection {
  text: SelectionParam | null
  image: SelectionParam | null
  /** 3D 선택 (사이클 #10). 고르지 않았으면 null 이다. */
  mesh: SelectionParam | null
  requiresReview?: boolean | null
}

// 쿼리 키. 쓰는 쪽과 읽는 쪽이 같은 상수를 보게 해서 한쪽만 바뀌는 것을 막는다
const KEYS = {
  image: 'image',
  textProvider: 'provider',
  textModel: 'model',
  imageProvider: 'imageProvider',
  imageModel: 'imageModel',
  meshProvider: 'meshProvider',
  meshModel: 'meshModel',
  review: 'review',
  // 캐릭터 고유 이어받기 값 (character-mesh-ui §FR-08)
  gender: 'gender',
  partHints: 'partHints',
} as const

/** 파츠 힌트 한 줄 — 구조 타입으로 받는다(이 파일은 아무것도 import 하지 않는다, 위 SelectionParam 과 같은 이유). */
interface CarriedPartHint {
  type: string
  count: number
  variant?: string
}

/** 캐릭터 입력 화면이 이어받을 선택 — 배경의 세 모델 선택 + 성별·파츠 힌트. */
export interface CarriedCharacterSelection extends CarriedSelection {
  gender: 'male' | 'female' | null
  partHints: CarriedPartHint[] | null
}

/**
 * 이미지와 모델 선택을 이어받는 입력 화면.
 *
 * **왜 URL 인가.** 컴포넌트 상태로 나르면 새로고침 한 번에 사라지고, 사용자는 왜
 * 이미지가 없어졌는지 알 수 없다. `backgroundJob` 이 같은 이유로 URL 을 쓴다 (FR-13) —
 * 화면은 상태 머신이 아니라 URL 의 함수다.
 *
 * **모델도 같은 이유로 여기 싣는다.** 같은 이미지로 모델만 바꿔 돌리는 것이 튜닝의
 * 기본 동작인데, 이어받지 않으면 다시 시도가 목록 첫 항목으로 되돌아가 비교의 통제
 * 변수가 깨진다.
 */
export function backgroundWithImagePath(
  storedImageId: string,
  carried?: Partial<CarriedSelection>,
): string {
  const params = new URLSearchParams({ [KEYS.image]: storedImageId })

  // 모델 id 는 공급자가 정하는 문자열이라 그대로 이어붙이면 `&` 하나에 쿼리가 끊긴다
  if (carried?.text) {
    params.set(KEYS.textProvider, carried.text.providerConfigId)
    params.set(KEYS.textModel, carried.text.model)
  }

  if (carried?.image) {
    params.set(KEYS.imageProvider, carried.image.providerConfigId)
    params.set(KEYS.imageModel, carried.image.model)
  }

  if (carried?.mesh) {
    params.set(KEYS.meshProvider, carried.mesh.providerConfigId)
    params.set(KEYS.meshModel, carried.mesh.model)
  }

  if (carried?.requiresReview !== undefined && carried?.requiresReview !== null) {
    params.set(KEYS.review, carried.requiresReview ? '1' : '0')
  }

  return `/background?${params.toString()}`
}

/** 쿼리에서 이어받은 선택을 읽는다. 짝이 맞지 않으면 그 쪽은 없는 것으로 본다. */
export function readModelSelection(params: URLSearchParams): CarriedSelection {
  const reviewRaw = params.get(KEYS.review)
  const requiresReview = reviewRaw === '1' ? true : reviewRaw === '0' ? false : null

  return {
    text: pair(params.get(KEYS.textProvider), params.get(KEYS.textModel)),
    image: pair(params.get(KEYS.imageProvider), params.get(KEYS.imageModel)),
    mesh: pair(params.get(KEYS.meshProvider), params.get(KEYS.meshModel)),
    requiresReview,
  }
}

/**
 * 이미지·모델·성별·파츠 힌트를 이어받는 캐릭터 입력 화면 (character-mesh-ui §FR-08).
 *
 * `backgroundWithImagePath` 와 같은 이유(모델이 목록 첫 항목으로 되돌아가면 통제 변수가
 * 깨진다) 를 성별·파츠 힌트에도 적용한다 — 캐릭터는 이 둘도 재시도마다 다시 고르게 하면
 * 안 되는 값이다. `gender`·`partHints` 를 뺀 나머지는 배경과 같은 쿼리 키를 쓴다.
 */
export function characterWithImagePath(
  storedImageId: string,
  carried?: Partial<CarriedCharacterSelection>,
): string {
  const params = new URLSearchParams({ [KEYS.image]: storedImageId })

  if (carried?.text) {
    params.set(KEYS.textProvider, carried.text.providerConfigId)
    params.set(KEYS.textModel, carried.text.model)
  }

  if (carried?.image) {
    params.set(KEYS.imageProvider, carried.image.providerConfigId)
    params.set(KEYS.imageModel, carried.image.model)
  }

  if (carried?.mesh) {
    params.set(KEYS.meshProvider, carried.mesh.providerConfigId)
    params.set(KEYS.meshModel, carried.mesh.model)
  }

  if (carried?.requiresReview !== undefined && carried?.requiresReview !== null) {
    params.set(KEYS.review, carried.requiresReview ? '1' : '0')
  }

  if (carried?.gender) {
    params.set(KEYS.gender, carried.gender)
  }

  // 빈 배열은 "힌트 없이 접수했다" 와 "아직 안 읽었다" 를 구분 못 하게 만든다 — 안 싣는다
  if (carried?.partHints && carried.partHints.length > 0) {
    params.set(KEYS.partHints, JSON.stringify(carried.partHints))
  }

  return `/character?${params.toString()}`
}

/**
 * 쿼리에서 캐릭터 이어받기 값을 읽는다.
 *
 * 손으로 조작한 URL 도 들어올 수 있으므로 `gender` 는 두 값 중 하나가 아니면, `partHints`
 * 는 JSON 파싱이 실패하면 각각 `null` 로 버린다 — 잘못된 값을 입력 화면에 그대로 넘기면
 * 렌더가 아니라 접수 시점에야 오류가 난다.
 */
export function readCharacterSelection(params: URLSearchParams): CarriedCharacterSelection {
  const genderRaw = params.get(KEYS.gender)
  const gender = genderRaw === 'male' || genderRaw === 'female' ? genderRaw : null

  const partHintsRaw = params.get(KEYS.partHints)
  const partHints = parsePartHints(partHintsRaw)

  return { ...readModelSelection(params), gender, partHints }
}

function parsePartHints(raw: string | null): CarriedPartHint[] | null {
  if (raw === null) {
    return null
  }

  try {
    const parsed: unknown = JSON.parse(raw)
    return Array.isArray(parsed) ? (parsed as CarriedPartHint[]) : null
  } catch {
    return null
  }
}

/**
 * 공급자와 모델은 짝이다 — 둘 다 있거나 둘 다 없다 (`PipelineTask` 의 같은 규칙).
 *
 * 반쪽만 채우면 사용자가 고른 적 없는 조합이 폼에 남고, 접수에서 거절된다.
 */
function pair(providerConfigId: string | null, model: string | null): SelectionParam | null {
  return providerConfigId && model ? { providerConfigId, model } : null
}

// category 를 쿼리로 싣는다 — 라우트는 :kind 그대로 두고 서버 `?category=` 와 표기를 맞춘다
export function adminPromptEditPath(kind: string, category?: string | null): string {
  return category ? `/admin/prompts/${kind}?category=${category}` : `/admin/prompts/${kind}`
}

export function adminGoldenRunsPath(sampleId: string): string {
  return `/admin/golden/${sampleId}`
}

export type RouteKey = keyof typeof ROUTES
export type RoutePath = (typeof ROUTES)[RouteKey]

export const ROUTE_PATHS: readonly string[] = Object.values(ROUTES)
