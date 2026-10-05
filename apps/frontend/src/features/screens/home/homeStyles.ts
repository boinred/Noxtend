/**
 * Design Ref: design-system §6 Module 5 — Home Tailwind 스타일 계약.
 * 작업 시작·활성 작업·최근 작업의 정보 위계와 반응형 계산값을 유지한다.
 */

// 홈 작업 시작 영역
export const homeScreenStyles = {
  sections: 'flex flex-col gap-8 max-[720px]:gap-7',
  launch:
    'grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-[18px] rounded-[14px] border border-border bg-popover p-6 text-foreground no-underline transition-[border-color,background-color,transform] duration-[var(--dur-release)] ease-[var(--ease-out)] hover:border-[var(--active-border)] hover:bg-[var(--card-hover)] active:scale-[var(--press-scale)] active:duration-[var(--dur-press)] max-[720px]:grid-cols-[auto_minmax(0,1fr)] max-[720px]:gap-3.5 max-[720px]:p-[18px]',
  launchIcon:
    'grid size-[46px] place-items-center rounded-[11px] border border-[var(--active-border)] bg-[var(--active-bg)] text-primary',
  launchBody: 'flex min-w-0 flex-col gap-[3px]',
  launchEyebrow: 'text-[0.6875rem] font-bold uppercase tracking-[0.08em] text-primary',
  launchTitle: 'text-base font-[650] text-foreground',
  launchCopy: 'text-[0.8125rem] leading-normal text-muted-foreground max-[720px]:hidden',
  launchAction:
    'inline-flex items-center gap-1.5 whitespace-nowrap text-[0.8rem] font-[650] text-primary max-[720px]:col-start-2 max-[720px]:mt-1.5 max-[720px]:justify-self-start',
  cta: 'mt-1.5 inline-flex items-center gap-1.5 whitespace-nowrap rounded-lg border border-[var(--active-border)] bg-[var(--active-bg)] px-3.5 py-[9px] text-[0.8rem] font-[650] text-primary no-underline transition-[background-color,transform] duration-[var(--dur-release)] ease-[var(--ease-out)] active:scale-[var(--press-scale)] active:duration-[var(--dur-press)]',
} as const

// 진행 중 작업 spotlight
export const activeJobStyles = {
  spotlight:
    'grid min-h-36 grid-cols-[96px_minmax(0,1fr)_auto] items-center gap-5 rounded-[14px] border border-[var(--active-border)] bg-card p-5 text-foreground no-underline transition-[border-color,background-color,transform] duration-[var(--dur-release)] ease-[var(--ease-out)] hover:border-primary hover:bg-[var(--card-hover)] active:scale-[var(--press-scale)] active:duration-[var(--dur-press)] max-[720px]:grid-cols-[72px_minmax(0,1fr)] max-[720px]:gap-3.5 max-[720px]:p-4',
  thumbnail:
    'size-24 rounded-[10px] border border-border bg-[var(--sunken-bg)] object-cover max-[720px]:size-[72px]',
  body: 'grid min-w-0 grid-cols-[auto_1fr] items-baseline',
  eyebrow:
    'col-span-full mb-1.5 text-[0.6875rem] font-bold uppercase tracking-[0.08em] text-primary',
  title: 'text-[1.05rem] font-[650] text-foreground',
  jobId: 'justify-self-end font-mono text-[0.6875rem] text-[var(--text-dim)]',
  progressTrack:
    'col-span-full mt-[18px] h-[5px] overflow-hidden rounded-full bg-[var(--sunken-bg)]',
  progressValue:
    'block h-full rounded-[inherit] bg-primary transition-[width] duration-[var(--dur-progress)] ease-[var(--ease-out)]',
  progressMeta: 'col-span-full mt-[7px] text-xs tabular-nums text-muted-foreground',
  openLabel:
    'inline-flex items-center gap-1.5 text-xs font-semibold text-muted-foreground max-[720px]:hidden',
} as const

// 홈 작업 현황과 최근 결과
export const workStatusStyles = {
  section: 'flex flex-col gap-3',
  title: 'flex items-center gap-[7px] text-[0.8125rem] font-[650] text-foreground',
  titleIcon: 'text-primary',
  // 제목을 읽는 흐름을 끊지 않도록 톤을 낮춘다 — 숫자는 곁다리 정보다
  titleCount:
    'rounded-full bg-muted px-[7px] py-px text-[0.6875rem] font-semibold leading-[1.45] text-muted-foreground tabular-nums',
  content: 'flex flex-col gap-2.5',
  empty:
    'flex min-h-[148px] flex-col items-center justify-center gap-2 rounded-xl border border-dashed border-border bg-popover px-5 py-7 text-center',
  emptyTitle: 'text-[0.85rem] font-semibold text-muted-foreground',
  emptyBody: 'max-w-[340px] text-xs leading-[1.7] text-[var(--text-dim)]',
  jobList: 'flex list-none flex-col gap-2 p-0',
  jobRow:
    'flex items-center gap-3.5 rounded-[10px] border border-border bg-popover px-3.5 py-3 transition-[border-color,background] duration-[var(--dur-hover)] ease-[var(--ease-out)] hover:border-[var(--active-border)] hover:bg-[var(--card-hover)] max-[720px]:gap-[11px] max-[720px]:p-2.5',
  jobLink:
    'grid min-w-0 flex-1 grid-cols-[48px_minmax(0,1fr)] items-center gap-3.5 no-underline text-foreground focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-2 max-[720px]:grid-cols-[44px_minmax(0,1fr)] max-[720px]:gap-[11px]',
  jobThumb:
    'size-12 shrink-0 rounded-lg border border-border bg-[var(--sunken-bg)] object-cover max-[720px]:size-11',
  jobBody: 'grid min-w-0 grid-cols-[minmax(0,1fr)_auto] items-baseline gap-x-3 gap-y-[3px]',
  jobName: 'truncate text-[0.85rem] font-semibold text-foreground',
  // **두 줄 전체의 가운데에 선다.** 첫 줄에만 걸면 줄 끝의 휴지통·꺾쇠(줄 높이 가운데)와
  // 8px 넘게 어긋난다 — 오른쪽 세 요소가 한 축에 안 서서 눈에 띈다
  jobStatus:
    "row-span-2 self-center justify-self-end text-xs font-semibold text-muted-foreground data-[status='failed']:text-destructive data-[status='succeeded']:text-primary max-[720px]:row-span-1 max-[720px]:col-span-full max-[720px]:justify-self-start",
  jobMeta: 'col-start-1 text-xs text-[var(--text-dim)] max-[720px]:col-span-full',
  jobActions: 'flex shrink-0 items-center gap-1',
  deleteButton:
    'grid size-7 place-items-center rounded-md text-[var(--text-dim)] transition-[color,background-color] duration-[var(--dur-hover)] ease-[var(--ease-out)] hover:bg-destructive/10 hover:text-destructive focus-visible:outline-2 focus-visible:outline-destructive focus-visible:outline-offset-2 disabled:pointer-events-none disabled:opacity-50',
  jobArrow:
    'grid size-7 place-items-center text-[var(--text-dim)] transition-colors hover:text-foreground no-underline',
} as const
