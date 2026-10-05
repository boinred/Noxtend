/**
 * Design Ref: design-system §6 Module 3 — 관리자 Tailwind 스타일 계약.
 * 여러 관리자 화면이 공유하는 표·폼 패턴의 계산값을 한곳에 유지한다.
 */

// 관리자 공통 레이아웃
export const adminStyles = {
  toolbar:
    'mb-4 flex items-center justify-between gap-3 max-[720px]:flex-col max-[720px]:items-stretch max-[720px]:[&>:last-child]:self-end',
  panel: 'overflow-x-auto rounded-xl border border-border bg-card',
  goldenForm: 'p-5',
  table:
    'w-full min-w-[720px] border-collapse text-sm [&_th]:whitespace-nowrap [&_th]:border-b [&_th]:border-border [&_th]:px-4 [&_th]:py-3 [&_th]:text-left [&_th]:font-semibold [&_th]:text-muted-foreground [&_td]:border-b [&_td]:border-border [&_td]:px-4 [&_td]:py-3.5 [&_td]:align-middle [&_td]:text-foreground [&_tr:last-child_td]:border-b-0',
  empty:
    'rounded-[10px] border border-dashed border-border p-5 text-center text-sm text-muted-foreground',

  // 공급자 목록 상태와 행 동작
  maskedKey: 'font-mono tracking-[0.05em] text-muted-foreground',
  actions: 'flex flex-col items-end gap-2',
  actionRow: 'flex gap-1.5',
  state:
    "inline-flex items-center gap-1.5 text-[0.8125rem] [&[data-enabled='true']>span]:bg-primary",
  stateDot: 'size-2 rounded-full bg-muted-foreground',
  testResult:
    "max-w-full text-right text-[0.8125rem] text-muted-foreground data-[ok='false']:text-destructive data-[ok='true']:text-primary",
  capabilityList: 'flex flex-wrap gap-1.5',
  capabilityBadge:
    'inline-flex items-center gap-1 whitespace-nowrap rounded-full border px-2 py-0.5 text-[0.6875rem] font-semibold data-[capability=imageGeneration]:border-[var(--capability-image-border)] data-[capability=imageGeneration]:bg-[var(--capability-image-bg)] data-[capability=imageGeneration]:text-[var(--capability-image)] data-[capability=textAnalysis]:border-[var(--capability-text-border)] data-[capability=textAnalysis]:bg-[var(--capability-text-bg)] data-[capability=textAnalysis]:text-[var(--capability-text)]',
  capabilityMark:
    'inline-grid w-3 place-items-center font-mono text-[0.625rem] font-bold leading-none',

  // 관리자 내부 탐색
  tabs: 'mb-5 inline-flex gap-1 overflow-x-visible rounded-[10px] border border-border bg-card p-1 max-[720px]:flex max-[720px]:max-w-full max-[720px]:overflow-x-auto',
  tab: 'rounded-[7px] px-4 py-[7px] text-sm font-semibold text-muted-foreground no-underline transition-[background,color] duration-[var(--dur-hover)] ease-[var(--ease-out)] hover:bg-accent hover:text-foreground max-[720px]:shrink-0 max-[720px]:px-[13px]',
  active: 'bg-[var(--sunken-bg)] text-foreground',

  // 관리자 공통 폼
  field: 'mb-4',
  label: 'mb-1.5 block text-[0.8125rem] font-semibold text-muted-foreground',
  input:
    'w-full rounded-lg border border-border bg-[var(--sunken-bg)] px-3 py-[9px] text-sm text-foreground focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1 disabled:cursor-not-allowed disabled:border-dashed disabled:bg-transparent disabled:text-muted-foreground',
  textarea:
    'w-full resize-y rounded-lg border border-border bg-[var(--sunken-bg)] px-3 py-[9px] font-mono text-sm leading-[1.6] text-foreground focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1 disabled:cursor-not-allowed disabled:border-dashed disabled:bg-transparent disabled:text-muted-foreground',
  hint: 'mt-1.5 text-xs text-muted-foreground',
  error: 'mb-3 text-[0.8125rem] text-destructive',
  formActions: 'mt-2 flex justify-end gap-2',

  // 프롬프트 버전과 이력
  version: 'font-mono font-bold text-foreground',
  missing: 'text-[0.8125rem] text-destructive',
  variables: 'font-mono text-xs text-muted-foreground',
  note: 'text-[0.8125rem] text-muted-foreground',
  historySection: 'mt-7',
  historyTitle: 'mb-3 text-sm font-semibold text-muted-foreground',
  history: 'm-0 flex list-none flex-col gap-2 p-0',
  historyItem: 'rounded-lg border border-border bg-card px-3 py-2.5',
  historyHead: 'flex items-center gap-2.5',
  historyNote: 'text-[0.8125rem] text-muted-foreground',
  activeBadge:
    'ml-2 rounded-full border border-primary px-2 py-0.5 text-[0.6875rem] font-semibold text-primary',

  // 골든 세트 목록
  goldenList: 'mt-5 flex list-none flex-col gap-2.5 p-0',
  goldenItem:
    'flex items-center gap-3.5 rounded-[10px] border border-border bg-card p-3 max-[720px]:items-start',
  goldenThumb: 'w-24 shrink-0 rounded-md bg-[var(--sunken-bg)] max-[720px]:w-[72px]',
  goldenBody: 'min-w-0 flex-1',
  goldenName: 'text-[0.9375rem] font-semibold text-foreground no-underline hover:underline',
  goldenExpected: 'mt-1 text-[0.8125rem] text-muted-foreground',

  // 골든 실행 비교와 판정
  compare: 'mt-5 grid grid-cols-[repeat(auto-fit,minmax(min(420px,100%),1fr))] gap-4',
  pane: 'rounded-xl border border-border bg-card p-3.5',
  // 골든 비교의 파츠 썸네일 (사이클 #7 FR-17) — 두 실행이 같은 크기여야 눈으로 견줄 수 있다
  paneThumb: 'block size-20 rounded-md border border-border bg-[var(--sunken-bg)] object-contain',
  paneTitle: 'mb-2.5 text-[0.8125rem] font-semibold text-muted-foreground',
  paneParts: 'mt-3 flex list-none flex-col gap-1 p-0 text-[0.8125rem] text-foreground',
  partCategory:
    'ml-1.5 rounded-full border border-border px-1.5 py-px text-[0.6875rem] text-muted-foreground',
  verdictForm: 'mt-3.5 flex flex-col gap-2',
  verdict: "data-[pass='false']:text-destructive data-[pass='true']:text-primary",

  // 단가 폼과 금액 표현
  formBody: 'p-5',
  formRow: 'grid grid-cols-[repeat(auto-fit,minmax(180px,1fr))] gap-3',
  amount: 'whitespace-nowrap text-right font-mono',
  scheduledBadge:
    'ml-2 rounded-full border border-border px-2 py-0.5 text-[0.6875rem] font-semibold text-muted-foreground',
  warning:
    'mb-4 rounded-lg border border-[var(--accent-amber)] px-3 py-2.5 text-[0.8125rem] leading-[1.6] text-foreground',
} as const

// 공급자 등록·수정 폼의 독립 폭과 간격
export const providerFormStyles = {
  form: 'mb-5 flex w-full max-w-form-max flex-col gap-4',
  field: 'flex flex-col gap-1.5',
  label: 'text-[0.8125rem] font-semibold text-muted-foreground',
  input:
    'rounded-lg border border-border bg-[var(--sunken-bg)] px-3 py-[9px] text-sm text-foreground focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1',
  hint: 'text-xs text-muted-foreground',
  capabilityList: 'flex flex-wrap gap-1.5',
  row: 'flex justify-end gap-2',
  error: 'text-[0.8125rem] text-destructive',
} as const
