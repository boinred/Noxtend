/**
 * 모델 단가 관리.
 *
 * 전에는 코드 안의 상수 표라 단가 하나를 고치려면 재배포해야 했다. 공급자가 단가를
 * 바꿀 때마다 배포가 필요한 구조는 결국 낡은 표를 방치하게 만든다.
 *
 * **이 화면의 핵심은 "추가" 와 "수정" 이 다른 일이라는 것이다.** 비용은 호출 시각에
 * 걸리는 단가로 계산되므로, 기존 행을 고치면 과거 호출의 비용까지 다시 계산된다.
 * 공급자 인상은 새 행, 오타는 수정 — 이 구분이 흐려지면 지난달 지출이 조용히 바뀐다.
 */
import { useState } from 'react'
import { PageContainer } from '@/features/screens/PageContainer'
import { Button } from '@/components/ui/button'
import { AdminTabs } from './AdminTabs'
import { PriceUpdatePanel } from './PriceUpdatePanel'
import { PriceProviderFilter } from './PriceProviderFilter'
import { matchesPriceProvider } from '@/domain/tuning/priceUpdate'
import type { PriceProviderFilter as Filter } from '@/domain/tuning/priceUpdate'
import { useModelPrices, usePriceMutations } from '@/app/queries/useTuning'
import { groupPricesByModel, isScheduled } from '@/domain/tuning/types'
import { adminStyles as styles } from './adminStyles'
import type { ModelPrice, ModelPriceDraft } from '@/domain/tuning/types'

/** 편집 대상. `null` 이면 닫힘, `'new'` 면 추가 */
type Editing = ModelPrice | 'new' | null

