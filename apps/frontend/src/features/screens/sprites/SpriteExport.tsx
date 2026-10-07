import { useState } from 'react'
import { Button } from '@/components/ui/button'
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
  const ids = selected.filter((id) => approvedAssetIds.includes(id))
  const excluded = sprite.assets.filter((a) => !ids.includes(a.id))
  return (
    <section className={styles.panel} aria-label="승인 결과 내보내기">
      <h2 className={styles.title}>승인 결과 내보내기</h2>
      <p className={styles.hint}>
        내보낼 승인 대상을 명시적으로 선택해 주세요. 저장된 ZIP은 바로 내려받을 수 있습니다. 제외
        대상이 있으면 현재 실행·대기 공정이 끝난 뒤 부분 성공으로 종료합니다.
      </p>
      <div className={styles.stack}>
        {sprite.assets.map((a) => (
          <label key={a.id} className={styles.row}>
            <input
              type="checkbox"
              disabled={disabled || mutation.isPending || !approvedAssetIds.includes(a.id)}
              checked={ids.includes(a.id)}
              onChange={(e) =>
                setSelected(
                  e.target.checked ? [...selected, a.id] : selected.filter((id) => id !== a.id),
                )
              }
            />
            {a.plan.name} 내보내기 선택 {approvedAssetIds.includes(a.id) ? '· 승인됨' : '· 미승인'}
          </label>
        ))}
      </div>
      <p className={styles.hint}>
        포함: {ids.length}개 · 제외 대상:{' '}
        {excluded.length ? excluded.map((a) => a.plan.name).join(', ') : '없음'}
      </p>
      <Button
        variant={
          sprite.phase === 'exportReady' || sprite.phase === 'completed' ? 'default' : 'outline'
        }
        disabled={disabled || mutation.isPending || ids.length === 0}
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
      {sprite.exports.length > 0 ? (
        <div className={styles.stack}>
          {sprite.exports.map((item) => (
            <a key={item.id} className={styles.link} href={spriteExportUrl(jobId, item.id)}>
              {item.isCurrent ? '현재 ZIP 다운로드' : '이전 ZIP 다운로드'} · {item.createdAt} · 포함{' '}
              {item.includedAssetIds.length} / 제외 {item.excludedAssetIds.length}
            </a>
          ))}
        </div>
      ) : (
        <p className={styles.hint}>저장된 ZIP이 없습니다. 패키징 완료 후 다운로드할 수 있습니다.</p>
      )}
    </section>
  )
}
