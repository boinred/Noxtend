import { Link, useParams } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { useJob, useRetryTask, useCancelJob } from '@/app/queries/useJob'
import { apiErrorMessage } from '@/app/queries/errors'
import { jobStatusLabel, taskKindLabel, isTerminal } from '@/domain/job/types'
import { ROUTES } from '@/routes/paths'
import { PageContainer } from '../PageContainer'
import { SpriteInput } from './SpriteInput'
import { SpritePlanReview } from './SpritePlanReview'
import { SpriteFrameReview } from './SpriteFrameReview'
import { SpritePreview } from './SpritePreview'
import { SpriteExport } from './SpriteExport'
import { spriteStyles as styles } from './spriteStyles'

const PHASE_LABELS = {
  analyzing: '제작 대상 분석',
  planReview: '제작 계획 검수',
  baseGeneration: '기준 이미지 생성',
  baseReview: '기준 이미지 검수',
  frameGeneration: '애니메이션 프레임 생성',
  frameReview: '애니메이션 검수',
  exportReady: '내보내기 대기',
  packaging: '패키징',
  completed: '완료',
}

export function SpriteStudioScreen() {
  const { jobId } = useParams()
  const query = useJob(jobId)
  const retry = useRetryTask(jobId)
  const cancel = useCancelJob()
  const job = query.job
  const sprite = job?.sprite
  const canceled = job?.status === 'canceled'
  const active = job?.tasks.some((t) => t.status === 'running' || t.status === 'pending') ?? false
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
          <section className={styles.panel} aria-label="작업 상태">
            <div className={styles.row}>
              <p role="status">
                {jobStatusLabel(job.status)} · {PHASE_LABELS[sprite.phase]} · revision{' '}
                {sprite.reviewRevision}
              </p>
              <Button variant="outline" onClick={() => void query.refetch()}>
                서버 상태 새로고침
              </Button>
              {!isTerminal(job.status) ? (
                <Button
                  variant="destructive"
                  disabled={cancel.isPending || query.isError}
                  onClick={() => cancel.mutate(job.id)}
                >
                  작업 취소
                </Button>
              ) : null}
            </div>
            {canceled ? (
              <p className={styles.hint}>
                취소된 작업입니다. 이미 저장된 PNG와 ZIP은 내려받을 수 있습니다.
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
            {job.tasks.map((task) => (
              <div key={task.id} className={styles.row}>
                <span>
                  {taskKindLabel(task.kind)} · {jobStatusLabel(task.status)} · 시도{' '}
                  {task.attemptCount}
                </span>
                {task.failureReason ? (
                  <span className={styles.error}>{task.failureReason}</span>
                ) : null}
                {task.status === 'failed' && !canceled ? (
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
          {sprite.phase === 'planReview' && !canceled ? (
            <SpritePlanReview
              key={job.id}
              jobId={job.id}
              sourceImageId={job.sourceImageId}
              sprite={sprite}
              model={job.models.image?.model ?? '모델 미확인'}
              disabled={active || query.isError}
            />
          ) : null}
          {sprite.phase !== 'analyzing' && sprite.phase !== 'planReview' ? (
            <>
              <SpritePreview sprite={sprite} timeMs={0} playing={false} />
              <SpriteFrameReview
                key={job.id}
                jobId={job.id}
                sprite={sprite}
                tasks={job.tasks}
                disabled={canceled || packing || query.isError}
              />
              <SpriteExport
                key={job.id}
                jobId={job.id}
                sprite={sprite}
                approvedAssetIds={sprite.assets.filter((a) => a.approval !== null).map((a) => a.id)}
                disabled={canceled || packing || query.isError}
              />
            </>
          ) : null}
        </div>
      )}
    </PageContainer>
  )
}
