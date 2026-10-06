/**
 * Design Ref: §3.4 — 작업 도메인 타입. 순수 TS 이며 외부 라이브러리를 쓰지 않는다.
 *
 * 캔버스의 `domain/execution` 과 분리한다 (§2.4). 캔버스는 보류됐고 스튜디오의 어휘가
 * 정본이다 — 지금 합치면 쓰이지 않는 추상이 남는다.
 *
 * 용어는 백엔드와 같다 (§0 용어 사전): 작업(`Job`) · 공정(`Task`) · 단계(`TaskKind`).
 */

/**
 * 작업 상태.
 *
 * 사이클 #7 에서 `partiallySucceeded` 가 늘었다 (Plan D-3) — 파츠 하나가 실패해도
 * 나머지 성공분은 남는다. **`succeeded` 로 뭉개지 않는 이유**는 이미지가 빈 파츠가
 * 있다는 사실이 조립 단계의 입력 조건이기 때문이다.
 *
 * 공정 상태에는 이 값이 없다 — 공정은 하나의 파츠를 그리므로 부분이 없다.
 */
export type JobStatus =
  | 'pending'
  | 'running'
  /**
   * 검수 대기 (review-gate §목표). 분해가 끝났지만 `requiresReview` 인 작업은
   * 사람이 전체 승인하기 전까지 파츠 생성으로 넘어가지 않는다. 종료 상태가 아니다 —
   * `TERMINAL_STATUSES` 에 넣지 않는다(폴링이 멈추면 승인 후 상태 전환을 못 본다).
   */
  | 'pendingReview'
  | 'succeeded'
  | 'partiallySucceeded'
  | 'failed'
  | 'canceled'

/**
 * 실행 순서와 나열 순서를 맞춰 둔다.
 *
 * `rewriteDescriptions` 는 검수 게이트를 켠 작업에서만 생긴다 (occludedby-recompute) —
 * 겹치는 파츠를 추가해 가려지게 된 파츠가 있을 때 승인 시점에 한 번 돈다.
 */
export type TaskKind =
  | 'analyze'
  | 'extract'
  | 'decompose'
  | 'rewriteDescriptions'
  | 'generate'
  | 'reconstruct'
  /**
   * 대칭(합성) 이미지 생성 — 항상 한 트랜잭션 안에서 즉시 끝난다(spec 20260917).
   * `generate` 와 분리된 이유는 합성 생성이 실제 이미지 재생성을 막지 않기 위해서다.
   */
  | 'synthesize'
  | 'analyzeSprites'
  | 'generateSprite'
  | 'packSprites'

/** 제작 대상 카테고리 — 서버 wire 값과 동일. */
export type AssetCategory = 'character' | 'object' | 'background'

/** 제작 대상 카테고리 순서. */
export const ASSET_CATEGORIES: readonly AssetCategory[] = ['character', 'object', 'background']

/** 제작 대상 화면 표기. */
export const ASSET_CATEGORY_LABELS: Record<AssetCategory, string> = {
  character: '캐릭터',
  object: '오브젝트',
  background: '배경',
}

/** 제작 대상 화면 표기 조회. */
export function assetCategoryLabel(category: AssetCategory): string {
  return ASSET_CATEGORY_LABELS[category]
}

/** 캐릭터 성별 — 서버 wire 값과 동일 (character-studio §5). */
export type CharacterGender = 'male' | 'female'

/**
 * 파츠 힌트 — 접수 페이로드의 wire 형태 (character-studio §5.1).
 *
 * 백엔드 `PartHintDto` 와 같은 모양이고 wire 는 camelCase 다. `variant` 는 변형이 있는
 * 파츠에서만 채워진다. 값의 정본은 프론트 택소노미(`characterPartTaxonomy`)다.
 */
export interface PartHint {
  type: string
  count: number
  variant?: string
}

/**
 * 파츠의 수평 기준 방향. 백엔드 `ViewDirection` 열거형과 같은 선언 순서다.
 *
 * **공급자에게 보내는 순서와 다르다.** Meshy 는 배열 위치로 방향을 읽는데 그쪽은
 * `front · left · back · right` 다 — 둘째와 넷째가 뒤바뀐다. 변환은 백엔드 어댑터
 * 한 곳에 갇혀 있고, 화면은 이 순서를 표시에만 쓴다.
 */
export type ViewDirection = 'front' | 'right' | 'back' | 'left'

/** 화면에 보이는 단계 이름. 코드값을 그대로 노출하면 사용자가 읽을 수 없다. */
const TASK_KIND_LABELS: Record<TaskKind, string> = {
  analyze: '장면 분석',
  extract: '파츠 식별',
  decompose: '파츠 분해',
  rewriteDescriptions: '서술 재작성',
  generate: '파츠 생성',
  reconstruct: '3D 제작',
  synthesize: '대칭 이미지 생성',
  analyzeSprites: '2D 배경 분석',
  generateSprite: '2D 배경 생성',
  packSprites: '2D 배경 내보내기',
}

/** 작업 상태의 화면 표기. 부분 성공은 성공과 다른 낱말이어야 한다 (C-3). */
const JOB_STATUS_LABELS: Record<JobStatus, string> = {
  pending: '대기',
  running: '실행 중',
  pendingReview: '검수 대기',
  succeeded: '성공',
  partiallySucceeded: '부분 성공',
  failed: '실패',
  canceled: '취소',
}

export function jobStatusLabel(status: JobStatus): string {
  return JOB_STATUS_LABELS[status] ?? status
}

export function taskKindLabel(kind: TaskKind): string {
  return TASK_KIND_LABELS[kind] ?? kind
}

