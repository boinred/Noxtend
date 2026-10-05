/**
 * 화면이 폭 규약(§5.0)을 실제로 따르는지 검사한다.
 *
 * **사용자가 홈과 관리자의 폭이 다르다고 지적해서 나왔다.** 설계는 둘을 다르게
 * 정해뒀지만(홈 960 / 관리자 1280), 홈은 `PageContainer` 를 쓰지 않고 자체
 * `max-width: 760px` 를 갖고 있었다 — 규약 밖의 세 번째 값이었다.
 *
 * 기존 가드가 못 잡은 이유:
 * - `contentWidth.test.ts` 는 토큰 값과 "PageContainer 가 소비한다" 만 본다
 * - `tokenReferences.test.ts` 는 미정의 토큰 참조만 본다 — 하드코딩된 px 는 통과
 * - L2 #W1~#W3 은 스튜디오와 관리자만 측정한다 — 홈은 대상이 아니었다
 *
 * 셋 다 "폭을 어디서 정하는가" 를 보지 않았다. 이 파일이 그 축을 담당한다.
 */
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const SCREENS = resolve(dirname(fileURLToPath(import.meta.url)))

function filesUnder(dir: string, match: (path: string) => boolean): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return filesUnder(full, match)
    return match(full) ? [full] : []
  })
}

/** 라우트에 직접 걸리는 최상위 화면 컴포넌트. 하위 조각은 폭을 정하지 않는다. */
const SCREEN_COMPONENTS = filesUnder(SCREENS, (p) => /Screen\.tsx$/.test(p))

describe('§5.0 폭 규약 — 적용 지점', () => {
  it('검사 대상 화면이 있다', () => {
    expect(SCREEN_COMPONENTS.length).toBeGreaterThanOrEqual(3)
  })

  it('모든 화면이 PageContainer 로 폭을 정한다', () => {
    const offenders = SCREEN_COMPONENTS.filter((file) => {
      const source = readFileSync(file, 'utf8')
      return !source.includes('PageContainer')
    }).map((f) => f.replace(`${SCREENS}/`, ''))

    /*
     * 예외는 §5.0 "화면별 적용" 표에 없는 화면들이다. 표는 스튜디오·홈·관리자만
     * 규정하며, 아래 화면은 의도적으로 그 밖에 있다.
     * 예외를 늘릴 때는 왜 폭 규약 밖인지 여기 적는다 — 적을 이유가 없으면 예외가 아니다.
     */
    const allowed = [
      // 규약 이전 화면이고 카테고리 안내 한 줄만 띄운다. 카테고리에 실제 기능이
      // 생기면 그 화면이 표에 들어오고 이 예외도 사라진다
      'coming-soon/ComingSoonScreen.tsx',
    ]

    expect(
      offenders.filter((f) => !allowed.includes(f)),
      `PageContainer 를 쓰지 않는 화면:\n${offenders.join('\n')}`,
    ).toEqual([])
  })

  it('화면이 폭 규약 유틸리티를 직접 소비하지 않는다', () => {
    const governedWidth = /max-w-(?:content-max|content-narrow|content-list|form-max)/g
    const offenders = SCREEN_COMPONENTS.flatMap((file) => {
      const source = readFileSync(file, 'utf8')
      const utilities = source.match(governedWidth) ?? []
      return utilities.map((utility) => `${file.replace(`${SCREENS}/`, '')} → ${utility}`)
    })

    expect(offenders, `PageContainer 밖의 폭 규약 사용:\n${offenders.join('\n')}`).toEqual([])
  })
})
