import { readFileSync, readdirSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const SRC = resolve(dirname(fileURLToPath(import.meta.url)), '..')

// 네이티브 select 재유입 방지 — 펼친 목록이 OS 렌더링으로 넘어가면 디자인 토큰이 무효화된다
function nativeSelectUsages(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return nativeSelectUsages(full)
    if (!full.endsWith('.tsx')) return []

    // JSX 여는 태그만 대상 — `<selection>` 같은 접두 일치와 닫는 태그는 제외
    return /<select[\s/>]/.test(readFileSync(full, 'utf8')) ? [full] : []
  })
}

describe('Select 프리미티브 마이그레이션', () => {
  it('src 전체에 네이티브 select가 남거나 다시 추가되지 않는다', () => {
    const remaining = nativeSelectUsages(SRC).map((file) => file.replace(`${SRC}/`, ''))

    expect(remaining, `남은 네이티브 select:\n${remaining.join('\n')}`).toEqual([])
  })
})
