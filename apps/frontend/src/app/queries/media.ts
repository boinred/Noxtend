/**
 * Design Ref: §9.2 — 화면이 서버 자원의 주소를 얻는 통로.
 *
 * 쿼리는 아니지만 여기에 둔다. 소스 이미지 URL 은 `infra/api` 가 아는 base URL 에
 * 의존하므로 도메인에 둘 수 없고, 화면이 `infra/api` 를 직접 참조하면 계층 규칙이
 * "네트워크 호출만 아니면 된다" 로 흐려진다 — 규칙의 경계는 호출 종류가 아니라 방향이다.
 */
export { sourceImageUrl } from '@/infra/api/uploadApi'

/**
 * 생성된 파츠 이미지의 주소 (사이클 #7).
 *
 * 같은 이유로 여기를 지난다 — 화면이 `infra/api` 를 직접 참조하면 계층 규칙이
 * "네트워크 호출만 아니면 된다" 로 흐려진다.
 */
export {
  generatedImageUrl,
  meshDownloadUrl,
  meshFbxDownloadUrl,
  meshPreviewUrl,
} from '@/infra/api/jobApi'
