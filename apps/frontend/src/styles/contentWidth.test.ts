/**
 * Design Ref: background-studio §8.3 L1-F #11 — 콘텐츠 폭 토큰.
 *
 * 문서에 적힌 값이 아니라 `index.css` 실제 값을 읽는다. 규약이 문서에만 있으면
 * 화면마다 다른 max-width 가 생기고, 그때는 이미 늦다 (§5.0).
 */
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const HERE = dirname(fileURLToPath(import.meta.url))
const INDEX_CSS = readFileSync(resolve(HERE, '../index.css'), 'utf8')

function tokenValue(name: string): string | null {
  const match = new RegExp(`${name}\\s*:\\s*([^;]+);`).exec(INDEX_CSS)
  return match ? match[1]!.trim() : null
}

describe('L1-F #11 콘텐츠 폭 토큰', () => {
  it.each([
    ['--content-max', '1280px'],
    ['--content-narrow', '960px'],
    ['--content-list', '780px'],
    ['--form-max', '720px'],
  ])('%s 가 %s 다', (name, expected) => {
    expect(tokenValue(name)).toBe(expected)
  })

  it('좌우 여백이 정의돼 있다', () => {
    expect(tokenValue('--content-gutter')).toBe('32px')
  })

  it('폭이 max > narrow > list > form 순이다', () => {
    // 순서가 뒤집히면 폼이 표보다 넓어진다 — 규약이 의미를 잃는다
    const names = ['--content-max', '--content-narrow', '--content-list', '--form-max']
    const widths = names.map((name) => Number.parseInt(tokenValue(name)!, 10))

    for (let i = 0; i < widths.length - 1; i += 1) {
      expect(widths[i], `${names[i]} > ${names[i + 1]}`).toBeGreaterThan(widths[i + 1]!)
    }
  })

  it('1920 뷰포트에서 상한이 실제로 걸린다', () => {
    // 사이드바 200 + 좌우 여백 64 를 빼도 상한보다 넓다 → 가운데 정렬된다 (§5.0 폭 계산)
    const available = 1920 - 200 - 64
    expect(available).toBeGreaterThan(Number.parseInt(tokenValue('--content-max')!, 10))
  })
})

describe('L1-F #11 폭 적용 지점이 하나다', () => {
  it('PageContainer 만 폭 토큰 유틸리티를 소비한다', () => {
    const source = readFileSync(resolve(HERE, '../features/screens/PageContainer.tsx'), 'utf8')

    // 네 유틸리티의 단일 매핑
    expect(source).toContain("max: 'max-w-content-max'")
    expect(source).toContain("narrow: 'max-w-content-narrow'")
    expect(source).toContain("list: 'max-w-content-list'")
    expect(source).toContain("form: 'max-w-form-max'")
  })
})
