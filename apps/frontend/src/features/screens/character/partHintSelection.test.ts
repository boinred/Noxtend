import { describe, expect, it } from 'vitest'
import { PART_TAXONOMY, type PartTaxon } from './characterPartTaxonomy'
import {
  buildPartHints,
  clampCount,
  defaultRowState,
  selectionFromPartHints,
  type HintSelection,
} from './partHintSelection'

describe('택소노미 정본 불변식', () => {
  // 종류 키가 겹치면 선택 상태 맵이 충돌한다
  it('종류(type) 키는 전역에서 유일하다', () => {
    const types = PART_TAXONOMY.map((taxon) => taxon.type)
    expect(new Set(types).size).toBe(types.length)
  })

  // variants 가 있으면 비어 있지 않아야 배타 select 가 성립한다
  it('변형이 있는 항목은 변형 목록이 비어 있지 않다', () => {
    for (const taxon of PART_TAXONOMY) {
      if (taxon.variants) {
        expect(taxon.variants.length).toBeGreaterThan(0)
      }
    }
  })
})

describe('개수 클램프', () => {
  it('1~20 범위로 자른다', () => {
    expect(clampCount(0)).toBe(1)
    expect(clampCount(21)).toBe(20)
    expect(clampCount(3)).toBe(3)
  })

  it('소수는 내림한다', () => {
    expect(clampCount(3.9)).toBe(3)
  })
})

describe('기본 행 상태', () => {
  it('포함 해제·개수 1 로 시작하고, 변형이 있으면 첫 변형을 고른다', () => {
    const withVariant: PartTaxon = {
      group: 'shoes',
      type: '신발',
      variants: ['발목형', '무릎형'],
      multiple: false,
    }
    const plain: PartTaxon = { group: 'accessory', type: '반지', multiple: true }

    expect(defaultRowState(withVariant)).toEqual({ included: false, count: 1, variant: '발목형' })
    expect(defaultRowState(plain)).toEqual({ included: false, count: 1, variant: undefined })
  })
})

describe('선택 → 전송 페이로드', () => {
  const ring: PartTaxon = { group: 'accessory', type: '반지', multiple: true }
  const gloves: PartTaxon = {
    group: 'accessory',
    type: '장갑',
    variants: ['손목형', '팔꿈치형'],
    multiple: false,
  }
  const top: PartTaxon = { group: 'top', type: '상의', multiple: false }
  const taxonomy = [ring, gloves, top]

  it('포함된 항목만 담는다', () => {
    const selection: HintSelection = {
      반지: { included: true, count: 3, variant: undefined },
      상의: { included: false, count: 1, variant: undefined },
    }

    const hints = buildPartHints(taxonomy, selection)

    expect(hints).toEqual([{ type: '반지', count: 3 }])
  })

  it('단일 파츠는 개수를 1 로 고정한다 — 상태가 커도 무시', () => {
    const selection: HintSelection = { 상의: { included: true, count: 9, variant: undefined } }

    expect(buildPartHints(taxonomy, selection)).toEqual([{ type: '상의', count: 1 }])
  })

  it('다중 파츠 개수는 1~20 으로 자른다', () => {
    const selection: HintSelection = { 반지: { included: true, count: 99, variant: undefined } }

    expect(buildPartHints(taxonomy, selection)).toEqual([{ type: '반지', count: 20 }])
  })

  it('변형이 있는 항목은 변형을 실어 보내고, 없는 항목은 variant 를 넣지 않는다', () => {
    const selection: HintSelection = {
      장갑: { included: true, count: 1, variant: '팔꿈치형' },
      반지: { included: true, count: 2, variant: undefined },
    }

    const hints = buildPartHints(taxonomy, selection)

    expect(hints).toContainEqual({ type: '장갑', count: 1, variant: '팔꿈치형' })
    expect(hints).toContainEqual({ type: '반지', count: 2 })
  })

  it('변형 항목인데 상태에 변형이 없으면 첫 변형으로 폴백한다', () => {
    const selection: HintSelection = { 장갑: { included: true, count: 1, variant: undefined } }

    expect(buildPartHints(taxonomy, selection)).toEqual([
      { type: '장갑', count: 1, variant: '손목형' },
    ])
  })

  it('선택이 하나도 없으면 빈 배열', () => {
    expect(buildPartHints(taxonomy, {})).toEqual([])
  })
})

// character-mesh-ui §FR-08 — "다시 시도" 이어받기가 PartHint[] 를 다시 선택 상태로 되돌린다
describe('전송 페이로드 → 선택 (이어받기)', () => {
  const ring: PartTaxon = { group: 'accessory', type: '반지', multiple: true }
  const gloves: PartTaxon = {
    group: 'accessory',
    type: '장갑',
    variants: ['손목형', '팔꿈치형'],
    multiple: false,
  }
  const top: PartTaxon = { group: 'top', type: '상의', multiple: false }
  const taxonomy = [ring, gloves, top]

  it('힌트에 있는 항목만 포함 상태로 켠다', () => {
    const selection = selectionFromPartHints(taxonomy, [{ type: '반지', count: 3 }])

    expect(selection).toEqual({ 반지: { included: true, count: 3, variant: undefined } })
  })

  it('변형을 그대로 되돌린다', () => {
    const selection = selectionFromPartHints(taxonomy, [
      { type: '장갑', count: 1, variant: '팔꿈치형' },
    ])

    expect(selection['장갑']).toEqual({ included: true, count: 1, variant: '팔꿈치형' })
  })

  it('택소노미에 없는 종류는 무시한다 — 택소노미가 바뀐 뒤의 옛 job도 안전해야 한다', () => {
    const selection = selectionFromPartHints(taxonomy, [{ type: '없는파츠', count: 1 }])

    expect(selection).toEqual({})
  })

  it('buildPartHints 와 왕복해도 값이 같다', () => {
    const hints = [
      { type: '반지', count: 2 },
      { type: '장갑', count: 1, variant: '손목형' },
    ]

    const roundTripped = buildPartHints(taxonomy, selectionFromPartHints(taxonomy, hints))

    expect(roundTripped).toEqual(hints)
  })
})
