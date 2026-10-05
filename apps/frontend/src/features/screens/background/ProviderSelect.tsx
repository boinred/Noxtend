/**
 * Design Ref: §5.1 · §5.4 — 등록된 공급자만 목록에 뜬다. 하나도 없으면 안내.
 */
import { Link } from 'react-router-dom'
import { ROUTES } from '@/routes/paths'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { backgroundStyles as styles } from './backgroundStyles'
import type { Provider } from '@/domain/provider/types'

export interface ProviderSelectProps {
  providers: Provider[]
  value: string | null
  onChange: (providerId: string) => void
  /**
   * 사이클 #7 — 텍스트와 이미지 두 벌이 한 화면에 선다 (D-4).
   *
   * `id` 와 `data-testid` 를 함께 나눠야 한다. 같은 값이면 라벨이 첫 select 를 가리켜
   * 두 번째 라벨을 눌러도 엉뚱한 칸에 포커스가 간다.
   */
  label?: string
  fieldId?: string
  testId?: string
  emptyLabel?: string
}

export function ProviderSelect({
  providers,
  value,
  onChange,
  label = '공급자',
  fieldId = 'studio-provider',
  testId = 'provider-select',
  emptyLabel = 'AI 공급자',
}: ProviderSelectProps) {
  // 공급자가 없으면 선택지가 아니라 다음 할 일을 보여준다 — 빈 select 는 막다른 길이다
  if (providers.length === 0) {
    return (
      <p className={styles.notice} data-testid="provider-empty-notice">
        등록된 {emptyLabel}가 없습니다.{' '}
        <Link className={styles.noticeLink} to={ROUTES.admin} data-testid="provider-empty-link">
          관리자
        </Link>
        에서 먼저 추가해 주세요.
      </p>
    )
  }

  return (
    <div className={styles.field}>
      <label className={styles.label} htmlFor={fieldId}>
        {label}
      </label>
      <Select value={value ?? undefined} onValueChange={onChange}>
        <SelectTrigger id={fieldId} data-testid={testId}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {providers.map((provider) => (
            <SelectItem key={provider.id} value={provider.id}>
              {provider.displayName}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  )
}
