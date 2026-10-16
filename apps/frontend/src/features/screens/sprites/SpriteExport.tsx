import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Icon } from '@/features/shell/Icon'
import { spriteExportUrl } from '@/app/queries/media'
import { useExportSprites } from '@/app/queries/useSprites'
import { apiErrorMessage, apiErrorStatus } from '@/app/queries/errors'
import type { SpriteState } from '@/domain/sprites/types'
import { spriteStyles as styles } from './spriteStyles'

export function SpriteExport({
  jobId,
  sprite,
  approvedAssetIds,
  disabled,
}: {
  jobId: string
  sprite: SpriteState
  approvedAssetIds: string[]
  disabled: boolean
}) {
  const [selected, setSelected] = useState<string[]>([])
  const mutation = useExportSprites()
  const eligible = sprite.assets.filter((a) => approvedAssetIds.includes(a.id))
  const ids = selected.filter((id) => eligible.some((a) => a.id === id))
  const busy = disabled || mutation.isPending
  const excluded = sprite.assets.filter((a) => !ids.includes(a.id))
  return (
    <section className={styles.panel} aria-label="승인 결과 내보내기">
      <h2 className={styles.title}>승인 결과 내보내기</h2>
      <p className={styles.hint}>승인된 대상 중 ZIP에 포함할 항목을 선택해 주세요.</p>
      <div className="my-4 flex flex-wrap items-center gap-x-4 gap-y-2">
        <label className="flex min-h-10 items-center gap-2 text-sm has-disabled:cursor-not-allowed has-disabled:opacity-60">
          <input
            type="checkbox"
            className="size-5 accent-primary focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-2"
            checked={eligible.length > 0 && ids.length === eligible.length}
            ref={(input) => {
              if (input) input.indeterminate = ids.length > 0 && ids.length < eligible.length
            }}
            disabled={busy || eligible.length === 0}
            onChange={(e) => setSelected(e.target.checked ? eligible.map((a) => a.id) : [])}
          />
          전체 선택
        </label>
        <p className={styles.hint}>
          선택 {ids.length} / 승인됨 {eligible.length}개
        </p>
        <div className="ml-auto">
          <Button
            variant={
              sprite.phase === 'exportReady' || sprite.phase === 'completed' ? 'default' : 'outline'
            }
            disabled={busy || ids.length === 0}
            onClick={() =>
              mutation.mutate({
                jobId,
                requestId: crypto.randomUUID(),
                expectedRevision: sprite.reviewRevision,
                assetIds: ids,
              })
            }
          >
            내보내기
          </Button>
        </div>
      </div>
      <div className="grid min-w-0 grid-cols-2 gap-2 max-[720px]:grid-cols-1">
        {sprite.assets.map((a) => {
          const approved = eligible.some((item) => item.id === a.id)
          const checked = ids.includes(a.id)
          return (
            <label
              key={a.id}
              className={`flex min-h-11 min-w-0 items-center gap-3 rounded-lg border px-3 py-2 has-disabled:cursor-not-allowed has-disabled:opacity-60 ${checked ? 'border-primary bg-primary/10' : 'border-border bg-[var(--sunken-bg)]'}`}
            >
              <span className="min-w-0 flex-1 text-sm text-foreground break-words">
                {a.plan.name}
              </span>
              <span className="shrink-0 rounded border border-border px-1.5 py-0.5 text-xs text-muted-foreground">
                {approved ? '승인됨' : '미승인'}
              </span>
              <input
                type="checkbox"
                aria-label={`${a.plan.name} 내보내기 선택`}
                className="size-5 shrink-0 accent-primary focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-2"
                disabled={busy || !approved}
                checked={checked}
                onChange={(e) =>
                  setSelected(e.target.checked ? [...ids, a.id] : ids.filter((id) => id !== a.id))
                }
              />
            </label>
          )
        })}
      </div>
      <div className="my-4 flex min-w-0 flex-col gap-2">
        <p className={styles.hint}>
          포함: {ids.length}개 · 제외: {excluded.length}개
        </p>
        {excluded.length > 0 ? (
          <>
            <details className={styles.hint}>
              <summary className="w-fit cursor-pointer rounded focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-2">
                제외 대상 {excluded.length}개
              </summary>
              <ul className="mt-2 list-inside list-disc break-words">
                {excluded.map((a) => (
                  <li key={a.id}>{a.plan.name}</li>
                ))}
              </ul>
            </details>
            <p className={styles.hint}>
              제외 대상이 있으면 현재 실행·대기 공정이 끝난 뒤 부분 성공으로 종료합니다.
            </p>
          </>
        ) : (
          <p className={styles.hint}>제외 대상 없음</p>
        )}
        {mutation.isPending ? (
          <p role="status" className={styles.hint}>
            패키징 요청 중…
          </p>
        ) : null}
      </div>
      {mutation.error ? (
        <p role="alert" className={styles.error}>
          {apiErrorStatus(mutation.error) === 409
            ? '내보내기 충돌: 서버 상태를 다시 조회했습니다. 선택을 확인한 뒤 새 동작으로 요청해 주세요. '
            : ''}
          {apiErrorMessage(mutation.error, '패키징 응답을 받지 못했습니다')}
        </p>
      ) : null}
      {mutation.error &&
      mutation.variables &&
      (apiErrorStatus(mutation.error) === null ||
        apiErrorStatus(mutation.error) === 0 ||
        (apiErrorStatus(mutation.error) ?? 0) >= 500) ? (
        <Button
          variant="outline"
          disabled={disabled || mutation.isPending}
          onClick={() => mutation.mutate(mutation.variables!)}
        >
          같은 내보내기 요청 재전송
        </Button>
      ) : null}
      <div className="mt-5 border-t border-border pt-4">
        <h3 className="mb-2 text-sm font-semibold text-foreground">저장된 ZIP</h3>
        <p className={`${styles.hint} mb-3`}>
          저장된 ZIP은 현재 공정과 관계없이 바로 내려받을 수 있습니다.
        </p>
        {sprite.exports.length > 0 ? (
          <div className={styles.stack}>
            {sprite.exports.map((item) => (
              <a
                key={item.id}
                className={`${styles.link} flex min-h-10 items-start gap-2`}
                href={spriteExportUrl(jobId, item.id)}
              >
                <span className="shrink-0 pt-0.5">
                  <Icon name="download" />
                </span>
                <span>
                  {item.isCurrent ? '현재 ZIP 다운로드' : '이전 ZIP 다운로드'} · {item.createdAt} ·
                  포함 {item.includedAssetIds.length} / 제외 {item.excludedAssetIds.length}
                </span>
              </a>
            ))}
          </div>
        ) : (
          <p className={styles.hint}>
            저장된 ZIP이 없습니다. 패키징 완료 후 다운로드할 수 있습니다.
          </p>
        )}
      </div>
    </section>
  )
}
