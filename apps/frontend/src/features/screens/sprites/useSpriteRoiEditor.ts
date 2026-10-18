import { useLayoutEffect, useRef, useState } from 'react'
import { resizeBounds, sameBounds } from '../background/boxDrag'
import type { Bounds } from '@/domain/job/types'
import type { SpriteAssetPlan } from '@/domain/sprites/types'
import type { OverlayHandle, OverlayPart } from '../background/PartsOverlay'
import type { PointerEvent as ReactPointerEvent } from 'react'

export function useSpriteRoiEditor({
  plans,
  parts,
  lockedIds,
  disabled,
  revision,
  onCommit,
}: {
  plans: SpriteAssetPlan[]
  // 선택 유효성 기준, draft plans 의 오버레이 상자
  parts: Pick<OverlayPart, 'id' | 'placements'>[]
  lockedIds: string
  disabled: boolean
  revision: string
  onCommit: (partId: string, ordinal: number, bounds: Bounds) => void
}) {
  const frameRef = useRef<HTMLDivElement>(null)
  const cancelRef = useRef<(() => void) | null>(null)
  const [selected, setSelected] = useState<{ partId: string; ordinal: number } | null>(null)
  const [preview, setPreview] = useState<{
    partId: string
    ordinal: number
    bounds: Bounds
  } | null>(null)
  // 역할 변경·대상 삭제·서버 계획 교체로 사라진 상자 선택 해제
  const valid =
    selected !== null &&
    parts.some((part) => part.id === selected.partId && selected.ordinal < part.placements.length)
  if (selected !== null && !valid) setSelected(null)

  // 잠금·서버 재조회·정밀 입력 변경 뒤 늦은 드래그 확정 방지
  useLayoutEffect(() => {
    cancelRef.current?.()
    return () => cancelRef.current?.()
  }, [plans, lockedIds, disabled, revision])

  function onPointerHandleDown(
    partId: string,
    ordinal: number,
    origin: Bounds,
    handle: OverlayHandle,
    event: ReactPointerEvent,
  ) {
    setSelected({ partId, ordinal })
    const frame = frameRef.current
    const image = frame?.querySelector('img')
    const rect = frame?.getBoundingClientRect()
    if (
      disabled ||
      lockedIds.split(',').includes(partId) ||
      !event.isPrimary ||
      event.button !== 0 ||
      !image?.complete ||
      !image.naturalWidth ||
      !rect?.width ||
      !rect.height ||
      !Object.values(origin).every(Number.isFinite) ||
      origin.x < 0 ||
      origin.y < 0 ||
      origin.w <= 0 ||
      origin.h <= 0 ||
      origin.x + origin.w > 1 ||
      origin.y + origin.h > 1
    )
      return
    event.preventDefault()
    cancelRef.current?.()
    const pointerId = event.pointerId
    const point = (e: { clientX: number; clientY: number }) => ({
      x: (e.clientX - rect.left) / rect.width,
      y: (e.clientY - rect.top) / rect.height,
    })
    const drag = { partId, ordinal, origin, handle, start: point(event) }
    let next = origin
    function cleanup() {
      document.removeEventListener('pointermove', move)
      document.removeEventListener('pointerup', finish)
      document.removeEventListener('pointercancel', cancel)
      document.removeEventListener('keydown', key)
      window.removeEventListener('blur', cancel)
      cancelRef.current = null
      setPreview(null)
    }
    function cancel() {
      cleanup()
    }
    function key(e: KeyboardEvent) {
      if (e.key === 'Escape') cancel()
    }
    function move(e: PointerEvent) {
      if (e.pointerId !== pointerId) return
      next = resizeBounds(drag, point(e))
      setPreview({ partId, ordinal, bounds: next })
    }
    function finish(e: PointerEvent) {
      if (e.pointerId !== pointerId) return
      cleanup()
      if (!sameBounds(origin, next)) onCommit(partId, ordinal, next)
    }
    cancelRef.current = cancel
    document.addEventListener('pointermove', move)
    document.addEventListener('pointerup', finish)
    document.addEventListener('pointercancel', cancel)
    document.addEventListener('keydown', key)
    window.addEventListener('blur', cancel)
  }
  function select(partId: string, ordinal: number) {
    cancelRef.current?.()
    setSelected({ partId, ordinal })
  }
  return { frameRef, selected: valid ? selected : null, select, preview, onPointerHandleDown }
}
