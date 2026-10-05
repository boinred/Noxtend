/**
 * Design Ref: §2.1 — 앱 골격. 사이드바 + 헤더 + 화면.
 *
 * 레이아웃 라우트의 element 이므로 화면이 바뀌어도 언마운트되지 않는다.
 * 접힘 상태가 화면 이동으로 초기화되지 않는 것은 그 결과다 (§4.2 #4).
 *
 * `HeaderSlotProvider` 가 `AppHeader` 와 `<Outlet/>` 을 모두 감싸야 한다.
 * 화면(Outlet 안)이 헤더(밖)에 컨트롤을 넣으려면 둘이 같은 컨텍스트 아래 있어야 한다.
 */
import { Outlet } from 'react-router-dom'
import { AppHeader } from '@/features/shell/layout/AppHeader'
import { HeaderSlotProvider } from '@/features/shell/layout/HeaderSlot'
import { Sidebar } from '@/features/shell/layout/Sidebar'
import { useSidebar } from '@/app/useSidebar'

export function AppLayout() {
  const { state, collapsed, toggle } = useSidebar()

  return (
    <div className="flex h-screen overflow-hidden" data-sidebar={state} data-testid="app-layout">
      <a
        className="fixed top-2 left-2 z-[1000] -translate-y-[160%] rounded-lg bg-[var(--popover-bg)] px-3 py-2 text-foreground no-underline transition-transform duration-[var(--dur-hover)] ease-[var(--ease-out)] focus:translate-y-0"
        href="#main-content"
      >
        본문으로 건너뛰기
      </a>
      <Sidebar collapsed={collapsed} onToggle={toggle} />

      <HeaderSlotProvider>
        <div className="flex min-w-0 flex-1 flex-col overflow-hidden max-[720px]:pb-16">
          <AppHeader />
          <main
            id="main-content"
            className="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden"
            data-testid="app-content"
          >
            <Outlet />
          </main>
        </div>
      </HeaderSlotProvider>
    </div>
  )
}