export interface JobTask {
  id: string
  kind: TaskKind
  ordinal: number
  status: JobStatus
  failureReason: string | null
  /** 몇 번째 시도인가. 계약 위반은 한도까지 다시 물어본다 */
  attemptCount: number
  startedAt: string | null
  completedAt: string | null
  /** 생성 공정만 값을 갖는다 — 어느 파츠를 그리는가 (사이클 #7). */
  partId: string | null
  /**
   * 생성 공정만 값을 갖는다 — 정면·우측·후면·좌측.
   *
   * **3D 제작은 null 이다.** 파츠 하나를 통째로 만들지 방향마다 만들지 않는다.
   */
  viewDirection: ViewDirection | null
  /**
   * 3D 제작 공정의 진행률 0~100. 나머지 공정은 `null` 이다.
   *
   * **0 이 아니라 null 인 이유**는 "진행률이 없다" 와 "0% 다" 가 다르기 때문이다.
   * 0 으로 두면 화면이 모든 공정에 빈 막대를 그린다.
   */
  progress: number | null
}

/** 화면상 위치. 정규화 0~1, 원점 좌상단. */
export interface Bounds {
  x: number
  y: number
  w: number
  h: number
}

/**
 * 장면 명세 — 이후 모든 단계가 참조하는 기준선.
 *
 * `camera` · `light` · `scale` 이 조립을 좌우한다. 사이클 #4 의 자유 문장
 * `consistencyPrompt` 에는 이 셋이 없었다.
 */
/**
 * 팔레트 한 칸.
 *
 * `hex` 가 null 인 것은 색상어를 식별할 수 없는 레거시 항목뿐이다. 신규 작업에서 null 이
 * 오면 백엔드 계약 위반이지만, 화면은 결과 전체를 깨뜨리지 않고 미확정으로 표시한다.
 */
export interface PaletteEntry {
  name: string
  hex: string | null
}

export interface SceneSpec {
  palette: PaletteEntry[]
  timeOfDay: string
  mood: string
  renderingStyle: string
  materialFeel: string
  camera: { type: string; eyeLevel: string; horizonY: number }
  light: { direction: string; temperature: string; shadowHardness: string }
  scale: { object: string; realWorldSize: string; heightMeters: number | null }
}

/** 분해가 채우는 필드는 그 전까지 null 이다. */
export interface AssetPart {
  id: string
  name: string
  ordinal: number
  description: string | null
  category: string | null
  /**
   * 이 파츠를 놓을 자리들 (사이클 #9).
   *
   * 분해 전에는 빈 배열이다 — `null` 이 아닌 이유는 "아직 없다" 와 "0개다" 가 여기서 같은
   * 뜻이고, 화면이 null 검사를 하지 않아도 되기 때문이다.
   */
  placements: Bounds[]
  depthOrder: number | null
  occludedBy: string[]
  /**
   * 면을 덮는 파츠인가 (#20 §4.1).
   *
   * 서버가 늘 보내지만 예전 응답에는 없어 선택이다 — 없으면 낱개 물건으로 본다.
   */
  surface?: 'none' | 'ground' | 'vertical'
  /** 생성이 채운다. 아직 안 만들었거나 실패했으면 null (사이클 #7). */
  generatedImageId: string | null
  /** 3D 재구성 입력용 방향별 최신 이미지. */
  generatedImages: GeneratedImage[]
  /** 가장 나중 3D 결과. 아직 없거나 실패했으면 null (사이클 #10). */
  generatedMesh: GeneratedMesh | null
}

/**
 * 내려받을 수 있는 3D 결과.
 *
 * **공급자 링크가 없다.** 그쪽 URL 은 5분이면 만료되므로 화면이 들고 있으면 어제 만든
 * 에셋을 못 받는다. 내려받기는 `/api/generated-meshes/{id}` 로 한다.
 */
export interface GeneratedMesh {
  id: string
  hasPreview: boolean
  /** Meshy 결과에만 있다 — Tripo 는 FBX 를 내지 않는다 (사이클 #12). */
  hasFbx: boolean
  sizeBytes: number
  /** FBX 크기. 구계약 응답에는 없다 — 그때는 이름만 적는다 */
  fbxSizeBytes: number | null
  creditsConsumed: number | null
  createdAt: string
}

export interface GeneratedImage {
  id: string
  viewDirection: ViewDirection
}

// ─────────────────── 검수 게이트 (review-gate) ───────────────────
//
// Design Ref: docs/specs/2026-08-28-review-gate.md §입력→출력
//
// 작업이 `pendingReview` 인 동안 사용자가 사각형으로 파츠를 추가·삭제하고
// 전체 승인 한 번으로만 Generate 팬아웃을 트리거한다.

/** 파츠 한 줄 — 검수 화면이 사각형을 겹쳐 그릴 좌표와 출처. */
export interface ReviewPart {
  id: string
  partRef: string
  name: string
  category: string | null
  description: string | null
  placements: Bounds[]
  /** VLM 이 찾았는지("detected") 사람이 사각형으로 추가했는지("manual"). */
  source: 'detected' | 'manual'
  /** 이 파츠를 가리는 파츠 — 검수자가 편집 결과를 눈으로 확인하는 값이다. */
  occludedBy: string[]
  /** 이 파츠가 가리는 기존 파츠. */
  occludes: string[]
  /** 서술 출처 — 서술 확인 단계의 칩 원천 (review-gate-staged 사이클 1). */
  descriptionSource: DescriptionSource
}

export type DescriptionSource = 'model' | 'rewritten' | 'human'

/** 검수 단계. 게이트 없는 작업·승인 완료는 `null`. */
export type ReviewPhase = 'boxes' | 'descriptions'

