/**
 * Design Ref: §9.2 — 화면은 `app/queries` 만 부른다.
 *
 * 업로드가 이 층에 없던 동안 `BackgroundStudioScreen` 이 `infra/api` 를 직접 불렀고,
 * 그래서 **업로드만 캐시·무효화 밖에 있었다** (Check 단계 G-5). 변이가 아무것도
 * 무효화하지 않더라도 경계를 지나는 통로는 하나여야 한다 — 예외를 하나 두면
 * 다음 화면이 그것을 선례로 삼는다.
 */
import { useMutation } from '@tanstack/react-query'
import { uploadImage } from '@/infra/api/uploadApi'

export function useUpload() {
  return useMutation({
    mutationFn: (file: File) => uploadImage(file),
  })
}
