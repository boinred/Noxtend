/**
 * Design Ref: sidebar-layout §8.2 L1 #8~10 + background-studio §8.3 L1-F #7~#9
 *
 * background-studio 사이클에서 재작성했다 (§8.6). 이 파일은 배경을 언급하지 않으면서도
 * **항목 개수와 준비 중 표시 개수를 전제**하므로 깨졌다 — "제거 대상을 참조하는 파일"
 * 이 아니라 "개수를 전제로 쓰는 파일" 기준으로 세야 한다는 교훈이 여기서 나왔다.
 */
import { describe, expect, it } from 'vitest'
import { NAV_ITEMS, NAV_ITEMS_FOOTER } from '@/routes/navItems'
import { ROUTES, ROUTE_PATHS, backgroundJobPath, characterJobPath } from '@/routes/paths'
import { assetCategoryLabel } from '@/domain/job/types'
import { CATEGORY_META_LIST } from '@/features/screens/categoryLabels'

/**
 * 사이드바가 가리켜야 하는 목적지.
 *
 * 제외 둘:
 * - 파라미터 라우트 — 가리킬 구체적 주소가 없다
 * - `/admin/*` 하위 — 사이클 #5 에서 관리자가 4화면이 됐지만 **섹션 탭**으로 나눴다
 *   (§2.3-6). 사이드바는 제작 흐름(캐릭터·오브젝트·배경)의 자리이고, 관리하는
 *   흐름을 섞으면 둘 다 흐려진다. `/admin` 진입점 하나만 사이드바에 있으면 된다.
 */
const NAVIGABLE_PATHS = ROUTE_PATHS.filter(
  // Task 14 메뉴 연결 전 정적 2D 직접 진입 경로
  (path) => !path.includes(':') && !path.startsWith('/admin/') && path !== ROUTES.spriteBackground,
)

describe('#8 항목 구성', () => {
  it('일반 4개 + 하단 1개다', () => {
    expect(NAV_ITEMS).toHaveLength(4)
    expect(NAV_ITEMS_FOOTER).toHaveLength(1)
  })

  it('제거된 캔버스 항목을 노출하지 않는다', () => {
    expect(NAV_ITEMS.some((item) => item.key === 'canvas')).toBe(false)
    expect(ROUTE_PATHS).not.toContain('/canvas')
  })

  it('첫 항목이 홈이다 (Plan §1.4 결정 2)', () => {
    expect(NAV_ITEMS[0]?.key).toBe('home')
    expect(NAV_ITEMS[0]?.path).toBe(ROUTES.home)
  })

  it('키가 중복되지 않는다', () => {
    const keys = [...NAV_ITEMS, ...NAV_ITEMS_FOOTER].map((item) => item.key)
    expect(new Set(keys).size).toBe(keys.length)
  })

  it('모든 항목이 라벨과 아이콘을 갖는다', () => {
    for (const item of [...NAV_ITEMS, ...NAV_ITEMS_FOOTER]) {
      expect(item.label, `${item.key} 라벨`).not.toBe('')
      expect(item.icon, `${item.key} 아이콘`).toBeTruthy()
    }
  })
})

describe('#9 카테고리 라벨이 도메인에서 온다', () => {
  it.each([
    ['character', 'character'],
    ['object', 'object'],
    ['background', 'background'],
  ] as const)('%s 항목이 assetCategoryLabel(%s) 과 일치', (navKey, category) => {
    const item = NAV_ITEMS.find((n) => n.key === navKey)
    expect(item?.label).toBe(assetCategoryLabel(category))
  })
})

describe('#10 경로가 ROUTES 상수에서 온다', () => {
  it('모든 항목 경로가 ROUTES 값에 있다 (하드코딩 없음)', () => {
    const all = [...NAV_ITEMS, ...NAV_ITEMS_FOOTER]
    const stray = all.filter((item) => !ROUTE_PATHS.includes(item.path))
    expect(stray.map((s) => `${s.key}:${s.path}`)).toEqual([])
  })

  it('이동 가능한 모든 경로가 사이드바에 노출된다', () => {
    const covered = new Set([...NAV_ITEMS, ...NAV_ITEMS_FOOTER].map((item) => item.path))
    const missing = NAVIGABLE_PATHS.filter((path) => !covered.has(path))
    expect(missing).toEqual([])
  })
})

// background-studio §8.3 L1-F #7·#8
describe('L1-F #7 하단 영역', () => {
  it('관리자 하나뿐이다', () => {
    expect(NAV_ITEMS_FOOTER.map((item) => item.key)).toEqual(['admin'])
    expect(NAV_ITEMS_FOOTER[0]?.path).toBe(ROUTES.admin)
  })

  it('하단 항목에는 준비 중 표시가 없다', () => {
    // 설정은 예고가 아니라 지금 쓰는 것이다
    expect(NAV_ITEMS_FOOTER.every((item) => !item.comingSoon)).toBe(true)
  })
})

describe('L1-F #8 준비 중 표시', () => {
  it('배경에는 붙지 않는다 — 실제 화면이 생겼다', () => {
    const background = NAV_ITEMS.find((item) => item.key === 'background')
    expect(background?.comingSoon).toBeUndefined()
  })

  it('캐릭터에도 붙지 않는다 — character-studio 사이클에서 실제 화면이 생겼다', () => {
    const character = NAV_ITEMS.find((item) => item.key === 'character')
    expect(character?.comingSoon).toBeUndefined()
  })

  it('오브젝트에만 남는다', () => {
    expect(NAV_ITEMS.filter((item) => item.comingSoon).map((item) => item.key)).toEqual(['object'])
  })
})

// background-studio §8.3 L1-F #9
describe('L1-F #9 작업 라우트', () => {
  it('/background/:jobId 가 상수에서 온다', () => {
    expect(ROUTES.backgroundJob).toBe('/background/:jobId')
  })

  it('경로 헬퍼가 배경 경로 아래에 붙는다', () => {
    // 리터럴 조합을 화면에 두면 경로를 바꿀 때 누락된다
    expect(backgroundJobPath('abc-123')).toBe(`${ROUTES.background}/abc-123`)
  })
})

// character-studio §6.3 — /character 스위치 세 곳이 함께 켜졌는지 고정한다.
// 하나라도 어긋나면 라우트만 생기고 배지가 "준비 중" 으로 남거나 자동 화면과 충돌한다
describe('character-studio 라우팅 스위치 세 곳', () => {
  it('① categoryLabels 에서 캐릭터가 comingSoon 이 아니다', () => {
    const character = CATEGORY_META_LIST.find((meta) => meta.category === 'character')
    expect(character?.comingSoon).toBe(false)
  })

  it('② 캐릭터 작업 라우트가 상수에서 온다', () => {
    expect(ROUTES.characterJob).toBe('/character/:jobId')
    expect(characterJobPath('abc-123')).toBe(`${ROUTES.character}/abc-123`)
  })

  it('③ 사이드바 캐릭터 항목에 준비 중 표시가 없다', () => {
    const character = NAV_ITEMS.find((item) => item.key === 'character')
    expect(character?.comingSoon).toBeUndefined()
  })
})
