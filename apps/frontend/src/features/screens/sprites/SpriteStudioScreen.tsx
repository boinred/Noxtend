import { Link, useParams } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { useJob, useRetryTask, useCancelJob } from '@/app/queries/useJob'
import { apiErrorMessage } from '@/app/queries/errors'
import { useProviders } from '@/app/queries/useProviders'
import {
  jobStatusLabel,
  taskKindLabel,
  taskStatusGroups,
  isTerminal,
  isSourceTask,
  type TaskStatusGroup,
} from '@/domain/job/types'
import { ROUTES } from '@/routes/paths'
import { PageContainer } from '../PageContainer'
import { ModelSummary } from '../ModelSummary'
import { SpriteAnalysisPanel } from './SpriteAnalysisPanel'
import { SpriteInput } from './SpriteInput'
import { SpritePlanReview } from './SpritePlanReview'
import { SpriteFrameReview } from './SpriteFrameReview'
import { SpritePreview } from './SpritePreview'
import { SpriteExport } from './SpriteExport'
import { SpriteProgress } from './SpriteProgress'
import { SpriteSourceCompare } from './SpriteSourceCompare'
import { SpriteSourceDraftPanel } from './SpriteSourceDraftPanel'
import { spriteStyles as styles } from './spriteStyles'

const SUMMARY_STATUSES = ['running', 'pending', 'succeeded', 'failed', 'canceled'] as const

export function taskGroupSummary(group: TaskStatusGroup): string {
  const parts = SUMMARY_STATUSES.filter((status) => group[status] > 0).map(
    (status) => `${jobStatusLabel(status)} ${group[status]}`,
  )
  if (group.retrying > 0) parts.push(`재시도 중 ${group.retrying}`)
  return [taskKindLabel(group.kind), `전체 ${group.total}`, ...parts].join(' · ')
}

