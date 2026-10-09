export const spriteStyles = {
  stack: 'flex min-w-0 flex-col gap-5',
  panel: 'min-w-0 rounded-xl border border-border bg-card p-5 max-[720px]:p-4',
  title: 'mb-3 text-base font-semibold text-foreground',
  hint: 'text-sm leading-relaxed text-muted-foreground',
  error: 'text-sm leading-relaxed text-destructive break-words',
  row: 'flex flex-wrap items-center gap-3',
  grid: 'grid min-w-0 grid-cols-2 gap-5 max-[720px]:grid-cols-1',
  field: 'flex min-w-0 flex-col gap-1.5 text-sm text-muted-foreground',
  textarea:
    'min-h-[120px] w-full resize-y rounded-lg border border-border bg-[var(--sunken-bg)] px-3 py-[9px] text-sm text-foreground focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1',
  results: 'grid grid-cols-3 gap-3 max-[720px]:grid-cols-2',
  result:
    'cursor-pointer overflow-hidden rounded-lg border border-border bg-[var(--sunken-bg)] p-1 aria-pressed:ring-2 aria-pressed:ring-primary focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-2',
  preview: 'relative w-full overflow-hidden rounded-lg border border-border',
  asset: 'flex min-w-0 flex-col gap-3 rounded-lg border border-border p-4',
  image: 'h-48 w-full object-contain',
  link: 'text-sm font-semibold text-primary underline underline-offset-4 break-all',
}

export const checkerboard = {
  backgroundColor: 'var(--card)',
  backgroundImage:
    'conic-gradient(var(--border) 25%, transparent 0 50%, var(--border) 0 75%, transparent 0)',
  backgroundSize: '20px 20px',
}
