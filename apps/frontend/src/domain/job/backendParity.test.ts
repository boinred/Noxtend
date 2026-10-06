import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { promptKindCategories, promptKindLabel } from '../tuning/types'
import type { PromptKind } from '../tuning/types'

/**
 * G-3 (사이클 #7 Design §8.2) — 프론트 유니온이 백엔드 열거형과 일치하는가.
 *
 * **왜 파일을 읽어서 비교하는가.** `JobStatus` 와 `TaskKind` 는 두 언어에 각각 적혀
 * 있고 컴파일러가 둘을 잇지 않는다. 백엔드에 값이 하나 늘면 서버는 새 문자열을
 * 내려주는데 프론트는 그것을 모르는 값으로 받아 **화면이 조용히 빈칸이 된다** —
 * 사이클 #7 이 `partiallySucceeded` 를 더하면서 실제로 그럴 뻔했다.
 *
 * 정규식으로 읽는 것은 조악하지만, 대안은 코드 생성이고 그것은 두 언어 사이에
 * 빌드 단계를 하나 더 만든다. 값이 늘 때 여기가 빨개지는 것으로 충분하다.
 */
const DOMAIN = resolve(__dirname, '../../../../backend/Noxtend.Domain')

function backendFile(relative: string): string {
  return readFileSync(resolve(DOMAIN, relative), 'utf8')
}

/** C# 열거형 본문에서 값 이름만 뽑는다 — 주석과 특성은 버린다. */
function backendEnumValues(source: string, name: string): string[] {
  const body = new RegExp(`enum\\s+${name}\\s*\\{([\\s\\S]*?)\\n\\}`).exec(source)?.[1]
  if (body === undefined) {
    throw new Error(`${name} 열거형을 찾지 못했습니다`)
  }

  return (
    body
      .split('\n')
      .map((line) => line.trim())
      // 값 줄만 남긴다: 식별자 뒤에 쉼표가 오는 줄
      .map((line) => /^([A-Z][A-Za-z0-9]*)(?:\s*=\s*\d+)?,$/.exec(line)?.[1])
      .filter((value): value is string => value !== undefined)
  )
}

/** 백엔드 `JobResponse.Wire` 와 같은 규칙 — 첫 글자만 소문자로 내린다. */
function toWire(name: string): string {
  return name.charAt(0).toLowerCase() + name.slice(1)
}

/**
 * `ProviderResponse.Wire(ProviderKind)` 와 같은 규칙.
 *
 * 머리글자 약어는 camelCase 를 쓰지 않는다 — `OpenAI` 가 `openAI` 로 나가면
 * 프론트 유니온(`'openai'`)과 어긋난다. 백엔드가 그 예외를 명시적으로 두고 있으므로
 * 여기도 같은 예외를 갖는다.
 */
function toKindWire(name: string): string {
  return name === 'OpenAI' ? 'openai' : toWire(name)
}

/** 프론트 타입 파일에서 문자열 리터럴 유니온을 뽑는다. */
function frontendUnion(source: string, name: string): string[] {
  // 다음 빈 줄이나 다음 `export` 앞에서 멈춘다 — 유니온 둘이 연달아 선언되면
  // 빈 줄만 기준으로 자를 경우 뒤 유니온의 값까지 빨려 들어온다
  const body = new RegExp(`export type ${name} =([\\s\\S]*?)(?=\\n\\s*\\n|\\nexport |$)`).exec(
    source,
  )?.[1]
  if (body === undefined) {
    throw new Error(`${name} 유니온을 찾지 못했습니다`)
  }

  return [...body.matchAll(/'([a-zA-Z]+)'/g)].map((match) => match[1]!)
}

describe('백엔드 열거형과 프론트 유니온이 일치한다', () => {
  const jobBackend = backendFile('Job/TaskKind.cs')
  const viewBackend = backendFile('Job/ViewDirection.cs')
  const genderBackend = backendFile('Job/Gender.cs')
  const jobFrontend = readFileSync(resolve(__dirname, 'types.ts'), 'utf8')

  const providerBackend =
    backendFile('Provider/ProviderKind.cs') + backendFile('Provider/ProviderCapability.cs')
  const providerFrontend = readFileSync(resolve(__dirname, '../provider/types.ts'), 'utf8')

  it('JobStatus — 값이 늘면 화면이 모르는 상태를 받는다', () => {
    expect(frontendUnion(jobFrontend, 'JobStatus').sort()).toEqual(
      backendEnumValues(jobBackend, 'JobStatus').map(toWire).sort(),
    )
  })

  it('TaskKind — 단계가 늘면 진행 표시에 빈칸이 생긴다', () => {
    expect(frontendUnion(jobFrontend, 'TaskKind').sort()).toEqual(
      backendEnumValues(jobBackend, 'TaskKind').map(toWire).sort(),
    )
  })

  it('LLM operation은 모든 프롬프트 라벨과 카테고리를 갖는다', () => {
    const operations = backendEnumValues(
      backendFile('Llm/LlmOperationKind.cs'),
      'LlmOperationKind',
    ).map(toWire)
    expect(operations.sort()).toEqual(
      [
        ...backendEnumValues(jobBackend, 'TaskKind')
          .map(toWire)
          .filter((kind) => !['reconstruct', 'synthesize'].includes(kind)),
        'similarityEvaluate',
      ].sort(),
    )
    for (const kind of operations) expect(promptKindLabel(kind as PromptKind)).toBeTruthy()
    expect(promptKindCategories('analyzeSprites')).toEqual(['background'])
  })

  it('ViewDirection — 방향이 바뀌면 파츠 이미지 타일이 비게 된다', () => {
    expect(frontendUnion(jobFrontend, 'ViewDirection')).toEqual(
      backendEnumValues(viewBackend, 'ViewDirection').map(toWire),
    )
  })

  it('CharacterGender — 성별이 늘면 캐릭터 접수 폼이 못 보내는 값이 생긴다', () => {
    // 설계 §11-3: 성별 확장은 additive 다. 백엔드에 값이 늘면 여기가 빨개진다
    expect(frontendUnion(jobFrontend, 'CharacterGender').sort()).toEqual(
      backendEnumValues(genderBackend, 'Gender').map(toWire).sort(),
    )
  })

  it('ProviderKind — 공급자가 늘면 관리자 폼이 고를 수 없는 종류가 생긴다', () => {
    expect(frontendUnion(providerFrontend, 'ProviderKind').sort()).toEqual(
      backendEnumValues(providerBackend, 'ProviderKind').map(toKindWire).sort(),
    )
  })

  it('ProviderCapability — 능력이 늘면 화면이 모르는 배지를 받는다', () => {
    expect(frontendUnion(providerFrontend, 'ProviderCapability').sort()).toEqual(
      backendEnumValues(providerBackend, 'ProviderCapability').map(toWire).sort(),
    )
  })
})
