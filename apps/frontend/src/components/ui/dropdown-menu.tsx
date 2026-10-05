import * as React from 'react'
import { useTopLayerBoundary } from '@/lib/top-layer-boundary'
import { DropdownMenu as DropdownMenuPrimitive } from 'radix-ui'

import { cn } from '@/lib/utils'

/**
 * 동작을 고르는 메뉴 (사이클 #13 §4.2).
 *
 * **`Select` 와 다른 자리다.** `Select` 는 값을 고르고 그 값이 트리거에 남지만, 여기서는
 * 항목을 누르는 것 자체가 동작이고 남는 상태가 없다. 체크 표시도 선택 유지도 필요 없다.
 *
 * 아이콘을 인라인으로 둔다 — `components/ui/` 는 `lib/` 외 계층을 참조할 수 없다
 * (`layerRules.test.ts`).
 */
function ChevronDown({ className }: { className?: string }) {
  return (
    <svg
      width={14}
      height={14}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      className={className}
    >
      <path d="m5 9 7 7 7-7" />
    </svg>
  )
}

const DropdownMenu = DropdownMenuPrimitive.Root

function DropdownMenuTrigger({
  className,
  children,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Trigger>) {
  return (
    <DropdownMenuPrimitive.Trigger
      data-slot="dropdown-menu-trigger"
      className={cn(
        'inline-flex cursor-pointer items-center gap-1.5 rounded-md border border-border bg-transparent px-2.5 py-1.5 text-xs font-semibold text-foreground outline-none transition-[background,color,border-color] duration-[var(--dur-hover)] ease-[var(--ease-out)] hover:border-primary hover:text-primary focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-2 motion-reduce:transition-none',
        className,
      )}
      {...props}
    >
      {children}
      {/* 열림 여부가 화살표로 드러난다 — 누르기 전에 메뉴인 줄 알아야 한다 */}
      <ChevronDown className="transition-transform duration-[var(--dur-hover)] ease-[var(--ease-out)] [[data-state=open]>&]:rotate-180 motion-reduce:transition-none" />
    </DropdownMenuPrimitive.Trigger>
  )
}

function DropdownMenuContent({
  className,
  sideOffset = 4,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Content>) {
  // Inside a native dialog the body portal would sink beneath the top layer —
  // target the dialog element instead (null outside any dialog: unchanged behavior)
  const boundary = useTopLayerBoundary()

  return (
    <DropdownMenuPrimitive.Portal container={boundary ?? undefined}>
      <DropdownMenuPrimitive.Content
        data-slot="dropdown-menu-content"
        sideOffset={sideOffset}
        className={cn(
          'z-[200] min-w-[10rem] origin-[var(--radix-dropdown-menu-content-transform-origin)] overflow-hidden rounded-[10px] border border-border bg-[var(--popover-bg)] p-1 text-foreground shadow-[var(--shadow-popover)] transition-[opacity,transform] duration-[var(--dur-popover)] ease-[var(--ease-out)] data-[state=closed]:scale-[var(--popover-enter-scale)] data-[state=closed]:opacity-0 data-[state=open]:scale-100 data-[state=open]:opacity-100 motion-reduce:transition-none',
          className,
        )}
        {...props}
      />
    </DropdownMenuPrimitive.Portal>
  )
}

function DropdownMenuItem({
  className,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Item>) {
  return (
    <DropdownMenuPrimitive.Item
      data-slot="dropdown-menu-item"
      className={cn(
        'relative flex w-full cursor-pointer items-center gap-2 rounded-[7px] px-2 py-[7px] text-sm text-muted-foreground outline-none select-none transition-[background,color] duration-[var(--dur-hover)] ease-[var(--ease-out)] data-[disabled]:pointer-events-none data-[disabled]:opacity-[0.45] data-[highlighted]:bg-accent data-[highlighted]:text-foreground motion-reduce:transition-none',
        className,
      )}
      {...props}
    />
  )
}

export { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger }
