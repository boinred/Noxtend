import { useId, useState, type ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { jobStatusLabel, type Job, type JobStatus } from '@/domain/job/types'
import { spriteProgressCounts } from '@/domain/sprites/rules'
import type { SpritePhase, SpriteState } from '@/domain/sprites/types'
import { Icon, type IconName } from '@/features/shell/Icon'
import { cn } from '@/lib/utils'
import { spritePhaseLabel } from '../categoryLabels'

const statusTone = (status: JobStatus) =>
  status === 'failed'
    ? 'text-destructive'
    : status === 'pendingReview' || status === 'partiallySucceeded'
      ? 'text-[var(--accent-amber)]'
      : status === 'canceled'
        ? 'text-muted-foreground'
        : 'text-primary'

const STATUS_ICONS: Record<JobStatus, IconName> = {
  pending: 'clock',
  running: 'clock',
  pendingReview: 'eye',
  succeeded: 'check',
  partiallySucceeded: 'info',
  failed: 'close',
  canceled: 'stop',
}

export function SpriteProgress({
  job,
  sprite,
  children,
}: {
  job: Job
  sprite: SpriteState
  children: ReactNode
}) {
  const [expanded, setExpanded] = useState(true)
  const detailsId = useId()
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
  const completedCount = completed.filter(Boolean).length
  const completionPercent = Math.round((completedCount / steps.length) * 100)
  const summaries = [
    {
      label: '원본 분석 성공',
      value: `${counts.analysis} / 1`,
      hint: '원본 이미지',
      icon: 'search' as const,
    },
    ...(counts.loops > 0
      ? [
          {
            label: '프레임 생성 성공',
            value: `${counts.frames} / ${counts.frameTotal}`,
            hint: '기준 이미지 제외',
            icon: 'play' as const,
          },
        ]
      : []),
    {
      label: '배경 생성 성공',
      value: total > 0 ? `${counts.backgrounds} / ${total}` : '대상 확인 중',
      hint: total > 0 ? '현재 기준 이미지' : '분석 후 대상 확정',
      icon: 'image' as const,
    },
  ]

  return (
    <div className="flex min-w-0 flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div role="status" className="flex min-w-0 flex-col gap-0.5">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="text-xs font-bold tracking-wider text-primary">파이프라인 진행 상태</h3>
            <span
              className={cn(
                'inline-flex items-center gap-1.5 text-xs font-semibold',
                statusTone(job.status),
              )}
            >
              <Icon name={STATUS_ICONS[job.status]} size={13} />
              {jobStatusLabel(job.status)}
            </span>
          </div>
          <p className="font-mono text-xs font-medium text-foreground">
            {current + 1}/{steps.length} ·{' '}
            {staticReview ? '최종 이미지 검수' : spritePhaseLabel(sprite.phase)} ·{' '}
            {completionPercent}% 단계 완료
            <span className="ml-2 text-muted-foreground">· revision {sprite.reviewRevision}</span>
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {children}
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="text-xs"
            aria-expanded={expanded}
            aria-controls={detailsId}
            onClick={() => setExpanded(!expanded)}
          >
            {expanded ? '상세 접기' : '상세 보기'}
            <Icon name="chevron-down" size={14} className={expanded ? 'rotate-180' : undefined} />
          </Button>
        </div>
      </div>
      <div
        role="progressbar"
        aria-label="완료된 제작 단계"
        aria-valuemin={0}
        aria-valuemax={steps.length}
        aria-valuenow={completedCount}
        aria-valuetext={`${completedCount} / ${steps.length} 단계 완료`}
        className="h-2 w-full overflow-hidden rounded-full bg-muted"
      >
        <div className="h-full bg-primary" style={{ width: `${completionPercent}%` }} />
      </div>
      <nav aria-label="2D 제작 단계">
        <ol
          id={detailsId}
          hidden={!expanded}
          className="grid grid-cols-[repeat(auto-fit,minmax(8rem,1fr))] gap-1.5"
        >
          {steps.map((step, index) => {
            const active = index === current
            const done = completed[index]
            return (
              <li
                key={step.phase}
                aria-current={active ? 'step' : undefined}
                className={cn(
                  'flex min-w-0 items-center justify-center gap-1.5 rounded-lg border px-2 py-2 text-center',
                  active
                    ? cn('border-current/40 bg-current/10', statusTone(job.status))
                    : done
                      ? 'border-primary/30 bg-primary/10 text-primary'
                      : index < current
                        ? 'border-amber-500/30 bg-amber-500/10 text-[var(--accent-amber)]'
                        : 'border-border/60 bg-muted/40 text-muted-foreground',
                )}
              >
                <span className="shrink-0 font-mono text-[0.6875rem] font-bold opacity-70">
                  {index + 1}.
                </span>
                {done || active || index < current ? (
                  <Icon name={done ? 'check' : active ? step.icon : 'info'} size={13} />
                ) : (
                  <span
                    aria-hidden="true"
                    className="size-1.5 rounded-full bg-current opacity-40"
                  />
                )}
                <p className="text-xs font-semibold leading-tight whitespace-nowrap">
                  {step.label}
                </p>
                <p className="sr-only">
                  {active
                    ? jobStatusLabel(job.status)
                    : done
                      ? '완료'
                      : index < current
                        ? '미완료'
                        : '대기'}
                </p>
              </li>
            )
          })}
        </ol>
      </nav>
      <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-3 border-t border-border pt-3">
        {summaries.map((summary) => (
          <div key={summary.label} className="flex items-center gap-2 max-[720px]:last:ml-auto">
            <Icon name={summary.icon} size={16} className="shrink-0 text-muted-foreground" />
            <dl className="flex items-center gap-2">
              <dt className="text-sm whitespace-nowrap text-muted-foreground max-[720px]:text-xs">
                {summary.label}
              </dt>
              <dd
                aria-label={summary.label}
                className="text-base font-semibold whitespace-nowrap text-foreground tabular-nums max-[720px]:text-sm"
              >
                {summary.value}
              </dd>
              <dd className="sr-only">{summary.hint}</dd>
            </dl>
          </div>
        ))}
      </div>
    </div>
  )
}
