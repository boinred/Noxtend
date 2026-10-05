/**
 * Design Ref: character-studio UX/UI 고도화 및 DESIGN.md 규약 준수.
 * 7단계 파이프라인(장면 분석 → 파츠 추출 → 파츠 분해 → 파츠 검수 → 서술 재작성 → 4방향 생성 → 3D 재구성)의
 * 시각적 진행 요약(`X/7 · 현재 단계 · X% 완료`), 진한 완료 막대 & 실행 중 buffer,
 * 7단계 이상 상세 접기/펼치기, 실패/재시도 횟수 직관적 표시.
 */
import { useState } from 'react'
import { cn } from '@/lib/utils'
import { Icon } from '@/features/shell/Icon'
import type { JobTask, TaskKind } from '@/domain/job/types'

export interface CharacterPipelineStepperProps {
  tasks: JobTask[]
  jobStatus?: string
  className?: string
}

interface StepDefinition {
  kind: TaskKind | 'review'
  label: string
  shortLabel: string
}

const STAGES: StepDefinition[] = [
  { kind: 'analyze', label: '장면 분석', shortLabel: '분석' },
  { kind: 'extract', label: '파츠 추출', shortLabel: '추출' },
  { kind: 'decompose', label: '파츠 분해', shortLabel: '분해' },
  { kind: 'review', label: '파츠 검수', shortLabel: '검수' },
  { kind: 'rewriteDescriptions', label: '서술 재작성', shortLabel: '서술' },
  { kind: 'generate', label: '4방향 생성', shortLabel: '생성' },
  { kind: 'reconstruct', label: '3D 재구성', shortLabel: '3D' },
]

export type StepState =
  | 'succeeded'
  | 'running'
  | 'pendingReview'
  | 'partiallySucceeded'
  | 'failed'
  | 'canceled'
  | 'upcoming'

export function computeStepStates(
  tasks: JobTask[],
  jobStatus?: string,
): Record<StepDefinition['kind'], StepState> {
  const getStepState = (stepKind: StepDefinition['kind']): StepState => {
    if (stepKind === 'review') {
      if (jobStatus === 'pendingReview') return 'pendingReview'
      if (jobStatus === 'canceled') return 'canceled'
      const decomposeSucceeded = tasks.some(
        (t) => t.kind === 'decompose' && t.status === 'succeeded',
      )
      const laterTaskStarted = tasks.some(
        (t) =>
          ['rewriteDescriptions', 'generate', 'reconstruct'].includes(t.kind) &&
          t.status !== 'pending',
      )
      if (
        decomposeSucceeded &&
        (laterTaskStarted || jobStatus === 'succeeded' || jobStatus === 'partiallySucceeded')
      ) {
        return 'succeeded'
      }
      return 'upcoming'
    }

    const matchingTasks = tasks.filter((t) => t.kind === stepKind)
    if (matchingTasks.length === 0) return 'upcoming'

    if (matchingTasks.some((t) => t.status === 'canceled') || jobStatus === 'canceled') {
      if (matchingTasks.every((t) => t.status === 'succeeded')) {
        return 'succeeded'
      }
      return 'canceled'
    }

    if (matchingTasks.some((t) => t.status === 'failed')) {
      return jobStatus === 'partiallySucceeded' ? 'partiallySucceeded' : 'failed'
    }

    if (matchingTasks.some((t) => t.status === 'running')) {
      return 'running'
    }

    if (matchingTasks.every((t) => t.status === 'succeeded')) {
      return 'succeeded'
    }

    if (matchingTasks.some((t) => t.status === 'succeeded')) {
      return 'running'
    }

    return 'upcoming'
  }

  const result = {} as Record<StepDefinition['kind'], StepState>
  for (const stage of STAGES) {
    result[stage.kind] = getStepState(stage.kind)
  }
  return result
}