export function PricesScreen() {
  const { prices, isLoading, errorMessage, refetch } = useModelPrices()
  const { create, update, remove } = usePriceMutations()
  const [editing, setEditing] = useState<Editing>(null)
  const [error, setError] = useState<string | null>(null)

  const [providerFilter, setProviderFilter] = useState<Filter>('all')
  const displayed = prices.filter((price) => matchesPriceProvider(price.provider, providerFilter))
  const groups = groupPricesByModel(displayed)

  const submit = async (draft: ModelPriceDraft) => {
    setError(null)

    try {
      if (editing === 'new') await create.mutateAsync(draft)
      else if (editing) await update.mutateAsync({ id: editing.id, draft })

      setEditing(null)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : '저장하지 못했습니다')
    }
  }

  const confirmRemove = async (price: ModelPrice) => {
    // 지우면 그 구간의 호출이 이전 행이나 미등록으로 계산된다 — 조용히 바뀌면 안 된다
    if (
      !window.confirm(`${price.model} 의 ${formatDate(price.effectiveFrom)} 시행 단가를 지울까요?`)
    ) {
      return
    }

    setError(null)

    try {
      await remove.mutateAsync(price.id)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : '삭제하지 못했습니다')
    }
  }

  return (
    <PageContainer
      width="max"
      title="단가"
      subtitle="호출 시각의 단가로 비용을 계산합니다. 공급자가 단가를 바꿨다면 새 행을 추가하세요."
      testId="prices-screen"
    >
      <AdminTabs />
      <PriceUpdatePanel />
      <div className="mb-4">
        <PriceProviderFilter
          id="price-provider-filter"
          label="기존 단가 공급자"
          value={providerFilter}
          onChange={setProviderFilter}
        />
      </div>

      <div className={styles.toolbar}>
        <span className={styles.note} data-testid="price-count">
          전체 {prices.length}개 행 · 표시 {displayed.length}개 행 · {groups.length}개 모델
        </span>
        <Button variant="default" onClick={() => setEditing('new')} data-testid="price-add">
          단가 추가
        </Button>
      </div>

      {error ? (
        <p className={styles.error} data-testid="price-error">
          {error}
        </p>
      ) : null}

      {editing ? (
        <PriceForm
          price={editing === 'new' ? null : editing}
          isSaving={create.isPending || update.isPending}
          onSubmit={submit}
          onCancel={() => {
            setEditing(null)
            setError(null)
          }}
        />
      ) : null}

      {errorMessage ? (
        <div>
          <p className={styles.error}>단가 조회 실패: {errorMessage}</p>
          <Button onClick={() => void refetch()}>단가 다시 조회</Button>
        </div>
      ) : null}
      {isLoading ? (
        <p className={styles.note}>단가 조회 중…</p>
      ) : errorMessage && prices.length === 0 ? null : groups.length === 0 ? (
        <p className={styles.empty} data-testid="price-empty">
          공급자 결과 없음. 단가를 모르는 모델의 호출은 비용 합계에서 빠집니다.
        </p>
      ) : (
        <div className={styles.panel}>
          <table className={styles.table} data-testid="price-table">
            <thead>
              <tr>
                <th>모델</th>
                <th>시행일</th>
                <th>입력 $/1M</th>
                <th>출력 $/1M</th>
                <th>장당 $</th>
                <th>장문 구간</th>
                <th>메모</th>
                <th aria-label="동작" />
              </tr>
            </thead>
            <tbody>
              {groups.flatMap((group) =>
                group.rows.map((price, index) => (
                  <tr key={price.id} data-testid="price-row" data-model={price.model}>
                    {/* 모델명은 묶음의 첫 행에만 — 같은 값을 반복하면 묶음 경계가 안 보인다 */}
                    <td>
                      {index === 0 ? <span className={styles.version}>{price.model}</span> : null}
                    </td>
                    <td>
                      {formatDate(price.effectiveFrom)}
                      {price.id === group.current?.id ? (
                        <span className={styles.activeBadge} data-testid="price-current">
                          적용 중
                        </span>
                      ) : isScheduled(price) ? (
                        <span className={styles.scheduledBadge} data-testid="price-scheduled">
                          예정
                        </span>
                      ) : null}
                    </td>
                    <td className={styles.amount}>{formatUsd(price.inputPerMillion)}</td>
                    <td className={styles.amount}>{formatUsd(price.outputPerMillion)}</td>
                    {/* 이미지 모델만 값을 갖는다 — 토큰 단가와 한눈에 구분돼야 한다 (D-8) */}
                    <td className={styles.amount} data-testid="price-per-image">
                      {price.perImage === null ? '—' : formatUsd(price.perImage)}
                    </td>
                    <td className={styles.note}>{describeLongTier(price)}</td>
                    <td className={styles.note}>{price.note}</td>
                    <td>
                      <div className={styles.actionRow}>
                        <Button onClick={() => setEditing(price)} data-testid="price-edit">
                          수정
                        </Button>
                        <Button
                          variant="ghost"
                          onClick={() => void confirmRemove(price)}
                          data-testid="price-delete"
                        >
                          삭제
                        </Button>
                      </div>
                    </td>
                  </tr>
                )),
              )}
            </tbody>
          </table>
        </div>
      )}
    </PageContainer>
  )
}

interface PriceFormProps {
  /** `null` 이면 새 행 */
  price: ModelPrice | null
  isSaving: boolean
  onSubmit: (draft: ModelPriceDraft) => void
  onCancel: () => void
}