export interface ReviewState {
  status: JobStatus
  parts: ReviewPart[]
  /** 상자 확정 시 서술을 다시 쓸 파츠 이름. */
  descriptionsStale: string[]
  reviewPhase: ReviewPhase | null
  /** 장면 팔레트 — 서술 단계에서 편집. */
  palette: PaletteEntry[]
}

/** 공급자 설정 참조와 모델 id 한 쌍. 짝이 맞지 않는 상태는 서버가 만들지 않는다. */
export interface ModelSelection {
  providerConfigId: string
  model: string
}

/**
 * 이 작업이 무엇으로 돌고 있는가 — 접수 시점에 고른 선택.
 *
 * 고르지 않았으면 `null` 이다. 이미지 생성 없이 접수된 옛 작업이 그렇다.
 */
export interface JobModels {
  text: ModelSelection | null
  image: ModelSelection | null
  /** 3D 를 고르지 않은 작업은 null 이다 (사이클 #10). */
  mesh: ModelSelection | null
}

export interface Job {
  id: string
  category: AssetCategory
  sourceImageId: string
  status: JobStatus
  /** 사이클 #5: `consistencyPrompt`(자유 문장)를 대체한다. */
  scene: SceneSpec | null
  /** 진행 표기와 재시도 프리필이 읽는다. */
  models: JobModels
  /** 공정이 셋이 되면서 화면이 실제로 쓴다 — 어느 단계에서 멈췄는지 보여준다. */
  tasks: JobTask[]
  parts: AssetPart[]
  failureReason: string | null
  createdAt: string
  completedAt: string | null
  /** 캐릭터가 아니면 `null` — "다시 시도" 이어받기의 입력(character-mesh-ui §FR-08). */
  gender: CharacterGender | null
  partHints: PartHint[]
  requiresReview?: boolean
}

/** 홈 두 섹션이 쓰는 요약. 목록에 프롬프트 전문과 공정을 싣지 않는다 (§4.2 #7). */
export interface JobSummary {
  id: string
  category: AssetCategory
  status: JobStatus
  sourceImageId: string
  partCount: number
  createdAt: string
}

/**
 * 진행률 — Angular Material Progress Bar 의 buffer 모드와 같은 두 값.
 *
 * `value` 는 **확정된 것**(성공한 공정), `buffer` 는 **진행 중인 것까지**다.
 * 하나의 %로 뭉치면 "3분째 33% 에 멈춰 있다" 로 보여 멈춘 것처럼 읽힌다.
 * 두 값이면 "1단계는 끝났고 2단계가 도는 중" 이 한눈에 들어온다.
 *
 * 단계 안의 진행은 알 수 없다 — LLM 호출은 중간 신호를 주지 않는다.
 * 그래서 buffer 는 "이 단계가 시작됐다" 까지만 말하고 그 안을 추측하지 않는다.
 */
export interface JobProgress {
  /** 0~1. 성공한 공정의 비율 */
  value: number
  /** 0~1. 진행 중인 공정까지 포함 */
  buffer: number
  /** 지금 도는 공정. 없으면 null */
  running: JobTask | null
  /** 1부터 시작하는 현재 공정 위치. 공정이 없으면 0 */
  current: number
  completed: number
  total: number
}

export function jobProgress(tasks: JobTask[]): JobProgress {
  const total = tasks.length
  if (total === 0) {
    return { value: 0, buffer: 0, running: null, current: 0, completed: 0, total: 0 }
  }

  // ordinal 값 자체는 순번이 아닐 수 있으므로 정렬된 배열 위치를 진행 단계로 사용한다
  const ordered = [...tasks].sort((a, b) => a.ordinal - b.ordinal)
  const completed = ordered.filter((task) => task.status === 'succeeded').length
  const running = ordered.find((task) => task.status === 'running') ?? null
  const unfinishedIndex = ordered.findIndex((task) => task.status !== 'succeeded')
  const current = unfinishedIndex === -1 ? total : unfinishedIndex + 1

  return {
    value: completed / total,
    // 도는 공정이 있으면 그것까지 buffer 에 넣는다. 없으면 value 와 같다 —
    // 대기 중인 공정을 buffer 에 넣으면 아무 일도 안 하는데 진행 중으로 보인다
    buffer: (completed + (running ? 1 : 0)) / total,
    running,
    current,
    completed,
    total,
  }
}

/** 실패한 공정. 어느 단계에서 멈췄는지 화면이 그 자리에 보여준다 */
export function failedTask(tasks: JobTask[]): JobTask | null {
  return tasks.find((t) => t.status === 'failed') ?? null
}

/** 공정 소요 시간(초). 아직 안 끝났으면 null */
export function taskDurationSeconds(task: JobTask): number | null {
  if (!task.startedAt || !task.completedAt) return null

  const ms = new Date(task.completedAt).getTime() - new Date(task.startedAt).getTime()
  return Math.max(0, Math.round(ms / 1000))
}

/**
 * 끝난 작업.
 *
 * **부분 성공도 끝난 것이다** (사이클 #7). 빠뜨리면 폴링이 영원히 돌고, 홈의
 * "실행 중" 에 끝난 작업이 남는다 — 실패한 파츠 방향을 다시 돌리는 것은 공정 단위
 * 재시도이지 작업의 재개가 아니다.
 */
const TERMINAL_STATUSES: readonly JobStatus[] = [
  'succeeded',
  'partiallySucceeded',
  'failed',
  'canceled',
]

/** 종료 상태면 폴링을 멈춘다. */
export function isTerminal(status: JobStatus): boolean {
  return TERMINAL_STATUSES.includes(status)
}

export function isActive(status: JobStatus): boolean {
  return !isTerminal(status)
}

