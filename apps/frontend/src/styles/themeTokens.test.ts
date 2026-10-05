import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const INDEX_CSS = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), '../index.css'),
  'utf8',
)

const SEMANTIC_COLOR_TOKENS = [
  '--background',
  '--foreground',
  '--card',
  '--card-foreground',
  '--popover',
  '--popover-foreground',
  '--primary',
  '--primary-foreground',
  '--secondary',
  '--secondary-foreground',
  '--muted',
  '--muted-foreground',
  '--accent',
  '--accent-foreground',
  '--destructive',
  '--border',
  '--input',
  '--ring',
] as const

// CSS 선택자 블록 토큰 추출
function tokensOf(selector: string): Record<string, string> {
  const escaped = selector.replace(/[[\]']/g, (match) => `\\${match}`)
  const block = new RegExp(`${escaped}\\s*\\{([^}]*)\\}`, 'g')
  const result: Record<string, string> = {}
  let match: RegExpExecArray | null

  while ((match = block.exec(INDEX_CSS)) !== null) {
    for (const line of match[1]!.split('\n')) {
      const declaration = /^\s*(--[\w-]+)\s*:\s*([^;]+);/.exec(line)
      if (declaration) result[declaration[1]!] = declaration[2]!.trim()
    }
  }

  return result
}

describe('design-system 의미 토큰', () => {
  const dark = tokensOf(':root')
  const light = tokensOf(":root[data-theme='light']")

  it.each(SEMANTIC_COLOR_TOKENS)('%s 이 다크·라이트 테마에 모두 존재한다', (token) => {
    expect(dark[token]).toBeDefined()
    expect(light[token]).toBeDefined()
  })

  it('Tailwind 색 유틸리티가 의미 토큰을 직접 참조한다', () => {
    for (const token of SEMANTIC_COLOR_TOKENS) {
      const utilityName = `--color-${token.slice(2)}`
      expect(INDEX_CSS).toContain(`${utilityName}: var(${token});`)
    }
  })

  it('프로젝트 폭 규약을 Tailwind spacing 토큰으로 노출한다', () => {
    expect(INDEX_CSS).toContain('--spacing-content-max: 1280px;')
    expect(INDEX_CSS).toContain('--spacing-content-narrow: 960px;')
    expect(INDEX_CSS).toContain('--spacing-content-list: 780px;')
    expect(INDEX_CSS).toContain('--spacing-form-max: 720px;')
  })
})
