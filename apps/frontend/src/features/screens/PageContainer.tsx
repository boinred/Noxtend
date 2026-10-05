/**
 * Design Ref: background-studio §5.0 — 콘텐츠 폭 규약의 유일한 적용 지점.
 *
 * 화면마다 max-width 를 적으면 세 값이 곧 다섯 값이 된다. 여기서 토큰 셋 중
 * 하나만 고르게 하면 규약이 코드로 강제된다.
 */
import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/**
 * `max` 표·대시보드 1280 / `narrow` 산문 읽기 폭 960 /
 * `list` 짧은 행 목록 780 / `form` 입력·폼 720
 */
export type ContentWidth = 'max' | 'narrow' | 'list' | 'form'

export interface PageContainerProps {
  width: ContentWidth
  title?: string
  subtitle?: string
  children: ReactNode
  testId?: string
}

// 콘텐츠 폭 규약과 Tailwind 유틸리티 대응
const WIDTH_CLASS: Record<ContentWidth, string> = {
  max: 'max-w-content-max',
  narrow: 'max-w-content-narrow',
  list: 'max-w-content-list',
  form: 'max-w-form-max',
}

export function PageContainer({ width, title, subtitle, children, testId }: PageContainerProps) {
  return (
    <div
      className="h-full w-full overflow-y-auto overflow-x-hidden px-[var(--content-gutter)] pt-10 pb-[72px] max-[720px]:pt-6 max-[720px]:pb-10"
      data-testid={testId}
    >
      <div
        className={cn('mx-auto w-full', WIDTH_CLASS[width])}
        data-width={width}
        data-testid="page-inner"
      >
        {title ? (
          <header className="mb-[30px] max-[720px]:mb-[22px]">
            <h1 className="m-0 font-[var(--font-display)] text-[clamp(1.375rem,2vw,1.75rem)] font-[680] tracking-[-0.025em] text-foreground">
              {title}
            </h1>
            {subtitle ? (
              <p className="mt-[7px] mb-0 max-w-[680px] text-sm leading-[1.6] text-muted-foreground">
                {subtitle}
              </p>
            ) : null}
          </header>
        ) : null}
        {children}
      </div>
    </div>
  )
}