/**
 * 결과 화면 진입 가능 여부.
 *
 * 작업 종료가 아니라 사용자가 확인할 결과의 존재를 기준으로 한다. 실패와 취소는
 * 원인·중단 상태를 보존하는 전용 화면이 우선한다.
 */
export function hasResultToShow(job: Job): boolean {
  if (job.status === 'failed' || job.status === 'canceled') return false
  if (job.status === 'succeeded' || job.status === 'partiallySucceeded') return true

  // 파츠 분해 완료 상태
  return job.parts.length > 0
}

/**
 * 폴링 간격 — 2s → 5s → 15s 상한.
 *
 * Design Ref: §3.4 · R-9 — 외부 호출이 10분 넘게 걸릴 수 있다. 2초 고정이면 300회다.
 * 초반을 촘촘히 두는 이유는 대부분의 작업이 수십 초에 끝나기 때문이고, 상한을 두는
 * 이유는 긴 작업에서 요청 수가 선형으로 늘지 않게 하기 위해서다.
 *
 * @param attempt 0부터 시작하는 시도 횟수
 */
export function nextPollDelayMs(attempt: number): number {
  if (attempt <= 0) return 2_000
  if (attempt === 1) return 5_000
  return 15_000
}

// ─────────────────── 파츠 생성 (사이클 #7) ───────────────────
//
// Design Ref: §5.2 · §5.3
//
// **화면이 아니라 여기 있는 이유**: 팬아웃 때문에 "몇 장 나왔나 · 몇 장 실패했나 ·
// 이 파츠는 어떤 상태인가" 가 전부 공정 목록에서 파생된다. 컴포넌트 안에 두면 결과
// 화면과 진행 표시가 같은 계산을 각자 하게 되고, 둘이 어긋나면 사용자가 본 숫자가
// 화면마다 달라진다.
//
// 별도 파일이 아니라 이 파일에 있는 이유는 §9.2 계층 규칙이다 — `domain/job` 은
// 타입조차 밖에서 끌어오지 않으므로 형제 파일 사이의 import 도 두지 않는다.

/** 파츠 카드 하나가 알아야 하는 것 전부. */
export interface PartCard {
  part: AssetPart
  /** 정면·우측·후면·좌측 고정 순서의 생성 상태. */
  views: PartViewCard[]
  /** 이 파츠를 그리는 공정. 팬아웃 전이면 null */
  task: JobTask | null
  /** 공정 상태를 그대로 쓴다 — 카드가 상태를 다시 해석하면 두 진실이 생긴다 */
  status: JobStatus | 'unplanned'
  imageId: string | null
  failureReason: string | null
}

export interface PartViewCard {
  viewDirection: ViewDirection
  task: JobTask | null
  status: JobStatus | 'unplanned'
  imageId: string | null
  failureReason: string | null
}

const VIEW_DIRECTIONS: ViewDirection[] = ['front', 'right', 'back', 'left']

/**
 * 파츠 순서대로 카드를 만든다.
 *
 * 공정이 파츠를 가리키는 방향이므로(백엔드 §3.1) 여기서 되찾는다. 재생성하면 공정이
 * 늘어날 수 있어 **가장 나중 것**을 쓴다 — 파츠는 하나이고 공정은 시도마다 늘어난다.
 */
export function partCards(job: Job): PartCard[] {
  const byPart = new Map<string, JobTask>()
  const byPartAndView = new Map<string, JobTask>()

  for (const task of job.tasks) {
    if (task.kind === 'generate' && task.partId !== null) {
      byPart.set(task.partId, task)
      // 방향 없는 이전 API 응답은 정면으로 간주
      byPartAndView.set(`${task.partId}:${task.viewDirection ?? 'front'}`, task)
    }
  }

  return [...job.parts]
    .sort((a, b) => a.ordinal - b.ordinal)
    .map((part) => {
      const task = byPart.get(part.id) ?? null
      const images = new Map(
        (part.generatedImages ?? []).map((image) => [image.viewDirection, image.id]),
      )
      if (images.size === 0 && part.generatedImageId !== null) {
        images.set('front', part.generatedImageId)
      }

      return {
        part,
        views: VIEW_DIRECTIONS.map((viewDirection) => {
          const viewTask = byPartAndView.get(`${part.id}:${viewDirection}`) ?? null

          return {
            viewDirection,
            task: viewTask,
            status: viewTask?.status ?? 'unplanned',
            imageId: images.get(viewDirection) ?? null,
            failureReason: viewTask?.failureReason ?? null,
          }
        }),
        task,
        // 분해까지만 끝난 작업은 생성 공정이 아직 없다 — '대기' 와 구분해야
        // 사용자가 "곧 그려진다" 와 "그릴 계획이 없다" 를 가를 수 있다
        status: task?.status ?? 'unplanned',
        imageId: part.generatedImageId,
        failureReason: task?.failureReason ?? null,
      }
    })
}

/** 결과 머리말의 세 숫자 — `파츠 N · 생성 M · 실패 K`. */
export interface GenerationTally {
  parts: number
  generated: number
  total: number
  failed: number
}

export function generationTally(job: Job): GenerationTally {
  const cards = partCards(job)

  return {
    parts: cards.length,
    // 방향 이미지의 실제 존재와 계획된 공정 수 기준 집계
    generated: cards.flatMap((card) => card.views).filter((view) => view.imageId !== null).length,
    total: cards.flatMap((card) => card.views).filter((view) => view.task !== null).length,
    failed: cards.flatMap((card) => card.views).filter((view) => view.status === 'failed').length,
  }
}

/** 3D 공정 진행 집계. */
export interface MeshTally {
  ready: number
  running: number
  planned: number
}

