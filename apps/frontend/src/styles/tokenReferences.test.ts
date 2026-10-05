/**
 * CSS가 참조하는 커스텀 속성이 실제로 정의되어 있는지 검사한다.
 *
 * **이 테스트는 background-studio 사이클의 Gap G-1 에서 나왔다.** 신규 화면 6개 파일이
 * `--text-primary` · `--text-secondary` 를 30회 참조했는데 두 토큰은 어디에도 없었다.
 * 무효 선언은 `body` 색을 상속하므로 **글자가 보인다** — E2E 166건이 전부 통과했고
 * 아무도 알아채지 못했다. 잃은 것은 시각 위계였고, 그것은 단언하기 어려운 종류다.
 *
 * 대비 테스트가 이것을 못 잡은 이유는 **정의된 토큰만** 검사하기 때문이다.
 * 존재하지 않는 토큰은 검사 대상 자체가 아니었다. 여기서 그 구멍을 막는다.
 */
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const SRC = resolve(dirname(fileURLToPath(import.meta.url)), '..')

function cssFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return cssFiles(full)
    return full.endsWith('.css') ? [full] : []
  })
}

/** `--name:` 형태의 선언을 모은다. 전역이든 컴포넌트 지역이든 정의는 정의다. */
function definedIn(css: string): Set<string> {
  return new Set([...css.matchAll(/(--[\w-]+)\s*:/g)].map((m) => m[1]!))
}

/**
 * `var(--name)` 참조를 모은다. 폴백이 있는 `var(--name, x)` 는 제외한다 —
 * 폴백은 미정의를 의도한 경우이고, 그 자체의 옳고 그름은 별개 문제다.
 */
function referencedIn(css: string): Set<string> {
  return new Set([...css.matchAll(/var\(\s*(--[\w-]+)\s*\)/g)].map((m) => m[1]!))
}

const ALL_CSS = cssFiles(SRC)
const GLOBAL_DEFINED = new Set<string>([
  ...definedIn(readFileSync(join(SRC, 'index.css'), 'utf8')),
  ...definedIn(readFileSync(join(SRC, 'styles/global.css'), 'utf8')),
])

describe('CSS 커스텀 속성 참조', () => {
  it('모든 var(--x) 참조가 정의된 토큰을 가리킨다', () => {
    const dangling: string[] = []

    for (const file of ALL_CSS) {
      const css = readFileSync(file, 'utf8')
      // 같은 파일에서 정의한 지역 커스텀 속성도 유효하다 (예: --press-response-duration)
      const known = new Set([...GLOBAL_DEFINED, ...definedIn(css)])

      for (const name of referencedIn(css)) {
        if (!known.has(name)) {
          dangling.push(`${file.replace(`${SRC}/`, '')} → ${name}`)
        }
      }
    }

    expect(dangling, `정의되지 않은 토큰 참조:\n${dangling.join('\n')}`).toEqual([])
  })

  it('폴백에 색을 하드코딩하지 않는다', () => {
    // Plan §4.2 — CSS 모듈 색 하드코딩 0건.
    // `var(--x, #hex)` 는 토큰이 없을 때 그 hex 가 실질 색이 되고, 테마를 따르지 않는다
    const hardcoded: string[] = []

    for (const file of ALL_CSS) {
      if (file.endsWith('index.css') || file.endsWith('global.css')) continue

      const css = readFileSync(file, 'utf8')
      for (const match of css.matchAll(/var\(\s*--[\w-]+\s*,\s*(#[0-9a-fA-F]{3,8})\s*\)/g)) {
        hardcoded.push(`${file.replace(`${SRC}/`, '')} → ${match[1]}`)
      }
    }

    expect(hardcoded, `폴백 하드코딩:\n${hardcoded.join('\n')}`).toEqual([])
  })
})
