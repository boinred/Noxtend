/**
 * 지연 로드된 화면 묶음을 미리 받아두는 자리.
 *
 * **`import()` 지정자가 여기 한 벌만 있어야 한다.** 라우트 정의와 프리페치가 서로 다른
 * 문자열을 쓰면 번들러가 청크를 둘로 쪼개고, 미리 받은 것과 실제로 쓰는 것이 달라진다.
 * 그래서 `routes/index.tsx` 의 `lazy()` 도 이 함수를 통해 같은 지정자를 가리킨다.
 *
 * 같은 모듈을 두 번 부르는 것은 공짜다 — 모듈 레지스트리가 이미 받은 것을 돌려준다.
 */
export function importBackgroundStudio() {
  return import('@/features/screens/background/BackgroundStudioScreen')
}

/**
 * 링크에 손이 닿는 순간 청크를 받아둔다.
 *
 * 반환값을 버리는 것이 의도다 — 프리페치는 실패해도 조용해야 한다. 실제로 화면이
 * 필요해지면 `lazy()` 가 같은 import 를 다시 부르고, 그때는 Suspense 가 실패를 맡는다.
 */
export function prefetchBackgroundStudio(): void {
  void importBackgroundStudio().catch(() => {})
}

export function importCharacterStudio() {
  return import('@/features/screens/character/CharacterStudioScreen')
}

export function prefetchCharacterStudio(): void {
  void importCharacterStudio().catch(() => {})
}

/**
 * 사이드바 항목이 미리 받을 것이 있는가.
 *
 * "어떤 화면이 지연 로드인가" 를 아는 자리는 여기다. `SidebarItem` 은 항목을 그리는
 * 일만 하므로, 거기에 화면 이름을 박아 넣으면 화면이 늘 때마다 그 컴포넌트를 고쳐야 한다.
 */
export function prefetchForNav(key: string): (() => void) | undefined {
  if (key === 'background') return prefetchBackgroundStudio
  if (key === 'character') return prefetchCharacterStudio
  return undefined
}

export function importSpriteStudio() {
  return import('@/features/screens/sprites/SpriteStudioScreen')
}

export function prefetchSpriteStudio(): void {
  void importSpriteStudio().catch(() => {})
}
