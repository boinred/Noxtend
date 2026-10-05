/**
 * "이 작업이 무엇으로 돌고 있는가" — 진행·결과·실패 화면 공통 머리줄.
 *
 * 모델은 접수 시점에 고정되므로 상태와 무관하게 같은 자리에 있다. 상태별 분기 안에
 * 각각 두면 실패 화면에서만 빠지는 식으로 어긋난다.
 */
import { modelBadges } from '@/domain/job/types'
import type { JobModels } from '@/domain/job/types'
import type { Provider } from '@/domain/provider/types'

import { backgroundStyles as styles } from './backgroundStyles'

interface ModelSummaryProps {
  /** 이 필드를 내려보내기 전 서버가 응답하면 통째로 없다 — `modelBadges` 가 흡수한다 */
  models: JobModels | null | undefined
  /** 이름표 원본. 사용 중지된 공급자도 이름은 보여야 하므로 전체 목록을 받는다 */
  providers: Provider[]
}

export function ModelSummary({ models, providers }: ModelSummaryProps) {
  const names = Object.fromEntries(providers.map((provider) => [provider.id, provider.displayName]))
  const badges = modelBadges(models, names)

  // 이미지 생성 없이 접수된 옛 작업은 배지가 없다 — 빈 줄을 그리지 않는다
  if (badges.length === 0) return null

  return (
    <div className={styles.modelSummary} data-testid="model-summary">
      {badges.map((badge) => (
        <div key={badge.role} className={styles.modelBadge} data-testid={`model-${badge.role}`}>
          <span className={styles.modelBadgeLabel}>{badge.label}</span>
          {/* 공급자가 지워졌으면 이름 없이 모델만 — 그쪽이 정보량이 크다 */}
          {badge.providerName === null ? null : (
            <span className={styles.modelBadgeProvider}>{badge.providerName}</span>
          )}
          <span className={styles.modelBadgeModel}>{badge.model}</span>
        </div>
      ))}
    </div>
  )
}
