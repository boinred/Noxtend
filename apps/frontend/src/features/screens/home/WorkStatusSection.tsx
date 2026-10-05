/**
 * Design Ref: §5.1, §5.3 — 홈의 "실행 중" / "최근 작업" 공용 섹션 (FR-06 · FR-10).
 *
 * 직전 사이클에는 빈 상태 전용이었다. background-studio 에서 **목록도 받는다** —
 * 다만 빈 상태 경로는 그대로 남는다. 비어 있음이 **고장이 아니라 예고로** 읽혀야 하고,
 * 무엇보다 **API 가 없을 때 여기로 떨어진다** (§6 · §8.6 회귀 방어 장치).
 */
import { Link } from 'react-router-dom'
import { Icon } from '@/features/shell/Icon'
import { sourceImageUrl } from '@/app/queries/media'
import { assetCategoryLabel } from '@/domain/job/types'
import { jobPath } from '@/routes/paths'
import { sectionCount, sectionCountLabel } from './sectionCount'
import { workStatusStyles as styles } from './homeStyles'
import type { IconName } from '@/features/shell/Icon'
import type { ReactNode } from 'react'
import type { JobSummary } from '@/domain/job/types'

export interface WorkStatusSectionProps {
  title: string
  icon: IconName
  emptyTitle: string
  emptyBody: ReactNode
  action?: ReactNode
  featured?: ReactNode
  testId: string
  /** 비어 있으면 빈 상태를 그린다 — 없음과 못 받음을 화면에서 구분하지 않는다 */
  jobs?: JobSummary[]
  /** 항목 삭제 핸들러 — 최근 작업 등 완료된 작업 삭제에 사용 */
  onDelete?: (jobId: string) => void
  /**
   * 조건에 맞는 전체 건수. 목록은 상한까지만 오므로 이보다 적을 수 있다 —
   * 그 차이를 배지가 설명한다. 모르면 보이는 수만 쓴다
   */
  total?: number
}

const STATUS_LABELS: Record<string, string> = {
  pending: '대기 중',
  running: '분석 중',
  succeeded: '완료',
  failed: '실패',
  canceled: '취소됨',
}

export function WorkStatusSection({
  title,
  icon,
  emptyTitle,
  emptyBody,
  action,
  featured,
  testId,
  jobs,
  onDelete,
  total,
}: WorkStatusSectionProps) {
  const hasJobs = jobs !== undefined && jobs.length > 0
  const hasContent = featured !== undefined || hasJobs

  // 크게 그린 한 건까지 세야 화면에 보이는 수와 같다
  const count = sectionCount({ jobs, hasFeatured: featured !== undefined })
  const countLabel = sectionCountLabel(count, total)

  return (
    <section className={styles.section} data-testid={testId}>
      <h2 className={styles.title}>
        <Icon name={icon} size={14} className={styles.titleIcon} />
        {title}
        {/* 0건이면 감춘다 — 빈 상태 문구가 이미 그 말을 한다 */}
        {count > 0 ? (
          <span className={styles.titleCount} data-testid={`${testId}-count`}>
            {countLabel}
          </span>
        ) : null}
      </h2>

      {hasContent ? (
        <div className={styles.content}>
          {featured}
          {hasJobs ? (
            <ul className={styles.jobList} data-testid={`${testId}-list`}>
              {jobs.map((job) => (
                <li key={job.id} className={styles.jobRow}>
                  {/* 항목 클릭 → 카테고리별 작업 주소 (§5.4 홈 · FR-13 · 독립 리뷰 #2) */}
                  <Link
                    to={jobPath(job.category, job.id)}
                    className={styles.jobLink}
                    data-testid="job-item"
                  >
                    <img
                      className={styles.jobThumb}
                      src={sourceImageUrl(job.sourceImageId)}
                      alt=""
                      loading="lazy"
                    />
                    <span className={styles.jobBody}>
                      <span className={styles.jobName}>
                        {assetCategoryLabel(job.category)} 작업
                      </span>
                      <span className={styles.jobStatus} data-status={job.status}>
                        {STATUS_LABELS[job.status] ?? job.status}
                      </span>
                      <span className={styles.jobMeta}>
                        {formatTime(job.createdAt)} · 파츠 {job.partCount}개
                      </span>
                    </span>
                  </Link>
                  {/*
                줄 끝의 조작 자리.

                **삭제가 카드 링크 안에 있으면 안 된다** — 앵커 안의 버튼은 클릭이
                링크로 새어 작업 상세로 넘어간다. 그래서 링크 밖에 형제로 둔다.

                삭제는 `onDelete` 가 있을 때만 그린다. 목록마다 지울 수 있는지가
                달라서, 없는 화면에 비활성 휴지통을 보이면 "왜 못 지우지" 를 묻게 된다.
              */}
                  <div className={styles.jobActions}>
                    {onDelete ? (
                      <button
                        type="button"
                        className={styles.deleteButton}
                        data-testid="job-delete"
                        aria-label="작업 삭제"
                        title="작업 삭제"
                        onClick={() => onDelete(job.id)}
                      >
                        <Icon name="trash" size={14} />
                      </button>
                    ) : null}
                    {/*
                  꺾쇠는 **같은 곳으로 가는 장식**이다. 위 카드 전체가 이미 그 링크라
                  탭 순서에 또 세우면 같은 목적지가 두 번 걸리고, 스크린 리더도 항목마다
                  링크를 둘씩 읽는다. 그래서 `tabIndex={-1}` 로 키보드 순서에서 뺀다 —
                  마우스로 꺾쇠를 눌러 온 사람에게는 그대로 동작한다.
                */}
                    <Link
                      to={jobPath(job.category, job.id)}
                      className={styles.jobArrow}
                      aria-label="작업 열기"
                      tabIndex={-1}
                    >
                      <Icon name="chevron-right" size={16} />
                    </Link>
                  </div>
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : (
        <div className={styles.empty}>
          <span className={styles.emptyTitle}>{emptyTitle}</span>
          <p className={styles.emptyBody}>{emptyBody}</p>
          {action}
        </div>
      )}
    </section>
  )
}

/** 서버가 ISO 문자열을 준다. 잘못된 값이 와도 화면이 깨지지 않게 원문을 남긴다. */
function formatTime(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso

  return date.toLocaleString('ko-KR', {
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}
