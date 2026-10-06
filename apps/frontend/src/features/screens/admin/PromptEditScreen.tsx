/**
 * Design Ref: §5.1 · §4.2 #18·#19 · §12.2 — 프롬프트 편집과 버전 이력.
 *
 * **저장이 활성화가 아니다.** 새 버전은 비활성으로 만들어지고, 켜는 것은 별도 동작이다.
 * 저장이 곧 반영이면 편집 중의 실수가 즉시 운영에 나간다 (FR-09).
 *
 * 롤백에 별도 기능이 없다 — 이전 버전을 켜는 것이 곧 롤백이다. 버전이 불변이라
 * 되돌릴 상태가 항상 남아 있다.
 *
 * 카테고리는 `?category=` 로 온다. 생략이면 기본 슬롯이다. 카테고리마다 이력·활성이
 * 독립이고, 행이 하나도 없는 카테고리에서 저장하면 그 카테고리의 첫 프롬프트가 된다 (§12.2).
 */
import { useEffect, useRef, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { Link } from 'react-router-dom'
import { PageContainer } from '@/features/screens/PageContainer'
import { Button } from '@/components/ui/button'
import { AdminTabs } from './AdminTabs'
import { usePromptMutations, usePromptVersions } from '@/app/queries/useTuning'
import { apiErrorMessage } from '@/app/queries/errors'
import { ASSET_CATEGORIES, assetCategoryLabel } from '@/domain/job/types'
import { promptKindCategories, promptKindLabel } from '@/domain/tuning/types'
import { adminPromptEditPath } from '@/routes/paths'
import { adminStyles as styles } from './adminStyles'
import type { AssetCategory } from '@/domain/job/types'
import type { PromptKind } from '@/domain/tuning/types'

const EMPTY_DRAFT = { system: '', user: '', jsonSchema: '', note: '' }

/** `?category=` 파싱 — 정의된 값만 통과, 그 외(생략·오타·숫자)는 기본 슬롯 */
function parseCategory(raw: string | null): AssetCategory | null {
  return ASSET_CATEGORIES.find((c) => c === raw) ?? null
}

function categoryLabel(category: AssetCategory | null): string {
  return category ? assetCategoryLabel(category) : '기본'
}

export function PromptEditScreen() {
  const { kind } = useParams<{ kind: string }>()
  const stage = (kind ?? 'analyze') as PromptKind

  const [params] = useSearchParams()
  // 배경 전용 분석·평가 슬롯의 기본 카테고리 선택
  const allowedCategories = promptKindCategories(stage)
  const requested = parseCategory(params.get('category'))
  const category = allowedCategories.includes(requested) ? requested : allowedCategories[0]!

  const { versions, isLoading } = usePromptVersions(stage, category)
  const { create, activate } = usePromptMutations(stage, category)

  const [draft, setDraft] = useState(EMPTY_DRAFT)
  const [error, setError] = useState<string | null>(null)
  // 어느 카테고리로 초안을 채웠는지 — 카테고리를 바꾸면 그 슬롯의 활성으로 다시 채운다
  const seededCategory = useRef<AssetCategory | null | undefined>(undefined)

  const active = versions.find((version) => version.isActive)

  // 활성 버전을 초안의 출발점으로 삼는다. 빈 화면에서 시작하면 매번 처음부터 쓰게 된다.
  // 카테고리가 바뀌면 다시 채우고, 같은 카테고리 안에서 사용자가 고친 뒤에는 덮어쓰지 않는다
  useEffect(() => {
    if (isLoading) return
    if (seededCategory.current === category) return

    setDraft(
      active
        ? { system: active.system, user: active.user, jsonSchema: active.jsonSchema, note: '' }
        : EMPTY_DRAFT,
    )
    seededCategory.current = category
  }, [category, active, isLoading])

  async function handleSave() {
    setError(null)

    try {
      await create.mutateAsync({
        system: draft.system,
        user: draft.user,
        jsonSchema: draft.jsonSchema,
        note: draft.note.trim() || undefined,
      })
    } catch (cause) {
      // 변수 오타는 여기서 걸린다 — 서버가 쓸 수 있는 변수까지 알려준다
      setError(apiErrorMessage(cause, '새 버전을 저장하지 못했습니다'))
    }
  }

  return (
    <PageContainer
      width="max"
      title={`${promptKindLabel(stage)} 프롬프트`}
      subtitle="저장하면 새 버전이 됩니다. 켜기 전까지는 실행에 영향이 없습니다."
      testId="prompt-edit-screen"
    >
      <AdminTabs />

      {/* 카테고리 선택 — 각 슬롯의 이력·활성은 독립이다. 없는 슬롯을 골라 첫 프롬프트를 만든다(§12.2) */}
      <nav className="mb-4 flex flex-wrap gap-1.5" data-testid="prompt-category-tabs">
        {allowedCategories.map((option) => {
          const selected = option === category

          return (
            <Link
              key={option ?? 'default'}
              to={adminPromptEditPath(stage, option)}
              data-testid="prompt-category-tab"
              data-category={option ?? 'default'}
              data-selected={selected}
              aria-current={selected ? 'page' : undefined}
              className={
                selected
                  ? 'rounded-md border border-primary bg-primary/10 px-3 py-1.5 text-sm font-semibold text-primary'
                  : 'rounded-md border border-border px-3 py-1.5 text-sm text-muted-foreground'
              }
            >
              {categoryLabel(option)}
            </Link>
          )
        })}
      </nav>

      {!isLoading && versions.length === 0 ? (
        // 행이 없는 카테고리 — 저장이 곧 첫 프롬프트다 (§12.2 진입점)
        <p className={styles.hint} data-testid="prompt-empty-category">
          {categoryLabel(category)} 카테고리에는 아직 프롬프트가 없습니다. 저장하면 첫 버전이
          됩니다.
        </p>
      ) : null}

      {active ? (
        <p className={styles.hint} data-testid="prompt-variables-hint">
          쓸 수 있는 변수:{' '}
          {active.allowedVariables.length > 0
            ? active.allowedVariables.map((v) => `{{${v}}}`).join(' · ')
            : '없음 (이 단계는 변수를 쓰지 않습니다)'}
        </p>
      ) : null}

      <div className={styles.field}>
        <label className={styles.label} htmlFor="prompt-system">
          System
        </label>
        <textarea
          id="prompt-system"
          className={styles.textarea}
          rows={12}
          value={draft.system}
          onChange={(event) => setDraft({ ...draft, system: event.target.value })}
          data-testid="prompt-system-input"
        />
      </div>

      <div className={styles.field}>
        <label className={styles.label} htmlFor="prompt-user">
          User
        </label>
        <textarea
          id="prompt-user"
          className={styles.textarea}
          rows={3}
          value={draft.user}
          onChange={(event) => setDraft({ ...draft, user: event.target.value })}
          data-testid="prompt-user-input"
        />
      </div>

      <div className={styles.field}>
        <label className={styles.label} htmlFor="prompt-schema">
          JSON Schema
        </label>
        <textarea
          id="prompt-schema"
          className={styles.textarea}
          rows={8}
          value={draft.jsonSchema}
          onChange={(event) => setDraft({ ...draft, jsonSchema: event.target.value })}
          data-testid="prompt-schema-input"
        />
      </div>

      <div className={styles.field}>
        <label className={styles.label} htmlFor="prompt-note">
          메모 — 무엇을 바꿨는가
        </label>
        <input
          id="prompt-note"
          className={styles.input}
          value={draft.note}
          onChange={(event) => setDraft({ ...draft, note: event.target.value })}
          placeholder="예: 가림 관계 지시를 명시적으로"
          data-testid="prompt-note-input"
        />
      </div>

      {error ? (
        <p className={styles.error} role="alert" data-testid="prompt-error">
          {error}
        </p>
      ) : null}

      <div className={styles.formActions}>
        <Button
          variant="default"
          onClick={handleSave}
          disabled={create.isPending || draft.system.trim().length === 0}
          data-testid="prompt-save"
        >
          {create.isPending ? '저장 중…' : '새 버전으로 저장'}
        </Button>
      </div>

      <section className={styles.historySection}>
        <h2 className={styles.historyTitle}>버전 이력 · {categoryLabel(category)}</h2>

        {isLoading ? null : (
          <ul className={styles.history} data-testid="prompt-history">
            {versions.map((version) => (
              <li key={version.id} className={styles.historyItem} data-testid="prompt-version">
                <div className={styles.historyHead}>
                  <span className={styles.version}>v{version.version}</span>
                  {version.isActive ? (
                    <span className={styles.activeBadge} data-testid="prompt-version-active">
                      활성
                    </span>
                  ) : (
                    <Button
                      variant="secondary"
                      onClick={() => activate.mutate(version.id)}
                      disabled={activate.isPending}
                      data-testid="prompt-activate"
                    >
                      이 버전 켜기
                    </Button>
                  )}
                  <span className={styles.historyNote}>{version.note ?? '—'}</span>
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>
    </PageContainer>
  )
}
