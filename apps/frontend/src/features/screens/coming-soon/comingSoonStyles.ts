/** Design Ref: design-system §6 Module 5 — 준비 중 화면 Tailwind 스타일 계약. */
export const comingSoonStyles = {
  screen: 'flex min-h-0 flex-1 flex-col overflow-hidden',
  body: 'flex min-h-0 flex-1 flex-col items-center justify-center gap-3.5 overflow-y-auto px-5 py-10 text-center',
  icon: 'flex size-14 items-center justify-center rounded-[14px] bg-[var(--card-hover)] text-[var(--text-dim)]',
  title: 'text-[1.3rem] font-semibold text-foreground',
  message: 'max-w-[380px] text-[0.85rem] leading-[1.8] text-muted-foreground',
  cta: 'mt-2 flex items-center gap-1.5 rounded-lg border border-primary bg-[var(--active-bg)] px-4 py-[9px] text-[0.8rem] font-semibold text-primary no-underline transition-[background-color,box-shadow,transform] duration-[var(--dur-release)] ease-[var(--ease-out)] hover:bg-[var(--accent-icon-bg-strong)] active:scale-[var(--press-scale)] active:duration-[var(--dur-press)]',
} as const
