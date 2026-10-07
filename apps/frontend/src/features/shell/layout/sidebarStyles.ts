import { cn } from '@/lib/utils'

// 사이드바 골격과 반응형 하단 레일
export const SIDEBAR_CLASS = cn(
  'group/sidebar z-[110] flex h-screen w-[var(--sidebar-w)] flex-[0_0_var(--sidebar-w)] flex-col overflow-visible border-r border-border bg-[var(--sidebar-bg)]',
  'data-[collapsed=true]:w-[var(--sidebar-w-collapsed)] data-[collapsed=true]:basis-[var(--sidebar-w-collapsed)]',
  'max-[720px]:fixed max-[720px]:inset-x-0 max-[720px]:bottom-0 max-[720px]:z-[300] max-[720px]:h-16 max-[720px]:w-full max-[720px]:basis-auto max-[720px]:flex-row max-[720px]:items-stretch max-[720px]:border-t max-[720px]:border-r-0',
)

export const SIDEBAR_BRAND_CLASS = cn(
  'flex h-[var(--header-h)] flex-[0_0_var(--header-h)] items-center gap-[10px] overflow-hidden border-b border-border px-4',
  'group-data-[collapsed=true]/sidebar:px-[18px] max-[720px]:hidden',
)

export const SIDEBAR_BRAND_TEXT_CLASS = cn(
  'font-[var(--font-display)] text-base font-bold whitespace-nowrap text-foreground transition-opacity duration-[var(--dur-hover)] ease-[var(--ease-out)]',
  'group-data-[collapsed=true]/sidebar:hidden',
)

export const SIDEBAR_NAV_CLASS = cn(
  'min-h-0 flex-1 overflow-y-auto overflow-x-hidden px-2 py-[10px]',
  'max-[720px]:flex max-[720px]:min-w-0 max-[720px]:flex-[3_1_0] max-[720px]:overflow-visible max-[720px]:px-[3px] max-[720px]:py-[5px]',
)

export const SIDEBAR_FOOTER_CLASS = cn(
  'flex-none overflow-x-hidden px-2 pb-[6px]',
  'max-[720px]:flex max-[720px]:min-w-0 max-[720px]:flex-[1_1_0] max-[720px]:overflow-visible max-[720px]:px-[3px] max-[720px]:py-[5px]',
)

export const SIDEBAR_LIST_CLASS =
  'm-0 flex list-none flex-col gap-[2px] p-0 max-[720px]:w-full max-[720px]:flex-row max-[720px]:gap-0'

export const SIDEBAR_ITEM_ROW_CLASS = 'relative max-[720px]:min-w-0 max-[720px]:flex-1'

// 탐색 항목 공통 상호작용과 모바일 레일 형태
export const SIDEBAR_ITEM_CLASS = cn(
  'group/item relative flex w-full items-center gap-[10px] rounded-lg border border-transparent bg-transparent px-[10px] py-[9px] text-[0.82rem] font-medium whitespace-nowrap text-muted-foreground no-underline',
  'transition-[background-color,color,transform] duration-[var(--dur-release)] ease-[var(--ease-out)] active:scale-[var(--press-scale)] active:duration-[var(--dur-press)] motion-reduce:transition-none motion-reduce:active:scale-100',
  '[@media(hover:hover)_and_(pointer:fine)]:hover:bg-[var(--sidebar-item-hover-bg)] [@media(hover:hover)_and_(pointer:fine)]:hover:text-foreground',
  'max-[720px]:h-[54px] max-[720px]:flex-col max-[720px]:justify-center max-[720px]:gap-[3px] max-[720px]:rounded-lg max-[720px]:px-[2px] max-[720px]:py-[5px]',
)

export const SIDEBAR_ITEM_ACTIVE_CLASS =
  'border-[var(--active-border)] bg-[var(--sidebar-item-active-bg)] text-primary'

export const SIDEBAR_ITEM_ICON_CLASS = 'flex flex-[0_0_20px] items-center justify-center'

export const SIDEBAR_ITEM_LABEL_CLASS = 'min-w-0 flex-1 overflow-hidden text-left text-ellipsis'

// 접힘 상태 라벨 툴팁과 모바일 라벨 복원
export const SIDEBAR_COLLAPSED_LABEL_CLASS = cn(
  'pointer-events-none invisible absolute top-1/2 left-0 z-[200] w-max -translate-y-1/2 scale-[var(--popover-enter-scale)] origin-left rounded-md border border-border bg-[var(--popover-bg)] px-[10px] py-[5px] text-xs text-foreground opacity-0 shadow-[var(--shadow-popover)]',
  'transition-[opacity,transform,visibility,left] duration-[var(--dur-hover)] ease-[var(--ease-out)]',
  '[@media(hover:hover)_and_(pointer:fine)]:group-hover/item:visible [@media(hover:hover)_and_(pointer:fine)]:group-hover/item:left-[calc(100%+10px)] [@media(hover:hover)_and_(pointer:fine)]:group-hover/item:scale-100 [@media(hover:hover)_and_(pointer:fine)]:group-hover/item:opacity-100 group-focus-visible/item:visible group-focus-visible/item:left-[calc(100%+10px)] group-focus-visible/item:scale-100 group-focus-visible/item:opacity-100',
  'max-[720px]:pointer-events-auto max-[720px]:visible max-[720px]:static max-[720px]:w-full max-[720px]:translate-y-0 max-[720px]:scale-100 max-[720px]:overflow-hidden max-[720px]:border-0 max-[720px]:bg-transparent max-[720px]:p-0 max-[720px]:text-center max-[720px]:text-[0.625rem] max-[720px]:leading-none max-[720px]:text-inherit max-[720px]:opacity-100 max-[720px]:shadow-none',
)

export const SIDEBAR_ITEM_DOT_CLASS =
  'size-[5px] flex-none rounded-full bg-[var(--text-dim)] max-[720px]:absolute max-[720px]:top-[7px] max-[720px]:right-[calc(50%-15px)]'

export const SIDEBAR_TOGGLE_CLASS = 'mx-2 mt-2 mb-2 w-auto flex-none max-[720px]:hidden'
