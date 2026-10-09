import { expect, it } from 'vitest'
import { canApplyPrice, matchesPriceProvider } from './priceUpdate'
import type { PriceCandidate } from './priceUpdate'

it('공급자 접두사를 추측하지 않고 저장된 종류만 필터링한다', () => {
  expect(matchesPriceProvider(null, 'openai')).toBe(false)
  expect(matchesPriceProvider(undefined, 'unknown')).toBe(true)
  expect(matchesPriceProvider('google', 'all')).toBe(true)
  expect(matchesPriceProvider('google', 'openai')).toBe(false)
})

it('미확인·미노출·변경 없음·보류 후보는 선택할 수 없다', () => {
  const candidate = {
    terms: {},
    blockedReason: null,
    changeKind: 'newModel',
    executionSupport: 'unverified',
  } as PriceCandidate
  expect(canApplyPrice(candidate)).toBe(true)
  for (const changeKind of ['unchanged', 'priceUnknown', 'notInCatalog'] as const)
    expect(canApplyPrice({ ...candidate, changeKind })).toBe(false)
  expect(canApplyPrice({ ...candidate, terms: null })).toBe(false)
  expect(canApplyPrice({ ...candidate, blockedReason: '계정별 환산 지원 필요' })).toBe(false)
})
