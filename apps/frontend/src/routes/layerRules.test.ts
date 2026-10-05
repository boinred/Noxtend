/**
 * Design Ref: §9.2 — 프론트 계층 의존 규칙.
 *
 * **Check 단계 G-5 에서 나왔다.** 규칙은 문서에 있었지만 강제하는 것이 없어
 * `BackgroundStudioScreen` 이 업로드만 `infra/api` 로 직접 불렀다 —
 * 그래서 업로드만 캐시·무효화 밖에 있었다.
 *
 * .NET 은 프로젝트 참조로 계층을 강제하지만 (§9.2) TypeScript 에는 그런 수단이 없다.
 * 이 테스트가 그 자리를 대신한다.
 */
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const SRC = resolve(dirname(fileURLToPath(import.meta.url)), '..')

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return sourceFiles(full)
    return /\.tsx?$/.test(full) && !/\.test\.tsx?$/.test(full) ? [full] : []
  })
}

/** `from '@/x/y'` 의 별칭 경로들. */
function importsOf(source: string): string[] {
  return [...source.matchAll(/from\s+'(@\/[^']+)'/g)].map((m) => m[1]!)
}

function violations(fromDir: string, forbidden: RegExp): string[] {
  const dir = join(SRC, fromDir)
  const found: string[] = []

  for (const file of sourceFiles(dir)) {
    for (const specifier of importsOf(readFileSync(file, 'utf8'))) {
      if (forbidden.test(specifier)) {
        found.push(`${file.replace(`${SRC}/`, '')} → ${specifier}`)
      }
    }
  }

  return found
}

describe('§9.2 계층 의존 규칙', () => {
  it('domain 은 외부 라이브러리를 참조하지 않는다', () => {
    for (const domain of ['domain/job', 'domain/provider']) {
      const dir = join(SRC, domain)
      for (const file of sourceFiles(dir)) {
        const source = readFileSync(file, 'utf8')
        // 어떤 import 도 없어야 한다 — 타입조차 밖에서 끌어오지 않는다
        expect(
          [...source.matchAll(/^import\s/gm)].length,
          `${file.replace(`${SRC}/`, '')} 에 import 가 있다`,
        ).toBe(0)
      }
    }
  })

  it('infra/api 는 features·store 를 참조하지 않는다', () => {
    expect(violations('infra/api', /^@\/(features|store)\b/)).toEqual([])
  })

  it('app/queries 는 features 를 참조하지 않는다', () => {
    expect(violations('app/queries', /^@\/features\b/)).toEqual([])
  })

  it('features 는 infra/api 를 직접 참조하지 않는다', () => {
    // 화면이 API 를 직접 부르면 캐시·폴링·무효화가 흩어진다 (§9.2).
    // 필요한 것은 app/queries 를 통해 나온다 — media.ts · errors.ts · use*.ts
    expect(violations('features', /^@\/infra\/api\b/)).toEqual([])
  })

  it('lib 은 프로젝트 계층을 참조하지 않는다', () => {
    expect(violations('lib', /^@\//)).toEqual([])
  })

  it('components/ui 는 lib 외 프로젝트 계층을 참조하지 않는다', () => {
    expect(violations('components/ui', /^@\/(?!lib(?:\/|$))/)).toEqual([])
  })
})
