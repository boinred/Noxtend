import { describe, expect, it } from 'vitest'
import { NAV_ITEMS, NAV_GROUPS, NAV_ITEMS_FOOTER } from '@/routes/navItems'
import {
  prefetchForNav,
  prefetchBackgroundStudio,
  prefetchCharacterStudio,
  prefetchSpriteStudio,
} from '@/routes/prefetch'
import { ROUTES, ROUTE_PATHS, jobPath } from '@/routes/paths'
import { CATEGORY_META_LIST } from '@/features/screens/categoryLabels'
import { assetCategoryLabel } from '@/domain/job/types'

const allItems = () => [
  ...NAV_ITEMS,
  ...NAV_GROUPS.flatMap((group) => group.items),
  ...NAV_ITEMS_FOOTER,
]
const navigablePaths = ROUTE_PATHS.filter(
  (path) => !path.includes(':') && !path.startsWith('/admin/'),
)

describe('홈·3D·2D 탐색', () => {
  it('홈과 설정은 제작 그룹 밖에 둔다', () => {
    expect(NAV_ITEMS.map((item) => item.key)).toEqual(['home'])
    expect(NAV_ITEMS[0]?.path).toBe(ROUTES.home)
    expect(NAV_ITEMS_FOOTER.map((item) => item.key)).toEqual(['admin'])
    expect(NAV_GROUPS.map((group) => group.key)).toEqual(['threeD', 'twoD'])
    expect(NAV_GROUPS.map((group) => group.label)).toEqual(['3D', '2D'])
    expect(NAV_ITEMS_FOOTER[0]?.path).toBe(ROUTES.admin)
    expect(
      CATEGORY_META_LIST.filter((meta) => meta.comingSoon).map((meta) => meta.category),
    ).toEqual(['object'])
  })
  it('각 그룹은 세 카테고리와 구별되는 접근 가능한 이름을 갖는다', () => {
    for (const group of NAV_GROUPS) {
      expect(group.items).toHaveLength(3)
      expect(group.items.map((item) => item.label)).toEqual(
        (['character', 'object', 'background'] as const).map(
          (category) => `${group.label} ${assetCategoryLabel(category)}`,
        ),
      )
    }
    const keys = allItems().map((item) => item.key)
    expect(new Set(keys).size).toBe(keys.length)
  })
  it('이동 가능한 모든 현재 경로를 홈·그룹·설정이 빠짐없이 제공한다', () => {
    const covered = new Set(allItems().map((item) => item.path))
    expect(navigablePaths.filter((path) => !covered.has(path))).toEqual([])
    expect(allItems().every((item) => ROUTE_PATHS.includes(item.path))).toBe(true)
    expect(allItems().every((item) => item.label && item.icon)).toBe(true)
    expect(ROUTE_PATHS).not.toContain('/canvas')
    expect(allItems().some((item) => item.key === 'canvas')).toBe(false)
  })
  it('3D 오브젝트와 2D 캐릭터·오브젝트는 준비 중이다', () => {
    expect(
      allItems()
        .filter((item) => item.comingSoon)
        .map((item) => item.key),
    ).toEqual(['object', 'spriteCharacter', 'spriteObject'])
  })
  it('구현된 스튜디오 키만 공유 지연 import를 프리페치한다', () => {
    expect(prefetchForNav('background')).toBe(prefetchBackgroundStudio)
    expect(prefetchForNav('character')).toBe(prefetchCharacterStudio)
    expect(prefetchForNav('spriteBackground')).toBe(prefetchSpriteStudio)
    for (const key of ['home', 'object', 'spriteObject', 'spriteCharacter', 'admin']) {
      expect(prefetchForNav(key)).toBeUndefined()
    }
  })
  it('LegacyAndSpriteJobs_RouteByMode', () => {
    expect(ROUTES.backgroundJob).toBe('/background/:jobId')
    expect(ROUTES.characterJob).toBe('/character/:jobId')
    expect(jobPath('background', 'old')).toBe('/background/old')
    expect(jobPath('character', 'old')).toBe('/character/old')
    expect(jobPath('background', 'new', 'twoD')).toBe('/2d/background/new')
    expect(jobPath('character', 'new', 'twoD')).toBe('/2d/character')
    expect(jobPath('object', 'new', 'twoD')).toBe('/2d/object')
    expect(ROUTES.spriteCharacter).toBe('/2d/character')
  })
})
