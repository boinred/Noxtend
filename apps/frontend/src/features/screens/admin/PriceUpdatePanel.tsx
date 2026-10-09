import { useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { usePriceUpdates, usePriceUpdateConfigs } from '@/app/queries/usePriceUpdates'
import { apiErrorCode, apiErrorMessage, apiErrorStatus } from '@/app/queries/errors'
import { providerKindLabel } from '@/domain/provider/types'
import { canApplyPrice, matchesPriceProvider } from '@/domain/tuning/priceUpdate'
import type {
  PriceApplyInput,
  PriceApplyReceipt,
  PriceProviderFilter as Filter,
  PriceTerms,
  PriceUpdatePreview,
} from '@/domain/tuning/priceUpdate'
import { adminStyles as styles } from './adminStyles'
import { PriceProviderFilter } from './PriceProviderFilter'

const changes = {
  newModel: '신규 모델',
  priceChanged: '단가 변경',
  unchanged: '변경 없음',
  priceUnknown: '가격 미확인',
  notInCatalog: '목록 미노출 · 폐기 확정 아님',
}
const support = {
  supported: '실행 지원 확인',
  unverified: '실행 지원 확인 필요',
  unsupported: '실행 미지원',
}
const result = { success: '성공', partial: '부분 실패', failed: '수집 실패' }
const listResult = { complete: '목록 완전 수집', partial: '목록 부분 실패', failed: '목록 실패' }
const localDate = (value: string) => new Date(value).toLocaleString('ko-KR')

export function PriceUpdatePanel() {
  const providers = usePriceUpdateConfigs()
  const { collect, apply } = usePriceUpdates()
  const [configIds, setConfigIds] = useState<string[]>([])
  const [preview, setPreview] = useState<PriceUpdatePreview | null>(null)
  const [filter, setFilter] = useState<Filter>('all')
  const [selected, setSelected] = useState<string[]>([])
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [status, setStatus] = useState('')
  const [receipt, setReceipt] = useState<PriceApplyReceipt | null>(null)
  const [needsCollection, setNeedsCollection] = useState(false)
  const [replay, setReplay] = useState<PriceApplyInput | null>(null)
  const busy = useRef(false)
  const pending = collect.isPending || apply.isPending
  const locked = pending || replay !== null
  const visible =
    preview?.candidates.filter((candidate) => matchesPriceProvider(candidate.provider, filter)) ??
    []
  const applicable = visible.filter(canApplyPrice)
  const selectedVisible = applicable
    .filter((candidate) => selected.includes(candidate.id))
    .map((candidate) => candidate.id)
  const schedule = effectiveFrom ? new Date(effectiveFrom).getTime() : null
  const officialFloor = Math.max(
    ...applicable
      .filter((candidate) => selected.includes(candidate.id))
      .map((candidate) =>
        candidate.terms?.officialEffectiveFrom
          ? Date.parse(candidate.terms.officialEffectiveFrom)
          : 0,
      ),
    0,
  )
  const invalidSchedule =
    schedule !== null &&
    (!Number.isFinite(schedule) || schedule < Math.max(Date.now(), officialFloor))

  const collectNow = async () => {
    if (busy.current || replay || configIds.length === 0) return
    busy.current = true
    setError(null)
    try {
      const next = await collect.mutateAsync(configIds)
      setPreview(next)
      setSelected([])
      setReceipt(null)
      setNeedsCollection(false)
      setStatus('새 수집 결과로 적용 선택을 해제했습니다')
    } catch (cause) {
      setError(
        `수집 실패: ${apiErrorMessage(cause, '수집하지 못했습니다. 버튼으로 다시 시도하세요')}`,
      )
    } finally {
      busy.current = false
    }
  }
  const applyNow = async () => {
    if (
      busy.current ||
      !preview ||
      needsCollection ||
      receipt ||
      (!replay && (selectedVisible.length === 0 || invalidSchedule))
    )
      return
    busy.current = true
    const input = replay ?? {
      requestId: crypto.randomUUID(),
      candidateIds: [...selectedVisible].sort(),
      effectiveFrom: schedule === null ? null : new Date(schedule).toISOString(),
    }
    setError(null)
    try {
      const saved = await apply.mutateAsync({ previewId: preview.id, input })
      setReceipt(saved)
      setReplay(null)
      setSelected([])
      setStatus(`${saved.items.length}개 단가를 새 시행 행으로 저장했습니다`)
    } catch (cause) {
      const code = apiErrorCode(cause)
      const statusCode = apiErrorStatus(cause)
      const uncertain =
        statusCode === 0 ||
        statusCode === null ||
        statusCode >= 500 ||
        code === 'MALFORMED_RESPONSE'
      setReplay(uncertain ? input : null)
      if (code === 'PriceUpdateExpired' || code === 'PriceUpdateConflict') setNeedsCollection(true)
      setError(
        `${apiErrorMessage(cause, '적용 실패')}${uncertain ? ' · 응답이 확인되지 않았습니다. 같은 입력으로 다시 확인하세요.' : ''}`,
      )
    } finally {
      busy.current = false
    }
  }

  return (
    <section
      className={`${styles.panel} mb-5`}
      data-testid="price-update-panel"
      aria-label="모델·단가 업데이트"
    >
      <div className="min-w-0 space-y-4 p-5 text-sm [overflow-wrap:anywhere]">
        <h2 className="font-semibold">모델·단가 업데이트</h2>
        <p className={styles.note}>
          수집은 저장된 단가·실행 설정을 바꾸지 않습니다. 검토 후 선택한 단가만 새 시행 행으로
          추가합니다.
        </p>
        <fieldset disabled={locked}>
          <legend className={styles.label}>수집 대상 설정 (1–25개)</legend>
          {providers.isLoading ? (
            <p>설정 조회 중…</p>
          ) : providers.isError ? (
            <div>
              <p className={styles.error}>설정 조회 실패: {providers.error?.message}</p>
              <Button onClick={() => void providers.refetch()}>설정 다시 조회</Button>
            </div>
          ) : providers.enabledProviders.length === 0 ? (
            <p className={styles.note}>사용 가능한 설정 결과 없음</p>
          ) : (
            <div className="flex flex-wrap gap-3">
              {providers.enabledProviders.map((config) => (
                <label key={config.id} className="flex min-w-0 items-center gap-2">
                  <input
                    type="checkbox"
                    data-testid="price-update-config"
                    checked={configIds.includes(config.id)}
                    onChange={(event) =>
                      setConfigIds((ids) =>
                        event.target.checked
                          ? [...ids, config.id]
                          : ids.filter((id) => id !== config.id),
                      )
                    }
                  />
                  {config.displayName} · {providerKindLabel(config.kind)}
                </label>
              ))}
            </div>
          )}
        </fieldset>
        <Button
          variant="secondary"
          data-testid="price-update-collect"
          disabled={locked || configIds.length === 0 || configIds.length > 25}
          onClick={() => void collectNow()}
        >
          {collect.isPending ? '수집 중…' : preview ? '최신 정보 다시 수집' : '최신 정보 수집'}
        </Button>
        <p role="status" aria-live="polite" className={styles.note}>
          {status}
        </p>
        {error ? (
          <p role="alert" className={styles.error}>
            {error}
          </p>
        ) : null}
        {preview ? (
          <>
            <p className={styles.note}>
              수집 {localDate(preview.createdAt)} · 검토 만료 {localDate(preview.expiresAt)} (사용자
              시간대)
            </p>
            <div
              data-testid="price-update-provider-summary"
              className="space-y-2 rounded-lg border border-border p-3"
            >
              {preview.providers.map((p) => (
                <p key={p.providerConfigId}>
                  {providerKindLabel(p.provider)} ·{' '}
                  {providers.providers.find((config) => config.id === p.providerConfigId)
                    ?.displayName ?? p.providerConfigId}{' '}
                  · {result[p.status]} · {listResult[p.modelListStatus]} ·{' '}
                  {localDate(p.collectedAt)}
                  {p.modelError ? ` · 모델 실패: ${p.modelError}` : ''}
                  {p.priceError ? ` · 단가 실패: ${p.priceError}` : ''}
                </p>
              ))}
            </div>
            <PriceProviderFilter
              id="price-update-provider-filter"
              label="변경안 공급자"
              value={filter}
              disabled={locked}
              onChange={(next) => {
                setFilter(next)
                setSelected([])
                setStatus('공급자 필터가 변경되어 적용 선택을 해제했습니다')
              }}
            />
            <p className={styles.note}>
              전체 {preview.candidates.length}개 · 표시 {visible.length}개 · 선택{' '}
              {selectedVisible.length}개
            </p>
            {visible.length === 0 ? (
              <p className={styles.empty}>공급자 결과 없음</p>
            ) : applicable.length === 0 ? (
              <p className={styles.note}>적용 가능 항목 없음</p>
            ) : null}
            <Button
              variant="secondary"
              data-testid="price-update-select-visible"
              disabled={locked || needsCollection || receipt !== null || applicable.length === 0}
              onClick={() => setSelected(applicable.map((candidate) => candidate.id))}
            >
              현재 표시된 적용 가능 항목 선택
            </Button>
            <div className="space-y-3">
              {visible.map((candidate) => (
                <article
                  key={candidate.id}
                  data-testid="price-update-candidate"
                  className="min-w-0 rounded-lg border border-border p-3"
                >
                  <label className="flex items-start gap-2">
                    <input
                      type="checkbox"
                      aria-label={`${candidate.model} 적용 선택`}
                      disabled={
                        locked || needsCollection || receipt !== null || !canApplyPrice(candidate)
                      }
                      checked={selected.includes(candidate.id)}
                      onChange={(event) =>
                        setSelected((ids) =>
                          event.target.checked
                            ? [...ids, candidate.id]
                            : ids.filter((id) => id !== candidate.id),
                        )
                      }
                    />
                    <span className={styles.version}>{candidate.model}</span>
                  </label>
                  <p>
                    {providerKindLabel(candidate.provider)} · {changes[candidate.changeKind]} ·{' '}
                    {support[candidate.executionSupport]}
                  </p>
                  <p>
                    {candidate.area === 'mesh'
                      ? '3D'
                      : candidate.area === 'image'
                        ? '이미지'
                        : '텍스트'}{' '}
                    · {candidate.operation} · {candidate.conditions}
                  </p>
                  <p className={styles.note}>
                    관찰 설정:{' '}
                    {candidate.providerConfigIds
                      .map(
                        (id) =>
                          providers.providers.find((config) => config.id === id)?.displayName ?? id,
                      )
                      .join(', ')}
                  </p>
                  <div className="mt-2 grid gap-2 sm:grid-cols-2">
                    <p>현재값: {describeTerms(candidate.currentTerms)}</p>
                    <p>수집값: {describeTerms(candidate.terms)}</p>
                  </div>
                  <p className={styles.note}>
                    공식 시행일:{' '}
                    {candidate.terms?.officialEffectiveFrom
                      ? localDate(candidate.terms.officialEffectiveFrom)
                      : '미기재 · 수집 시각과 별개'}
                  </p>
                  {candidate.blockedReason ? (
                    <p className={styles.warning}>적용 보류: {candidate.blockedReason}</p>
                  ) : null}
                  <details className="mt-2">
                    <summary className="cursor-pointer">출처·조건·수집 근거</summary>
                    {candidate.evidence.map((e, index) => (
                      <div key={index} className="mt-2 space-y-1">
                        <a
                          className="text-primary underline"
                          href={e.url}
                          target="_blank"
                          rel="noreferrer"
                        >
                          {e.url}
                        </a>
                        <p>{e.conditions}</p>
                        <p>수집 UTC {e.collectedAt}</p>
                        <p className="font-mono">SHA-256 {e.sha256}</p>
                        <p>
                          {e.creditsPerTask !== null
                            ? `${e.creditsPerTask} credit/작업`
                            : 'credit/작업 미확인'}{' '}
                          ·{' '}
                          {e.usdPerCredit !== null
                            ? `$${e.usdPerCredit}/credit`
                            : 'USD/credit 미확인'}
                        </p>
                      </div>
                    ))}
                  </details>
                </article>
              ))}
            </div>
            <div>
              <label htmlFor="price-update-effective" className={styles.label}>
                예약 시행 시각 (사용자 시간대, 비우면 즉시)
              </label>
              <input
                id="price-update-effective"
                className={styles.input}
                type="datetime-local"
                value={effectiveFrom}
                disabled={locked || receipt !== null}
                onChange={(event) => setEffectiveFrom(event.target.value)}
              />
              <p className={styles.hint}>
                적용 확정 서버 UTC와 확인된 공식 시행일 중 늦은 시각 이후에 시행합니다.
              </p>
              {invalidSchedule ? (
                <p className={styles.error}>예약 시각은 현재와 공식 시행일 이후여야 합니다.</p>
              ) : null}
            </div>
            {needsCollection ? (
              <p className={styles.warning}>
                검토 결과가 만료되었거나 단가가 변경되었습니다. 다시 수집해 검토하세요.
              </p>
            ) : null}
            {receipt ? (
              <div className={styles.note}>
                {receipt.items.map((item) => (
                  <p key={item.candidateId}>
                    저장 완료 ·{' '}
                    {
                      preview.candidates.find((candidate) => candidate.id === item.candidateId)
                        ?.model
                    }{' '}
                    · 시행 {localDate(item.effectiveFrom)}
                  </p>
                ))}
              </div>
            ) : null}
            <div className="flex flex-wrap justify-end gap-2">
              <Button
                variant="ghost"
                disabled={locked}
                onClick={() => {
                  setSelected([])
                  setStatus('적용 선택을 취소했습니다')
                }}
              >
                선택 취소
              </Button>
              <Button
                variant="default"
                className="max-w-full whitespace-normal"
                data-testid="price-update-apply"
                disabled={
                  pending ||
                  needsCollection ||
                  receipt !== null ||
                  (!replay && (selectedVisible.length === 0 || invalidSchedule))
                }
                onClick={() => void applyNow()}
              >
                {apply.isPending
                  ? '적용 중…'
                  : replay
                    ? '같은 입력으로 적용 결과 다시 확인'
                    : '선택 적용'}
              </Button>
            </div>
          </>
        ) : null}
      </div>
    </section>
  )
}

function describeTerms(terms: PriceTerms | null) {
  if (!terms) return '미확인'
  const values = [
    terms.inputPerMillion === null ? null : `입력 $${terms.inputPerMillion}/1M tokens`,
    terms.outputPerMillion === null ? null : `출력 $${terms.outputPerMillion}/1M tokens`,
    terms.perImage === null ? null : `결과당 $${terms.perImage}`,
    terms.longContextFrom === null
      ? null
      : `${terms.longContextFrom.toLocaleString()} tokens 초과 입력 $${terms.longInputPerMillion} / 출력 $${terms.longOutputPerMillion} per 1M`,
  ]
  return values.filter(Boolean).join(' · ') || 'USD 단가 미확인'
}