export function SpriteStudioScreen() {
  const { jobId } = useParams()
  const query = useJob(jobId)
  const retry = useRetryTask(jobId)
  const cancel = useCancelJob()
  const { providers } = useProviders()
  const job = query.job
  const sprite = job?.sprite
  const currentFrameTasks = new Set(
    sprite?.assets.flatMap((a) => a.frames.map((f) => f.currentTaskId)),
  )
  const canceled = job?.status === 'canceled'
  const drafting = sprite?.phase === 'sourceDraft'
  const analyzed = sprite?.phase !== 'analyzing' && !drafting
  // 원본 준비 공정은 최신 Ordinal 하나만 current
  const currentSourceTask = drafting
    ? job?.tasks
        .filter(isSourceTask)
        .sort((a, b) => a.ordinal - b.ordinal)
        .at(-1)
    : undefined
  const relevantTasks = (job?.tasks ?? []).filter(
    (task) =>
      (task.kind !== 'generateSprite' || currentFrameTasks.has(task.id)) &&
      (!isSourceTask(task) || task.id === currentSourceTask?.id),
  )
  const taskGroups = taskStatusGroups(relevantTasks)
  const packTask = job?.tasks.filter((task) => task.kind === 'packSprites').at(-1)
  const packing =
    sprite?.phase === 'packaging' &&
    (packTask?.status === 'running' || packTask?.status === 'pending')
  return (
    <PageContainer
      key={jobId ?? 'input'}
      width="max"
      title="2D 배경 스튜디오"
      subtitle="이미지에서 배경 레이어와 반복 타일을 만들고 기준 이미지를 검수합니다."
      testId="sprite-studio"
    >
      {!jobId ? (
        <SpriteInput />
      ) : query.isNotFound && !job ? (
        <div role="alert" className={styles.panel}>
          <p>작업을 찾을 수 없습니다.</p>
          <Link to={ROUTES.spriteBackground}>새 2D 작업 시작</Link>
        </div>
      ) : query.isError && !job ? (
        <div role="alert" className={styles.panel}>
          <p className={styles.error}>
            {apiErrorMessage(query.error, '작업을 불러올 수 없습니다. 연결을 확인해 주세요')}
          </p>
          <Button variant="outline" onClick={() => void query.refetch()}>
            작업 다시 조회
          </Button>
        </div>
      ) : query.isLoading ? (
        <p role="status">작업을 불러오는 중…</p>
      ) : !sprite || !job || job.productionMode !== 'twoD' ? (
        <p role="alert">이 주소는 2D 작업에만 사용할 수 있습니다.</p>
      ) : (
        <div className={styles.stack}>
          <section className={`${styles.panel} flex flex-col gap-4`} aria-label="작업 상태">
            <SpriteProgress
              job={job}
              sprite={sprite}
              onRefresh={() => void query.refetch()}
              onCancel={
                !isTerminal(job.status)
                  ? () => {
                      if (
                        window.confirm(
                          drafting
                            ? '원본 준비를 취소하면 이 작업에서 더 생성할 수 없습니다. 만든 후보는 볼 수 있습니다.'
                            : '작업을 취소할까요?\n\n이미 저장된 PNG와 ZIP은 계속 내려받을 수 있습니다.\n취소한 작업의 추가 생성과 검수는 진행할 수 없습니다.',
                        )
                      ) {
                        cancel.mutate(job.id)
                      }
                    }
                  : undefined
              }
              cancelDisabled={cancel.isPending || query.isError}
            />
            {canceled ? (
              <p className={styles.hint}>
                {drafting
                  ? '취소된 작업입니다. 만든 후보는 볼 수 있습니다.'
                  : '취소된 작업입니다. 이미 저장된 PNG와 ZIP은 내려받을 수 있습니다.'}
              </p>
            ) : null}
            {query.isError ? (
              <div role="alert">
                <p className={styles.error}>
                  {apiErrorMessage(query.error, '작업을 다시 조회할 수 없습니다')}
                </p>
                <p className={styles.hint}>
                  마지막 조회 결과와 미저장 편집을 유지했습니다. 다시 조회한 뒤 작업을 이어가세요.
                </p>
                <Button variant="outline" onClick={() => void query.refetch()}>
                  작업 다시 조회
                </Button>
              </div>
            ) : null}
            {job.failureReason ? (
              <p role="alert" className={styles.error}>
                {job.failureReason}
              </p>
            ) : null}
            {taskGroups
              .filter((group) => group.total > group.succeeded)
              .map((group) => (
                <p
                  key={group.kind}
                  data-testid="sprite-task-summary"
                  className="text-sm tabular-nums break-words"
                >
                  {taskGroupSummary(group)}
                </p>
              ))}
            {relevantTasks
              .filter((task) => task.status === 'failed')
              .map((task) => (
                <div key={task.id} className={styles.row}>
                  <span>
                    {taskKindLabel(task.kind)} · {jobStatusLabel(task.status)} · 시도{' '}
                    {task.attemptCount}
                  </span>
                  {task.failureReason ? (
                    <span className={styles.error}>{task.failureReason}</span>
                  ) : null}
                  {!canceled ? (
                    <Button
                      variant="outline"
                      disabled={retry.isPending || query.isError}
                      onClick={() => retry.mutate(task.id)}
                    >
                      실패 공정 재시도
                    </Button>
                  ) : null}
                </div>
              ))}
            {retry.error || cancel.error ? (
              <p role="alert" className={styles.error}>
                {apiErrorMessage(
                  retry.error ?? cancel.error,
                  '요청에 실패했습니다. 다시 시도해 주세요',
                )}
              </p>
            ) : null}
            {packing ? (
              <p role="status">ZIP 패키징 중입니다. 완료된 파일만 다운로드할 수 있습니다.</p>
            ) : null}
          </section>
          {/* 모델은 접수 시점 고정 — 분석 중에도 표시 */}
          <ModelSummary models={job.models} providers={providers} className="mb-0" />
          {drafting && sprite.source ? (
            <SpriteSourceDraftPanel
              key={job.id}
              jobId={job.id}
              sprite={sprite}
              source={sprite.source}
              tasks={job.tasks}
              canceled={canceled}
              disabled={query.isError}
            />
          ) : null}
          {analyzed ? <SpriteAnalysisPanel sprite={sprite} /> : null}
          {analyzed && !canceled && job.sourceImageId !== null ? (
            <SpritePlanReview
              key={job.id}
              jobId={job.id}
              sourceImageId={job.sourceImageId}
              sprite={sprite}
              model={job.models.image?.model ?? '모델 미확인'}
              tasks={job.tasks}
              disabled={packing || query.isError}
            />
          ) : null}
          {analyzed && (sprite.phase !== 'planReview' || sprite.images.length > 0) ? (
            <>
              <SpritePreview sprite={sprite} timeMs={0} playing={false} />
              {job.sourceImageId !== null ? (
                <SpriteSourceCompare
                  jobId={job.id}
                  sourceImageId={job.sourceImageId}
                  sprite={sprite}
                />
              ) : null}
              <SpriteFrameReview
                key={job.id}
                jobId={job.id}
                sprite={sprite}
                tasks={job.tasks}
                disabled={canceled || packing || query.isError || sprite.phase === 'planReview'}
              />
              <SpriteExport
                key={job.id}
                jobId={job.id}
                sprite={sprite}
                approvedAssetIds={sprite.assets.filter((a) => a.approval !== null).map((a) => a.id)}
                disabled={canceled || packing || query.isError || sprite.phase === 'planReview'}
              />
            </>
          ) : null}
        </div>
      )}
    </PageContainer>
  )
}
