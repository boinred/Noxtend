/**
 * Design Ref: §4.3, §5.1 — 전 화면 공통 상단 바.
 *
 * 헤더가 아는 것은 두 가지뿐이다: 슬롯 자리와 우측 고정 컨트롤.
 * 화면이 무엇을 넣는지는 알지 못한다 — 그래서 화면이 늘어도 이 파일은 그대로다.
 *
 * `ThemeToggle` 은 이제 여기 한 곳에만 있다 (§4.3 #3).
 * 직전 사이클에는 홈·준비중·캔버스 헤더에 각각 하나씩, 총 3개가 있었다.
 */
import { Avatar } from '@/features/shell/Avatar'
import { ThemeToggle } from '@/features/shell/ThemeToggle'
import { useHeaderSlotRef } from '@/features/shell/layout/HeaderSlot'

export function AppHeader() {
  const slotRef = useHeaderSlotRef()

  return (
    <header
      className="z-[100] flex h-[var(--header-h)] flex-[0_0_var(--header-h)] items-center justify-between gap-4 border-b border-border bg-[var(--header-bg)] px-4"
      data-testid="app-header"
    >
      <div
        className="flex min-w-0 flex-[1_1_auto] self-stretch items-center gap-4 overflow-hidden"
        ref={slotRef}
        data-testid="header-slot"
      />

      <div className="flex flex-[0_0_auto] items-center gap-[10px]">
        {/* 아바타가 테마 토글 왼쪽 (Plan §1.4 결정 5, §4.3 #1) */}
        <Avatar />
        <ThemeToggle />
      </div>
    </header>
  )
}
