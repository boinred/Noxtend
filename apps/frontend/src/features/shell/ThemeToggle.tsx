/**
 * Design Ref: §5.5 — 다크↔라이트 전환.
 *
 * 컴포넌트는 테마로 분기하지 않는다 (§10.3). 색은 CSS 토큰이 알아서 바꾼다.
 * 여기서 theme 을 읽는 것은 아이콘과 접근성 상태를 표시하기 위해서다.
 */
import { Icon } from '@/features/shell/Icon'
import { useTheme } from '@/app/useTheme'

export function ThemeToggle() {
  const { theme, toggle } = useTheme()
  const isDark = theme === 'dark'

  return (
    <button
      type="button"
      className="flex items-center gap-[6px] whitespace-nowrap rounded-lg border border-border bg-popover px-3 py-[7px] text-[0.76rem] font-semibold text-muted-foreground transition-[background-color,color,border-color,transform] duration-[var(--dur-release)] ease-[var(--ease-out)] [@media(hover:hover)_and_(pointer:fine)]:hover:bg-[var(--card-hover)] [@media(hover:hover)_and_(pointer:fine)]:hover:text-foreground active:scale-[var(--press-scale)] active:duration-[var(--dur-press)]"
      onClick={toggle}
      aria-pressed={!isDark}
      aria-label={isDark ? '라이트 모드로 전환' : '다크 모드로 전환'}
      data-testid="theme-toggle"
      data-theme-state={theme}
    >
      <Icon name={isDark ? 'moon' : 'sun'} size={14} className="flex-none" />
      {isDark ? '다크' : '라이트'}
    </button>
  )
}
