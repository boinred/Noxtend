import { NavLink, useLocation } from 'react-router-dom'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Icon } from '@/features/shell/Icon'
import { NAV_GROUPS } from '@/routes/navItems'
import { prefetchForNav } from '@/routes/prefetch'
import { SIDEBAR_ITEM_CLASS, SIDEBAR_ITEM_ACTIVE_CLASS } from './sidebarStyles'
import { cn } from '@/lib/utils'

export default function MobileStudioMenu() {
  const { pathname } = useLocation()
  return (
    <>
      {NAV_GROUPS.map((group) => {
        const active = group.items.some(
          (item) => pathname === item.path || pathname.startsWith(`${item.path}/`),
        )
        return (
          <li key={group.key} className="min-w-0 flex-1">
            <DropdownMenu>
              <DropdownMenuTrigger
                className={cn(
                  SIDEBAR_ITEM_CLASS,
                  '[&>svg:last-child]:hidden',
                  active && SIDEBAR_ITEM_ACTIVE_CLASS,
                )}
                aria-label={`${group.label} 제작 메뉴`}
                aria-current={active ? 'page' : undefined}
                data-testid={`nav-group-${group.key}`}
              >
                <Icon name={group.key === 'threeD' ? 'cube' : 'image'} size={16} />
                <span>{group.label}</span>
              </DropdownMenuTrigger>
              <DropdownMenuContent
                side="top"
                className="z-[310]"
                aria-label={`${group.label} 제작`}
              >
                {group.items.map((item) => (
                  <DropdownMenuItem key={item.key} asChild>
                    <NavLink
                      to={item.path}
                      onFocus={prefetchForNav(item.key)}
                      onMouseEnter={prefetchForNav(item.key)}
                    >
                      <Icon name={item.icon} size={16} />
                      {item.label}
                      {item.comingSoon ? <span className="ml-auto text-xs">준비 중</span> : null}
                    </NavLink>
                  </DropdownMenuItem>
                ))}
              </DropdownMenuContent>
            </DropdownMenu>
          </li>
        )
      })}
    </>
  )
}
