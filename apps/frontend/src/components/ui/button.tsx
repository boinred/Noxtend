import * as React from 'react'
import { cva, type VariantProps } from 'class-variance-authority'
import { Slot } from 'radix-ui'

import { cn } from '@/lib/utils'

// shadcn Button variant and size mapping
const buttonVariants = cva(
  "group/button inline-flex shrink-0 items-center justify-center gap-[6px] rounded-lg border border-transparent bg-clip-padding px-[14px] py-2 text-sm font-semibold whitespace-nowrap outline-none select-none transition-[background,color,border-color,filter,transform] duration-[var(--dur-release)] ease-[var(--ease-out)] focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-2 active:not-disabled:scale-[var(--press-scale)] active:not-disabled:duration-[var(--dur-press)] disabled:cursor-not-allowed disabled:opacity-[0.45] motion-reduce:transition-none motion-reduce:active:not-disabled:scale-100 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
  {
    variants: {
      variant: {
        default: 'bg-primary text-primary-foreground hover:brightness-[1.08]',
        outline:
          'border-border bg-background text-foreground hover:bg-accent hover:text-foreground aria-expanded:bg-accent aria-expanded:text-foreground',
        secondary:
          'border-border bg-transparent text-foreground hover:border-muted-foreground hover:bg-accent aria-expanded:bg-accent aria-expanded:text-foreground',
        ghost:
          'bg-transparent text-muted-foreground hover:bg-accent hover:text-foreground aria-expanded:bg-accent aria-expanded:text-foreground',
        destructive:
          'border-[color-mix(in_srgb,var(--destructive)_32%,transparent)] bg-transparent text-destructive hover:border-destructive hover:bg-[color-mix(in_srgb,var(--destructive)_12%,transparent)] focus-visible:outline-destructive',
        link: 'text-primary underline-offset-4 hover:underline',
      },
      size: {
        default: '',
        xs: "h-6 gap-1 rounded-[min(var(--radius-md),10px)] px-2 text-xs in-data-[slot=button-group]:rounded-lg has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&_svg:not([class*='size-'])]:size-3",
        sm: "h-7 gap-1 rounded-[min(var(--radius-md),12px)] px-2.5 text-[0.8rem] in-data-[slot=button-group]:rounded-lg has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&_svg:not([class*='size-'])]:size-3.5",
        lg: 'h-9 gap-1.5 px-2.5 has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2',
        icon: 'size-8',
        'icon-xs':
          "size-6 rounded-[min(var(--radius-md),10px)] in-data-[slot=button-group]:rounded-lg [&_svg:not([class*='size-'])]:size-3",
        'icon-sm':
          'size-7 rounded-[min(var(--radius-md),12px)] in-data-[slot=button-group]:rounded-lg',
        'icon-lg': 'size-9',
      },
    },
    defaultVariants: {
      variant: 'default',
      size: 'default',
    },
  },
)

// Polymorphic button rendering
function Button({
  className,
  variant = 'default',
  size = 'default',
  asChild = false,
  ...props
}: React.ComponentProps<'button'> &
  VariantProps<typeof buttonVariants> & {
    asChild?: boolean
  }) {
  const Comp = asChild ? Slot.Root : 'button'

  return (
    <Comp
      data-slot="button"
      data-variant={variant}
      data-size={size}
      className={cn(buttonVariants({ variant, size, className }))}
      {...props}
    />
  )
}

export { Button, buttonVariants }
