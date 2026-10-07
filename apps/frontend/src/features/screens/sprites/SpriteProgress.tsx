import type { ReactElement } from 'react'
import { jobStatusLabel, type Job } from '@/domain/job/types'
import { spriteProgressCounts } from '@/domain/sprites/rules'
import type { SpritePhase, SpriteState } from '@/domain/sprites/types'
import type { IconName } from '@/features/shell/Icon'
import { spritePhaseLabel } from '../categoryLabels'
import {
  JobProgressPanel,
  type JobProgressActions,
  type JobProgressStep,
  type JobProgressSummary,
} from '../JobProgressPanel'

export function SpriteProgress({
  job,
  sprite,
  ...actions
}: { job: Job; sprite: SpriteState } & JobProgressActions): ReactElement {
  const counts = spriteProgressCounts(sprite, job.tasks)
  const total = sprite.assets.length
  const staticReview = counts.loops === 0 && sprite.phase === 'frameReview'
  const steps: { phase: SpritePhase; label: string; icon: IconName; done: boolean }[] = [
    { phase: 'analyzing', label: '분석', icon: 'search', done: counts.analysis === 1 },
    { phase: 'planReview', label: '계획 검수', icon: 'prompt', done: false },
    {
      phase: 'baseGeneration',
      label: '배경 생성',
      icon: 'image',
      done: total > 0 && counts.backgrounds === total,
    },
    {
      phase: 'baseReview',
      label: staticReview ? '최종 검수' : '기준 검수',
      icon: 'eye',
      done:
        total > 0 &&
        counts.approvedBases === total &&
        sprite.assets.every((asset) => asset.plan.loop || asset.approval !== null),
    },
  ]
  if (counts.loops > 0) {
    steps.push(
      {
        phase: 'frameGeneration',
        label: '프레임 생성',
        icon: 'play',
        done: counts.frameTotal > 0 && counts.frames === counts.frameTotal,
      },
      {
        phase: 'frameReview',
        label: '최종 검수',
        icon: 'eye',
        done: counts.loops > 0 && counts.approvedLoops === counts.loops,
      },
    )
  }
  steps.push({
    phase: 'exportReady',
    label: '내보내기',
    icon: 'upload',
    done: counts.exported === 1,
  })
  const phase =
    sprite.phase === 'packaging' || sprite.phase === 'completed'
      ? 'exportReady'
      : staticReview
        ? 'baseReview'
        : sprite.phase
  const current = steps.findIndex((step) => step.phase === phase)
  steps[1]!.done = current > 1
  const completed = steps.map(
    (step, index) =>
      step.done && (index < current || (index === current && sprite.phase === 'completed')),
  )
  const summaries: JobProgressSummary[] = [
    {
      id: 'analysis',
      label: '원본 분석 성공',
      value: `${counts.analysis} / 1`,
      hint: '원본 이미지',
      icon: 'search' as const,
    },
    ...(counts.loops > 0
      ? [
          {
            id: 'frames',
            label: '프레임 생성 성공',
            value: `${counts.frames} / ${counts.frameTotal}`,
            hint: '기준 이미지 제외',
            icon: 'play' as const,
          },
        ]
      : []),
    {
      id: 'backgrounds',
      label: '배경 생성 성공',
      value: total > 0 ? `${counts.backgrounds} / ${total}` : '대상 확인 중',
      hint: total > 0 ? '현재 기준 이미지' : '분석 후 대상 확정',
      icon: 'image' as const,
    },
  ]

  const progressSteps: JobProgressStep[] = steps.map((step, index) => {
    const active = index === current
    const done = completed[index]!
    return {
      id: step.phase,
      label: step.label,
      icon: done ? 'check' : active ? step.icon : index < current ? 'info' : null,
      state: active ? job.status : done ? 'succeeded' : index < current ? 'incomplete' : 'upcoming',
      completed: done,
      statusText: active
        ? jobStatusLabel(job.status)
        : done
          ? '완료'
          : index < current
            ? '미완료'
            : '대기',
    }
  })

  return (
    <JobProgressPanel
      status={job.status}
      steps={progressSteps}
      currentStepId={phase}
      currentPosition={current + 1}
      currentLabel={staticReview ? '최종 이미지 검수' : spritePhaseLabel(sprite.phase)}
      revision={sprite.reviewRevision}
      summaries={summaries}
      navigationLabel="2D 제작 단계"
      {...actions}
    />
  )
}
