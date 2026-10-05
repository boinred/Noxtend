/**
 * Design Ref: §4.3, §5.3 — 내정보 진입점 (FR-07).
 *
 * 인증은 이번 범위 밖이므로 자리와 모양만 잡는다 (Plan §1.4 결정 6).
 * 다만 눌러도 아무 일이 없으면 고장으로 읽히므로, 무엇이 올 예정인지 말해준다
 * (준비 중 화면과 같은 원칙 — 비어 있음이 예고로 읽혀야 한다).
 */
import { useEffect, useRef, useState } from 'react'
import type { FocusEvent, TransitionEvent } from 'react'
import { Icon } from '@/features/shell/Icon'

const POPOVER_EXIT_FALLBACK_MS = 250

export function Avatar() {
  const [open, setOpen] = useState(false)
  const [mounted, setMounted] = useState(false)
  const wrapperRef = useRef<HTMLDivElement>(null)

  /**
   * 바깥을 누르면 닫는다. document 리스너 대신 blur 를 쓰는 이유:
   * 키보드로 초점이 빠져나갈 때도 같은 규칙이 적용된다.
   */
  const handleBlur = (event: FocusEvent<HTMLDivElement>) => {
    if (!wrapperRef.current?.contains(event.relatedTarget)) setOpen(false)
  }

  // 논리적 열림 상태와 종료 전환 동안의 DOM 수명 분리
  const handleToggle = () => {
    if (open) {
      setOpen(false)
      return
    }
    setMounted(true)
    setOpen(true)
  }

  // 자식 전환 배제 및 재진입 상태 보존
  const handleTransitionEnd = (event: TransitionEvent<HTMLDivElement>) => {
    if (event.currentTarget === event.target && event.propertyName === 'opacity' && !open) {
      setMounted(false)
    }
  }

  useEffect(() => {
    if (open || !mounted) return

    // transitionend 누락 시 숨은 DOM이 남지 않도록 하는 종료 안전망
    const timeoutId = window.setTimeout(() => setMounted(false), POPOVER_EXIT_FALLBACK_MS)
    return () => window.clearTimeout(timeoutId)
  }, [open, mounted])

  return (
    <div className="relative flex items-center" ref={wrapperRef} onBlur={handleBlur}>
      <button
        type="button"
        className="flex size-[30px] items-center justify-center rounded-full border border-border bg-[var(--avatar-bg)] text-muted-foreground transition-[background-color,color,border-color,transform] duration-[var(--dur-release)] ease-[var(--ease-out)] [@media(hover:hover)_and_(pointer:fine)]:hover:border-[var(--active-border)] [@media(hover:hover)_and_(pointer:fine)]:hover:text-foreground active:scale-[var(--press-scale)] active:duration-[var(--dur-press)]"
        onClick={handleToggle}
        aria-expanded={open}
        aria-label="내 정보"
        data-testid="avatar"
      >
        <Icon name="user" size={15} />
      </button>

      {mounted ? (
        <div
          className="absolute top-[calc(100%+8px)] right-0 z-[200] flex w-[220px] origin-top-right flex-col gap-[6px] rounded-[10px] border border-border bg-[var(--popover-bg)] px-[14px] py-3 opacity-100 shadow-[var(--shadow-popover)] transition-[opacity,transform] duration-[var(--dur-popover)] ease-[var(--ease-out)] starting:scale-[var(--popover-enter-scale)] starting:opacity-0 data-[state=closed]:pointer-events-none data-[state=closed]:scale-[var(--popover-enter-scale)] data-[state=closed]:opacity-0"
          role="status"
          data-testid="avatar-popover"
          data-state={open ? 'open' : 'closed'}
          aria-hidden={!open}
          inert={!open}
          onTransitionEnd={handleTransitionEnd}
        >
          <span className="text-[0.8rem] font-semibold text-foreground">내 정보</span>
          <span className="text-[0.72rem] leading-[1.6] text-[var(--text-dim)]">
            계정 기능은 아직 준비 중입니다.
            <br />
            로그인이 생기면 이 자리에 들어옵니다.
          </span>
        </div>
      ) : null}
    </div>
  )
}
