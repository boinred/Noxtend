/**
 * Design Ref: §5.2 — 아이콘은 CDN(FontAwesome) 의존 대신 인라인 SVG 로 제공한다.
 * NFR: 외부 자산 장애 시에도 핵심 레이아웃과 조작이 유지되어야 한다.
 */
import type { SVGProps } from 'react'

export type IconName =
  | 'brand'
  | 'user'
  | 'cube'
  | 'globe'
  | 'flask'
  | 'search'
  | 'play'
  | 'reset'
  | 'info'
  | 'terminal'
  | 'trash'
  | 'vial'
  | 'plus'
  | 'close'
  | 'zoom-in'
  | 'zoom-out'
  | 'fit'
  | 'eye'
  | 'image'
  | 'prompt'
  | 'sparkles'
  | 'layers'
  | 'stop'
  | 'home'
  | 'arrow-left'
  | 'arrow-right'
  | 'sun'
  | 'moon'
  | 'clock'
  | 'chevron-left'
  | 'chevron-right'
  | 'chevron-down'
  | 'chevron-up'
  | 'settings'
  | 'copy'
  | 'upload'
  | 'check'

/** 24x24 viewBox, currentColor stroke 기준 */
const PATHS: Record<IconName, string> = {
  brand: 'M12 2 3 7l9 5 9-5-9-5Zm-9 10 9 5 9-5M3 17l9 5 9-5',
  user: 'M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z',
  cube: 'M21 8v8l-9 5-9-5V8l9-5 9 5Zm-18 0 9 5 9-5m-9 5v8',
  globe:
    'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Zm-9-9h18M12 3c2.5 2.5 3.5 6 3.5 9S14.5 18.5 12 21c-2.5-2.5-3.5-6-3.5-9S9.5 5.5 12 3Z',
  flask: 'M9 3h6M10 3v6L5 19a2 2 0 0 0 1.8 3h10.4A2 2 0 0 0 19 19l-5-10V3M7.5 14h9',
  search: 'M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm10 2-4.35-4.35',
  play: 'M6 4l14 8-14 8V4Z',
  reset: 'M3 12a9 9 0 1 0 3-6.7M3 4v5h5',
  info: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Zm0-13h.01M11 12h1v5h1',
  terminal: 'M4 17l6-5-6-5M12 19h8',
  trash: 'M3 6h18M8 6V4h8v2M6 6l1 15h10l1-15M10 11v6M14 11v6',
  vial: 'M15 3 6.5 11.5a4.95 4.95 0 0 0 7 7L22 10M12 6l6 6M4 21h6',
  plus: 'M12 5v14M5 12h14',
  close: 'M18 6 6 18M6 6l12 12',
  'zoom-in': 'M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm10 2-4.35-4.35M11 8v6M8 11h6',
  'zoom-out': 'M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm10 2-4.35-4.35M8 11h6',
  fit: 'M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5',
  eye: 'M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7Zm10 3a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z',
  image: 'M3 5h18v14H3V5Zm3 10 3.5-4 3 3.5L15 11l3 4M8.5 9.5h.01',
  prompt: 'M4 6h16M4 12h10M4 18h7M17 14l4 4-4 4v-8Z',
  sparkles:
    'M12 3l1.8 4.7L18.5 9.5 13.8 11.3 12 16l-1.8-4.7L5.5 9.5l4.7-1.8L12 3ZM19 15l.9 2.3L22 18l-2.1.8L19 21l-.9-2.2L16 18l2.1-.7L19 15Z',
  layers: 'M12 3 3 8l9 5 9-5-9-5ZM3 13l9 5 9-5M3 18l9 5 9-5',
  stop: 'M6 6h12v12H6z',
  home: 'M3 10.5 12 3l9 7.5M5.5 9.5V20h13V9.5M10 20v-6h4v6',
  'arrow-left': 'M19 12H5m6-7-7 7 7 7',
  'arrow-right': 'M5 12h14m-6-7 7 7-7 7',
  sun: 'M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10Zm0-14v2m0 14v2M3 12h2m14 0h2M5.6 5.6l1.4 1.4m10 10 1.4 1.4m0-12.8-1.4 1.4m-10 10L5.6 18.4',
  moon: 'M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5Z',
  clock: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Zm0-14v5l3.5 2',
  'chevron-left': 'm15 5-7 7 7 7',
  'chevron-right': 'm9 5 7 7-7 7',
  'chevron-down': 'm5 9 7 7 7-7',
  'chevron-up': 'm5 15 7-7 7 7',
  settings:
    'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm7.4-3a7.4 7.4 0 0 0-.1-1.1l2-1.5-2-3.4-2.3 1a7.5 7.5 0 0 0-1.9-1.1L14.7 3H9.3l-.4 2.4a7.5 7.5 0 0 0-1.9 1.1l-2.3-1-2 3.4 2 1.5a7.4 7.4 0 0 0 0 2.2l-2 1.5 2 3.4 2.3-1c.6.5 1.2.8 1.9 1.1l.4 2.4h5.4l.4-2.4c.7-.3 1.3-.6 1.9-1.1l2.3 1 2-3.4-2-1.5c.1-.4.1-.7.1-1.1Z',
  copy: 'M9 9h10v12H9zM5 15H3V3h12v2',
  upload: 'M12 16V4m-5 5 5-5 5 5M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2',
  check: 'm5 13 4 4 10-10',
}

export interface IconProps extends Omit<SVGProps<SVGSVGElement>, 'name'> {
  name: IconName
  size?: number
}

export function Icon({ name, size = 16, ...rest }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...rest}
    >
      <path d={PATHS[name]} />
    </svg>
  )
}

/** 레지스트리 icon 키 → 아이콘 이름. 모르는 키는 cube 로 대체한다. */
export function iconForDefinition(key: string): IconName {
  return (Object.keys(PATHS) as IconName[]).includes(key as IconName) ? (key as IconName) : 'cube'
}
