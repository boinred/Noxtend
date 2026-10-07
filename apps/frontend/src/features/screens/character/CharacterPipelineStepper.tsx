import type { ReactElement } from 'react'
import { cn } from '@/lib/utils'
import type { IconName } from '@/features/shell/Icon'
import { JobProgressPanel, type JobProgressActions } from '../JobProgressPanel'
import { productionProgressSummaries } from '../productionProgress'
import type { Job, JobTask, TaskKind } from '@/domain/job/types'

export type CharacterPipelineStepperProps = {
  job: Job
  className?: string
} & JobProgressActions

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
  job,
  className,
  ...actions
}: CharacterPipelineStepperProps): ReactElement {
  const stepStates = computeStepStates(job.tasks, job.status)
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
    job.status === 'succeeded'
      ? totalStages
      : Math.min(succeededCount + (isRunningOrReview ? 1 : 0), totalStages)

  return (
    <div
      className={cn('rounded-xl border border-border bg-card p-4 shadow-xs', className)}
      data-testid="character-pipeline-stepper"
    >
      <JobProgressPanel
        {...actions}
        status={job.status}
        steps={STAGES.map((stage) => ({
          id: stage.kind,
          label: stage.shortLabel,
          icon: STEP_ICONS[stepStates[stage.kind]],
          state: stepStates[stage.kind],
          completed: stepStates[stage.kind] === 'succeeded',
          statusText: stepBadgeText(stepStates[stage.kind]),
          testId: `stepper-step-${stage.kind}`,
        }))}
        currentStepId={STAGES[currentStageIndex]!.kind}
        currentPosition={currentPosition}
        currentLabel={currentStageLabel}
        summaries={productionProgressSummaries(job, '방향 이미지 생성 성공')}
        navigationLabel="파이프라인 진행 상태"
      />
    </div>
  )
}

const STEP_ICONS: Record<StepState, IconName | null> = {
  succeeded: 'check',
  running: 'clock',
  pendingReview: 'eye',
  partiallySucceeded: 'info',
  failed: 'close',
  canceled: 'stop',
  upcoming: null,
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
