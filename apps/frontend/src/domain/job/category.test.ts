import { describe, expect, it } from 'vitest'
import { ASSET_CATEGORIES } from './types'

describe('제작 대상 카테고리 계약', () => {
  it('서버 wire 값과 같은 이름과 순서를 사용한다', () => {
    expect(ASSET_CATEGORIES).toEqual(['character', 'object', 'background'])
  })
})
