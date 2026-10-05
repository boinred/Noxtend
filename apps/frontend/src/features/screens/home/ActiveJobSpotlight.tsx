import { Link } from 'react-router-dom'
import { useJob } from '@/app/queries/useJob'
import { sourceImageUrl } from '@/app/queries/media'
import { assetCategoryLabel, jobProgress, taskKindLabel } from '@/domain/job/types'
import { Icon } from '@/features/shell/Icon'
import { jobPath } from '@/routes/paths'
import { activeJobStyles as styles } from './homeStyles'
import type { JobSummary } from '@/domain/job/types'

export interface ActiveJobSpotlightProps {
  summary: JobSummary
}

/** 진행 위치·현재 단계·확정 완료율을 한 줄에 유지하는 확장형 요약. */
export function activeJobProgressText(
  current: number,
  total: number,
  currentLabel: string | null,
  percentage: number,
): string {
  if (total === 0 || currentLabel === null) return '진행 정보를 불러오는 중'
  return `${current}/${total} · ${currentLabel} · ${percentage}% 완료`
}

export function ActiveJobSpotlight({ summary }: ActiveJobSpotlightProps) {
  const { job } = useJob(summary.id)
  const progress = job ? jobProgress(job.tasks) : null
  const orderedTasks = job ? [...job.tasks].sort((a, b) => a.ordinal - b.ordinal) : []
  const currentTask = progress ? (orderedTasks[progress.current - 1] ?? null) : null
  const percentage = progress ? Math.round(progress.value * 100) : 0
  const progressText = activeJobProgressText(
    progress?.current ?? 0,
    progress?.total ?? 0,
    currentTask ? taskKindLabel(currentTask.kind) : null,
    percentage,
  )

  return (
    <Link
      to={jobPath(summary.category, summary.id)}
      className={styles.spotlight}
      data-testid="job-item"
    >
      <img className={styles.thumbnail} src={sourceImageUrl(summary.sourceImageId)} alt="" />

      <span className={styles.body}>
        <span className={styles.eyebrow}>실행 중인 작업</span>
        <span className={styles.title}>{assetCategoryLabel(summary.category)} 작업</span>
        <span className={styles.jobId}>#{summary.id.slice(0, 8)}</span>

        <span
          className={styles.progressTrack}
          role="progressbar"
          aria-label="작업 진행률"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={percentage}
        >
          <span className={styles.progressValue} style={{ width: `${percentage}%` }} />
        </span>
        <span className={styles.progressMeta}>{progressText}</span>
      </span>

      <span className={styles.openLabel}>
        작업 열기
        <Icon name="arrow-right" size={16} />
      </span>
    </Link>
  )
}
