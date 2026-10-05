/**
 * Design Ref: §4.2 #2~#4 — 업로드 엔드포인트.
 *
 * 이 계층은 `domain/` 만 참조한다 (§9.2).
 */
import { apiRequest, apiUrl } from './client'

export interface UploadResult {
  id: string
  originalName: string
  contentType: string
  sizeBytes: number
}

export function uploadImage(file: File, signal?: AbortSignal): Promise<UploadResult> {
  const form = new FormData()
  form.append('file', file)

  return apiRequest<UploadResult>('/api/uploads', { method: 'POST', body: form, signal })
}

/**
 * 소스 이미지 URL. `<img src>` 가 직접 건다.
 *
 * 이 엔드포인트만 봉투를 쓰지 않는다 (§4.2 #4) — 바이너리를 JSON 에 담을 수 없다.
 * 그래서 fetch 로 감싸지 않고 URL 만 만들어 준다.
 */
export function sourceImageUrl(uploadId: string): string {
  return apiUrl(`/api/uploads/${uploadId}/content`)
}
