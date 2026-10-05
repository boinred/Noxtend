/**
 * Design Ref: docs/02-design/features/review-gate-staged.design.md 사이클 1 §3.5 — 서술 확인 단계.
 *
 * 상자 확정·재작성 뒤, 생성 전에 모델러가 파츠 서술과 장면 팔레트를 원본 이미지와 대조해
 * 고친다. 캔버스는 읽기 전용이고 행에 마우스를 올리면 그 상자만 강조된다.
 */
import { useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import {
  useConfirmDescriptions,
  useEditReviewDescription,
  useEditReviewPalette,
  useReturnToBoxes,
} from '@/app/queries/useReview'
import { sourceImageUrl } from '@/app/queries/media'
import { apiErrorMessage } from '@/app/queries/errors'
import { Icon } from '@/features/shell/Icon'
import { PartsOverlay } from './PartsOverlay'
import {
  descriptionChips,
  emptyDescriptionParts,
  normalizeHex,
  sortForDescriptionReview,
  thumbnailStyle,
} from './descriptionReview'
import { backgroundStyles as styles } from './backgroundStyles'
import type { PaletteEntry, ReviewPart, ReviewState } from '@/domain/job/types'

export interface DescriptionsReviewProps {
  jobId: string
  sourceImageId: string
  review: ReviewState
}

const THUMBNAIL_MAX = 64

// 브라우저 EyeDropper API (Chrome·Edge) — 표준 lib 타입에 없음
type EyeDropperCtor = new () => { open: () => Promise<{ sRGBHex: string }> }
const EyeDropper = (globalThis as { EyeDropper?: EyeDropperCtor }).EyeDropper

export function DescriptionsReview({ jobId, sourceImageId, review }: DescriptionsReviewProps) {
  const confirm = useConfirmDescriptions(jobId)
  const returnBack = useReturnToBoxes(jobId)
  const [highlighted, setHighlighted] = useState<string | null>(null)
  const imageAspect = useImageAspect(sourceImageId)

  // 저장 안 된 편집이 있는 행 — 저장 중이거나 실패했거나 아직 blur 하지 않은 상태를 전부
  // 포함한다. 독립 리뷰 #2: blur 저장 실패 뒤 포커스 없이 다시 클릭하면 두 번째 blur 가
  // 안 일어나 옛 서술로 확정될 수 있었다. "서버 값과 다른 행이 남아 있으면 막는다" 로
  // 저장 진행 중·실패 상태를 따로 추적하지 않고 한 번에 막는다
  const [dirtyPartIds, setDirtyPartIds] = useState(new Set<string>())
  const setDirty = (partId: string, isDirty: boolean) =>
    setDirtyPartIds((current) => {
      if (isDirty === current.has(partId)) return current
      const next = new Set(current)
      if (isDirty) {
        next.add(partId)
      } else {
        next.delete(partId)
      }
      return next
    })
  const [paletteDirty, setPaletteDirty] = useState(false)

  const empty = emptyDescriptionParts(review.parts)
  const emptyIds = new Set(empty.map((part) => part.id))

  return (
    <div data-testid="review-descriptions">
      <PartsOverlay
        sourceImageId={sourceImageId}
        parts={review.parts.map((part) => ({ ...part, label: `${part.partRef} · ${part.name}` }))}
        alwaysShowBoxes
        highlightedPartId={highlighted}
      />

      <PaletteEditor jobId={jobId} palette={review.palette} onDirtyChange={setPaletteDirty} />

      <ul className="mt-4 flex flex-col gap-2" data-testid="description-rows">
        {sortForDescriptionReview(review.parts).map((part) => (
          <DescriptionRow
            key={part.id}
            jobId={jobId}
            part={part}
            thumbnail={
              part.placements[0] && imageAspect
                ? thumbnailStyle(part.placements[0], imageAspect, THUMBNAIL_MAX)
                : null
            }
            imageUrl={sourceImageUrl(sourceImageId)}
            isEmpty={emptyIds.has(part.id)}
            onHover={setHighlighted}
            onDirtyChange={setDirty}
          />
        ))}
      </ul>

      <div className={styles.actions}>
        <Button
          variant="ghost"
          onClick={() => returnBack.mutate()}
          disabled={returnBack.isPending || confirm.isPending}
          data-testid="review-return-to-boxes"
        >
          상자로 돌아가기
        </Button>
        <Button
          onClick={() => confirm.mutate()}
          disabled={confirm.isPending || empty.length > 0 || dirtyPartIds.size > 0 || paletteDirty}
          data-testid="review-confirm-descriptions"
        >
          {confirm.isPending ? '시작 중' : `생성 시작 (${review.parts.length}개 파츠)`}
        </Button>
      </div>

      {confirm.isError || returnBack.isError ? (
        <p className={styles.failureReason} data-testid="review-descriptions-error">
          {apiErrorMessage(confirm.error ?? returnBack.error, '요청을 처리하지 못했습니다')}
        </p>
      ) : null}
    </div>
  )
}

// 원본 가로/세로 비율 — 썸네일 크롭 비율 계산용
function useImageAspect(sourceImageId: string): number | null {
  const [aspect, setAspect] = useState<number | null>(null)

  useEffect(() => {
    const image = new Image()
    image.onload = () => {
      if (image.naturalHeight > 0) setAspect(image.naturalWidth / image.naturalHeight)
    }
    image.src = sourceImageUrl(sourceImageId)
  }, [sourceImageId])

  return aspect
}

interface DescriptionRowProps {
  jobId: string
  part: ReviewPart
  thumbnail: ReturnType<typeof thumbnailStyle> | null
  imageUrl: string
  isEmpty: boolean
  onHover: (partId: string | null) => void
  /** 독립 리뷰 #2 — 저장 안 된 편집이 있는 동안 부모가 생성 시작을 막는다. */
  onDirtyChange: (partId: string, isDirty: boolean) => void
}

function DescriptionRow({
  jobId,
  part,
  thumbnail,
  imageUrl,
  isEmpty,
  onHover,
  onDirtyChange,
}: DescriptionRowProps) {
  const edit = useEditReviewDescription(jobId)
  // 행별 draft. 마지막으로 따라간 서버 값 — 편집 중이 아니면(dirty 아니면) 재조회로
  // 값이 바뀔 때 draft 를 같이 옮긴다. 독립 리뷰 #1: 상자 확정 직후 캐시된 재작성 전
  // 서술로 mount 됐다가 재작성 완료 응답이 와도 draft 가 그대로 남던 문제
  const lastServerValue = useRef(part.description ?? '')
  const [draft, setDraft] = useState(lastServerValue.current)

  useEffect(() => {
    const next = part.description ?? ''
    if (next === lastServerValue.current) return
    setDraft((current) => (current === lastServerValue.current ? next : current))
    lastServerValue.current = next
  }, [part.description])

  // 서버 값과 다르면 저장 중·저장 실패·아직 blur 안 함 어느 쪽이든 미저장 상태다
  const dirty = draft.trim() !== lastServerValue.current.trim()
  useEffect(() => {
    onDirtyChange(part.id, dirty)
    // 행이 사라질 때(되돌리기 등) 부모의 dirty 집합에 남지 않게
    return () => onDirtyChange(part.id, false)
    // onDirtyChange 는 매 렌더 새 함수라 의존성에 넣으면 매번 재구독된다
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [part.id, dirty])

  const save = () => {
    const text = draft.trim()
    // 빈 문자열은 저장 안 함, 바뀐 경우만 PUT
    if (!text || text === lastServerValue.current) return

    void edit
      .mutateAsync({ partId: part.id, description: text })
      .then((state) => {
        const saved = state.parts.find((p) => p.id === part.id)?.description ?? text
        lastServerValue.current = saved
        setDraft(saved)
      })
      .catch(() => {})
  }

  return (
    <li
      className={`flex items-start gap-3 rounded-lg border p-2 ${
        isEmpty ? 'border-destructive bg-destructive/10' : 'border-border'
      }`}
      onMouseEnter={() => onHover(part.id)}
      onMouseLeave={() => onHover(null)}
      data-testid="description-row"
      data-part={part.name}
      data-empty={isEmpty}
    >
      <div
        className="shrink-0 rounded border border-border bg-no-repeat"
        style={
          thumbnail
            ? { ...thumbnail, backgroundImage: `url(${imageUrl})` }
            : { width: THUMBNAIL_MAX, height: THUMBNAIL_MAX }
        }
        data-testid="description-thumbnail"
      />
      <div className="flex w-40 shrink-0 flex-col gap-1 text-sm">
        <span className="font-semibold">
          {part.partRef} {part.name}
        </span>
        <span className="flex flex-wrap gap-1">
          {descriptionChips(part).map((chip) => (
            <span
              key={chip}
              className="rounded-full border border-border px-2 py-0.5 text-xs"
              data-testid="description-chip"
            >
              {chip}
            </span>
          ))}
        </span>
        {part.occludedBy.length > 0 ? (
          <span className="text-xs text-muted-foreground">
            가려짐: {part.occludedBy.join(', ')}
          </span>
        ) : null}
      </div>
      <div className="flex flex-1 flex-col gap-1">
        <textarea
          className={`${styles.input} min-h-[110px] w-full resize-y leading-relaxed`}
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          onBlur={save}
          data-testid="description-textarea"
        />
        {edit.isError ? (
          <p className="text-xs text-destructive" data-testid="description-error">
            {apiErrorMessage(edit.error, '서술을 저장하지 못했습니다')}
          </p>
        ) : null}
      </div>
    </li>
  )
}

interface PaletteEditorProps {
  jobId: string
  palette: PaletteEntry[]
  onDirtyChange: (isDirty: boolean) => void
}

// 서술 행과 같은 dirty 판정 — 배열이라 문자열로 비교한다
function samePalette(a: PaletteEntry[], b: PaletteEntry[]): boolean {
  return JSON.stringify(a) === JSON.stringify(b)
}

function PaletteEditor({ jobId, palette, onDirtyChange }: PaletteEditorProps) {
  const edit = useEditReviewPalette(jobId)
  // 마지막으로 따라간 서버 값 — 독립 리뷰 #1 과 같은 이유로 dirty 가 아닐 때만 재조회를 따라간다
  const lastServerValue = useRef(palette)
  const [draft, setDraft] = useState(lastServerValue.current)

  useEffect(() => {
    if (samePalette(palette, lastServerValue.current)) return
    setDraft((current) => (samePalette(current, lastServerValue.current) ? palette : current))
    lastServerValue.current = palette
  }, [palette])

  const dirty = !samePalette(draft, lastServerValue.current)
  useEffect(() => {
    onDirtyChange(dirty)
    return () => onDirtyChange(false)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dirty])

  const commit = (next: PaletteEntry[]) => {
    setDraft(next)
    if (samePalette(next, lastServerValue.current)) return

    void edit
      .mutateAsync(next)
      .then((state) => {
        lastServerValue.current = state.palette
        setDraft(state.palette)
      })
      .catch(() => {})
  }

  const update = (index: number, entry: Partial<PaletteEntry>) =>
    draft.map((current, i) => (i === index ? { ...current, ...entry } : current))

  const pick = async (index: number) => {
    if (!EyeDropper) return
    try {
      const { sRGBHex } = await new EyeDropper().open()
      commit(update(index, { hex: normalizeHex(sRGBHex) }))
    } catch {
      // Esc 로 스포이트 취소 — 저장할 것 없음
    }
  }

  return (
    <div className="mt-4" data-testid="palette-editor">
      <p className="mb-2 text-xs text-muted-foreground">
        팔레트 · 중간톤 고유색 (밝은 면·그림자 면 아님)
      </p>
      <div className="flex flex-wrap items-center gap-2">
        {draft.map((entry, index) => (
          <div
            key={index}
            className="flex items-center gap-1 rounded-lg border border-border p-1"
            data-testid="palette-entry"
          >
            <input
              type="color"
              className="size-7 cursor-pointer"
              value={entry.hex ?? '#000000'}
              onChange={(event) =>
                setDraft(update(index, { hex: normalizeHex(event.target.value) }))
              }
              onBlur={() => commit(draft)}
              aria-label={`${entry.name} 색`}
              data-testid="palette-color"
            />
            {EyeDropper ? (
              <button
                type="button"
                className="inline-flex items-center justify-center rounded p-1 text-xs text-muted-foreground hover:bg-muted hover:text-foreground transition-colors"
                onClick={() => void pick(index)}
                aria-label={`${entry.name} 스포이트`}
                data-testid="palette-eyedropper"
                title="원본 이미지에서 색상 스포이트 추출"
              >
                <Icon name="search" size={14} />
              </button>
            ) : null}
            <input
              className={`${styles.input} w-28 py-1`}
              value={entry.name}
              placeholder="색상 이름"
              onChange={(event) => setDraft(update(index, { name: event.target.value }))}
              onBlur={() => commit(draft)}
              data-testid="palette-name"
            />
            <button
              type="button"
              className="inline-flex items-center justify-center rounded p-1 text-xs text-muted-foreground hover:bg-destructive/10 hover:text-destructive transition-colors"
              onClick={() => commit(draft.filter((_, i) => i !== index))}
              aria-label={`${entry.name} 삭제`}
              data-testid="palette-remove"
            >
              <Icon name="close" size={14} />
            </button>
          </div>
        ))}
        {EyeDropper ? (
          <Button
            variant="outline"
            size="sm"
            onClick={async () => {
              try {
                const { sRGBHex } = await new EyeDropper().open()
                const hex = normalizeHex(sRGBHex)
                const next = [...draft, { name: '새 색상', hex }]
                setDraft(next)
                commit(next)
              } catch {
                // 스포이트 취소시 동작 없음
              }
            }}
            data-testid="palette-add-eyedropper"
          >
            <Icon name="search" size={14} className="mr-1" />
            스포이드로 추가
          </Button>
        ) : null}
        <Button
          variant="ghost"
          size="sm"
          onClick={() => setDraft([...draft, { name: '', hex: '#808080' }])}
          data-testid="palette-add"
        >
          <Icon name="plus" size={14} />
        </Button>
      </div>
      {edit.isError ? (
        <p className="mt-1 text-xs text-destructive" data-testid="palette-error">
          {apiErrorMessage(edit.error, '팔레트를 저장하지 못했습니다')}
        </p>
      ) : null}
    </div>
  )
}
