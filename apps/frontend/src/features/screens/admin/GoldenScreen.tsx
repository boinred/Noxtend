/**
 * Design Ref: §5.1 · §4.2 #20~22 — 골든 세트.
 *
 * **평가의 기준선이다** (Plan D-4). 고정된 이미지에 대해 "이런 파츠들이 나와야 한다" 를
 * 사람이 적어두고, 프롬프트를 바꿀 때마다 같은 이미지를 돌려 비교한다.
 *
 * 기대를 구조화하지 않는다 — 무엇이 좋은 분해인지 아직 모르므로(D-3) 지금 형식을
 * 굳히면 잘못된 기준을 고착시킨다. 사람이 읽고 사람이 판단한다.
 */
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageContainer } from '@/features/screens/PageContainer'
import { Button } from '@/components/ui/button'
import { AdminTabs } from './AdminTabs'
import { useGoldenMutations, useGoldenSamples } from '@/app/queries/useTuning'
import { apiErrorMessage } from '@/app/queries/errors'
import { sourceImageUrl } from '@/app/queries/media'
import { adminGoldenRunsPath } from '@/routes/paths'
import { adminStyles as styles } from './adminStyles'

export function GoldenScreen() {
  const { samples, isLoading } = useGoldenSamples()
  const { create, remove } = useGoldenMutations()

  const [draft, setDraft] = useState({ storedImageId: '', name: '', expectedNote: '' })
  const [error, setError] = useState<string | null>(null)

  async function handleCreate() {
    setError(null)

    try {
      await create.mutateAsync(draft)
      setDraft({ storedImageId: '', name: '', expectedNote: '' })
    } catch (cause) {
      setError(apiErrorMessage(cause, '골든 샘플을 등록하지 못했습니다'))
    }
  }

  return (
    <PageContainer
      width="max"
      title="골든 세트"
      subtitle="프롬프트를 바꿀 때마다 같은 이미지를 돌려 비교합니다. 기대는 사람이 적고 사람이 읽습니다."
      testId="golden-screen"
    >
      <AdminTabs />

      <div className={`${styles.panel} ${styles.goldenForm}`} data-testid="golden-form">
        <div className={styles.field}>
          <label className={styles.label} htmlFor="golden-image">
            업로드 id
          </label>
          <input
            id="golden-image"
            className={styles.input}
            value={draft.storedImageId}
            onChange={(event) => setDraft({ ...draft, storedImageId: event.target.value })}
            placeholder="스튜디오에서 한 번 돌려본 이미지의 id"
            data-testid="golden-image-input"
          />
          {/* 이미지를 새로 받지 않는 이유는 저장 경로가 둘이 되면 둘 다 관리해야 하기 때문이다 */}
          <p className={styles.hint}>
            스튜디오에서 이미 올린 이미지를 재사용합니다. 화면을 꽉 채우는 배경 아트여야 좌표가
            의미를 갖습니다.
          </p>
        </div>

        <div className={styles.field}>
          <label className={styles.label} htmlFor="golden-name">
            이름
          </label>
          <input
            id="golden-name"
            className={styles.input}
            value={draft.name}
            onChange={(event) => setDraft({ ...draft, name: event.target.value })}
            placeholder="예: 안개 낀 성문"
            data-testid="golden-name-input"
          />
        </div>

        <div className={styles.field}>
          <label className={styles.label} htmlFor="golden-expected">
            기대 — 이 이미지에서 무엇이 나와야 하는가
          </label>
          <textarea
            id="golden-expected"
            className={styles.textarea}
            rows={4}
            value={draft.expectedNote}
            onChange={(event) => setDraft({ ...draft, expectedNote: event.target.value })}
            placeholder="예: 성문·문루·성벽·탑이 나와야 하고, 안개는 파츠가 아니다"
            data-testid="golden-expected-input"
          />
        </div>

        {error ? (
          <p className={styles.error} role="alert" data-testid="golden-error">
            {error}
          </p>
        ) : null}

        <div className={styles.formActions}>
          <Button
            variant="default"
            onClick={handleCreate}
            disabled={create.isPending || draft.name.trim().length === 0}
            data-testid="golden-create"
          >
            {create.isPending ? '등록 중…' : '골든 샘플 등록'}
          </Button>
        </div>
      </div>

      {isLoading ? null : samples.length === 0 ? (
        <p className={styles.empty} data-testid="golden-empty">
          아직 골든 샘플이 없습니다. 기준선이 없으면 프롬프트를 바꿔도 좋아졌는지 알 수 없습니다.
        </p>
      ) : (
        <ul className={styles.goldenList} data-testid="golden-list">
          {samples.map((sample) => (
            <li key={sample.id} className={styles.goldenItem} data-testid="golden-item">
              <img
                className={styles.goldenThumb}
                src={sourceImageUrl(sample.storedImageId)}
                alt=""
              />
              <div className={styles.goldenBody}>
                <Link to={adminGoldenRunsPath(sample.id)} className={styles.goldenName}>
                  {sample.name}
                </Link>
                <p className={styles.goldenExpected}>{sample.expectedNote}</p>
              </div>
              <Button
                variant="destructive"
                onClick={() => remove.mutate(sample.id)}
                data-testid="golden-delete"
              >
                삭제
              </Button>
            </li>
          ))}
        </ul>
      )}
    </PageContainer>
  )
}