export function CharacterPipelineStepper({
  tasks,
  jobStatus,
  className,
}: CharacterPipelineStepperProps) {
  const stepStates = computeStepStates(tasks, jobStatus)
  const [expanded, setExpanded] = useState(true)

  const totalStages = STAGES.length
  let succeededCount = 0
  let currentStageIndex = 0
  let currentStageLabel = '공정 준비 중'
  let isRunningOrReview = false

  STAGES.forEach((stage, index) => {
    const state = stepStates[stage.kind]
    if (state === 'succeeded') {
      succeededCount++
    }
    if (state === 'running' || state === 'pendingReview') {
      currentStageIndex = index
      currentStageLabel = stage.label
      isRunningOrReview = true
    } else if (state === 'failed' || state === 'partiallySucceeded') {
      currentStageIndex = index
      currentStageLabel = `${stage.label} (오류)`
    }
  })

  if (!isRunningOrReview && succeededCount === totalStages) {
    currentStageIndex = totalStages - 1
    currentStageLabel = '모든 공정 완료'
  } else if (!isRunningOrReview && succeededCount > 0 && currentStageLabel === '공정 준비 중') {
    currentStageIndex = succeededCount - 1
    currentStageLabel = STAGES[currentStageIndex]?.label ?? '공정 진행'
  }

  const currentPosition =
    jobStatus === 'succeeded'
      ? totalStages
      : Math.min(succeededCount + (isRunningOrReview ? 1 : 0), totalStages)
  const completionPercent = Math.round((succeededCount / totalStages) * 100)

  const confirmedPercent = Math.round((succeededCount / totalStages) * 100)
  const bufferPercent = isRunningOrReview ? Math.round((1 / totalStages) * 100) : 0

  return (
    <nav
      aria-label="파이프라인 진행 상태"
      className={cn('rounded-xl border border-border bg-card p-4 shadow-xs', className)}
      data-testid="character-pipeline-stepper"
    >
      <div className="flex items-center justify-between gap-2">
        <div className="flex flex-col gap-0.5">
          <div className="flex items-center gap-2">
            <h3 className="text-xs font-bold uppercase tracking-wider text-primary">
              파이프라인 진행 상태
            </h3>
            <span className="text-xs font-semibold">{renderJobStatusBadge(jobStatus)}</span>
          </div>
          {/* DESIGN.md 81줄 규약: 현재 위치/전체 단계 · 현재 단계 · 완료율 */}
          <div className="font-mono text-xs font-medium text-foreground">
            {currentPosition}/{totalStages} · {currentStageLabel} · {completionPercent}% 완료
          </div>
        </div>

        <button
          type="button"
          onClick={() => setExpanded(!expanded)}
          className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium text-muted-foreground hover:bg-muted transition-colors"
          aria-expanded={expanded}
        >
          <span>{expanded ? '상세 접기' : '상세 보기'}</span>
          <Icon
            name="chevron-down"
            size={14}
            className={cn('transition-transform duration-200', expanded && 'rotate-180')}
          />
        </button>
      </div>

      {/* DESIGN.md 83줄 규약: 확정 완료는 진한 막대, 현재 실행 단계는 buffer로 구분 */}
      <div className="mt-3 flex h-2 w-full overflow-hidden rounded-full bg-muted">
        <div
          className="h-full bg-primary transition-all duration-300"
          style={{ width: `${confirmedPercent}%` }}
        />
        {bufferPercent > 0 ? (
          <div
            className="h-full bg-primary/40 animate-pulse transition-all duration-300"
            style={{ width: `${bufferPercent}%` }}
          />
        ) : null}
      </div>

      {/* DESIGN.md 85줄 규약: 7단계 이상은 접을 수 있는 상세 목록으로 전환 */}
      {expanded ? (
        <ol className="mt-3 grid grid-cols-7 gap-1.5 max-[768px]:grid-cols-4 max-[480px]:grid-cols-2">
          {STAGES.map((stage, idx) => {
            const state = stepStates[stage.kind]
            const matchingTasks = tasks.filter((t) => t.kind === stage.kind)
            const maxAttempts =
              matchingTasks.length > 0 ? Math.max(...matchingTasks.map((t) => t.attemptCount)) : 0

            return (
              <li
                key={stage.kind}
                className={cn(
                  'group relative flex flex-col items-center justify-center rounded-lg border p-2 text-center transition-colors duration-160',
                  stepStyle(state),
                )}
                data-testid={`stepper-step-${stage.kind}`}
                data-state={state}
              >
                <div className="mb-1 flex items-center justify-center gap-1 font-mono text-[0.6875rem] font-bold">
                  <span className="opacity-70">{idx + 1}.</span>
                  <StepIcon state={state} />
                </div>
                <span className="text-[0.75rem] font-semibold leading-tight">
                  {stage.shortLabel}
                </span>
                <span className="mt-0.5 text-[0.625rem] font-medium opacity-85">
                  {stepBadgeText(state)}
                </span>

                {/* DESIGN.md 87줄 규약: 실패와 재시도 횟수는 해당 단계 행에서 직접 보여준다 */}
                {maxAttempts > 1 ? (
                  <span className="mt-1 rounded-full bg-amber-500/15 px-1.5 py-0.5 text-[0.6rem] font-medium text-amber-700 dark:text-amber-300">
                    재시도 {maxAttempts}회
                  </span>
                ) : null}
              </li>
            )
          })}
        </ol>
      ) : null}
    </nav>
  )
}

