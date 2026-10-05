/**
 * Design Ref: §9.3 — 공통 프런트엔드 의존성 조립 지점.
 */
import { useMemo } from 'react'
import type { ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router-dom'
import { createLocalLayoutPreferences } from '@/infra/layout/localLayoutPreferences'
import { LayoutPreferencesContext } from '@/app/useSidebar'

export function AppProviders({ children }: { children: ReactNode }) {
  /**
   * 사이드바 선호는 편집기 상태와 무관하므로 EditorDeps 에 넣지 않는다.
   * 사용자별 설정이 서버로 가면 이 한 줄만 교체된다 (Design §3.1).
   */
  const layoutPreferences = useMemo(() => createLocalLayoutPreferences(), [])
  const queryClient = useMemo(
    () =>
      new QueryClient({
        defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
      }),
    [],
  )

  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <LayoutPreferencesContext value={layoutPreferences}>{children}</LayoutPreferencesContext>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
