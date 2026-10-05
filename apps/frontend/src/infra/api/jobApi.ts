/**
 * Design Ref: §4.2 #5~#8 — 작업 엔드포인트.
 */
import { apiRequest, apiUrl } from './client'
import type {
  SceneLayoutData,
  AssetCategory,
  CharacterGender,
  Job,
  JobSummary,
  PartHint,
  ViewDirection,
} from '@/domain/job/types'

export interface StartJobInput {
  category: AssetCategory
  uploadId: string
  providerConfigId: string
  /** 공급자가 아니라 작업이 모델을 정한다 (§4.2 #5). 목록에서 고른 id. */
  model: string
  /**
   * 캐릭터 고유 입력 (character-studio §5). **선택이다** — 캐릭터만 필수이고 그 규칙은
   * 서버 접수 검증이 지킨다. 타 카테고리는 보내지 않는다.
   */
  gender?: CharacterGender
  partHints?: PartHint[]
  /**
   * 파츠를 그릴 공급자·모델 (사이클 #7 §4.2 #1).
   *
   * 텍스트 모델과 **다른 목록**에서 고른다 — 한 목록에 섞으면 텍스트 단계에 이미지
   * 모델을 고를 수 있고, 그 오류는 실행 시점에야 드러난다.
   */
  imageProviderConfigId?: string
  imageModel?: string
  /** 3D 는 선택이다 — 비우면 이미지까지만 도는 기존 작업이 된다 (사이클 #10). */
  meshProviderConfigId?: string
  meshModel?: string
  /**
   * 검수 게이트 opt-in (review-gate §목표). true 면 분해 직후 자동 팬아웃 대신
   * 검수 대기(`pendingReview`)로 멈추고, 전체 승인으로만 진행한다. 전 카테고리 공통.
   */
  requiresReview?: boolean
}

export interface JobAccepted {
  id: string
  status: string
}

export function startJob(input: StartJobInput, signal?: AbortSignal): Promise<JobAccepted> {
  return apiRequest<JobAccepted>('/api/jobs', { method: 'POST', body: input, signal })
}

export function getJob(jobId: string, signal?: AbortSignal): Promise<Job> {
  return apiRequest<Job>(`/api/jobs/${jobId}`, { signal })
}

export type JobListFilter = 'active' | 'terminal'

interface JobListEnvelope {
  items: JobSummary[]
  /** 조건에 맞는 전체 건수 — `items` 는 limit 까지만 담는다 */
  total?: number
}

/** 한 쪽과 그 조건의 전체 건수. 둘이 다를 수 있다는 것이 이 형태의 이유다. */
export interface JobListPage {
  items: JobSummary[]
  total?: number
}

/** Design Ref: §4.2 #7 — 홈 두 섹션. */
export async function listJobs(
  filter: JobListFilter,
  limit = 10,
  signal?: AbortSignal,
): Promise<JobListPage> {
  const result = await apiRequest<JobListEnvelope>(`/api/jobs?status=${filter}&limit=${limit}`, {
    signal,
  })

  // 옛 서버는 total 을 안 보낸다 — 그때는 배지가 보이는 수만 쓴다
  return { items: result.items, total: result.total }
}

export function cancelJob(jobId: string, signal?: AbortSignal): Promise<JobAccepted> {
  return apiRequest<JobAccepted>(`/api/jobs/${jobId}/cancel`, { method: 'POST', signal })
}

/**
 * 끝난 작업을 목록에서 지운다.
 *
 * **돌려주는 값이 없다.** 서버가 204 로 답하므로 읽을 본문이 없고, `boolean` 을 두면
 * 늘 `true` 인 값을 호출부가 확인하게 된다 — 그 자리는 값이 아니라 잡음이다.
 * 실패는 예외로 온다.
 */
export function deleteJob(jobId: string, signal?: AbortSignal): Promise<void> {
  return apiRequest<void>(`/api/jobs/${jobId}`, { method: 'DELETE', signal })
}

/**
 * 실패한 파츠 방향 생성 공정 하나를 다시 돌린다 (사이클 #7 §4.2 #3 · FR-08).
 *
 * **공정 단위 어휘다** (C-5). 재시도 범위는 파츠 전체가 아니라 실패한 방향 하나다.
 */
export function retryTask(
  jobId: string,
  taskId: string,
  signal?: AbortSignal,
): Promise<JobAccepted> {
  return apiRequest<JobAccepted>(`/api/jobs/${jobId}/tasks/${taskId}/retry`, {
    method: 'POST',
    signal,
  })
}

