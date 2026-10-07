/**
 * Design Ref: §4.1, §5.1 — 좌측 탐색 (FR-01~FR-03).
 *
 * 사이드바는 `NAV_ITEMS` 를 그리기만 한다. 화면이 늘면 `routes/navItems.ts` 만 고친다.
 *
 * 접어도 완전히 숨기지 않는다. 숨기면 캔버스 폭은 되찾지만 이동할 때마다 펼쳐야 한다.
 * 56px 아이콘 레일이 그 절충점이다 (§1.1 목표 2).
 */
import { lazy, Suspense, useEffect, useState } from 'react'
import { Icon } from '@/features/shell/Icon'
import { SidebarItem } from '@/features/shell/layout/SidebarItem'
import {
  SIDEBAR_BRAND_CLASS,
  SIDEBAR_BRAND_TEXT_CLASS,
  SIDEBAR_CLASS,
  SIDEBAR_COLLAPSED_LABEL_CLASS,
  SIDEBAR_FOOTER_CLASS,
  SIDEBAR_ITEM_CLASS,
  SIDEBAR_ITEM_ICON_CLASS,
  SIDEBAR_ITEM_LABEL_CLASS,
  SIDEBAR_LIST_CLASS,
  SIDEBAR_NAV_CLASS,
  SIDEBAR_TOGGLE_CLASS,
} from '@/features/shell/layout/sidebarStyles'
import { NAV_ITEMS, NAV_GROUPS, NAV_ITEMS_FOOTER } from '@/routes/navItems'
import { cn } from '@/lib/utils'

const MobileStudioMenu = lazy(() => import('./MobileStudioMenu'))

export interface SidebarProps {
  collapsed: boolean
  onToggle: () => void
}

export function Sidebar({ collapsed, onToggle }: SidebarProps) {
  const [mobile, setMobile] = useState(() => window.matchMedia('(max-width: 720px)').matches)
  useEffect(() => {
    const media = window.matchMedia('(max-width: 720px)')
    const update = () => setMobile(media.matches)
    media.addEventListener('change', update)
    return () => media.removeEventListener('change', update)
  }, [])
  return (
    <aside
      className={SIDEBAR_CLASS}
      data-collapsed={collapsed}
      data-testid="sidebar"
      aria-label="주요 메뉴"
    >
      <div className={SIDEBAR_BRAND_CLASS}>
        <Icon name="brand" size={20} className="flex-none text-primary" />
        <span className={SIDEBAR_BRAND_TEXT_CLASS}>Noxtend</span>
      </div>

      <nav
        className={cn(
          SIDEBAR_NAV_CLASS,
          'max-[720px]:hidden group-data-[collapsed=true]/sidebar:overflow-visible',
        )}
        aria-label="제작 탐색"
      >
        <ul className={SIDEBAR_LIST_CLASS}>
          {NAV_ITEMS.map((item) => (
            <SidebarItem key={item.key} item={item} collapsed={collapsed} />
          ))}
        </ul>
        {NAV_GROUPS.map((group) => (
          <div key={group.key} role="group" aria-label={`${group.label} 제작`}>
            <h2
              className={cn(
                'mt-4 mb-1 px-[10px] text-xs font-semibold text-muted-foreground',
                collapsed && 'px-0 text-center',
              )}
            >
              {group.label}
            </h2>
            <ul className={SIDEBAR_LIST_CLASS}>
              {group.items.map((item) => (
                <SidebarItem key={item.key} item={item} collapsed={collapsed} />
              ))}
            </ul>
          </div>
        ))}
      </nav>

      <nav
        className={cn(SIDEBAR_NAV_CLASS, 'hidden max-[720px]:flex')}
        aria-label="모바일 제작 탐색"
      >
        <ul className={SIDEBAR_LIST_CLASS}>
          {NAV_ITEMS.map((item) => (
            <SidebarItem
              key={item.key}
              item={item}
              collapsed={collapsed}
              testId={`nav-mobile-${item.key}`}
            />
          ))}
          {mobile ? (
            <Suspense
              fallback={NAV_GROUPS.map((group) => (
                <li key={group.key} className="min-w-0 flex-1">
                  <span className={SIDEBAR_ITEM_CLASS} role="status">
                    {group.label} 메뉴 불러오는 중…
                  </span>
                </li>
              ))}
            >
              <MobileStudioMenu />
            </Suspense>
          ) : null}
        </ul>
      </nav>

      {/*
        Design Ref: background-studio §5.1 — 하단 영역.
        설정은 작업 대상이 아니므로 카테고리들과 같은 줄에 두지 않는다.
        `nav` 가 flex:1 이라 이 블록은 자연히 바닥으로 밀린다.
      */}
      <nav className={SIDEBAR_FOOTER_CLASS} aria-label="설정">
        <div
          className="mx-1 mb-2 h-px bg-border max-[720px]:hidden"
          data-testid="sidebar-footer-divider"
        />
        <ul className={SIDEBAR_LIST_CLASS}>
          {NAV_ITEMS_FOOTER.map((item) => (
            <SidebarItem key={item.key} item={item} collapsed={collapsed} />
          ))}
        </ul>
      </nav>

      <button
        type="button"
        className={cn(
          SIDEBAR_ITEM_CLASS,
          SIDEBAR_TOGGLE_CLASS,
          collapsed && 'justify-center gap-0 px-0',
        )}
        onClick={onToggle}
        aria-expanded={!collapsed}
        aria-label={collapsed ? '사이드바 펼치기' : '사이드바 접기'}
        data-testid="sidebar-toggle"
      >
        <span className={SIDEBAR_ITEM_ICON_CLASS}>
          <Icon name={collapsed ? 'chevron-right' : 'chevron-left'} size={16} />
        </span>
        <span className={cn(SIDEBAR_ITEM_LABEL_CLASS, collapsed && SIDEBAR_COLLAPSED_LABEL_CLASS)}>
          {collapsed ? '펼치기' : '접기'}
        </span>
      </button>
    </aside>
  )
}