function PriceForm({ price, isSaving, onSubmit, onCancel }: PriceFormProps) {
  const [model, setModel] = useState(price?.model ?? '')
  const [input, setInput] = useState(String(price?.inputPerMillion ?? ''))
  const [output, setOutput] = useState(String(price?.outputPerMillion ?? ''))
  const [longFrom, setLongFrom] = useState(price?.longContextFrom?.toString() ?? '')
  const [longInput, setLongInput] = useState(price?.longInputPerMillion?.toString() ?? '')
  const [longOutput, setLongOutput] = useState(price?.longOutputPerMillion?.toString() ?? '')
  const [effectiveFrom, setEffectiveFrom] = useState(
    price ? price.effectiveFrom.slice(0, 10) : new Date().toISOString().slice(0, 10),
  )
  const [note, setNote] = useState(price?.note ?? '')
  const [perImage, setPerImage] = useState(price?.perImage?.toString() ?? '')

  // 장문 구간은 셋이 함께 있거나 셋 다 없어야 한다 — 반쪽 행은 조용히 짧은 단가로 계산된다
  const longFields = [longFrom, longInput, longOutput].map((v) => v.trim().length > 0)
  const longHalfFilled = longFields.some(Boolean) && !longFields.every(Boolean)

  const canSave =
    model.trim().length > 0 &&
    isNumber(input) &&
    isNumber(output) &&
    (perImage.trim().length === 0 || isNumber(perImage)) &&
    !longHalfFilled &&
    (!longFields[0] || (isNumber(longFrom) && isNumber(longInput) && isNumber(longOutput)))

  const handleSubmit = () => {
    onSubmit({
      model: model.trim(),
      inputPerMillion: Number(input),
      outputPerMillion: Number(output),
      longContextFrom: longFields[0] ? Number(longFrom) : null,
      longInputPerMillion: longFields[0] ? Number(longInput) : null,
      longOutputPerMillion: longFields[0] ? Number(longOutput) : null,
      // 날짜만 받고 자정(UTC)으로 고정한다 — 시각까지 받으면 정확도는 늘지만
      // "같은 날 두 번 올린" 행을 구분해야 해서 표가 읽기 어려워진다
      effectiveFrom: `${effectiveFrom}T00:00:00Z`,
      note: note.trim(),
      // 비우면 토큰 과금 모델이다 — 0 으로 두면 "장당 공짜" 로 읽힌다
      provider: price?.provider ?? null,
      perImage: perImage.trim().length > 0 ? Number(perImage) : null,
    })
  }

  return (
    <div className={styles.panel} data-testid="price-form">
      <div className={styles.formBody}>
        {price ? (
          <p className={styles.warning} data-testid="price-edit-warning">
            기존 행을 고치면 <strong>이미 쌓인 호출의 비용도 다시 계산됩니다.</strong> 공급자가
            단가를 바꿨다면 수정 대신 새 행을 추가하세요.
          </p>
        ) : null}

        <div className={styles.field}>
          <label className={styles.label} htmlFor="price-model">
            모델
          </label>
          <input
            id="price-model"
            className={styles.input}
            value={model}
            // 모델명은 행의 정체성이다 — 바꿔야 한다면 그것은 다른 모델이고 새 행이 맞다
            disabled={price !== null}
            onChange={(event) => setModel(event.target.value)}
            placeholder="gpt-5.6-luna"
            data-testid="price-model-input"
          />
          <p className={styles.hint}>
            {price
              ? '모델명은 바꿀 수 없습니다 — 다른 모델이라면 새 행으로 추가하세요.'
              : '날짜 접미사는 빼고 적으세요. claude-opus-4-5-20251101 은 claude-opus-4-5 행에 걸립니다.'}
          </p>
        </div>

        <div className={styles.formRow}>
          <NumberField
            id="price-input"
            label="입력 $/1M"
            value={input}
            onChange={setInput}
            testId="price-input-input"
          />
          <NumberField
            id="price-output"
            label="출력 $/1M"
            value={output}
            onChange={setOutput}
            testId="price-output-input"
          />
          <div className={styles.field}>
            <label className={styles.label} htmlFor="price-effective">
              시행일
            </label>
            <input
              id="price-effective"
              type="date"
              className={styles.input}
              value={effectiveFrom}
              onChange={(event) => setEffectiveFrom(event.target.value)}
              data-testid="price-effective-input"
            />
          </div>
        </div>

        <div className={styles.formRow}>
          <NumberField
            id="price-long-from"
            label="장문 시작 토큰"
            value={longFrom}
            onChange={setLongFrom}
            placeholder="비워두면 구간 없음"
            testId="price-long-from-input"
          />
          <NumberField
            id="price-long-input"
            label="장문 입력 $/1M"
            value={longInput}
            onChange={setLongInput}
            testId="price-long-input-input"
          />
          <NumberField
            id="price-long-output"
            label="장문 출력 $/1M"
            value={longOutput}
            onChange={setLongOutput}
            testId="price-long-output-input"
          />
        </div>

        {longHalfFilled ? (
          <p className={styles.error} data-testid="price-long-warning">
            장문 구간은 세 값을 모두 채우거나 모두 비워야 합니다. 하나만 빠지면 짧은 단가로 계산되어
            실제보다 싼 값이 나옵니다.
          </p>
        ) : null}

        <div className={styles.field}>
          <label className={styles.label} htmlFor="price-per-image">
            장당 $ (이미지 모델)
          </label>
          <input
            id="price-per-image"
            className={styles.input}
            value={perImage}
            onChange={(event) => setPerImage(event.target.value)}
            placeholder="토큰 과금 모델이면 비워 두세요"
            data-testid="price-per-image-input"
          />
        </div>

        <div className={styles.field}>
          <label className={styles.label} htmlFor="price-note">
            메모
          </label>
          <input
            id="price-note"
            className={styles.input}
            value={note}
            onChange={(event) => setNote(event.target.value)}
            placeholder="출처나 변경 이유"
            data-testid="price-note-input"
          />
        </div>

        <div className={styles.formActions}>
          <Button variant="ghost" onClick={onCancel} data-testid="price-cancel">
            취소
          </Button>
          <Button
            variant="default"
            disabled={!canSave || isSaving}
            onClick={handleSubmit}
            data-testid="price-save"
          >
            {isSaving ? '저장 중…' : price ? '수정' : '추가'}
          </Button>
        </div>
      </div>
    </div>
  )
}

