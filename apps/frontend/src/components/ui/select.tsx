import * as React from 'react'
import { Select as SelectPrimitive } from 'radix-ui'

import { cn } from '@/lib/utils'

/**
 * 아이콘을 인라인으로 둔다 — `components/ui/` 는 `lib/` 외 계층을 참조할 수 없다
 * (design-system Design §9, `layerRules.test.ts`). `features/shell/Icon` 을 끌어오면
 * 프리미티브가 화면 계층에 묶인다.
 */
function ChevronDown({ className }: { className?: string }) {
  return (
    <svg
      width={16}
      height={16}
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

function CheckMark() {
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
    >
      <path d="m5 13 4 4 10-10" />
    </svg>
  )
}

function Select({ ...props }: React.ComponentProps<typeof SelectPrimitive.Root>) {
  return <SelectPrimitive.Root data-slot="select" {...props} />
}

function SelectGroup({ ...props }: React.ComponentProps<typeof SelectPrimitive.Group>) {
  return <SelectPrimitive.Group data-slot="select-group" {...props} />
}

function SelectValue({ ...props }: React.ComponentProps<typeof SelectPrimitive.Value>) {
  return <SelectPrimitive.Value data-slot="select-value" {...props} />
}

// 네이티브 select 가 쓰던 골격을 그대로 잇는다 — 필드 높이가 바뀌면 폼 레이아웃이 흔들린다
function SelectTrigger({
  className,
  children,
  ...props
}: React.ComponentProps<typeof SelectPrimitive.Trigger>) {
  return (
    <SelectPrimitive.Trigger
      data-slot="select-trigger"
      className={cn(
        'group/select-trigger flex w-full items-center justify-between gap-2 rounded-lg border border-border bg-[var(--sunken-bg)] px-3 py-[9px] text-left text-sm text-foreground outline-none transition-[background,border-color] duration-[var(--dur-hover)] ease-[var(--ease-out)] focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1 disabled:cursor-not-allowed disabled:opacity-[0.45] data-[placeholder]:text-muted-foreground [&_svg]:pointer-events-none [&_svg]:shrink-0',
        className,
      )}
      {...props}
    >
      {children}
      <SelectPrimitive.Icon asChild>
        {/* 열림 상태를 화살표 회전으로 알린다 */}
        <ChevronDown className="text-muted-foreground transition-transform duration-[var(--dur-hover)] ease-[var(--ease-out)] group-data-[state=open]/select-trigger:rotate-180 motion-reduce:transition-none" />
      </SelectPrimitive.Icon>
    </SelectPrimitive.Trigger>
  )
}

// Avatar 팝오버와 같은 진입 모션 — 팝오버끼리 다르게 열리면 화면이 산만해진다
function SelectContent({
  className,
  children,
  position = 'popper',
  ...props
}: React.ComponentProps<typeof SelectPrimitive.Content>) {
  return (
    <SelectPrimitive.Portal>
      <SelectPrimitive.Content
        data-slot="select-content"
        position={position}
        className={cn(
          'relative z-[200] max-h-[min(24rem,var(--radix-select-content-available-height))] min-w-[8rem] origin-[var(--radix-select-content-transform-origin)] overflow-hidden rounded-[10px] border border-border bg-[var(--popover-bg)] text-foreground shadow-[var(--shadow-popover)] transition-[opacity,transform] duration-[var(--dur-popover)] ease-[var(--ease-out)] data-[state=closed]:scale-[var(--popover-enter-scale)] data-[state=closed]:opacity-0 data-[state=open]:scale-100 data-[state=open]:opacity-100 motion-reduce:transition-none',
          // popper 모드에서 트리거 너비에 맞춰 목록이 어긋나 보이지 않게 한다
          position === 'popper' &&
            'w-[var(--radix-select-trigger-width)] data-[side=bottom]:translate-y-1 data-[side=top]:-translate-y-1',
          className,
        )}
        {...props}
      >
        <SelectPrimitive.Viewport className="p-1">{children}</SelectPrimitive.Viewport>
      </SelectPrimitive.Content>
    </SelectPrimitive.Portal>
  )
}

function SelectLabel({ className, ...props }: React.ComponentProps<typeof SelectPrimitive.Label>) {
  return (
    <SelectPrimitive.Label
      data-slot="select-label"
      className={cn('px-2 py-1.5 text-xs font-semibold text-muted-foreground', className)}
      {...props}
    />
  )
}

function SelectItem({
  className,
  children,
  hint,
  ...props
}: React.ComponentProps<typeof SelectPrimitive.Item> & {
  /**
   * 항목에만 붙는 보조 한 줄. **`ItemText` 바깥에 둔다** — 안에 넣으면 닫힌 트리거의
   * `SelectValue` 까지 이 글자를 따라 그려서 고른 값을 읽기 어려워진다.
   */
  hint?: React.ReactNode
}) {
  return (
    <SelectPrimitive.Item
      data-slot="select-item"
      className={cn(
        'relative flex w-full cursor-pointer items-center gap-2 rounded-[7px] py-[7px] pr-2 pl-8 text-sm text-muted-foreground outline-none select-none transition-[background,color] duration-[var(--dur-hover)] ease-[var(--ease-out)] data-[disabled]:pointer-events-none data-[disabled]:opacity-[0.45] data-[highlighted]:bg-accent data-[highlighted]:text-foreground data-[state=checked]:text-foreground motion-reduce:transition-none [&_svg]:pointer-events-none [&_svg]:shrink-0',
        className,
      )}
      {...props}
    >
      {/* 체크 자리를 항상 비워둔다 — 선택이 바뀔 때 글자가 밀리지 않는다 */}
      <span className="absolute left-2 flex items-center justify-center text-primary">
        <SelectPrimitive.ItemIndicator>
          <CheckMark />
        </SelectPrimitive.ItemIndicator>
      </span>
      <span className="flex min-w-0 flex-col gap-0.5">
        <SelectPrimitive.ItemText>{children}</SelectPrimitive.ItemText>
        {hint === undefined ? null : <span className="text-xs text-muted-foreground">{hint}</span>}
      </span>
    </SelectPrimitive.Item>
  )
}

function SelectSeparator({
  className,
  ...props
}: React.ComponentProps<typeof SelectPrimitive.Separator>) {
  return (
    <SelectPrimitive.Separator
      data-slot="select-separator"
      className={cn('-mx-1 my-1 h-px bg-border', className)}
      {...props}
    />
  )
}

export {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectSeparator,
  SelectTrigger,
  SelectValue,
}
