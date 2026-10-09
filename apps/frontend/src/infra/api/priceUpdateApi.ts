import { ApiError, API_ERROR_CODES, apiRequest } from './client'
import { PROVIDER_KINDS } from '@/domain/provider/types'
import type {
  PriceApplyInput,
  PriceApplyReceipt,
  PriceUpdatePreview,
} from '@/domain/tuning/priceUpdate'

type ObjectValue = Record<string, unknown>
const object = (value: unknown): value is ObjectValue =>
  typeof value === 'object' && value !== null && !Array.isArray(value)
const string = (value: unknown): value is string => typeof value === 'string'
const nullableString = (value: unknown) => value === null || string(value)
const date = (value: unknown) => string(value) && Number.isFinite(Date.parse(value))
const guid = (value: unknown) =>
  string(value) && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
const ids = (value: unknown) => Array.isArray(value) && value.every(guid)
const number = (value: unknown) =>
  value === null || (typeof value === 'number' && Number.isFinite(value) && value >= 0)
const provider = (value: unknown) => PROVIDER_KINDS.some((kind) => kind === value)
const oneOf = (value: unknown, values: string[]) => string(value) && values.includes(value)
function terms(value: unknown): boolean {
  return (
    value === null ||
    (object(value) &&
      [
        'inputPerMillion',
        'outputPerMillion',
        'longContextFrom',
        'longInputPerMillion',
        'longOutputPerMillion',
        'perImage',
      ].every((key) => number(value[key])) &&
      (value.officialEffectiveFrom === null || date(value.officialEffectiveFrom)))
  )
}
function evidence(value: unknown): boolean {
  if (!object(value) || !string(value.url)) return false
  let url: URL
  try {
    url = new URL(value.url)
  } catch {
    return false
  }
  return (
    url.protocol === 'https:' &&
    date(value.collectedAt) &&
    string(value.sha256) &&
    /^[a-f0-9]{64}$/.test(value.sha256) &&
    string(value.conditions) &&
    number(value.creditsPerTask) &&
    number(value.usdPerCredit)
  )
}
function validPreview(value: unknown): value is PriceUpdatePreview {
  if (
    !object(value) ||
    !guid(value.id) ||
    !date(value.createdAt) ||
    !date(value.expiresAt) ||
    !Array.isArray(value.providers) ||
    !Array.isArray(value.candidates)
  )
    return false
  const candidates = value.candidates
  if (
    new Set(candidates.map((candidate) => (object(candidate) ? candidate.id : null))).size !==
    candidates.length
  )
    return false
  return (
    candidates.every(
      (c) =>
        object(c) &&
        guid(c.id) &&
        provider(c.provider) &&
        string(c.model) &&
        oneOf(c.area, ['text', 'image', 'mesh']) &&
        string(c.operation) &&
        string(c.conditions) &&
        ids(c.providerConfigIds) &&
        terms(c.currentTerms) &&
        terms(c.terms) &&
        Array.isArray(c.evidence) &&
        c.evidence.every(evidence) &&
        oneOf(c.changeKind, [
          'newModel',
          'priceChanged',
          'unchanged',
          'priceUnknown',
          'notInCatalog',
        ]) &&
        oneOf(c.executionSupport, ['supported', 'unverified', 'unsupported']) &&
        nullableString(c.blockedReason),
    ) &&
    value.providers.every(
      (p) =>
        object(p) &&
        guid(p.providerConfigId) &&
        provider(p.provider) &&
        oneOf(p.status, ['success', 'partial', 'failed']) &&
        oneOf(p.modelListStatus, ['complete', 'partial', 'failed']) &&
        date(p.collectedAt) &&
        nullableString(p.modelError) &&
        nullableString(p.priceError) &&
        ids(p.candidateIds),
    )
  )
}
function malformed(): never {
  throw new ApiError(
    API_ERROR_CODES.malformedResponse,
    '단가 업데이트 응답을 해석할 수 없습니다',
    200,
  )
}
export async function collectPriceUpdate(providerConfigIds: string[]): Promise<PriceUpdatePreview> {
  const data = await apiRequest<unknown>('/api/prices/update-previews', {
    method: 'POST',
    body: { providerConfigIds },
  })
  if (!validPreview(data)) malformed()
  return data
}
export async function applyPriceUpdate(
  previewId: string,
  input: PriceApplyInput,
): Promise<PriceApplyReceipt> {
  const data = await apiRequest<unknown>(`/api/prices/update-previews/${previewId}/apply`, {
    method: 'POST',
    body: input,
  })
  if (
    !object(data) ||
    data.requestId !== input.requestId ||
    data.previewId !== previewId ||
    !date(data.appliedAt) ||
    !Array.isArray(data.items) ||
    data.items.length !== input.candidateIds.length ||
    !data.items.every(
      (item) =>
        object(item) &&
        guid(item.priceId) &&
        date(item.effectiveFrom) &&
        input.candidateIds.includes(String(item.candidateId)),
    ) ||
    new Set(data.items.map((item) => item.candidateId)).size !== data.items.length
  )
    malformed()
  return data as unknown as PriceApplyReceipt
}
