/**
 * Design Ref: §9.2 · §6 — 오류를 화면이 쓸 수 있는 문구로 바꾼다.
 *
 * 화면이 `ApiError` 를 `instanceof` 로 판별하던 것을 여기로 옮겼다. 그편이 계층
 * 규칙에 맞을 뿐 아니라 **화면이 전송 계층의 타입을 알 이유가 없기** 때문이다 —
 * 필요한 것은 "무엇을 보여줄 문구인가" 하나다.
 */
import { ApiError } from '@/infra/api/client'

/** 서버·네트워크 실패를 사용자에게 보일 한 줄로. 모르는 오류는 호출자가 준 문구를 쓴다. */
export function apiErrorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiError ? error.message : fallback
}

/** 서버가 준 오류 코드. 화면이 오류 종류에 따라 다르게 반응해야 할 때만 쓴다. */
export function apiErrorCode(error: unknown): string | null {
  return error instanceof ApiError ? error.code : null
}

/** 서버에 닿지 못했다 — 빈 상태로 보여줄지 오류로 보여줄지 가르는 판정 (§6). */
export function isUnreachable(error: unknown): boolean {
  return error instanceof ApiError && error.isUnreachable
}

/** HTTP 충돌과 응답 유실 재전송 구분 */
export function apiErrorStatus(error: unknown): number | null {
  return error instanceof ApiError ? error.status : null
}
