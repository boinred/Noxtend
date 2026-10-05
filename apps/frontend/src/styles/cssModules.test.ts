import { readdirSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const SRC = resolve(dirname(fileURLToPath(import.meta.url)), '..')

// 신규 화면까지 포함하는 CSS Module 재유입 방지
function cssModules(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return cssModules(full)
    return full.endsWith('.module.css') ? [full] : []
  })
}

describe('CSS Modules 마이그레이션', () => {
  it('src 전체에 CSS Module이 남거나 다시 추가되지 않는다', () => {
    const remaining = cssModules(SRC).map((file) => file.replace(`${SRC}/`, ''))

    expect(remaining, `남은 CSS Modules:\n${remaining.join('\n')}`).toEqual([])
  })
})