/**
 * 3D 공정이 실제로 생긴 파츠 기준 진행 집계.
 *
 * 일부 파츠만 3D 제작 대상일 수 있으므로 전체 파츠 수를 분모로 사용하지 않는다.
 */
export function meshTally(job: Job): MeshTally {
  const latestTaskByPart = new Map<string, JobTask>()

  // 파츠별 최신 3D 공정 선택
  for (const task of job.tasks) {
    if (task.kind === 'reconstruct' && task.partId !== null) {
      latestTaskByPart.set(task.partId, task)
    }
  }

  let ready = 0
  let running = 0
  let planned = 0

  // 결과 또는 3D 공정이 있는 파츠만 집계
  for (const part of job.parts) {
    const task = latestTaskByPart.get(part.id)
    if (part.generatedMesh === null && task === undefined) continue

    planned += 1
    if (part.generatedMesh !== null) ready += 1
    else if (task?.status === 'running') running += 1
  }

  return { ready, running, planned }
}

/**
 * 진행 표시용 — 생성 공정을 한 칸으로 묶는다 (§5.3).
 *
 * 파츠가 20개일 때 칸 20개를 그리면 읽을 수 없다. 앞 세 단계는 이름으로 두고
 * 생성만 `파츠 생성 7/9` 로 접는다.
 */
export interface StageRow {
  key: string
  /** 라벨을 만들 단계. 묶인 칸도 `generate` 라 이름이 그대로 붙는다 */
  kind: TaskKind
  status: JobStatus
  /** 묶인 칸의 진척 (`7/9`). 개별 단계면 null */
  note: string | null
  /** 개별 단계의 원본 공정. 묶인 칸이면 null */
  task: JobTask | null
  /** 묶인 생성 공정들. 개별 단계면 null */
  group: JobTask[] | null
}

export function groupGenerationStages(tasks: JobTask[]): StageRow[] {
  const ordered = [...tasks].sort((a, b) => a.ordinal - b.ordinal)

  // **개수가 고정이 아닌 단계가 둘이다** (사이클 #7·#10). 둘 다 파츠 수에 따라 늘어나므로
  // 한 줄로 접고 진행을 숫자로 보여 준다 — 스물이 넘는 줄을 그대로 그리면 앞 세 단계가
  // 화면 밖으로 밀려난다
  const grouped: TaskKind[] = ['generate', 'reconstruct']

  const rows: StageRow[] = ordered
    .filter((t) => !grouped.includes(t.kind))
    .map((task) => ({
      key: task.id,
      kind: task.kind,
      status: task.status,
      note: null,
      task,
      group: null,
    }))

  for (const kind of grouped) {
    const group = ordered.filter((t) => t.kind === kind)

    if (group.length === 0) {
      continue
    }

    const done = group.filter((t) => t.status === 'succeeded').length

    rows.push({
      key: kind,
      kind,
      status: rollUpStatus(group),
      // **분모가 도중에 는다.** 3D 공정은 파츠마다 네 장이 모일 때 하나씩 생기므로
      // 이미지가 끝나기 전에는 총수를 알 수 없다 (§11.4)
      note: `${done}/${group.length}`,
      task: null,
      group,
    })
  }

  return rows
}

/**
 * 형제 공정들의 상태를 한 값으로 접는다.
 *
 * 순서가 규칙이다: 하나라도 도는 중이면 진행 중, 전부 끝났으면 실패가 섞였는지 본다.
 * **실패가 성공보다 우선한다** — 접힌 칸이 성공으로 보이면 사용자가 리스트를 열어 볼
 * 이유가 없어진다.
 */
function rollUpStatus(tasks: JobTask[]): JobStatus {
  if (tasks.some((t) => t.status === 'running')) return 'running'
  if (tasks.some((t) => t.status === 'pending')) return 'running'
  if (tasks.some((t) => t.status === 'canceled')) return 'canceled'
  if (tasks.some((t) => t.status === 'failed')) return 'failed'

  return 'succeeded'
}

/** 상단 요약 한 줄의 배지 하나. */
export interface ModelBadge {
  role: 'text' | 'image'
  label: string
  /** 공급자 목록에서 찾지 못하면 null — 삭제됐거나 비활성인 설정. */
  providerName: string | null
  model: string
}

const MODEL_ROLE_LABELS: Record<ModelBadge['role'], string> = {
  text: '텍스트',
  image: '이미지',
}

/**
 * "무엇으로 돌고 있는가" 를 화면이 그릴 수 있는 형태로 편다.
 *
 * **이름표를 인자로 받는 이유**는 `domain/job` 이 아무것도 import 하지 않기 때문이다
 * (§9.2). 공급자 표시명은 `domain/provider` 의 것이므로 화면이 이어 준다.
 *
 * **이름을 못 찾아도 배지를 버리지 않는다.** 공급자가 지워져도 "이 작업이 어느 모델로
 * 돌았는가" 는 남아야 하고, 그 중 정보량이 큰 쪽은 모델 id 다.
 *
 * **`models` 가 통째로 없을 수 있다.** 타입은 필수라고 말하지만 그것은 이 필드를
 * 내려보내는 서버와 짝지어졌을 때의 이야기다 — 화면이 먼저 배포되면 기존 작업 응답에
 * 이 칸이 없다. 방향 없는 이전 응답을 정면으로 간주하는 `partCards` 와 같은 자리다.
 */
