/**
 * Design Ref: docs/specs/2026-08-28-review-gate.md — 검수 게이트 화면.
 *
 * 분해가 끝나 `pendingReview` 로 멈춘 작업에서, 사람이 원본 이미지 위에 사각형을 그려
 * 빠진 파츠를 추가하고 잘못된 파츠를 지운 뒤 **한 번의 전체 승인**으로만 Generate 팬아웃을
 * 트리거한다 (§목표). 카테고리를 몰라도 되게 캐릭터·배경 스튜디오가 공유한다 (§배경 D-01).
 */
import { useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import {
  useAddReviewPart,
  useApproveReview,
  useFindOverlaps,
  useMoveReviewPlacement,
  useRemoveReviewPart,
  useReview,
} from '@/app/queries/useReview'
import { apiErrorCode, apiErrorMessage } from '@/app/queries/errors'
import { PartsOverlay } from './PartsOverlay'
import { DescriptionsReview } from './DescriptionsReview'
import { boundsFromDrag, clamp01, resizeBounds, sameBounds } from './boxDrag'
import type { HandleDrag, Point } from './boxDrag'
import type { OverlayHandle } from './PartsOverlay'
import { backgroundStyles as styles } from './backgroundStyles'
import type { CSSProperties, MouseEvent as ReactMouseEvent } from 'react'
import type { Bounds } from '@/domain/job/types'

export interface ReviewGateProps {
  jobId: string
  sourceImageId: string
  /** 승인이 성공하면 화면이 진행 표시로 넘어가도록 알린다. */
  onApproved: () => void
}

const DRAFT_COLOR = '#ffffff'

function boxStyle(box: Bounds, color: string): CSSProperties {
  return {
    position: 'absolute',
    left: `${box.x * 100}%`,
    top: `${box.y * 100}%`,
    width: `${box.w * 100}%`,
    height: `${box.h * 100}%`,
    border: `2px solid ${color}`,
    pointerEvents: 'none',
  }
}

export function ReviewGate({ jobId, sourceImageId, onApproved }: ReviewGateProps) {
  const { review, isLoading } = useReview(jobId, true)
  const addPart = useAddReviewPart(jobId)
  const findOverlaps = useFindOverlaps(jobId)
  const removePart = useRemoveReviewPart(jobId)
  const movePlacement = useMoveReviewPlacement(jobId)
  const approve = useApproveReview(jobId)

  const frameRef = useRef<HTMLDivElement>(null)
  const [dragStart, setDragStart] = useState<Point | null>(null)
  const [draftBounds, setDraftBounds] = useState<Bounds | null>(null)
  // 드래그 종료 리스너가 읽는 최신 좌표 — 상태로 읽으면 재구독 경합이 생긴다
  const draftRef = useRef<Bounds | null>(null)
  draftRef.current = draftBounds
  const [name, setName] = useState('')
  const [category, setCategory] = useState('')
  const [description, setDescription] = useState('')
  // 그린 사각형과 겹치는 파츠 이름 → 이 파츠가 그것을 가리는가.
  // 기본은 전부 켬 — 검수에서 추가하는 파츠는 대개 위에 얹히는 것들이다
  const [occlusion, setOcclusion] = useState<Record<string, boolean>>({})

  const pointFromEvent = (event: ReactMouseEvent): Point | null => {
    const rect = frameRef.current?.getBoundingClientRect()
    if (!rect || rect.width === 0 || rect.height === 0) return null

    return {
      x: clamp01((event.clientX - rect.left) / rect.width),
      y: clamp01((event.clientY - rect.top) / rect.height),
    }
  }

  const resetDraft = () => {
    setDraftBounds(null)
    setName('')
    setCategory('')
    setDescription('')
    // 안 지우면 다음 사각형에 이전 선택이 그대로 남는다
    setOcclusion({})
  }

  const onMouseDown = (event: ReactMouseEvent) => {
    const point = pointFromEvent(event)
    if (!point) return

    setDragStart(point)
    setDraftBounds({ x: point.x, y: point.y, w: 0, h: 0 })
    setOcclusion({})
  }

  const onMouseMove = (event: ReactMouseEvent) => {
    if (!dragStart) return

    const point = pointFromEvent(event)
    if (!point) return

    setDraftBounds(boundsFromDrag(dragStart, point))
  }

  /**
   * 드래그 종료는 **문서에서 받는다.**
   *
   * 프레임에 걸면 손을 떼는 순간 커서가 프레임의 자식(그리는 중 나타나는 지우기 버튼)이나
   * 이미지 밖에 있을 때 이벤트를 놓친다 — 상자는 남았는데 겹침 조회가 안 나가고 체크박스가
   * 뜨지 않는다. 실제로 그렇게 놓쳤다.
   */
  useEffect(() => {
    if (!dragStart) return

    const finish = () => {
      setDragStart(null)

      // 겹침 조회는 드래그가 끝난 뒤 한 번만 — draftBounds 를 의존성에 넣으면 mousemove 마다
      // 리스너를 뗐다 붙이게 되고, 그 틈에 mouseup 이 떨어지면 통째로 놓친다. 최신 좌표는
      // ref 로 읽고 구독은 드래그당 한 번만 한다
      const box = draftRef.current
      if (!box || box.w <= 0 || box.h <= 0) return

      findOverlaps.mutate(box, {
        onSuccess: ({ overlapping }) =>
          setOcclusion(Object.fromEntries(overlapping.map((partName) => [partName, true]))),
      })
    }

    document.addEventListener('mouseup', finish)
    return () => document.removeEventListener('mouseup', finish)
    // findOverlaps 는 매 렌더 새 객체라 의존성에 넣으면 같은 재구독 문제가 생긴다
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dragStart])

  // 손잡이 드래그 — 시작 정보와 미리보기 좌표를 따로 든다. 미리보기는 매 mousemove 마다
  // 바뀌고, 종료 리스너는 시작 정보만 있으면 되기 때문이다
  const [handleDrag, setHandleDrag] = useState<HandleDrag | null>(null)
  const [preview, setPreview] = useState<Bounds | null>(null)
  const previewRef = useRef<Bounds | null>(null)
  previewRef.current = preview

  const onHandleDown = (
    partId: string,
    ordinal: number,
    box: Bounds,
    handle: OverlayHandle,
    event: ReactMouseEvent,
  ) => {
    const point = pointFromEvent(event)
    if (!point) return

    setHandleDrag({ partId, ordinal, handle, origin: box, start: point })
    setPreview(box)
  }

  /**
   * 손잡이 드래그는 문서에서 받는다 — 새 사각형 그리기와 같은 이유다. 손을 떼는 순간
   * 커서가 프레임 밖이면 프레임 리스너는 그 이벤트를 못 본다.
   */
  useEffect(() => {
    if (!handleDrag) return

    const rect = () => frameRef.current?.getBoundingClientRect()

    const move = (event: MouseEvent) => {
      const box = rect()
      if (!box || box.width === 0 || box.height === 0) return

      setPreview(
        resizeBounds(handleDrag, {
          x: clamp01((event.clientX - box.left) / box.width),
          y: clamp01((event.clientY - box.top) / box.height),
        }),
      )
    }

    const finish = () => {
      const bounds = previewRef.current
      setHandleDrag(null)

      // 제자리에서 뗀 것은 서버까지 갈 필요가 없다
      if (!bounds || sameBounds(bounds, handleDrag.origin)) {
        setPreview(null)
        return
      }

      movePlacement.mutate(
        { partId: handleDrag.partId, ordinal: handleDrag.ordinal, bounds },
        // 응답이 캐시에 들어온 뒤에 미리보기를 걷는다. 먼저 걷으면 옛 좌표가 한 번
        // 스치고 지나가 상자가 되돌아갔다 오는 것처럼 보인다
        { onSettled: () => setPreview(null) },
      )
    }

    document.addEventListener('mousemove', move)
    document.addEventListener('mouseup', finish)
    return () => {
      document.removeEventListener('mousemove', move)
      document.removeEventListener('mouseup', finish)
    }
    // movePlacement 는 매 렌더 새 객체라 의존성에 넣으면 드래그 중 재구독이 돈다
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [handleDrag])

  const hasDraftBox = draftBounds !== null && draftBounds.w > 0 && draftBounds.h > 0

  const overlappingNames = Object.keys(occlusion)

  const confirmDraft = () => {
    if (!draftBounds || !hasDraftBox || !name.trim()) return

    addPart.mutate(
      {
        name: name.trim(),
        bounds: draftBounds,
        category: category.trim() || undefined,
        description: description.trim() || undefined,
        // 겹치는 게 없으면 필드를 보내지 않는다. 빈 배열은 "전부 해제" 라는 다른 뜻이다
        occludes:
          overlappingNames.length > 0
            ? overlappingNames.filter((partName) => occlusion[partName])
            : undefined,
      },
      {
        onSuccess: resetDraft,
        // §함정8 — 조회와 추가 사이에 파츠가 삭제되면 겹침 목록이 어긋나 400 이 난다.
        // 메시지만 띄우면 사용자는 영문을 모른다. 겹침을 다시 조회해 목록을 새로 그린다.
        //
        // **이 오류에서만 재조회한다.** 이름 중복·좌표 오류에서도 돌리면 검수자가 애써
        // 해제한 체크가 전부 다시 켜지고, 이름만 고쳐 재시도하면 의도하지 않은 가림
        // 관계가 저장된다
        onError: (error) => {
          if (apiErrorCode(error) !== 'PART_NOT_OVERLAPPING') return

          findOverlaps.mutate(draftBounds, {
            onSuccess: ({ overlapping }) =>
              setOcclusion(Object.fromEntries(overlapping.map((partName) => [partName, true]))),
          })
        },
      },
    )
  }

  if (isLoading || !review) {
    return (
      <p className={styles.notice} data-testid="review-gate-loading">
        검수 정보를 불러오는 중…
      </p>
    )
  }

  // 서술 확인 단계 — 상자 편집 UI 대신 서술·팔레트 확인 (review-gate-staged 사이클 1)
  if (review.reviewPhase === 'descriptions') {
    return <DescriptionsReview jobId={jobId} sourceImageId={sourceImageId} review={review} />
  }

  return (
    <div data-testid="review-gate">
      <p className={styles.onboardingNotice} data-testid="review-gate-notice">
        탐지된 파츠를 확인하세요. 원본 이미지 위를 드래그하면 빠진 파츠를 사각형으로 추가할 수
        있습니다. 기존 파츠와 겹쳐도 되며, 겹치면 어느 파츠를 가리는지 확인하게 됩니다. 틀린 파츠는
        상자 옆 "삭제"로 지우세요. 다 됐으면 아래 "상자 확정"을 누르면 다음 단계로 넘어갑니다.
      </p>

      <PartsOverlay
        sourceImageId={sourceImageId}
        parts={review.parts.map((part) => ({
          ...part,
          // 줄로 나눈다 — 한 줄에 이어 붙이면 칩이 가로로 길어져 서로 덮고 실선까지 가린다
          label: (
            <>
              <span className="block">
                {part.partRef} · {part.name}
                {part.source === 'manual' ? ' (직접 추가)' : ''}
              </span>
              {part.occludedBy.length > 0 ? (
                <span className="block font-normal opacity-90">
                  가려짐 · {part.occludedBy.join(', ')}
                </span>
              ) : null}
            </>
          ),
        }))}
        // 검수자는 전체를 훑어야 하므로 상자를 늘 보여준다 (배경 결과 화면은 지목했을 때만)
        alwaysShowBoxes
        action={{ label: '삭제', onClick: (partId) => removePart.mutate(partId) }}
        editing={{
          onHandleDown,
          preview:
            handleDrag && preview
              ? { partId: handleDrag.partId, ordinal: handleDrag.ordinal, bounds: preview }
              : null,
        }}
        frameRef={frameRef}
        frameProps={{
          style: { cursor: 'crosshair', userSelect: 'none' },
          onMouseDown,
          onMouseMove,
          'data-testid': 'review-canvas',
        }}
      >
        {draftBounds ? (
          <>
            <div data-testid="review-draft-box" style={boxStyle(draftBounds, DRAFT_COLOR)} />
            {/* 잘못 그렸을 때 사각형 자리에서 바로 지운다 — 하단 폼까지 눈을 옮기지 않는다 */}
            {hasDraftBox ? (
              <button
                type="button"
                className={styles.chipJump}
                style={{
                  position: 'absolute',
                  left: `${(draftBounds.x + draftBounds.w) * 100}%`,
                  top: `${draftBounds.y * 100}%`,
                  zIndex: 3,
                }}
                onMouseDown={(event) => event.stopPropagation()}
                onClick={resetDraft}
                aria-label="그린 사각형 지우기"
                data-testid="review-draft-clear"
              >
                ✕
              </button>
            ) : null}
          </>
        ) : null}
        {/*
          입력을 사각형 바로 아래에 띄운다 — 화면 하단에 두면 방금 그린 사각형이 어느
          것이었는지 눈을 옮겼다가 다시 찾아야 한다. 사각형이 아래쪽이면 위로 뒤집는다
        */}
        {/* 드래그가 끝난 뒤에만 띄운다 — 그리는 도중에 뜨면 사각형을 가려 방해가 된다 */}
        {hasDraftBox && draftBounds && !dragStart ? (
          <div
            className={`${styles.settingsRow} rounded-md border border-border bg-background p-2 shadow-lg`}
            style={{
              position: 'absolute',
              left: `${draftBounds.x * 100}%`,
              top: `${(draftBounds.y + draftBounds.h) * 100}%`,
              transform: draftBounds.y + draftBounds.h > 0.72 ? 'translateY(-100%)' : undefined,
              zIndex: 4,
              flexWrap: 'wrap',
              maxWidth: '62%',
            }}
            onMouseDown={(event) => event.stopPropagation()}
            data-testid="review-add-form"
          >
            <input
              className={styles.input}
              placeholder="파츠 이름 (필수 · 예: 벨트 / 왼쪽 장갑 손목 아래)"
              value={name}
              onChange={(event) => setName(event.target.value)}
              data-testid="review-add-name"
            />
            <input
              className={styles.input}
              placeholder="카테고리 (선택 · 비우면 AI가 채움 · 예: Belt)"
              value={category}
              onChange={(event) => setCategory(event.target.value)}
              data-testid="review-add-category"
            />
            <input
              className={styles.input}
              placeholder="설명 (선택 · 비우면 AI가 원본을 보고 씀 · 예: 갈색 가죽 벨트, 금속 버클)"
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              data-testid="review-add-description"
            />

            {overlappingNames.length > 0 ? (
              <fieldset className="w-full text-xs" data-testid="review-occludes">
                <legend className="mb-1 text-xs text-muted-foreground">이 파츠가 가리는 것</legend>
                {/* 한 줄에 체크박스 하나 + 문장 하나 — 이어 붙으면 어느 체크가 어느 문장인지 안 보인다 */}
                {overlappingNames.map((partName) => (
                  <label
                    key={partName}
                    className="flex items-center gap-1.5 py-0.5"
                    data-testid="review-occludes-option"
                  >
                    <input
                      type="checkbox"
                      className="size-3.5 shrink-0"
                      checked={occlusion[partName]}
                      onChange={(event) =>
                        setOcclusion((current) => ({
                          ...current,
                          [partName]: event.target.checked,
                        }))
                      }
                      data-testid={`review-occludes-${partName}`}
                    />
                    {/* 방향을 문장으로 밝힌다 — "겹침" 만 보이면 무엇이 무엇을 가리는지 알 수 없다 */}
                    <span>{`${name.trim() || '이 파츠'}가 ${partName}을(를) 가림`}</span>
                  </label>
                ))}
              </fieldset>
            ) : null}

            <Button
              onClick={confirmDraft}
              disabled={addPart.isPending || !name.trim()}
              data-testid="review-add-confirm"
            >
              {addPart.isPending ? '추가 중' : '추가'}
            </Button>
            <Button variant="ghost" onClick={resetDraft} data-testid="review-add-cancel">
              취소
            </Button>

            {addPart.isError ? (
              <p className={styles.failureReason} data-testid="review-add-error">
                {apiErrorMessage(addPart.error, '요청을 처리하지 못했습니다')}
              </p>
            ) : null}
          </div>
        ) : null}
      </PartsOverlay>

      <div className={styles.actions}>
        <Button
          onClick={() => approve.mutate(undefined, { onSuccess: onApproved })}
          disabled={approve.isPending || review.parts.length === 0}
          data-testid="review-approve"
        >
          {approve.isPending ? '확정 중' : `상자 확정 (${review.parts.length}개 파츠)`}
        </Button>
      </div>

      {approve.isError ? (
        <p className={styles.failureReason} data-testid="review-approve-error">
          {apiErrorMessage(approve.error, '요청을 처리하지 못했습니다')}
        </p>
      ) : null}
    </div>
  )
}
