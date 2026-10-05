/**
 * Design Ref: §8.2 L1 #6~7 — 테마 대비율.
 *
 * 라이트 테마에는 시각 정본(목업)이 없다. "비슷한가" 로는 판정할 수 없으므로
 * WCAG AA(4.5:1) 를 계산해 판정한다. 눈대중 통과 선언을 막는 장치다.
 *
 * index.css 를 직접 읽어 검사한다. 문서에 적힌 값이 아니라 실제 코드가 대상이다.
 */
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const INDEX_CSS = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), '../index.css'),
  'utf8',
)

/** `:root { ... }` 처럼 셀렉터 블록 하나의 토큰을 뽑는다 */
function tokensOf(selector: string): Record<string, string> {
  const escaped = selector.replace(/[[\]']/g, (m) => `\\${m}`)
  const block = new RegExp(`${escaped}\\s*\\{([^}]*)\\}`, 'g')
  const result: Record<string, string> = {}
  let match: RegExpExecArray | null
  while ((match = block.exec(INDEX_CSS)) !== null) {
    for (const line of match[1]!.split('\n')) {
      const decl = /^\s*(--[\w-]+)\s*:\s*([^;]+);/.exec(line)
      if (decl) result[decl[1]!] = decl[2]!.trim()
    }
  }
  return result
}

function parseColor(value: string): { r: number; g: number; b: number; a: number } {
  const hex = /^#([0-9a-f]{6})$/i.exec(value)
  if (hex) {
    const n = parseInt(hex[1]!, 16)
    return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255, a: 1 }
  }
  const rgba = /^rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:[,\s/]+([\d.]+))?\s*\)$/i.exec(
    value,
  )
  if (rgba) {
    return {
      r: Number(rgba[1]),
      g: Number(rgba[2]),
      b: Number(rgba[3]),
      a: rgba[4] === undefined ? 1 : Number(rgba[4]),
    }
  }

  const oklch = /^oklch\(\s*([\d.]+)%?\s+([\d.]+)\s+([\d.]+)\s*\)$/i.exec(value)
  if (oklch) {
    const lightness = Number(oklch[1]) / (value.includes('%') ? 100 : 1)
    const chroma = Number(oklch[2])
    const hue = (Number(oklch[3]) * Math.PI) / 180
    const a = chroma * Math.cos(hue)
    const b = chroma * Math.sin(hue)

    // CSS Color 4 OKLab conversion followed by linear-sRGB companding.
    const lRoot = lightness + 0.3963377774 * a + 0.2158037573 * b
    const mRoot = lightness - 0.1055613458 * a - 0.0638541728 * b
    const sRoot = lightness - 0.0894841775 * a - 1.291485548 * b
    const l = lRoot ** 3
    const m = mRoot ** 3
    const s = sRoot ** 3
    const linear = [
      4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
      -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
      -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
    ]
    const encode = (channel: number) =>
      255 * (channel <= 0.0031308 ? 12.92 * channel : 1.055 * channel ** (1 / 2.4) - 0.055)
    const [r, g, blue] = linear.map((channel) => Math.min(255, Math.max(0, encode(channel))))
    return { r: r!, g: g!, b: blue!, a: 1 }
  }

  throw new Error(`해석할 수 없는 색: ${value}`)
}

/** 반투명 전경을 배경 위에 합성한다 */
function composite(fg: ReturnType<typeof parseColor>, bg: ReturnType<typeof parseColor>) {
  return {
    r: fg.r * fg.a + bg.r * (1 - fg.a),
    g: fg.g * fg.a + bg.g * (1 - fg.a),
    b: fg.b * fg.a + bg.b * (1 - fg.a),
    a: 1,
  }
}

function relativeLuminance({ r, g, b }: { r: number; g: number; b: number }): number {
  const channel = (v: number) => {
    const s = v / 255
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)
}

/** 반투명 색을 불투명 배경 위에 얹어 실제로 보이는 색(hex 아닌 rgb 문자열)을 만든다 */
function flatten(overlay: string, base: string): string {
  const { r, g, b } = composite(parseColor(overlay), parseColor(base))
  return `rgb(${Math.round(r)}, ${Math.round(g)}, ${Math.round(b)})`
}

export function contrastRatio(foreground: string, background: string): number {
  const bg = parseColor(background)
  const fg = composite(parseColor(foreground), bg)
  const [light, dark] = [relativeLuminance(fg), relativeLuminance(bg)].sort((a, b) => b - a)
  return (light! + 0.05) / (dark! + 0.05)
}

const AA = 4.5

/**
 * 본문 텍스트로 쓰이는 토큰들. 배경 대비 AA 를 넘어야 한다.
 *
 * `--destructive` 는 background-studio 사이클에서 추가됐다. 그전까지 오류 색이
 * CSS 안에 `#f87171` 로 하드코딩돼 있었고, 테마를 따르지 않아 라이트에서 2.77:1 이었다.
 */
const TEXT_TOKENS = [
  '--foreground',
  '--muted-foreground',
  '--text-dim',
  '--primary',
  '--destructive',
] as const

describe('대비율 계산기 자체 검증', () => {
  it('알려진 값과 일치한다', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 1)
    expect(contrastRatio('#ffffff', '#ffffff')).toBeCloseTo(1, 2)
  })

  it('반투명 전경을 배경과 합성한다', () => {
    // 완전 투명한 검정은 배경과 같아져 대비 1
    expect(contrastRatio('rgba(0, 0, 0, 0)', '#ffffff')).toBeCloseTo(1, 2)
  })
})

