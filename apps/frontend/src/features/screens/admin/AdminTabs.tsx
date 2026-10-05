/**
 * Design Ref: §5.2 · §2.3-6 — 관리자 섹션 탭.
 *
 * **사이드바를 늘리지 않는다.** 사이드바는 제작 흐름(캐릭터·오브젝트·배경)의
 * 자리이고 관리자는 그 밖이다. 화면이 1개에서 4개로 늘었다고 사이드바에 항목을 더하면
 * 만드는 흐름과 관리하는 흐름이 섞인다.
 */
import { NavLink } from 'react-router-dom'
import { ROUTES } from '@/routes/paths'
import { adminStyles as styles } from './adminStyles'

const SECTIONS = [
  { to: ROUTES.adminProviders, label: '공급자', testId: 'admin-tab-providers' },
  { to: ROUTES.adminPrompts, label: '프롬프트', testId: 'admin-tab-prompts' },
  { to: ROUTES.adminGolden, label: '골든 세트', testId: 'admin-tab-golden' },
  { to: ROUTES.adminPrices, label: '단가', testId: 'admin-tab-prices' },
  { to: ROUTES.adminCalls, label: '내역', testId: 'admin-tab-calls' },
] as const

export function AdminTabs() {
  return (
    <nav className={styles.tabs} data-testid="admin-tabs">
      {SECTIONS.map((section) => (
        <NavLink
          key={section.to}
          to={section.to}
          // 하위 경로(프롬프트 편집 등)에서도 상위 탭이 켜져 있어야 위치를 잃지 않는다.
          // NavLink 의 end=false 기본값이 그것을 해준다
          className={({ isActive }) => (isActive ? `${styles.tab} ${styles.active}` : styles.tab)}
          data-testid={section.testId}
        >
          {section.label}
        </NavLink>
      ))}
    </nav>
  )
}
