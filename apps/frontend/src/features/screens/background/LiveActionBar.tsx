/** 진행 중 결과 화면의 상태 요약과 취소 동작. */
import { Button } from '@/components/ui/button'
import { generationTally, meshTally } from '@/domain/job/types'
import { backgroundStyles as styles } from './backgroundStyles'
import type { Job } from '@/domain/job/types'

export interface LiveActionBarProps {
  job: Job
  onCancel: () => void
  canceling: boolean
}

export function LiveActionBar({ job, onCancel, canceling }: LiveActionBarProps) {
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

      <Button variant="outline" onClick={onCancel} disabled={canceling} data-testid="live-cancel">
        {canceling ? '취소하는 중…' : '취소'}
      </Button>
    </div>
  )
}
