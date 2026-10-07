/**
 * Design Ref: §9.2 — 쿼리 키의 유일한 정의처.
 *
 * 무효화가 키를 문자열로 재구성하는 순간, 한 곳을 고치면 다른 곳이 조용히 안 맞는다.
 */
import type { JobListFilter } from '@/infra/api/jobApi'
import type { ProductionMode } from '@/domain/sprites/types'
import type { AssetCategory } from '@/domain/job/types'

export const queryKeys = {
  job: (jobId: string) => ['job', jobId] as const,
  jobList: (filter: JobListFilter, limit = 10, productionMode?: ProductionMode) =>
    ['jobs', filter, limit, productionMode] as const,
  /** 두 섹션을 한꺼번에 무효화할 때 쓴다 — 작업이 끝나면 active 와 terminal 이 함께 바뀐다 */
  jobLists: () => ['jobs'] as const,
  /** 완성 GLB 수를 키에 싣는다 — 3D 가 도착하면 키가 바뀌어 낡은 명세를 다시 받는다 */
  sceneLayout: (jobId: string, meshCount: number) => ['scene-layout', jobId, meshCount] as const,
  providers: () => ['providers'] as const,
  /** 종류별 사용 용도 — 배포 사이에 바뀌지 않는 정적 표다 */
  providerCapabilities: () => ['providers', 'capabilities'] as const,
  /** 공급자별로 다른 목록이다 — 키에 id 가 없으면 공급자를 바꿔도 이전 모델이 남는다 */
  providerModels: (providerId: string) => ['providers', providerId, 'models'] as const,
  /** 이미지 모델은 별도 목록이다 (사이클 #7) — 텍스트 키와 섞이면 드롭다운이 뒤바뀐다 */
  providerImageModels: (providerId: string) => ['providers', providerId, 'image-models'] as const,
  providerMeshModels: (providerId: string) => ['providers', providerId, 'mesh-models'] as const,

  // ─── 튜닝 (사이클 #5) ───
  prompts: () => ['prompts'] as const,
  /** (단계 × 카테고리) 유효 활성 격자 — 활성 목록 화면 */
  promptGrid: () => ['prompts', 'grid'] as const,
  /**
   * 단계·카테고리별로 다른 이력이다.
   *
   * **카테고리를 키에 넣지 않으면 캐시가 섞인다** (prompt-category-axis §12.1) —
   * 캐릭터 이력을 본 뒤 기본 이력을 열면 캐릭터 행이 남는다.
   */
  promptVersions: (kind: string, category: AssetCategory | null) =>
    ['prompts', kind, 'versions', category ?? 'default'] as const,
  golden: () => ['golden'] as const,
  goldenRuns: (sampleId: string) => ['golden', sampleId, 'runs'] as const,
  jobCalls: (jobId: string) => ['job', jobId, 'calls'] as const,

  // ─── 유사도 (background-similarity-tuning) ───
  similarityStatus: (jobId: string) => ['similarity', jobId, 'status'] as const,
  similarityRun: (jobId: string, runId: string) => ['similarity', jobId, 'run', runId] as const,
  similarityEstimate: (jobId: string, model: string, maxIterations: number) =>
    ['similarity', jobId, 'estimate', model, maxIterations] as const,
  /** review-gate — 검수 대기 상태·파츠 목록. */
  review: (jobId: string) => ['job', jobId, 'review'] as const,
  callStats: () => ['calls', 'stats'] as const,
  prices: () => ['prices'] as const,
} as const