function renderJobStatusBadge(status?: string) {
  switch (status) {
    case 'running':
      return (
        <span className="inline-flex items-center gap-1.5 text-primary">
          <Icon name="play" size={13} className="animate-spin" /> 공정 진행 중
        </span>
      )
    case 'pendingReview':
      return (
        <span className="inline-flex items-center gap-1.5 text-amber-600 dark:text-amber-400">
          <Icon name="user" size={13} /> 검수 대기 중
        </span>
      )
    case 'succeeded':
      return (
        <span className="inline-flex items-center gap-1.5 text-primary">
          <Icon name="check" size={13} /> 공정 완료
        </span>
      )
    case 'partiallySucceeded':
      return (
        <span className="inline-flex items-center gap-1.5 text-amber-600 dark:text-amber-400">
          <Icon name="info" size={13} /> 일부 완료
        </span>
      )
    case 'failed':
      return (
        <span className="inline-flex items-center gap-1.5 text-destructive">
          <Icon name="close" size={13} /> 공정 오류
        </span>
      )
    case 'canceled':
      return (
        <span className="inline-flex items-center gap-1.5 text-muted-foreground">
          <Icon name="stop" size={13} /> 작업 취소됨
        </span>
      )
    default:
      return <span className="text-muted-foreground">공정 준비 중</span>
  }
}

function StepIcon({ state }: { state: StepState }) {
  switch (state) {
    case 'succeeded':
      return <Icon name="check" size={13} />
    case 'running':
      return <Icon name="play" size={13} className="animate-spin" />
    case 'pendingReview':
      return <Icon name="user" size={13} />
    case 'partiallySucceeded':
      return <Icon name="info" size={13} />
    case 'failed':
      return <Icon name="close" size={13} />
    case 'canceled':
      return <Icon name="stop" size={13} />
    case 'upcoming':
      return <span className="inline-block size-1.5 rounded-full bg-current opacity-40" />
  }
}

function stepBadgeText(state: StepState): string {
  switch (state) {
    case 'succeeded':
      return '완료'
    case 'running':
      return '진행 중'
    case 'pendingReview':
      return '검수 대기'
    case 'partiallySucceeded':
      return '부분 성공'
    case 'failed':
      return '실패'
    case 'canceled':
      return '취소됨'
    case 'upcoming':
      return '대기'
  }
}

function stepStyle(state: StepState): string {
  switch (state) {
    case 'succeeded':
      return 'border-primary/30 bg-primary/10 text-primary'
    case 'running':
      return 'border-primary/40 bg-primary/15 text-primary'
    case 'pendingReview':
      return 'border-amber-500/40 bg-amber-500/10 text-amber-600 dark:text-amber-400'
    case 'partiallySucceeded':
      return 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400'
    case 'failed':
      return 'border-destructive/40 bg-destructive/10 text-destructive'
    case 'canceled':
      return 'border-muted-foreground/30 bg-muted/20 text-muted-foreground'
    case 'upcoming':
      return 'border-border/60 bg-muted/40 text-muted-foreground/70'
  }
}
