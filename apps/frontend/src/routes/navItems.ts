/**
 * Design Ref: §3.2, §10.2 — 사이드바 항목의 유일한 정의처.
 *
 * 화면이 늘면 여기만 고친다. 사이드바는 이 배열을 그리기만 한다.
 *
 * 카테고리 3종의 라벨은 도메인의 `categoryLabel()` 을 그대로 쓴다
 * (app-shell 사이클 D-4 결정 유지). 홈·캔버스는 카테고리가 아니므로 여기서 정한다.
 */
import { assetCategoryLabel } from '@/domain/job/types'
import { ROUTES } from '@/routes/paths'
import type { IconName } from '@/features/shell/Icon'

export interface NavItem {
  key: string
  label: string
  icon: IconName
  path: string
  /** 준비 중인 화면임을 사이드바에서 표시할지 */
  comingSoon?: boolean
}

export const NAV_ITEMS: readonly NavItem[] = [
  { key: 'home', label: '홈', icon: 'home', path: ROUTES.home },
]

export interface NavGroup {
  key: 'threeD' | 'twoD'
  label: string
  items: readonly NavItem[]
}

export const NAV_GROUPS: readonly NavGroup[] = [
  {
    key: 'threeD',
    label: '3D',
    items: [
      {
        key: 'character',
        label: `3D ${assetCategoryLabel('character')}`,
        icon: 'user',
        path: ROUTES.character,
      },
      {
        key: 'object',
        label: `3D ${assetCategoryLabel('object')}`,
        icon: 'cube',
        path: ROUTES.object,
        comingSoon: true,
      },
      {
        key: 'background',
        label: `3D ${assetCategoryLabel('background')}`,
        icon: 'globe',
        path: ROUTES.background,
      },
    ],
  },
  {
    key: 'twoD',
    label: '2D',
    items: [
      {
        key: 'spriteCharacter',
        label: `2D ${assetCategoryLabel('character')}`,
        icon: 'user',
        path: ROUTES.spriteCharacter,
        comingSoon: true,
      },
      {
        key: 'spriteObject',
        label: `2D ${assetCategoryLabel('object')}`,
        icon: 'cube',
        path: ROUTES.spriteObject,
        comingSoon: true,
      },
      {
        key: 'spriteBackground',
        label: `2D ${assetCategoryLabel('background')}`,
        icon: 'image',
        path: ROUTES.spriteBackground,
      },
    ],
  },
]

/**
 * Design Ref: background-studio §5.1 — 사이드바 하단 영역.
 *
 * 관리자는 작업 대상이 아니라 설정이므로 카테고리들과 같은 줄에 두지 않는다.
 * 구분선이 그 위계를 보여준다. 접힘 상태에서도 아이콘 레일에 남는다.
 */
export const NAV_ITEMS_FOOTER: readonly NavItem[] = [
  { key: 'admin', label: '관리자', icon: 'settings', path: ROUTES.admin },
]
