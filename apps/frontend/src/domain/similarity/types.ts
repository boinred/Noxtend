/**
 * 유사도 평가 계약 (background-similarity-tuning §5 · §10).
 *
 * **overall 은 서버 계산값이다** (D-05) — 화면은 절대 다시 합성하지 않는다.
 * 점수는 같은 원본·모델·프롬프트·캡처 조건 안의 **상대 개선 신호**이며 절대 품질
 * 보증으로 표시하지 않는다 (§5.3).
 */

export type SimilarityRunStatus =
  'evaluating' | 'readyForAdjustment' | 'awaitingRender' | 'completed' | 'failed' | 'canceled'

export type SimilarityEvaluationStatus =
  'awaitingRender' | 'pending' | 'running' | 'succeeded' | 'failed' | 'canceled'

export type SimilarityDimensionKind =
  'composition' | 'camera' | 'scale' | 'shape' | 'material' | 'lighting'

export interface SimilarityDimension {
  kind: SimilarityDimensionKind
  score: number
  evidence: string
  recommendation: string
}

export interface SimilarityScore {
  overall: number
  dimensions: SimilarityDimension[]
}

/** 보정 명령 — 서버 allowlist 의 discriminated 형태 그대로 (§6). */
export type SceneAdjustmentCommand =
  | { type: 'scaleScene'; factor: number }
  | { type: 'moveInstance'; partId: string; ordinal: number; deltaX: number; deltaZ: number }
  | { type: 'rotateInstance'; partId: string; ordinal: number; deltaDegrees: number }
  | { type: 'scaleInstance'; partId: string; ordinal: number; factor: number }
  | {
      type: 'adjustCamera'
      yawDeltaDegrees: number
      pitchDeltaDegrees: number
      distanceFactor: number
      fovDeltaDegrees: number
    }
  | {
      type: 'adjustLight'
      azimuthDeltaDegrees: number
      elevationDeltaDegrees: number
      intensityFactor: number
    }

export interface SimilarityAdjustment {
  id: string
  command: SceneAdjustmentCommand
  confidence: number
  reason: string
}

export interface SimilarityEvaluation {
  id: string
  layoutId: string
  sequence: number
  kind: 'baseline' | 'candidate'
  status: SimilarityEvaluationStatus
  attemptCount: number
  score: SimilarityScore | null
  adjustments: SimilarityAdjustment[]
  regenerationNotes: string[]
}

export interface SimilarityRun {
  id: string
  jobId: string
  model: string
  maxIterations: number
  currentIteration: number
  maxCalls: number
  status: SimilarityRunStatus
  failureCode: string | null
  createdAt: string
  completedAt: string | null
  evaluations: SimilarityEvaluation[]
}

export interface SimilarityStatus {
  eligible: boolean
  /** 버튼 비활성 사유 — 서버 문구를 그대로 보여준다 (§11.1). */
  blockingReasons: string[]
  activeLayoutId: string | null
  openRun: SimilarityRun | null
}

/** 후보 생성 응답 — 화면이 즉시 offscreen 캡처할 후보 배치를 함께 받는다 (§9.2 4단계). */
export interface SimilarityCandidate {
  run: SimilarityRun
  candidateLayout: {
    id: string
    revision: number
    instances: {
      partId: string
      ordinal: number
      position: { x: number; y: number; z: number }
      rotationY: number
      scale: number
      /** 축별 배율 (#20 §4.2) — 표면은 세 값이 다르다. 균일로 뭉개면 평가 렌더가 어긋난다. */
      scaleVector: { x: number; y: number; z: number }
    }[]
    camera: {
      position: { x: number; y: number; z: number }
      target: { x: number; y: number; z: number }
      fieldOfViewDegrees: number
    }
    light: {
      azimuthDegrees: number
      elevationDegrees: number
      keyIntensity: number
      keyColor: string
      ambientIntensity: number
      ambientColor: string
    }
  }
  evaluation: SimilarityEvaluation
}

/** revision 이력 한 줄 (§14.1 실행 이력) — 복원 대상 선택의 재료. */
export interface SceneRevision {
  id: string
  revision: number
  state: 'active' | 'candidate' | 'rejected' | 'superseded'
  origin: 'composed' | 'similarityAdjustment' | 'restore'
  composedAt: string
}

export const REVISION_ORIGIN_LABELS: Record<SceneRevision['origin'], string> = {
  composed: '자동 배치',
  similarityAdjustment: '유사도 보정',
  restore: '복원',
}

export interface SimilarityCostEstimate {
  maximumCalls: number
  inputTokensPerCall: number
  outputTokensPerCall: number
  /** null = 단가 미등록 — 0원이 아니다 (§11.2). */
  estimatedMaximumCostUsd: number | null
  priceKnown: boolean
}

/** 축 이름 — 색만으로 상태를 표현하지 않는다 (§14.1): 숫자·bar·텍스트를 함께 쓴다. */
export const DIMENSION_LABELS: Record<SimilarityDimensionKind, string> = {
  composition: '구성',
  camera: '카메라',
  scale: '스케일',
  shape: '형태',
  material: '재질',
  lighting: '조명',
}

/** 가중치 (§5.3) — 표시용. 합성은 서버만 한다. */
export const DIMENSION_WEIGHTS: Record<SimilarityDimensionKind, number> = {
  composition: 25,
  camera: 20,
  scale: 20,
  shape: 15,
  material: 10,
  lighting: 10,
}

/** 진행 중인가 — 폴링 지속 판정. */
export function isRunActive(run: SimilarityRun | null): boolean {
  return run !== null && !['completed', 'failed', 'canceled'].includes(run.status)
}

/** 기준 평가 — sequence 1 의 성공한 점수. 비교의 왼쪽 축이다. */
export function baselineScore(run: SimilarityRun | null): SimilarityScore | null {
  return (
    run?.evaluations.find((e) => e.kind === 'baseline' && e.status === 'succeeded')?.score ?? null
  )
}

/** confidence 0.75 이상만 기본 선택한다 (§6) — 낮아도 사용자가 직접 고를 수 있다. */
export function defaultSelectedAdjustments(adjustments: SimilarityAdjustment[]): string[] {
  return adjustments.filter((a) => a.confidence >= 0.75).map((a) => a.id)
}

/** 보정 명령의 사람용 요약 — 카드에 "무엇이 얼마나" 를 한 줄로. */
export function adjustmentSummary(command: SceneAdjustmentCommand): string {
  switch (command.type) {
    case 'scaleScene':
      return `장면 배율 ×${command.factor}`
    case 'moveInstance':
      return `이동 X ${signed(command.deltaX)}m · Z ${signed(command.deltaZ)}m`
    case 'rotateInstance':
      return `회전 ${signed(command.deltaDegrees)}°`
    case 'scaleInstance':
      return `크기 ×${command.factor}`
    case 'adjustCamera':
      return `카메라 yaw ${signed(command.yawDeltaDegrees)}° · 거리 ×${command.distanceFactor}`
    case 'adjustLight':
      return `광원 방위 ${signed(command.azimuthDeltaDegrees)}° · 세기 ×${command.intensityFactor}`
  }
}

function signed(value: number): string {
  return value > 0 ? `+${value}` : `${value}`
}
