/**
 * 후보 생성·렌더 업로드의 통로 (background-similarity-tuning §9.2).
 *
 * media.ts 와 같은 이유로 여기를 지난다 — 화면이 `infra/api` 를 직접 참조하면
 * 계층 규칙이 "네트워크 호출만 아니면 된다" 로 흐려진다. 명령형 흐름(생성 → 캡처 →
 * 업로드)이라 훅이 아니라 함수로 낸다.
 */
export {
  cancelSimilarityRun,
  completeSimilarityRun,
  createSimilarityCandidate,
  getSimilarityRun,
  listSceneRevisions,
  restoreSceneRevision,
  retrySimilarityRun,
  uploadCandidateRender,
} from '@/infra/api/similarityApi'
