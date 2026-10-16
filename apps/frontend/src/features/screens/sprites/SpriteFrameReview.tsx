import { assetRole } from '@/domain/sprites/rules'
import { spriteRoleLabel } from '@/domain/sprites/labels'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Icon } from '@/features/shell/Icon'
import { spriteImageUrl } from '@/app/queries/media'
import {
  useApproveSpriteBases,
  useRegenerateSpriteFrame,
  useApproveSpriteAsset,
} from '@/app/queries/useSprites'
import { apiErrorMessage, apiErrorStatus } from '@/app/queries/errors'
import type { SpriteState } from '@/domain/sprites/types'
import type { JobTask } from '@/domain/job/types'
import { checkerboard, spriteStyles as styles } from './spriteStyles'

export function SpriteFrameReview({
  jobId,
  sprite,
  tasks,
  disabled,
}: {
  jobId: string
  sprite: SpriteState
  tasks: JobTask[]
  disabled: boolean
}) {
  const [selected, setSelected] = useState<string[]>([])
  const approve = useApproveSpriteBases()
  const regenerate = useRegenerateSpriteFrame()
  const approveAsset = useApproveSpriteAsset()
  const busy = disabled || approve.isPending || regenerate.isPending || approveAsset.isPending
  const reviewing =
    sprite.phase !== 'completed' && sprite.phase !== 'packaging' && sprite.phase !== 'planReview'
  const selectable = sprite.assets.filter(
    (a) =>
      a.frames[0]?.currentImageId &&
      reviewing &&
      a.approvedBaseImageId !== a.frames[0]?.currentImageId &&
      !tasks.some(
        (t) =>
          a.frames.some((f) => f.currentTaskId === t.id) &&
          (t.status === 'running' || t.status === 'pending'),
      ),
  )
  const ids = selected.filter((id) => selectable.some((a) => a.id === id))
  const context = () => ({
    jobId,
    requestId: crypto.randomUUID(),
    expectedRevision: sprite.reviewRevision,
  })
  return (
    <section className={styles.panel} aria-label="기준 이미지 검수">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-base font-semibold text-foreground">기준 이미지 검수</h2>
        <label className="flex min-h-10 items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="size-5 focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-2"
            checked={selectable.length > 0 && ids.length === selectable.length}
            ref={(input) => {
              if (input) input.indeterminate = ids.length > 0 && ids.length < selectable.length
            }}
            disabled={busy || selectable.length === 0}
            onChange={(e) => setSelected(e.target.checked ? selectable.map((a) => a.id) : [])}
          />
          전체 선택
        </label>
      </div>
      <p className={styles.hint}>
        체크무늬에서 투명 영역을 확인하고 승인할 대상을 선택해 주세요. 정적 대상은 기준 승인으로
        최종 승인됩니다.
      </p>
      <div className={styles.grid}>
        {sprite.assets.map((a) => {
          const imageId = a.frames[0]?.currentImageId
          const active = a.frames.some((f) =>
            tasks.some(
              (t) => t.id === f.currentTaskId && (t.status === 'pending' || t.status === 'running'),
            ),
          )
          const baseApproved = !!a.approvedBaseImageId && a.approvedBaseImageId === imageId
          const complete = baseApproved && a.frames.every((f) => f.currentImageId) && !active
          return (
            <article key={a.id} className={styles.asset}>
              <h3 className="font-semibold break-words">{a.plan.name}</h3>
              <span className={styles.hint}>
                {spriteRoleLabel(assetRole(sprite.settings, a.plan))}
              </span>
              {imageId ? (
                <>
                  <img
                    src={spriteImageUrl(jobId, imageId)}
                    alt={`${a.plan.name} 기준 이미지`}
                    className={styles.image}
                    style={checkerboard}
                  />
                </>
              ) : (
                <p className={styles.hint}>{active ? '기준 이미지 생성 중' : '기준 이미지 없음'}</p>
              )}
              <div className="flex flex-nowrap items-center gap-2">
                {imageId ? (
                  <Button asChild variant="outline" size="icon" className="size-10">
                    <a
                      href={spriteImageUrl(jobId, imageId)}
                      aria-label={`PNG 다운로드 · ${a.plan.name}`}
                      title="PNG 다운로드"
                    >
                      <Icon name="download" />
                    </a>
                  </Button>
                ) : null}
                {sprite.phase !== 'planReview' && sprite.phase !== 'packaging' ? (
                  <Button
                    variant="outline"
                    size="icon"
                    className="size-10"
                    aria-label={`${a.plan.name} 기준 재생성`}
                    title="기준 재생성"
                    disabled={busy || active}
                    onClick={() => {
                      if (
                        window.confirm(
                          `${a.plan.name} 기준 이미지를 재생성할까요? 기존 기준 승인과 최종 승인이 무효화됩니다.${a.plan.loop ? ' 후속 프레임도 무효화됩니다.' : ''} 기준 이미지를 다시 검수해야 합니다. 진행하시겠습니까?`,
                        )
                      ) {
                        regenerate.mutate({ ...context(), assetId: a.id, index: 0 })
                      }
                    }}
                  >
                    <Icon name="reset" />
                  </Button>
                ) : null}
                {!a.approval && reviewing && !baseApproved ? (
                  <label
                    className="ml-auto flex min-h-10 shrink-0 items-center justify-center gap-2 px-3 rounded-lg border border-border has-disabled:opacity-45"
                    title="기준 승인 선택"
                  >
                    <input
                      type="checkbox"
                      className="size-5 focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-2"
                      aria-label={`${a.plan.name} 기준 승인 선택`}
                      checked={ids.includes(a.id)}
                      disabled={busy || !selectable.some((asset) => asset.id === a.id)}
                      onChange={(e) =>
                        setSelected(
                          e.target.checked
                            ? [...selected, a.id]
                            : selected.filter((id) => id !== a.id),
                        )
                      }
                    />
                    승인
                  </label>
                ) : null}
              </div>
              {a.approval ? (
                <p className={styles.hint}>
                  최종 승인 완료 · {a.approval.snapshot.imageIds.length}프레임
                </p>
              ) : null}
              {a.plan.loop ? (
                <>
                  <p className={styles.hint}>
                    기준 재생성은 후속 프레임과 최종 승인을 무효화합니다. 기준 이미지를 다시 검수해
                    주세요.
                  </p>
                  <div className={styles.grid}>
                    {a.frames
                      .filter((f) => f.index > 0)
                      .map((frame) => {
                        const current = tasks.find((t) => t.id === frame.currentTaskId)
                        const running =
                          current?.status === 'running' || current?.status === 'pending'
                        return (
                          <div key={frame.index} className={styles.asset}>
                            <p>
                              프레임 {frame.index + 1} ·{' '}
                              {running
                                ? '생성 중'
                                : current?.status === 'failed'
                                  ? '실패'
                                  : frame.currentImageId
                                    ? '완료'
                                    : '누락'}
                            </p>
                            {current?.failureReason ? (
                              <p className={styles.error}>{current.failureReason}</p>
                            ) : null}
                            {frame.currentImageId ? (
                              <img
                                className={styles.image}
                                style={checkerboard}
                                src={spriteImageUrl(jobId, frame.currentImageId)}
                                alt={`${a.plan.name} 프레임 ${frame.index + 1}`}
                              />
                            ) : null}
                            <Button
                              variant="outline"
                              disabled={busy || running || !baseApproved}
                              onClick={() =>
                                regenerate.mutate({
                                  ...context(),
                                  assetId: a.id,
                                  index: frame.index,
                                })
                              }
                            >
                              {a.plan.name} 프레임 {frame.index + 1} 재생성
                            </Button>
                          </div>
                        )
                      })}
                  </div>
                </>
              ) : null}
              {!a.approval && baseApproved ? (
                <Button
                  disabled={busy || !complete}
                  onClick={() => approveAsset.mutate({ ...context(), assetId: a.id })}
                >
                  {a.plan.name} {a.plan.loop ? '애니메이션' : '최종'} 승인
                </Button>
              ) : null}
            </article>
          )
        })}
      </div>
      {selectable.length > 0 ? (
        <Button
          disabled={busy || ids.length === 0}
          onClick={() => approve.mutate({ ...context(), assetIds: ids })}
        >
          선택한 기준 이미지 승인
        </Button>
      ) : null}
      {[approve, regenerate, approveAsset].map((mutation, index) =>
        mutation.error ? (
          <p key={index} role="alert" className={styles.error}>
            {apiErrorStatus(mutation.error) === 409
              ? '검수 충돌: 서버 상태를 다시 조회했습니다. 확인 후 새 동작으로 승인해 주세요. '
              : ''}
            {apiErrorMessage(mutation.error, '요청 응답을 받지 못했습니다')}
          </p>
        ) : null,
      )}
      {approve.error &&
      approve.variables &&
      (apiErrorStatus(approve.error) === null ||
        apiErrorStatus(approve.error) === 0 ||
        (apiErrorStatus(approve.error) ?? 0) >= 500) ? (
        <Button
          variant="outline"
          disabled={busy}
          onClick={() => approve.mutate(approve.variables!)}
        >
          같은 기준 승인 재전송
        </Button>
      ) : null}
      {approveAsset.error &&
      approveAsset.variables &&
      (apiErrorStatus(approveAsset.error) === null ||
        apiErrorStatus(approveAsset.error) === 0 ||
        (apiErrorStatus(approveAsset.error) ?? 0) >= 500) ? (
        <Button
          variant="outline"
          disabled={busy}
          onClick={() => approveAsset.mutate(approveAsset.variables!)}
        >
          같은 애니메이션 승인 재전송
        </Button>
      ) : null}
      {regenerate.error &&
      regenerate.variables &&
      (apiErrorStatus(regenerate.error) === null ||
        apiErrorStatus(regenerate.error) === 0 ||
        (apiErrorStatus(regenerate.error) ?? 0) >= 500) ? (
        <Button
          variant="outline"
          disabled={busy}
          onClick={() => regenerate.mutate(regenerate.variables!)}
        >
          같은 재생성 요청 재전송
        </Button>
      ) : null}
    </section>
  )
}
