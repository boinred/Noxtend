/**
 * Design Ref: §3.4 — 업로드 검증.
 *
 * **같은 규칙이 서버에도 있다** (`Noxtend.Domain/Validation/UploadRules.cs`).
 * 여기는 즉시 피드백용이고 **서버가 정본**이다 — 클라이언트를 신뢰하지 않는다 (§2.2).
 * 두 벌인 것이 중복이 아니라 역할 분담인 이유가 그것이다.
 */

export const ALLOWED_IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/webp'] as const

export const MAX_IMAGE_BYTES = 12 * 1024 * 1024

export type UploadRejection = 'UNSUPPORTED_TYPE' | 'TOO_LARGE' | 'EMPTY'

export interface UploadCandidate {
  type: string
  size: number
}

/** 위반이 없으면 `null`. */
export function validateUpload(file: UploadCandidate): UploadRejection | null {
  // 빈 파일을 먼저 본다 — 형식이 맞아도 보낼 내용이 없다 (서버와 같은 순서)
  if (file.size <= 0) return 'EMPTY'

  const allowed = ALLOWED_IMAGE_TYPES.some(
    (type) => type.toLowerCase() === file.type?.toLowerCase(),
  )
  if (!allowed) return 'UNSUPPORTED_TYPE'

  return file.size > MAX_IMAGE_BYTES ? 'TOO_LARGE' : null
}

const REJECTION_MESSAGES: Record<UploadRejection, string> = {
  EMPTY: '빈 파일입니다',
  UNSUPPORTED_TYPE: 'PNG · JPG · WEBP 만 올릴 수 있습니다',
  TOO_LARGE: `최대 ${MAX_IMAGE_BYTES / 1024 / 1024} MB 까지 올릴 수 있습니다`,
}

export function uploadRejectionMessage(rejection: UploadRejection): string {
  return REJECTION_MESSAGES[rejection]
}

/** 드롭존 안내 문구. 허용 형식이 늘면 안내도 함께 는다. */
export const UPLOAD_HINT = `PNG · JPG · WEBP / 최대 ${MAX_IMAGE_BYTES / 1024 / 1024} MB`