export function modelBadges(
  models: JobModels | null | undefined,
  providerNames: Record<string, string>,
): ModelBadge[] {
  if (!models) return []

  // 파이프라인 순서 — 텍스트 세 공정이 돌고 나서 이미지가 돈다
  const roles: ModelBadge['role'][] = ['text', 'image']

  return roles.flatMap((role) => {
    const selection = models[role]
    // null 뿐 아니라 칸 자체가 빠진 응답도 같이 걸러진다
    if (!selection) return []

    return [
      {
        role,
        label: MODEL_ROLE_LABELS[role],
        providerName: providerNames[selection.providerConfigId] ?? null,
        model: selection.model,
      },
    ]
  })
}

/**
 * 파츠 한 줄의 3D 상태.
 *
 * Design Ref: §11.3
 *
 * **화면이 아니라 여기서 정하는 이유**는 갈래가 일곱이기 때문이다. JSX 안에서 삼항으로
 * 엮으면 어떤 조합이 빠졌는지 읽어서는 알 수 없고, 테스트도 렌더를 거쳐야 한다.
 */
export type MeshState =
  /** 3D 를 고르지 않은 작업 — 옛 작업과 이미지까지만 원하는 신규 작업이 함께 온다. */
  | { kind: 'notRequested' }
  /** 방향 이미지가 아직 모자란다. */
  | { kind: 'awaitingImages'; ready: number }
  /** 네 장이 모였고 공정이 서기를 기다린다 — 오케스트레이터가 계획하기까지의 틈. */
  | { kind: 'planning' }
  | { kind: 'queued' }
  | { kind: 'running'; progress: number }
  | { kind: 'failed'; taskId: string; failureReason: string | null }
  | { kind: 'ready'; mesh: GeneratedMesh }

/** 3D 재구성 입력이 되는 방향 넷. */
const MESH_INPUT_DIRECTIONS = 4

/**
 * 파츠 하나의 3D 상태를 정한다.
 *
 * **성공 판정의 기준이 공정이 아니라 결과다.** 공정이 성공했는데 결과가 안 붙은 순간이
 * 있는데(취소가 그 사이에 들어온 경우), 그것을 성공으로 그리면 내려받기 버튼이 눌리지
 * 않는 상태로 남는다.
 */
export function meshState(job: Job, part: AssetPart, tasks: JobTask[]): MeshState {
  if (job.models.mesh === null) {
    return { kind: 'notRequested' }
  }

  if (part.generatedMesh !== null) {
    return { kind: 'ready', mesh: part.generatedMesh }
  }

  const ready = new Set(part.generatedImages.map((image) => image.viewDirection)).size

  if (ready < MESH_INPUT_DIRECTIONS) {
    return { kind: 'awaitingImages', ready }
  }

  // 재시도가 공정을 늘리지는 않지만, 방어적으로 가장 나중 것을 쓴다
  const task = tasks
    .filter((candidate) => candidate.kind === 'reconstruct' && candidate.partId === part.id)
    .at(-1)

  if (task === undefined) {
    return { kind: 'planning' }
  }

  switch (task.status) {
    case 'pending':
      return { kind: 'queued' }

    case 'running':
      // 막대를 그리려면 숫자가 있어야 한다 — 없는 것과 0% 는 화면에서 같은 그림이다
      return { kind: 'running', progress: task.progress ?? 0 }

    case 'failed':
    case 'canceled':
      return {
        kind: 'failed',
        taskId: task.id,
        failureReason: task.failureReason,
      }

    default:
      // 성공했는데 결과가 아직 없다 — 붙는 순간까지는 진행 중이다
      return { kind: 'planning' }
  }
}

/** 3D 재구성이 요구하는 방향 수. 공급자 상한이기도 하다. */
const MESH_INPUT_VIEWS = 4

/**
 * 3D 를 만들 수 있는 파츠 수 — 네 방향 이미지가 다 있는 것.
 *
 * Design Ref: §7.3 · Plan FR-08
 *
 * 유료 호출이 파츠당 1건이므로 이 숫자가 곧 생성 요청 건수다. 실제 크레딧은 공급자별
 * 완료 응답이 정하며, 화면은 모델 단가와 이 건수를 곱해 실행 전 예상 비용을 보여준다.
 */
export function meshReadyPartCount(job: Job): number {
  return job.parts.filter(
    (part) =>
      new Set(part.generatedImages.map((image) => image.viewDirection)).size === MESH_INPUT_VIEWS,
  ).length
}

/**
 * 이 작업에 3D 를 뒤늦게 붙일 수 있는가.
 *
 * Design Ref: §7.1
 *
 * **화면이 아니라 여기서 판단하는 이유**는 조건이 셋이기 때문이다. JSX 안에서 엮으면
 * 어떤 조합이 빠졌는지 읽어서는 알 수 없다 — `meshState` 와 같은 이유다.
 *
 * 공급자가 등록돼 있는가는 여기서 보지 않는다. 그것은 작업의 성질이 아니라 화면이
 * 이미 받아 둔 목록의 문제다.
 */
export function canAddMeshProduction(job: Job): boolean {
  // 한 번 붙이면 그 뒤는 파츠별 다시 시도가 맡는다 (D-06)
  if (job.models.mesh !== null) {
    return false
  }

  // 취소는 "끝났다" 가 아니라 "하지 말라" 다 (D-08)
  if (job.status === 'canceled') {
    return false
  }

  return meshReadyPartCount(job) > 0
}

/** 내려받을 수 있는 3D 형식 (사이클 #13). */
export type MeshDownloadFormat = 'glb' | 'fbx'

/**
 * 메뉴에 설 항목 하나.
 *
 * **URL 이 없다.** `domain` 은 어떤 것도 import 하지 않으므로 (§9.2 계층 규칙) 주소를
 * 만들 수단이 없고, 만들어서도 안 된다. 여기서 정하는 것은 **무엇을 어떤 차례로
 * 보여줄 것인가**이고, 그 형식을 주소로 옮기는 일은 위층이 한다.
 */
