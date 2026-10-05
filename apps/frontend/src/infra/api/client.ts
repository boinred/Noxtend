/**
 * Design Ref: §4.0 · §6 · §9.2 — 봉투 해석과 오류 정규화의 유일한 지점.
 *
 * 이 계층은 `domain/` 만 참조한다. `features/`·`store/` 를 알면 화면이 API 를 직접 부를
 * 길이 생기고, 캐시·폴링·무효화가 흩어진다.
 *
 * **API 가 없어도 던지되, 던지는 모양이 항상 같다.** §6 의 "API 자체가 없음 → 빈 상태로
 * 표시" 는 훅이 담당하지만, 그러려면 네트워크 실패와 서버 오류가 같은 타입이어야 한다.
 */

/** Design Ref: §4.0 — { data, error } */
interface ApiEnvelope<T> {
  data: T | null
  error: ApiErrorBody | null
}

interface ApiErrorBody {
  code: string
  message: string
  fields?: Record<string, string> | null
}

/**
 * 정규화된 오류. 화면은 `code` 로 분기하고 `message` 를 보여준다.
 *
 * 네트워크 실패도 이 타입이다 — 호출부가 `instanceof` 두 가지를 구분할 이유가 없다.
 */
export class ApiError extends Error {
  constructor(
    readonly code: string,
    message: string,
    readonly status: number,
    readonly fields?: Record<string, string> | null,
  ) {
    super(message)
    this.name = 'ApiError'
  }

  /** 서버에 닿지 못했다. 백엔드 미기동도 여기로 들어온다. */
  get isUnreachable(): boolean {
    return this.status === 0
  }
}

export const API_ERROR_CODES = {
  networkUnreachable: 'NETWORK_UNREACHABLE',
  malformedResponse: 'MALFORMED_RESPONSE',
} as const

/**
 * 개발 서버는 다른 오리진이므로 절대 URL 이 필요하고, 배포에서는 같은 오리진이라
 * 상대 경로가 맞다. 빌드 시점 값 하나로 둘을 가른다.
 */
const BASE_URL: string = import.meta.env.VITE_API_BASE_URL ?? ''

export function apiUrl(path: string): string {
  return `${BASE_URL}${path}`
}

export interface RequestOptions {
  method?: string
  body?: unknown
  signal?: AbortSignal
  /** 추가 헤더 — Idempotency-Key 같은 계약 헤더 (background-similarity-tuning §10.1). */
  headers?: Record<string, string>
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, signal, headers } = options

  const isFormData = body instanceof FormData

  let response: Response
  try {
    response = await fetch(apiUrl(path), {
      method,
      // FormData 는 브라우저가 boundary 를 포함해 Content-Type 을 정한다.
      // 직접 지정하면 boundary 가 빠져 서버가 본문을 파싱하지 못한다
      // FormData 는 브라우저가 Content-Type(boundary 포함)을 정한다 — 빈 headers 라도
      // 넘기면 일부 환경이 이를 "지정됨" 으로 본다. 없으면 undefined 를 유지한다
      headers: mergeHeaders(
        isFormData || body === undefined ? undefined : { 'Content-Type': 'application/json' },
        headers,
      ),
      body: isFormData ? body : body === undefined ? undefined : JSON.stringify(body),
      signal,
    })
  } catch (cause) {
    // 취소는 오류가 아니다. 그대로 올려야 훅이 상태를 덮어쓰지 않는다
    if (cause instanceof DOMException && cause.name === 'AbortError') throw cause

    throw new ApiError(API_ERROR_CODES.networkUnreachable, '서버에 연결할 수 없습니다', 0)
  }

  return parseEnvelope<T>(response)
}

function mergeHeaders(
  base: Record<string, string> | undefined,
  extra: Record<string, string> | undefined,
): Record<string, string> | undefined {
  if (!base && !extra) return undefined
  return { ...base, ...extra }
}

/** 본문이 없는 성공(204)도 있다. Design Ref: §4.2 #12 */
export async function apiRequestVoid(path: string, options: RequestOptions = {}): Promise<void> {
  await apiRequest<unknown>(path, options)
}

async function parseEnvelope<T>(response: Response): Promise<T> {
  if (response.status === 204) {
    return undefined as T
  }

  let envelope: ApiEnvelope<T>
  try {
    envelope = (await response.json()) as ApiEnvelope<T>
  } catch {
    // 봉투가 아닌 응답 — 프록시 오류 페이지나 게이트웨이 타임아웃이 여기로 온다
    throw new ApiError(
      API_ERROR_CODES.malformedResponse,
      '서버 응답을 해석할 수 없습니다',
      response.status,
    )
  }

  if (!response.ok || envelope.error) {
    const error = envelope.error
    throw new ApiError(
      error?.code ?? API_ERROR_CODES.malformedResponse,
      error?.message ?? '요청을 처리하지 못했습니다',
      response.status,
      error?.fields,
    )
  }

  return envelope.data as T
}
