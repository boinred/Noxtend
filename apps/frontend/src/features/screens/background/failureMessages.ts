const TRANSIENT_PROVIDER_FAILURES = new Set(['HttpRequestException', 'PROVIDER_CALL_FAILED'])

const TRANSIENT_PROVIDER_MESSAGE =
  '외부 API와 보안 연결이 일시적으로 끊겼습니다. 잠시 후 다시 시도해 주세요.'

/**
 * 3D 실패 코드 → 사람이 읽는 말 (§11.5).
 *
 * **공급자 원문은 오지 않는다.** 어댑터가 코드만 남기므로 여기서 옮길 것은 우리가 정한
 * 코드뿐이고, 모르는 코드는 그대로 보여 준다 — 지어내는 것보다 낫다.
 */
const MESH_FAILURE_MESSAGES: Record<string, string> = {
  MESH_AUTH_FAILED: 'Tripo 인증에 실패했습니다. 관리자에서 키를 확인하세요.',
  MESH_CREDITS_INSUFFICIENT: 'Tripo 크레딧이 부족합니다.',
  MESH_INPUT_REJECTED: '입력 이미지를 3D 공급자가 받지 않았습니다.',
  MESH_CONTENT_REJECTED: '3D 공급자가 이 이미지의 내용을 거절했습니다.',
  MESH_MODEL_DEPRECATED: '선택한 3D 모델이 더 이상 제공되지 않습니다.',
  MESH_TOO_COMPLEX: '3D 로 만들기에 형상이 너무 복잡합니다.',
  MESH_RATE_LIMITED: '3D 공급자 요청이 몰려 있습니다. 잠시 후 다시 시도해 주세요.',
  MESH_PROVIDER_UNAVAILABLE: '3D 공급자에 연결하지 못했습니다.',
  MESH_SUBMISSION_UNKNOWN: '제출 결과를 확인할 수 없습니다. 운영자 확인이 필요합니다.',
  MESH_TASK_FAILED: '3D 제작이 실패했습니다.',
  MESH_TASK_CANCELED: '3D 제작이 취소되었습니다.',
  MESH_TIMEOUT: '3D 제작이 시간 안에 끝나지 않았습니다.',
  MESH_RESULT_EXPIRED: '3D 결과를 저장하기 전에 공급자 링크가 만료되었습니다.',
  MESH_RESULT_INVALID: '3D 결과 파일이 올바르지 않습니다.',
}

export function meshFailureMessage(reason: string | null | undefined): string {
  if (!reason) {
    return '3D 제작이 실패했습니다.'
  }

  return MESH_FAILURE_MESSAGES[reason] ?? reason
}

export function formatFailureReason(
  reason: string | null | undefined,
  fallback = '알 수 없는 오류',
): string {
  // 외부 공급자 전송 실패 안내 통합
  if (reason && TRANSIENT_PROVIDER_FAILURES.has(reason)) {
    return TRANSIENT_PROVIDER_MESSAGE
  }

  return reason ?? fallback
}
