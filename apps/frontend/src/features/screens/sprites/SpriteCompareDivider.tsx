import { useEffect, useRef } from 'react'
import type { PointerEvent as ReactPointerEvent } from 'react'

const clamp = (value: number) => Math.min(100, Math.max(0, value))
const LABEL =
  'pointer-events-none absolute bottom-2 rounded-md bg-black/65 px-2 py-1 text-xs font-semibold leading-none text-white'

// position 은 프레임 왼쪽 기준 0~100(%), 왼쪽은 원본 오른쪽은 재구성
export function SpriteCompareDivider({
  position,
  onChange,
  interactive,
}: {
  position: number
  onChange: (position: number) => void
  interactive: boolean
}) {
  const surfaceRef = useRef<HTMLDivElement>(null)
  const stopRef = useRef<(() => void) | null>(null)
  // 비교 종료·언마운트 때 진행 중인 끌기 정리
  useEffect(() => () => stopRef.current?.(), [interactive])

  function onPointerDown(event: ReactPointerEvent<HTMLDivElement>) {
    const rect = surfaceRef.current?.getBoundingClientRect()
    if (!event.isPrimary || event.button !== 0 || !rect?.width) return
    stopRef.current?.()
    const pointerId = event.pointerId
    const start = position
    const at = (e: { clientX: number }) => clamp(((e.clientX - rect.left) / rect.width) * 100)
    function stop() {
      document.removeEventListener('pointermove', move)
      document.removeEventListener('pointerup', finish)
      document.removeEventListener('pointercancel', cancel)
      stopRef.current = null
    }
    function move(e: PointerEvent) {
      if (e.pointerId === pointerId) onChange(at(e))
    }
    function finish(e: PointerEvent) {
      if (e.pointerId === pointerId) stop()
    }
    // 세로 스크롤로 넘어간 터치는 누르기 전 위치로
    function cancel(e: PointerEvent) {
      if (e.pointerId !== pointerId) return
      stop()
      onChange(start)
    }
    stopRef.current = stop
    document.addEventListener('pointermove', move)
    document.addEventListener('pointerup', finish)
    document.addEventListener('pointercancel', cancel)
    onChange(at(event))
  }

  return (
    <div className="pointer-events-none absolute inset-0 select-none">
      <div
        data-testid="compare-line"
        className="pointer-events-none absolute inset-y-0 w-0.5 -translate-x-1/2 bg-white shadow-[0_0_0_1px_rgba(0,0,0,0.45)]"
        style={{ left: `${position}%` }}
      />
      {interactive ? (
        <div
          ref={surfaceRef}
          data-testid="compare-surface"
          className="pointer-events-auto absolute inset-0 cursor-ew-resize touch-pan-y"
          onPointerDown={onPointerDown}
        >
          <div
            role="slider"
            aria-label="원본·재구성 경계"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={Math.round(position)}
            aria-valuetext={`${Math.round(position)}%`}
            tabIndex={0}
            className="absolute top-1/2 grid size-8 -translate-x-1/2 -translate-y-1/2 place-items-center rounded-full border border-black/50 bg-white text-black shadow-[0_1px_3px_rgba(0,0,0,0.45)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
            style={{ left: `${position}%` }}
            onKeyDown={(event) => {
              const step = event.key === 'ArrowLeft' ? -1 : event.key === 'ArrowRight' ? 1 : 0
              if (!step) return
              event.preventDefault()
              onChange(clamp(position + step))
            }}
          >
            <svg
              viewBox="0 0 24 24"
              className="size-4"
              fill="none"
              stroke="currentColor"
              strokeWidth={2.5}
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
            >
              <path d="M9 6l-6 6 6 6M15 6l6 6-6 6" />
            </svg>
          </div>
        </div>
      ) : null}
      <span aria-hidden="true" className={`${LABEL} left-2`}>
        원본
      </span>
      <span aria-hidden="true" className={`${LABEL} right-2`}>
        재구성
      </span>
    </div>
  )
}
