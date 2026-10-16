import { useLayoutEffect, useRef, useState } from 'react'
import { resizeBounds, sameBounds } from '../background/boxDrag'
import type { Bounds } from '@/domain/job/types'
import type { SpriteAssetPlan } from '@/domain/sprites/types'
import type { OverlayHandle } from '../background/PartsOverlay'
import type { PointerEvent as ReactPointerEvent } from 'react'

export function useSpriteRoiEditor({
  plans,
  lockedIds,
  disabled,
  revision,
  onCommit,
}: {
  plans: SpriteAssetPlan[]
  lockedIds: string
  disabled: boolean
  revision: string
  onCommit: (id: string, bounds: Bounds) => void
}) {
  const frameRef = useRef<HTMLDivElement>(null)
  const cancelRef = useRef<(() => void) | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [preview, setPreview] = useState<{
    partId: string
    ordinal: number
    bounds: Bounds
  } | null>(null)

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
    setSelectedId(partId)
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
      if (!sameBounds(origin, next)) onCommit(partId, next)
    }
    cancelRef.current = cancel
    document.addEventListener('pointermove', move)
    document.addEventListener('pointerup', finish)
    document.addEventListener('pointercancel', cancel)
    document.addEventListener('keydown', key)
    window.addEventListener('blur', cancel)
  }
  function select(id: string) {
    cancelRef.current?.()
    setSelectedId(id)
  }
  return { frameRef, selectedId, setSelectedId: select, preview, onPointerHandleDown }
}