interface NumberFieldProps {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  placeholder?: string
  testId: string
}

function NumberField({ id, label, value, onChange, placeholder, testId }: NumberFieldProps) {
  return (
    <div className={styles.field}>
      <label className={styles.label} htmlFor={id}>
        {label}
      </label>
      <input
        id={id}
        className={styles.input}
        inputMode="decimal"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        data-testid={testId}
      />
    </div>
  )
}

function isNumber(value: string): boolean {
  const trimmed = value.trim()
  return trimmed.length > 0 && Number.isFinite(Number(trimmed)) && Number(trimmed) >= 0
}

/** 소수점 이하가 의미 있는 값이라 잘라내지 않는다 — $0.05 가 $0.1 로 보이면 안 된다 */
/**
 * 단가 표기.
 *
 * **소수 셋째 자리까지 본다** (사이클 #7 Check). 토큰 단가는 100만 토큰당이라 두 자리로
 * 충분했지만, 장당 단가는 셋째 자리에 산다 — `gpt-image-1-mini`($0.011)와
 * `gemini-2.5-flash-image`($0.039)가 두 자리에서는 각각 `$0.01`·`$0.04` 로 뭉개져
 * 서로 다른 모델의 단가가 같아 보인다.
 *
 * 뒤의 0 은 떨군다 — `$5.00` 이 아니라 `$5` 여야 표가 읽힌다.
 */
function formatUsd(value: number): string {
  return `$${value.toFixed(3).replace(/\.?0+$/, '')}`
}

function describeLongTier(price: ModelPrice): string {
  if (price.longContextFrom === null) return '—'

  const from = price.longContextFrom.toLocaleString('ko-KR')
  return `${from} 초과 → ${formatUsd(price.longInputPerMillion ?? 0)} / ${formatUsd(price.longOutputPerMillion ?? 0)}`
}

function formatDate(iso: string): string {
  return iso.slice(0, 10)
}