describe('#7 다크 테마 대비율', () => {
  const dark = tokensOf(':root')

  it.each(TEXT_TOKENS)('%s 이 배경 대비 AA 이상', (token) => {
    const ratio = contrastRatio(dark[token]!, dark['--background']!)
    expect(ratio, `${token} = ${dark[token]} → ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(AA)
  })

  it('패널 위 본문도 AA 이상', () => {
    expect(contrastRatio(dark['--foreground']!, dark['--node-header']!)).toBeGreaterThanOrEqual(AA)
  })

  it('콘솔 로그 색이 콘솔 배경 대비 AA 이상', () => {
    for (const token of ['--log-info', '--log-success', '--log-warn', '--log-error'] as const) {
      const ratio = contrastRatio(dark[token]!, dark['--console-bg']!)
      expect(ratio, `${token} = ${dark[token]} → ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(AA)
    }
  })
})

describe('#6 라이트 테마 대비율', () => {
  const light = tokensOf(":root[data-theme='light']")

  it('라이트 블록이 정의되어 있다', () => {
    expect(Object.keys(light).length).toBeGreaterThan(20)
  })

  it.each(TEXT_TOKENS)('%s 이 배경 대비 AA 이상', (token) => {
    const ratio = contrastRatio(light[token]!, light['--background']!)
    expect(ratio, `${token} = ${light[token]} → ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(AA)
  })

  it('패널 위 본문도 AA 이상', () => {
    expect(contrastRatio(light['--foreground']!, light['--node-header']!)).toBeGreaterThanOrEqual(
      AA,
    )
  })

  it('콘솔 로그 색이 콘솔 배경 대비 AA 이상', () => {
    for (const token of ['--log-info', '--log-success', '--log-warn', '--log-error'] as const) {
      const ratio = contrastRatio(light[token]!, light['--console-bg']!)
      expect(ratio, `${token} = ${light[token]} → ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(AA)
    }
  })

  it('액센트 위 텍스트(--primary-foreground)가 AA 이상', () => {
    const ratio = contrastRatio(light['--primary-foreground']!, light['--primary']!)
    expect(ratio, `→ ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(AA)
  })
})

describe('승인된 Calm Studio 팔레트', () => {
  const dark = tokensOf(':root')

  it.each([
    ['--background', 'oklch(15% 0.01 145)'],
    ['--primary', 'oklch(72% 0.14 140)'],
    ['--foreground', 'oklch(94% 0.008 145)'],
    ['--node-header', 'oklch(23% 0.014 145)'],
    ['--popover-bg', 'oklch(23% 0.014 145)'],
  ])('%s 가 승인 팔레트 값이다', (token, expected) => {
    expect(dark[token]).toBe(expected)
  })
})

/**
 * sidebar-layout Design §8.2 L1 #11~13.
 *
 * 사이드바는 자체 배경을 갖는다. 본문 배경 위에서 AA 를 통과했다고
 * 사이드바 위에서도 통과한다는 보장은 없다 — 별도로 계산한다.
 */
describe('#11~12 사이드바 대비율', () => {
  const SIDEBAR_TEXT = ['--foreground', '--muted-foreground', '--text-dim', '--primary'] as const

  it.each([
    [':root', '다크'],
    [":root[data-theme='light']", '라이트'],
  ])('%s (%s) 사이드바 배경 위 텍스트가 AA 이상', (selector) => {
    const theme = tokensOf(selector)
    const base = tokensOf(':root')
    // 사이드바 배경은 반투명이므로 본문 배경 위에 합성된 값이 실제 배경이다
    const pageBg = theme['--background'] ?? base['--background']!
    const sidebarBg = theme['--sidebar-bg']!

    for (const token of SIDEBAR_TEXT) {
      const fg = theme[token] ?? base[token]!
      // 반투명 사이드바 배경을 페이지 배경 위에 먼저 얹은 뒤 텍스트를 얹는다
      const effectiveBg = flatten(sidebarBg, pageBg)
      const ratio = contrastRatio(fg, effectiveBg)
      expect(
        ratio,
        `${token} = ${fg} on ${effectiveBg} → ${ratio.toFixed(2)}:1`,
      ).toBeGreaterThanOrEqual(AA)
    }
  })
})

describe('#13 사이드바 토큰이 두 테마에 모두 있다', () => {
  it('라이트가 --sidebar-bg 를 덮어쓴다', () => {
    const light = tokensOf(":root[data-theme='light']")
    expect(light['--sidebar-bg']).toBeDefined()
    expect(light['--sidebar-bg']).not.toBe(tokensOf(':root')['--sidebar-bg'])
  })

  it('레이아웃 폭 토큰이 정의되어 있다 (§3.3)', () => {
    const root = tokensOf(':root')
    expect(root['--sidebar-w']).toBe('200px')
    expect(root['--sidebar-w-collapsed']).toBe('56px')
  })
})

describe('토큰 대응', () => {
  it('라이트가 다크의 색 토큰을 빠짐없이 덮어쓴다', () => {
    const dark = tokensOf(':root')
    const light = tokensOf(":root[data-theme='light']")

    // 모션·치수 토큰은 테마와 무관하므로 제외한다
    const colorLike = Object.keys(dark).filter((k) => {
      const v = dark[k]!
      return v.startsWith('#') || v.startsWith('rgb') || v.startsWith('oklch')
    })

    const missing = colorLike.filter((k) => !(k in light))
    expect(missing, `라이트에 누락된 색 토큰: ${missing.join(', ')}`).toEqual([])
  })
})