export interface MeshDownloadOption {
  format: MeshDownloadFormat
  label: string
}

/**
 * 이 결과로 받을 수 있는 형식들.
 *
 * Design Ref: §3 · D-09
 *
 * **GLB 는 늘 있고 FBX 는 있을 때만 있다** — Tripo 는 FBX 를 내지 않는다 (사이클 #12).
 * 순서를 GLB 먼저로 고정하는 이유는 그것이 3D 결과 자체이고 FBX 는 엔진에 넣기 위한
 * 부수 형식이기 때문이다.
 *
 * **목록으로 두는 것이 요점이다.** 형식마다 줄을 늘리면 셋째 형식이 올 때 화면이
 * 또 넓어진다.
 */
export function meshDownloadOptions(mesh: GeneratedMesh): MeshDownloadOption[] {
  const options: MeshDownloadOption[] = [
    { format: 'glb', label: `GLB · ${formatMeshSize(mesh.sizeBytes)}` },
  ]

  if (mesh.hasFbx) {
    // 고르는 순간의 판단 재료다 — 다만 모르는 크기를 지어내지는 않는다 (구계약 호환)
    options.push({
      format: 'fbx',
      label: mesh.fbxSizeBytes === null ? 'FBX' : `FBX · ${formatMeshSize(mesh.fbxSizeBytes)}`,
    })
  }

  return options
}

/** 소수 한 자리면 충분하다 — 사용자가 재는 것은 자릿수이지 바이트가 아니다. */
export function formatMeshSize(bytes: number): string {
  const mb = bytes / (1024 * 1024)
  return mb >= 1 ? `${mb.toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`
}

// ─── 3D 조립 (scene-assembly) ───

/** 월드 좌표 — 서버 유도 결과를 그대로 그린다. 브라우저는 계산하지 않는다 (Plan D-01). */
export interface SceneVector {
  x: number
  y: number
  z: number
}

/** 배치 하나가 놓일 자리 — "이 에셋을 여기에". */
export interface SceneInstanceData {
  partId: string
  partName: string
  meshId: string
  ordinal: number
  position: SceneVector
  rotationY: number
  /** 정규화된 GLB(높이 1 · 바닥 중심 원점)에 곱하는 값 — 정규화는 뷰어 몫이다 (§3.2). */
  scale: number
  /**
   * 축별 배율 (background-surface-parts #20 §4.2).
   *
   * 낱개 물건은 세 값이 `scale` 과 같고, 면을 덮는 표면(포장·도로·절벽 단면)만 갈린다 —
   * 배치 사각형이 곧 크기여서 폭·깊이를 각각 맞춰야 한다.
   */
  scaleVector: SceneVector
}

/** 수치 카메라 — 서버가 정본 (background-similarity-tuning D-03). 캡처 결정성의 재료다. */
export interface SceneNumericCamera {
  position: SceneVector
  target: SceneVector
  fieldOfViewDegrees: number
}

export interface SceneNumericLight {
  azimuthDegrees: number
  elevationDegrees: number
  keyIntensity: number
  keyColor: string
  ambientIntensity: number
  ambientColor: string
}

export interface SceneLayoutData {
  /** revision 정체 — 유사도 실행이 기준 layout 을 지목할 때 쓴다 (§10.1). */
  id: string
  revision: number
  sourceMeshSignature: string
  instances: SceneInstanceData[]
  /** 초기 카메라 힌트 — 첫인상이 원본과 닮아야 한다 (§5.2). */
  camera: { eyeLevel: string; horizonY: number } | null
  light: { direction: string; temperature: string } | null
  /** 평가 렌더와 같은 값 — Orbit 상태와 무관한 결정적 캡처의 근거 (§8.1). */
  numericCamera: SceneNumericCamera
  numericLight: SceneNumericLight
  /** GLB 가 없어 조립에서 빠진 파츠 (FR-08). */
  missingPartNames: string[]
  composedAt: string
  /** 유도가 내린 판단. 사이클 #18 이전에 합성된 레이아웃에는 없다 (FR-07). */
  composition: SceneCompositionData | null
}

/**
 * 배치를 월드로 옮길 때 유도가 내린 판단.
 *
 * Design Ref: background-scale-calibration(#18) §4.7
 *
 * `elevatedCount` 는 발끝이 지평선 위라 접지로 볼 수 없던 배치 수다. `anchorSpread` 는
 * 기준 물체 배치들의 크기 편차로, 1 에서 멀수록 보정 계수를 덜 믿을 만하다.
 */
export interface SceneCompositionData {
  groundedCount: number
  elevatedCount: number
  scaleDepthForElevated: number
  /** 면을 덮는 것으로 표시돼 눕거나 선 배치 수 (#20 FR-07). */
  surfaceCount: number
  /**
   * 크기 기준 물체가 표면으로 표시됐는가 (#20 §6).
   *
   * 표면의 `bounds.H` 는 높이가 아니라 누운 면의 세로 범위라, 그것으로 보정 계수를
   * 뽑으면 장면 전체가 어긋난다. 예전 응답에는 없어 선택이다.
   */
  anchorIsSurface?: boolean
  /** 인스턴스 상한에 걸려 버려진 배치 수 (#20 FR-06). */
  droppedCount?: number
  /** `ground` 로 표시됐으나 GLB 는 서 있는 형태인 파츠 수 — 부차 신호 (#20 §4.1). */
  surfaceShapeMismatch?: number
  anchorSpread: number
}

