/**
 * Design Ref: §5.4 관리자 — 등록 · 수정 폼.
 *
 * **수정 시 키를 비우면 유지된다는 것을 화면이 말한다.** 서버는 그렇게 동작하지만
 * (§4.2 #11) 사용자는 빈 입력창을 보고 "지워졌다" 고 읽는다. 안내가 없으면
 * 매번 재입력하게 되고, 그러다 실수로 잘못된 키를 넣는다.
 *
 * **모델 입력이 없다.** 여기서 문자열로 받으면 오타나 부적합 모델(예: 이미지를 못 읽는
 * 모델)이 실행 후에야 드러난다. 스튜디오가 공급자에게 지원 목록을 물어 고르게 한다 —
 * 등록은 이름 · 종류 · 키 세 가지다.
 */
import { useState } from 'react'
import type { FormEvent } from 'react'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { PROVIDER_KINDS, providerCapabilitiesFor, providerKindLabel } from '@/domain/provider/types'
import { useProviderCapabilities } from '@/app/queries/useProviders'
import { providerFormStyles as styles } from './adminStyles'
import { ProviderCapabilityBadge } from './ProviderCapabilityBadge'
import type { Provider, ProviderInput, ProviderKind } from '@/domain/provider/types'

export interface ProviderFormProps {
  /** 있으면 수정, 없으면 등록. 이 차이가 키 필드의 의미를 바꾼다 */
  editing?: Provider
  pending?: boolean
  error?: string | null
  onSubmit: (input: ProviderInput) => void
  onCancel: () => void
}

export function ProviderForm({ editing, pending, error, onSubmit, onCancel }: ProviderFormProps) {
  // 종류별 사용 용도는 서버가 준다 — 프론트가 사본을 들면 백엔드와 어긋난다
  const capabilityTable = useProviderCapabilities()
  const [displayName, setDisplayName] = useState(editing?.displayName ?? '')
  const [kind, setKind] = useState<ProviderKind>(editing?.kind ?? 'openai')
  const [apiKey, setApiKey] = useState('')

  const isEditing = Boolean(editing)
  const canSubmit =
    displayName.trim().length > 0 &&
    // 등록에는 키가 반드시 필요하고, 수정에는 선택이다
    (isEditing || apiKey.trim().length > 0)

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!canSubmit || pending) return

    onSubmit({
      displayName: displayName.trim(),
      kind,
      apiKey: apiKey.trim() || undefined,
      isEnabled: editing?.isEnabled ?? true,
    })
  }

  return (
    <form className={styles.form} onSubmit={handleSubmit} data-testid="provider-form">
      <div className={styles.field}>
        <label className={styles.label} htmlFor="provider-name">
          이름
        </label>
        <input
          id="provider-name"
          className={styles.input}
          value={displayName}
          onChange={(event) => setDisplayName(event.target.value)}
          placeholder="예: Claude 운영"
          data-testid="provider-name-input"
        />
      </div>

      <div className={styles.field}>
        <label className={styles.label} htmlFor="provider-kind">
          종류
        </label>
        <Select value={kind} onValueChange={(next) => setKind(next as ProviderKind)}>
          <SelectTrigger id="provider-kind" data-testid="provider-kind-select">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {PROVIDER_KINDS.map((option) => (
              <SelectItem key={option} value={option}>
                {providerKindLabel(option)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className={styles.field} data-testid="provider-capabilities">
        <span className={styles.label}>사용 용도</span>
        <div className={styles.capabilityList}>
          {providerCapabilitiesFor(capabilityTable, kind).map((capability) => (
            <ProviderCapabilityBadge key={capability} capability={capability} />
          ))}
        </div>
        <span className={styles.hint}>하나의 API 키를 지원하는 용도에 공통으로 사용합니다</span>
      </div>

      <div className={styles.field}>
        <label className={styles.label} htmlFor="provider-key">
          API 키
        </label>
        <input
          id="provider-key"
          className={styles.input}
          // 저장된 키는 되읽을 수 없다. 입력 중인 값도 어깨너머로 보이지 않게 한다
          type="password"
          value={apiKey}
          onChange={(event) => setApiKey(event.target.value)}
          placeholder={isEditing ? '변경하지 않으려면 비워두세요' : 'sk-…'}
          autoComplete="off"
          data-testid="provider-key-input"
        />
        {isEditing ? (
          <span className={styles.hint} data-testid="provider-key-hint">
            비워두면 기존 키가 그대로 유지됩니다
          </span>
        ) : null}
      </div>

      {error ? (
        <span className={styles.error} role="alert" data-testid="provider-form-error">
          {error}
        </span>
      ) : null}

      <div className={styles.row}>
        <Button variant="ghost" onClick={onCancel} data-testid="provider-cancel">
          취소
        </Button>
        <Button
          type="submit"
          variant="default"
          disabled={!canSubmit || pending}
          data-testid="provider-submit"
        >
          {isEditing ? '저장' : '추가'}
        </Button>
      </div>
    </form>
  )
}