export function generateSelectedViews(
  jobId: string,
  directions: ViewDirection[],
  signal?: AbortSignal,
): Promise<JobAccepted> {
  return apiRequest<JobAccepted>(`/api/jobs/${jobId}/generate-views`, {
    method: 'POST',
    body: { directions },
    signal,
  })
}

export function returnToDescriptions(
  jobId: string,
  partId: string,
  signal?: AbortSignal,
): Promise<JobAccepted> {
  return apiRequest<JobAccepted>(`/api/jobs/${jobId}/return-to-descriptions`, {
    method: 'POST',
    body: { partId },
    signal,
  })
}

/**
 * 끝난 작업에 3D 를 뒤늦게 붙인다 (사이클 #11).
 *
 * **이미지를 다시 만들지 않는다.** 이미 있는 4방향 이미지를 그대로 입력으로 쓰므로,
 * 전체 재실행이 다시 내는 $1.34 대신 파츠 수만큼의 3D 크레딧만 나간다.
 */
export function addMeshProduction(
  jobId: string,
  meshProviderConfigId: string,
  meshModel: string,
  signal?: AbortSignal,
): Promise<JobAccepted> {
  return apiRequest<JobAccepted>(`/api/jobs/${jobId}/mesh`, {
    method: 'POST',
    body: { meshProviderConfigId, meshModel },
    signal,
  })
}

/** 좌우 축의 3D 전송 선택 (spec 20260917). */
export type LeftRightPlan =
  'skip' | 'left' | 'right' | 'both' | 'mirrorFromLeft' | 'mirrorFromRight'

/** 전후 축의 3D 전송 선택 (spec 20260917). */
export type BackPlan = 'skip' | 'include' | 'mirrorFromFront'

/**
 * 파츠별 "3D 전송 뷰 자유 선택 + 대칭" (spec 20260917).
 *
 * **합성 이미지 생성은 이 요청 안에서 서버가 처리한다.** 프론트는 방향(enum 이름)만
 * 보내고 이미지 ID를 전혀 몰라도 된다 — 서버가 그 시점 최신 이미지를 다시 조회한다.
 */
export function replanPartMesh(
  jobId: string,
  partId: string,
  meshProviderConfigId: string,
  meshModel: string,
  leftRight: LeftRightPlan,
  back: BackPlan,
  signal?: AbortSignal,
): Promise<JobAccepted> {
  return apiRequest<JobAccepted>(`/api/jobs/${jobId}/replan-mesh`, {
    method: 'POST',
    body: { partId, meshProviderConfigId, meshModel, leftRight, back },
    signal,
  })
}

/**
 * 생성 이미지의 주소 (사이클 #7 §4.2 #4).
 *
 * GUID 로만 조회하므로 추측도 순회도 되지 않는다. 화면이 `<img src>` 에 그대로 건다.
 */
export function generatedImageUrl(imageId: string): string {
  return apiUrl(`/api/generated-images/${imageId}`)
}

/**
 * 완성된 3D 내려받기 (사이클 #10).
 *
 * **공급자 링크가 아니다.** Tripo 가 주는 URL 은 5분이면 만료되므로 화면이 그것을 들고
 * 있으면 어제 만든 에셋을 못 받는다. 우리 저장소를 거치므로 만료가 없다.
 */
/** Design Ref: scene-assembly §4 — 없거나 낡았으면 서버가 유도해 저장한 뒤 준다. */
export function getSceneLayout(jobId: string, signal?: AbortSignal): Promise<SceneLayoutData> {
  return apiRequest<SceneLayoutData>(`/api/jobs/${jobId}/scene-layout`, { signal })
}

export function meshDownloadUrl(meshId: string): string {
  return apiUrl(`/api/generated-meshes/${meshId}`)
}

/**
 * FBX 내려받기 (사이클 #12).
 *
 * **형식별 경로다.** `?format=fbx` 로 하면 `immutable` 캐시가 쿼리별로 나뉘는 것을
 * 중간 캐시가 보장하지 않는다.
 */
export function meshFbxDownloadUrl(meshId: string): string {
  return apiUrl(`/api/generated-meshes/${meshId}/fbx`)
}

export function meshPreviewUrl(meshId: string): string {
  return apiUrl(`/api/generated-meshes/${meshId}/preview`)
}
