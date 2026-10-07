import { useId, useState, type ReactElement } from 'react'
import { Button } from '@/components/ui/button'
import { jobStatusLabel, type JobStatus } from '@/domain/job/types'
import { Icon, type IconName } from '@/features/shell/Icon'
import { cn } from '@/lib/utils'

export type JobProgressActions = {
  onRefresh: () => void
  onCancel?: () => void
  cancelDisabled?: boolean
}
export type JobProgressStep = {
  id: string
  label: string
  icon: IconName | null
  state: JobStatus | 'upcoming' | 'incomplete'
  completed: boolean
  statusText: string
  testId?: string
}
export type JobProgressSummary = {
  id: string
  label: string
  value: string
  hint: string
  icon: IconName
}
export type JobProgressPanelProps = JobProgressActions & {
  status: JobStatus
  steps: readonly JobProgressStep[]
  currentStepId: string
  currentPosition: number
  currentLabel: string
  revision?: number
  summaries: readonly JobProgressSummary[]
  navigationLabel: string
}

const statusTone = (status: JobProgressStep['state']) =>
  status === 'failed'
    ? 'text-destructive'
    : status === 'pendingReview' || status === 'partiallySucceeded' || status === 'incomplete'
      ? 'text-[var(--accent-amber)]'
      : status === 'canceled' || status === 'upcoming'
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

export function JobProgressPanel({
  status,
  steps,
  currentStepId,
  currentPosition,
  currentLabel,
  revision,
  summaries,
  navigationLabel,
  onRefresh,
  onCancel,
  cancelDisabled,
}: JobProgressPanelProps): ReactElement {
  const [expanded, setExpanded] = useState(true)
  const detailsId = useId()
  const completedCount = steps.filter((step) => step.completed).length
  const completionPercent = Math.round((completedCount / steps.length) * 100)

  return (
    <div className="flex min-w-0 flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div role="status" className="flex min-w-0 flex-col gap-0.5">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="text-xs font-bold tracking-wider text-primary">파이프라인 진행 상태</h3>
            <span
              className={cn(
                'inline-flex items-center gap-1.5 text-xs font-semibold',
                statusTone(status),
              )}
            >
              <Icon name={STATUS_ICONS[status]} size={13} />
              {jobStatusLabel(status)}
            </span>
          </div>
          <p className="font-mono text-xs font-medium text-foreground">
            {currentPosition}/{steps.length} · {currentLabel} · {completionPercent}% 단계 완료
            {revision !== undefined ? (
              <span className="ml-2 text-muted-foreground">· revision {revision}</span>
            ) : null}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="outline"
            size="icon-lg"
            className="rounded-full p-0"
            aria-label="서버 상태 새로고침"
            title="서버 상태 새로고침"
            onClick={onRefresh}
          >
            <Icon name="reset" size={15} />
          </Button>
          {onCancel ? (
            <Button
              variant="destructive"
              size="icon-lg"
              className="rounded-full p-0"
              aria-label="작업 취소"
              title="작업 취소"
              disabled={cancelDisabled}
              onClick={onCancel}
            >
              <Icon name="stop" size={15} />
            </Button>
          ) : null}
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
      <nav aria-label={navigationLabel}>
        <ol
          id={detailsId}
          hidden={!expanded}
          className="grid grid-cols-[repeat(auto-fit,minmax(8rem,1fr))] gap-1.5"
        >
          {steps.map((step, index) => {
            const active = step.id === currentStepId
            const done = step.completed
            return (
              <li
                key={step.id}
                data-testid={step.testId}
                data-state={step.state}
                aria-current={active ? 'step' : undefined}
                className={cn(
                  'flex min-w-0 items-center justify-center gap-1.5 rounded-lg border px-2 py-2 text-center',
                  active
                    ? cn('border-current/40 bg-current/10', statusTone(step.state))
                    : done
                      ? 'border-primary/30 bg-primary/10 text-primary'
                      : step.state === 'incomplete'
                        ? 'border-amber-500/30 bg-amber-500/10 text-[var(--accent-amber)]'
                        : 'border-border/60 bg-muted/40 text-muted-foreground',
                )}
              >
                <span className="shrink-0 font-mono text-[0.6875rem] font-bold opacity-70">
                  {index + 1}.
                </span>
                {step.icon ? (
                  <Icon name={step.icon} size={13} />
                ) : (
                  <span
                    aria-hidden="true"
                    className="size-1.5 rounded-full bg-current opacity-40"
                  />
                )}
                <p className="text-xs font-semibold leading-tight whitespace-nowrap">
                  {step.label}
                </p>
                <p className="sr-only">{step.statusText}</p>
              </li>
            )
          })}
        </ol>
      </nav>
      <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-3 border-t border-border pt-3">
        {summaries.map((summary) => (
          <div key={summary.id} className="flex items-center gap-2 max-[720px]:last:ml-auto">
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
