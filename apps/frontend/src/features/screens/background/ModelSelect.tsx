/**
 * Design Ref: §5.1 · §4.2 #14 — 선택한 공급자가 지원하는 모델.
 *
 * 이전에는 관리자가 모델을 문자열로 등록했다. 오타든 부적합 모델(이미지를 못 읽는
 * 모델)이든 **실행해 봐야 알 수 있었다.** 이제 공급자에게 물어보고, 서버가 추출에
 * 필요한 능력(이미지 입력 · 구조화 출력)을 갖춘 것만 걸러 보낸다.
 *
 * **목록을 못 가져오면 막는다.** 자유 입력 폴백을 두지 않기로 했다 — 그 칸이 있으면
 * 목록이 있는 이유가 없어지고, 다시 "실행해 봐야 아는" 상태로 돌아간다.
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
import type { ProviderModel } from '@/domain/provider/types'

export interface ModelSelectProps {
  models: ProviderModel[]
  isLoading: boolean
  /** 목록 조회가 실패한 이유. 성공이면 `null` */
  errorMessage: string | null
  value: string | null
  onChange: (model: string) => void
  /** 사이클 #7 — 텍스트와 이미지 두 벌이 한 화면에 선다. id 가 겹치면 라벨이 어긋난다 */
  label?: string
  fieldId?: string
  testId?: string
  /**
   * 사이클 #8 — 항목에 붙일 단가 한 줄. 화면이 단가를 조회하지 않는 경우 생략한다.
   *
   * 값이 아니라 함수인 이유는 `ModelSelect` 가 단가 목록의 모양을 몰라도 되기 때문이다.
   */
  priceHint?: (modelId: string) => string
}

export function ModelSelect({
  models,
  isLoading,
  errorMessage,
  value,
  onChange,
  label = '모델',
  fieldId = 'studio-model',
  testId = 'model-select',
  priceHint,
}: ModelSelectProps) {
  if (isLoading) {
    return (
      <div className={styles.field}>
        <label className={styles.label} htmlFor={fieldId}>
          {label}
        </label>
        {/* 완료 상태와 같은 필드 골격 — 모델 조회 중 레이아웃 점프 방지 */}
        <Select disabled>
          <SelectTrigger id={fieldId} aria-busy="true" data-testid="model-loading">
            <SelectValue placeholder="모델 목록을 불러오는 중…" />
          </SelectTrigger>
        </Select>
      </div>
    )
  }

  // 목록이 비는 경로가 둘이다: 조회 실패와 "쓸 모델이 없음". 둘 다 실행을 막지만
  // 사용자가 할 일이 다르므로 서버가 준 문구를 그대로 보여준다
  if (errorMessage !== null || models.length === 0) {
    return (
      <p className={styles.rejection} role="alert" data-testid="model-unavailable">
        {errorMessage ?? '이 공급자에서 쓸 수 있는 모델이 없습니다'}
        {' — '}
        <Link className={styles.noticeLink} to={ROUTES.admin} data-testid="model-admin-link">
          관리자
        </Link>
        에서 연결을 확인해 주세요.
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
          {models.map((model) => (
            <SelectItem key={model.id} value={model.id} hint={priceHint?.(model.id)}>
              {model.displayName}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  )
}
