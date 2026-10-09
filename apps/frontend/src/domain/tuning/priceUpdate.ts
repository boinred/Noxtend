import type { ProviderKind } from '@/domain/provider/types'

export type PriceProviderFilter = 'all' | 'unknown' | ProviderKind
export interface PriceTerms {
  inputPerMillion: number | null
  outputPerMillion: number | null
  longContextFrom: number | null
  longInputPerMillion: number | null
  longOutputPerMillion: number | null
  perImage: number | null
  officialEffectiveFrom: string | null
}
export interface PriceEvidence {
  url: string
  collectedAt: string
  sha256: string
  conditions: string
  creditsPerTask: number | null
  usdPerCredit: number | null
}
export interface PriceCandidate {
  id: string
  provider: ProviderKind
  model: string
  area: 'text' | 'image' | 'mesh'
  operation: string
  conditions: string
  providerConfigIds: string[]
  currentTerms: PriceTerms | null
  terms: PriceTerms | null
  evidence: PriceEvidence[]
  changeKind: 'newModel' | 'priceChanged' | 'unchanged' | 'priceUnknown' | 'notInCatalog'
  executionSupport: 'supported' | 'unverified' | 'unsupported'
  blockedReason: string | null
}
export interface PriceUpdatePreview {
  id: string
  createdAt: string
  expiresAt: string
  providers: {
    providerConfigId: string
    provider: ProviderKind
    status: 'success' | 'partial' | 'failed'
    modelListStatus: 'complete' | 'partial' | 'failed'
    collectedAt: string
    modelError: string | null
    priceError: string | null
    candidateIds: string[]
  }[]
  candidates: PriceCandidate[]
}
export interface PriceApplyInput {
  requestId: string
  candidateIds: string[]
  effectiveFrom: string | null
}
export interface PriceApplyReceipt {
  requestId: string
  previewId: string
  appliedAt: string
  items: { candidateId: string; priceId: string; effectiveFrom: string }[]
}
export function matchesPriceProvider(
  provider: ProviderKind | null | undefined,
  filter: PriceProviderFilter,
) {
  return filter === 'all' || (filter === 'unknown' ? provider == null : provider === filter)
}
export function canApplyPrice(candidate: PriceCandidate) {
  return (
    candidate.terms !== null &&
    candidate.blockedReason === null &&
    (candidate.changeKind === 'newModel' || candidate.changeKind === 'priceChanged')
  )
}
