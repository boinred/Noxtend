/**
 * 재시도가 모델 선택을 이어받는 계약.
 *
 * **왕복이 핵심이다.** 쓰는 쪽(결과 화면의 "다시 시도")과 읽는 쪽(입력 화면의 초기
 * 선택)이 다른 파일에 있어서, 쿼리 키 이름이 한쪽에서만 바뀌면 아무 오류 없이
 * 프리필만 조용히 사라진다. 두 함수를 한 파일에 두고 여기서 왕복을 못 박는다.
 */
import { describe, expect, it } from 'vitest'

import {
  backgroundWithImagePath,
  characterWithImagePath,
  readCharacterSelection,
  readModelSelection,
} from './paths'

const TEXT = { providerConfigId: 'provider-text', model: 'gpt-5.6-luna' }
const IMAGE = { providerConfigId: 'provider-image', model: 'gemini-3-image' }
const MESH = { providerConfigId: 'provider-mesh', model: 'P1-20260311' }

/** 경로에서 쿼리만 떼어 파서에 넘긴다 — 화면이 `useSearchParams` 로 받는 것과 같은 값. */
function paramsOf(path: string): URLSearchParams {
  return new URLSearchParams(path.slice(path.indexOf('?')))
}

describe('backgroundWithImagePath', () => {
  it('모델을 주지 않으면 이미지만 싣는다', () => {
    expect(backgroundWithImagePath('image-1')).toBe('/background?image=image-1')
  })

  it('쓴 선택을 그대로 되읽는다', () => {
    const path = backgroundWithImagePath('image-1', {
      text: TEXT,
      image: IMAGE,
      mesh: MESH,
    })

    expect(readModelSelection(paramsOf(path))).toEqual({
      text: TEXT,
      image: IMAGE,
      mesh: MESH,
      requiresReview: null,
    })
  })

  it('한쪽만 고른 작업은 그쪽만 싣는다', () => {
    const path = backgroundWithImagePath('image-1', { text: TEXT, image: null })

    expect(readModelSelection(paramsOf(path))).toEqual({
      text: TEXT,
      image: null,
      mesh: null,
      requiresReview: null,
    })
  })

  it('파츠 검수 여부를 그대로 이어받는다', () => {
    const path = backgroundWithImagePath('image-1', { requiresReview: false })

    expect(readModelSelection(paramsOf(path)).requiresReview).toBe(false)
  })

  it('3D 없이 돌린 작업은 3D 를 싣지 않는다', () => {
    // 3D 는 선택이다. 빈 값을 실으면 다시 시도가 고른 적 없는 조합을 만든다
    const path = backgroundWithImagePath('image-1', { text: TEXT, image: IMAGE })

    expect(readModelSelection(paramsOf(path)).mesh).toBeNull()
  })

  it('모델 id 의 특수문자를 이스케이프한다', () => {
    // 모델 id 는 공급자가 정하는 문자열이다. 그대로 이어붙이면 `&` 하나에 쿼리가 끊긴다
    const odd = { providerConfigId: 'p', model: 'gpt&5?6=luna' }
    const path = backgroundWithImagePath('image-1', { text: odd, image: null })

    expect(readModelSelection(paramsOf(path)).text).toEqual(odd)
  })
})

// character-mesh-ui §FR-08 — 배경과 같은 왕복 계약 + 캐릭터 고유값(gender·partHints)
describe('characterWithImagePath', () => {
  it('모델·성별·힌트를 주지 않으면 이미지만 싣는다', () => {
    expect(characterWithImagePath('image-1')).toBe('/character?image=image-1')
  })

  it('쓴 선택을 그대로 되읽는다 — 모델 3종 + 성별 + 파츠 힌트', () => {
    const partHints = [
      { type: '반지', count: 2 },
      { type: '장갑', count: 1, variant: '손목형' },
    ]
    const path = characterWithImagePath('image-1', {
      text: TEXT,
      image: IMAGE,
      mesh: MESH,
      gender: 'female',
      partHints,
    })

    expect(readCharacterSelection(paramsOf(path))).toEqual({
      text: TEXT,
      image: IMAGE,
      mesh: MESH,
      gender: 'female',
      partHints,
      requiresReview: null,
    })
  })

  it('성별·힌트를 안 주면 null 로 읽힌다', () => {
    const path = characterWithImagePath('image-1', { text: TEXT, image: IMAGE })

    const carried = readCharacterSelection(paramsOf(path))
    expect(carried.gender).toBeNull()
    expect(carried.partHints).toBeNull()
  })

  it('빈 파츠 힌트 배열은 쿼리에 싣지 않는다', () => {
    const path = characterWithImagePath('image-1', { gender: 'male', partHints: [] })

    expect(path).not.toContain('partHints')
    expect(readCharacterSelection(paramsOf(path)).partHints).toBeNull()
  })
})

describe('readCharacterSelection', () => {
  it('아무것도 없으면 전부 null 이다', () => {
    expect(readCharacterSelection(new URLSearchParams(''))).toEqual({
      text: null,
      image: null,
      mesh: null,
      gender: null,
      partHints: null,
      requiresReview: null,
    })
  })

  it('gender 값이 male/female 이 아니면 버린다 — 손으로 조작한 URL 방어', () => {
    const params = new URLSearchParams('gender=unknown')
    expect(readCharacterSelection(params).gender).toBeNull()
  })

  it('partHints 가 JSON 이 아니면 버린다 — 손으로 조작한 URL 방어', () => {
    const params = new URLSearchParams('partHints=not-json')
    expect(readCharacterSelection(params).partHints).toBeNull()
  })
})

describe('readModelSelection', () => {
  it('아무것도 없으면 전부 null 이다', () => {
    expect(readModelSelection(new URLSearchParams(''))).toEqual({
      text: null,
      image: null,
      mesh: null,
      requiresReview: null,
    })
  })

  it('공급자만 있고 모델이 없으면 버린다', () => {
    // 짝이 맞지 않는 선택은 접수에서 거절된다 (PipelineTask 의 공급자·모델 짝 규칙).
    // 반쪽을 채워 두면 사용자가 고른 적 없는 조합이 폼에 남는다
    const params = new URLSearchParams('image=image-1&provider=provider-text')

    expect(readModelSelection(params)).toEqual({
      text: null,
      image: null,
      mesh: null,
      requiresReview: null,
    })
  })
})
