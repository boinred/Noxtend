import type { SpriteRepeat, SpriteSettings, SpriteView } from './types'

const VIEW_LABELS: Record<SpriteView, string> = {
  sideView: '횡스크롤',
  topDown: '탑다운',
  isometric: '아이소메트릭',
}

export function spriteViewLabel(view: SpriteView): string {
  return VIEW_LABELS[view]
}

// 아이소메트릭 반복은 화면 축이 아니라 다이아몬드 격자 축
export function spriteRepeatLabel(repeat: SpriteRepeat, view: SpriteView): string {
  if (repeat === 'both') return '양쪽'
  if (view === 'isometric') return repeat === 'x' ? '격자 X축' : '격자 Y축'
  return repeat === 'x' ? '가로' : '세로'
}

export function spriteOutputLabel(settings: SpriteSettings): string {
  if (settings.outputKind === 'layers') return '배경 레이어'
  return `반복 타일 · ${settings.tileWidth}px · ${spriteRepeatLabel(settings.repeat, settings.view)}`
}
