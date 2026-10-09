import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
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
      <Select value={value} disabled={disabled} onValueChange={(next) => onChange(next as Filter)}>
        <SelectTrigger id={id} data-testid={id} className={styles.input}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">전체</SelectItem>
          {PROVIDER_KINDS.map((kind) => (
            <SelectItem key={kind} value={kind}>
              {kind === 'google' ? 'Google' : providerKindLabel(kind)}
            </SelectItem>
          ))}
          <SelectItem value="unknown">미분류</SelectItem>
        </SelectContent>
      </Select>
    </div>
  )
}
