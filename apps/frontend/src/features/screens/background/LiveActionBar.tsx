/** 진행 중 결과 화면의 상태 요약. */
import { generationTally, meshTally } from '@/domain/job/types'
import { backgroundStyles as styles } from './backgroundStyles'
import type { Job } from '@/domain/job/types'

export interface LiveActionBarProps {
  job: Job
}

export function LiveActionBar({ job }: LiveActionBarProps) {
  const images = generationTally(job)
  const meshes = meshTally(job)
  const imageSummary =
    images.total > 0 ? `이미지 ${images.generated}/${images.total}` : `파츠 ${images.parts}개 준비`
  const meshSummary = meshes.planned > 0 ? ` · 3D ${meshes.ready}/${meshes.planned}` : ''

  return (
    <div className={styles.liveActionBar} data-testid="live-action-bar">
      <div className={styles.liveActivity} role="status" aria-live="polite">
        <span className={styles.livePulse} aria-hidden="true" />
        <span>
          {imageSummary}
          {meshSummary} 만드는 중
        </span>
      </div>
    </div>
  )
}
