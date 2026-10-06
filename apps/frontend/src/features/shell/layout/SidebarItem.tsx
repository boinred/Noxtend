/**
 * Design Ref: §4.1 #2, §5.4 — 사이드바 항목 하나.
 *
 * `NavLink` 를 쓰는 이유는 활성 판정을 직접 하지 않기 위해서다.
 * `aria-current="page"` 도 라우터가 붙여준다 — 시각 표시와 접근성 표시가
 * 서로 다른 조건으로 달라질 여지를 없앤다 (§4.1 #2).
 */
import { NavLink } from 'react-router-dom'
import { Icon } from '@/features/shell/Icon'
import {
  SIDEBAR_COLLAPSED_LABEL_CLASS,
  SIDEBAR_ITEM_ACTIVE_CLASS,
  SIDEBAR_ITEM_CLASS,
  SIDEBAR_ITEM_DOT_CLASS,
  SIDEBAR_ITEM_ICON_CLASS,
  SIDEBAR_ITEM_LABEL_CLASS,
  SIDEBAR_ITEM_ROW_CLASS,
} from '@/features/shell/layout/sidebarStyles'
import type { NavItem } from '@/routes/navItems'
import { cn } from '@/lib/utils'
import { prefetchForNav } from '@/routes/prefetch'

export interface SidebarItemProps {
  item: NavItem
  collapsed: boolean
  testId?: string
}

export function SidebarItem({ item, collapsed, testId }: SidebarItemProps) {
  // 지연 로드된 화면이면 손이 닿는 순간 청크를 받아둔다
  const prefetch = prefetchForNav(item.key)

  return (
    <li className={SIDEBAR_ITEM_ROW_CLASS}>
      <NavLink
        to={item.path}
        end={item.key === 'home'}
        aria-label={item.label}
        onMouseEnter={prefetch}
        onFocus={prefetch}
        className={({ isActive }) =>
          cn(
            SIDEBAR_ITEM_CLASS,
            collapsed && 'justify-center gap-0 px-0',
            isActive && SIDEBAR_ITEM_ACTIVE_CLASS,
          )
        }
        data-testid={testId ?? `nav-${item.key}`}
      >
        <span className={SIDEBAR_ITEM_ICON_CLASS}>
          <Icon name={item.icon} size={16} />
        </span>

        {/*
          접혀도 라벨을 DOM 에서 지우지 않는다. 지우면 링크의 접근 가능한 이름이 사라진다.
          대신 CSS 가 툴팁으로 바꾼다 (§5.1 "접힘 상태에서 hover 시 라벨 툴팁").
        */}
        <span className={cn(SIDEBAR_ITEM_LABEL_CLASS, collapsed && SIDEBAR_COLLAPSED_LABEL_CLASS)}>
          {item.label}
        </span>

        {item.comingSoon && !collapsed ? (
          <span className={SIDEBAR_ITEM_DOT_CLASS} aria-hidden="true" title="준비 중" />
        ) : null}
      </NavLink>
    </li>
  )
}