/**
 * "3D 배경" 탭을 열 수 있는가 (FR-04).
 *
 * **완성 GLB 가 하나는 있어야 연다.** 없는데 열면 빈 캔버스가 실패로 읽힌다.
 * 진행 중이어도 도착한 것이 있으면 연다 — live-result 와 같은 문법이다.
 */
export function canOpenSceneTab(job: Job): boolean {
  return job.parts.some((part) => part.generatedMesh !== null)
}

/** 원본 시점을 재현할 초기 카메라 자세 — 높이(월드 유닛)와 내려보는 각(라디안). */
export interface SceneCameraPose {
  height: number
  /** 양수 = 내려본다. 지평선이 화면 가운데보다 위에 있으면 내려보는 구도다. */
  pitchDown: number
}

const EYE_DEFAULT = 2.2
const EYE_MIN = 0.8
const EYE_MAX = 6

/**
 * 카메라 힌트 → 초기 자세 (scene-assembly §5.2).
 *
 * **eyeLevel 은 자유 문장이다** — 실측: "지면에서 1.7m". 고정 매핑표는 실데이터에서
 * 전부 기본값으로 떨어졌다. 숫자 표기 → 키워드 → 기본값 순으로 가늠하고, 극단값은
 * 장면을 벗어나지 않게 누른다.
 */
export function sceneCameraPose(
  camera: { eyeLevel: string; horizonY: number } | null,
): SceneCameraPose {
  if (camera === null) return { height: EYE_DEFAULT, pitchDown: 0 }

  const text = camera.eyeLevel.toLowerCase()

  // "1.7m" 류의 미터 표기 — 가장 믿을 만한 신호
  const meters = /([0-9]+(?:\.[0-9]+)?)\s*m/.exec(text)

  let height = EYE_DEFAULT
  if (meters !== null) height = Number(meters[1])
  else if (/low|낮|로우/.test(text)) height = 1.2
  else if (/high|높|부감|조감|bird/.test(text)) height = 4.5

  height = Math.min(EYE_MAX, Math.max(EYE_MIN, height))

  // 지평선이 화면 가운데에서 벗어난 만큼 기울인다 — 시야각(50°) 비례 근사
  const fovRad = (50 * Math.PI) / 180
  const pitchDown = (0.5 - camera.horizonY) * fovRad

  return { height, pitchDown }
}

/** 광원 온도 — 주광과 환경광이 다르게 서술될 수 있어 따로 든다. */
export type SceneLightTone = 'warm' | 'neutral' | 'cool'

/** 광원 자세 — 방위는 정면(+z)에서 시계 방향, 고도는 지면 기준. */
export interface SceneLightRig {
  azimuthDeg: number
  elevationDeg: number
  key: SceneLightTone
  ambient: SceneLightTone
}

// 방위 사분면 — 키워드 조합의 평균. 회화 관습(좌후방)이 기본값이다
const AZIMUTHS: Array<[RegExp, number]> = [
  [/(좌|left).*(후방|back)|(후방|back).*(좌|left)/, 225],
  [/(우|right).*(후방|back)|(후방|back).*(우|right)/, 135],
  [/(좌|left).*(전방|front)|(전방|front).*(좌|left)/, 315],
  [/(우|right).*(전방|front)|(전방|front).*(우|right)/, 45],
  [/좌|left/, 270],
  [/우|right/, 90],
  [/후방|back/, 180],
  [/전방|front/, 0],
]

function toneOf(text: string, fallback: SceneLightTone): SceneLightTone {
  if (/따뜻|warm|황갈|골드|golden|주황/.test(text)) return 'warm'
  if (/차가|cool|회청|푸른|blue/.test(text)) return 'cool'
  return fallback
}

/**
 * 광원 힌트 → 광원 자세 (G-01 해소).
 *
 * **모델의 광원 서술은 자유 문장이다** — 실측: "좌측 후방 20° 방위, 28° 고도".
 * 8방위 고정 표는 실데이터에서 한 번도 맞지 않았다. 키워드로 사분면을, 숫자로 고도를
 * 읽고, 극단 고도는 그림자가 무너지지 않게 누른다. `sceneCameraPose` 와 같은 문법이다.
 */
export function sceneLightRig(
  light: { direction: string; temperature: string } | null,
): SceneLightRig {
  if (light === null) {
    return { azimuthDeg: 225, elevationDeg: 40, key: 'neutral', ambient: 'neutral' }
  }

  const direction = light.direction.toLowerCase()

  const azimuthDeg = AZIMUTHS.find(([pattern]) => pattern.test(direction))?.[1] ?? 225

  // "28° 고도" · "elevation 28" — 고도라는 말과 짝인 숫자만 믿는다
  const elevation =
    /([0-9]+(?:\.[0-9]+)?)\s*°?\s*(?:고도|elevation)/.exec(direction) ??
    /(?:고도|elevation)\s*([0-9]+(?:\.[0-9]+)?)/.exec(direction)

  let elevationDeg =
    elevation !== null ? Number(elevation[1]) : /upper|상단|높/.test(direction) ? 50 : 40
  elevationDeg = Math.min(75, Math.max(15, elevationDeg))

  // 주광과 환경광이 다르게 서술된다 — "환경광" 앞뒤를 나눠 읽고, 없으면 전체에서
  const temperature = light.temperature.toLowerCase()
  // 비탐욕 — 탐욕적으로 잡으면 환경광 뒤의 주광 서술까지 삼킨다
  const ambientPart = /([^,.]*?(?:환경광|ambient))/.exec(temperature)?.[1] ?? ''
  const keyPart = temperature.replace(ambientPart, '')

  return {
    azimuthDeg,
    elevationDeg,
    key: toneOf(keyPart, toneOf(temperature, 'neutral')),
    ambient: toneOf(ambientPart, 'neutral'),
  }
}
