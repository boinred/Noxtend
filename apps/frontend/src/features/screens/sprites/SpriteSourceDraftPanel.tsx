import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import {
  useEnhanceSpriteSource,
  useGenerateSpriteSourceCandidate,
  useSaveSpriteSourcePrompt,
  useSelectSpriteSource,
} from '@/app/queries/useSprites'
import { apiErrorMessage } from '@/app/queries/errors'
import { spriteSourceCandidateUrl } from '@/app/queries/media'
import { activeSourceTask, type JobTask } from '@/domain/job/types'
import {
  SPRITE_SOURCE_CANDIDATE_LIMIT,
  type SpriteSourceDraft,
  type SpriteState,
} from '@/domain/sprites/types'
import { spriteOutputLabel, spriteViewLabel } from '@/domain/sprites/labels'
import { ROUTES } from '@/routes/paths'
import { spriteStyles as styles } from './spriteStyles'

const between = (text: string, max: number) => text.length >= 1 && text.length <= max

export function SpriteSourceDraftPanel({
  jobId,
  sprite,
  source,
  tasks,
  canceled,
  disabled,
}: {
  jobId: string
  sprite: SpriteState
  source: SpriteSourceDraft
  tasks: JobTask[]
  canceled: boolean
  disabled: boolean
}) {
  const saved = source.prompt ?? ''
  const [draft, setDraft] = useState({ revision: sprite.reviewRevision, prompt: saved, saved })
  const dirty = draft.prompt !== draft.saved
  // 미저장 프롬프트 보존, 저장 반영 또는 변경 없는 draft만 동기화
  if (draft.revision !== sprite.reviewRevision && (!dirty || draft.prompt === saved))
    setDraft({ revision: sprite.reviewRevision, prompt: saved, saved })
  const [description, setDescription] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)
  const enhance = useEnhanceSpriteSource()
  const save = useSaveSpriteSourcePrompt()
  const generate = useGenerateSpriteSourceCandidate()
  const select = useSelectSpriteSource()
  const pending = enhance.isPending || save.isPending || generate.isPending || select.isPending
  // 진행 표시는 상단 JobProgressPanel 담당
  const locked = disabled || canceled || pending || activeSourceTask(tasks) !== undefined
  const prompt = draft.prompt.trim()
  const full = source.candidates.length >= SPRITE_SOURCE_CANDIDATE_LIMIT
  const selected = source.candidates.some((candidate) => candidate.id === selectedId)
  // 화면에 보이는 서버 상태 기준 revision
  function context() {
    setError(null)
    return { jobId, requestId: crypto.randomUUID(), expectedRevision: sprite.reviewRevision }
  }
  const failed = { onError: (cause: unknown) => setError(cause) }
  function reenhance(text: string) {
    if (
      (source.prompt || prompt) &&
      !window.confirm('다시 보완하면 현재 프롬프트가 새 결과로 바뀝니다')
    )
      return
    setDraft((current) => ({ ...current, prompt: current.saved }))
    enhance.mutate(
      { ...context(), description: text },
      { ...failed, onSuccess: () => setDescription(null) },
    )
  }
  return (
    <section className={styles.panel} aria-label="원본 준비">
      <div className={styles.stack}>
        <div className="flex flex-wrap items-baseline justify-between gap-3">
          <h2 className={`${styles.title} mb-0`}>원본 준비</h2>
          {canceled ? (
            <Link to={ROUTES.spriteBackground} className={styles.link}>
              새 2D 작업
            </Link>
          ) : null}
        </div>
        <p data-testid="sprite-source-settings" className={styles.hint}>
          {spriteViewLabel(sprite.settings.view)} · {spriteOutputLabel(sprite.settings)}
        </p>
        <div className={styles.field}>
          <span>장면 설명</span>
          {description === null ? (
            <div className="flex min-w-0 items-start justify-between gap-3 max-[720px]:flex-col">
              <p
                data-testid="sprite-source-description"
                className="min-w-0 text-sm leading-relaxed whitespace-pre-wrap wrap-anywhere text-foreground"
              >
                {source.description}
              </p>
              <Button
                variant="outline"
                disabled={locked}
                onClick={() => setDescription(source.description)}
              >
                다시 보완
              </Button>
            </div>
          ) : (
            <>
              <textarea
                data-testid="sprite-source-description-input"
                aria-label="장면 설명 수정"
                aria-describedby="sprite-source-description-count"
                className={styles.textarea}
                value={description}
                disabled={locked}
                autoFocus
                onChange={(event) => setDescription(event.target.value)}
              />
              <span id="sprite-source-description-count" className={styles.hint}>
                {description.trim().length}/1000
              </span>
              <div className={styles.row}>
                <Button
                  variant="outline"
                  disabled={locked || !between(description.trim(), 1000)}
                  onClick={() => reenhance(description.trim())}
                >
                  보완 실행
                </Button>
                <Button variant="ghost" disabled={pending} onClick={() => setDescription(null)}>
                  취소
                </Button>
              </div>
            </>
          )}
        </div>
        <div className={styles.field}>
          <label htmlFor="sprite-source-prompt">보완 프롬프트</label>
          <textarea
            id="sprite-source-prompt"
            data-testid="sprite-source-prompt"
            className={styles.textarea}
            value={draft.prompt}
            disabled={locked}
            aria-describedby="sprite-source-prompt-count"
            onChange={(event) => setDraft({ ...draft, prompt: event.target.value })}
          />
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span id="sprite-source-prompt-count" className={styles.hint}>
              {prompt.length}/4000
            </span>
            <Button
              variant="outline"
              disabled={locked || !between(prompt, 4000) || prompt === saved}
              onClick={() => save.mutate({ ...context(), prompt }, failed)}
            >
              저장
            </Button>
          </div>
          {source.prompt === null && !canceled && activeSourceTask(tasks) === undefined ? (
            <p className={styles.hint}>프롬프트를 직접 입력해 생성할 수 있습니다</p>
          ) : null}
        </div>
        {source.candidates.length > 0 ? (
          <div data-testid="sprite-source-candidates" className={styles.results}>
            {source.candidates.map((candidate, index) => (
              <div key={candidate.id} className="flex min-w-0 flex-col gap-1.5">
                <button
                  type="button"
                  data-testid="sprite-source-candidate"
                  className={styles.result}
                  aria-pressed={candidate.id === selectedId}
                  aria-label={`원본 후보 ${index + 1} 선택`}
                  disabled={locked}
                  onClick={() => setSelectedId(candidate.id === selectedId ? null : candidate.id)}
                >
                  <img
                    src={spriteSourceCandidateUrl(jobId, candidate.id)}
                    alt=""
                    className={styles.image}
                  />
                </button>
                <details className="min-w-0 text-xs text-muted-foreground">
                  <summary className="cursor-pointer">사용한 프롬프트</summary>
                  <p className="mt-1 leading-relaxed whitespace-pre-wrap wrap-anywhere">
                    {candidate.prompt}
                  </p>
                </details>
              </div>
            ))}
          </div>
        ) : (
          <p className={styles.hint}>아직 원본 후보가 없습니다</p>
        )}
        <div className={styles.row}>
          <Button
            variant={selected ? 'outline' : 'default'}
            disabled={locked || full || !between(prompt, 4000)}
            onClick={() => generate.mutate({ ...context(), prompt }, failed)}
          >
            원본 후보 생성
          </Button>
          <Button
            variant={selected ? 'default' : 'outline'}
            disabled={locked || !selected}
            onClick={() => select.mutate({ ...context(), candidateId: selectedId! }, failed)}
          >
            이 원본으로 분석
          </Button>
        </div>
        {full ? (
          <p className={styles.hint}>후보는 최대 {SPRITE_SOURCE_CANDIDATE_LIMIT}장입니다</p>
        ) : null}
        {error ? (
          <p role="alert" className={styles.error}>
            {apiErrorMessage(error, '요청에 실패했습니다. 다시 시도해 주세요')}
          </p>
        ) : null}
      </div>
    </section>
  )
}
