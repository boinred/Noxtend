import { PROVIDER_KINDS, providerKindLabel } from '@/domain/provider/types'
import type { PriceProviderFilter as Filter } from '@/domain/tuning/priceUpdate'
import { adminStyles as styles } from './adminStyles'

export function PriceProviderFilter({
  id,
  label,
  value,
  onChange,
  disabled = false,
}: {
  id: string
  label: string
  value: Filter
  onChange: (value: Filter) => void
  disabled?: boolean
}) {
  return (
    <div className="min-w-0">
      <label className={styles.label} htmlFor={id}>
        {label}
      </label>
      <select
        id={id}
        data-testid={id}
        className={styles.input}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value as Filter)}
      >
        <option value="all">전체</option>
        {PROVIDER_KINDS.map((kind) => (
          <option key={kind} value={kind}>
            {kind === 'google' ? 'Google' : providerKindLabel(kind)}
          </option>
        ))}
        <option value="unknown">미분류</option>
      </select>
    </div>
  )
}
