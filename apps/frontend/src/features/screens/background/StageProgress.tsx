/**
 * 공정 진행 표시.
 *
 * Design Ref: §5.3
 *
 * **사용자가 "진행 상태를 알 수 없다" 고 한 것에 대한 답이다.** 이전에는 "분석하는 중"
 * 한 줄뿐이라 세 단계 중 어디인지, 몇 번째 시도인지, 실패했다면 어느 단계에서인지
 * 알 수 없었다.
 *
 * **막대가 두 값을 갖는다** (Angular Material Progress Bar 의 buffer 모드와 같다).
 * 확정된 것과 진행 중인 것을 가르지 않으면 "3분째 33% 에 멈춰 있다" 로 보여 멈춘 것처럼
 * 읽힌다. 단계 안의 진행은 알 수 없으므로 — LLM 호출은 중간 신호를 주지 않는다 —
 * buffer 는 "이 단계가 시작됐다" 까지만 말하고 그 안을 추측하지 않는다.
 */
import { groupGenerationStages } from '@/domain/job/types'
import { jobProgress, taskDurationSeconds, taskKindLabel } from '@/domain/job/types'
import { cn } from '@/lib/utils'
import { backgroundStyles as styles } from './backgroundStyles'
import { formatFailureReason } from './failureMessages'
import type { StageRow } from '@/domain/job/types'
import type { JobTask } from '@/domain/job/types'

export interface StageProgressProps {
  tasks: JobTask[]
}

const INLINE_STAGE_LIMIT = 6

/** 짧은 파이프라인은 훑어보고, 긴 파이프라인은 필요할 때만 상세를 펼친다. */
export function shouldCollapseStageList(total: number): boolean {
  return total > INLINE_STAGE_LIMIT
}

export function StageProgress({ tasks }: StageProgressProps) {
  const progress = jobProgress(tasks)
  const ordered = [...tasks].sort((a, b) => a.ordinal - b.ordinal)
  const currentTask = ordered[progress.current - 1] ?? null
  const percentage = Math.round(progress.value * 100)

  // 생성 공정을 한 칸으로 묶는다 (사이클 #7 §5.3) — 파츠가 20개일 때 칸 20개를
  // 그리면 읽을 수 없다. 막대와 백분율은 공정 하나하나를 그대로 세므로 영향이 없다
  const rows = groupGenerationStages(ordered)
  const collapseStages = shouldCollapseStageList(rows.length)

  return (
    <div className={styles.stageProgress} data-testid="stage-progress">
      <div
        className={styles.progressBar}
        role="progressbar"
        aria-valuenow={percentage}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label="공정 진행률"
        // 도는 단계가 없으면 줄무늬를 멈춘다 — 끝난 막대가 계속 흐르면 아직 도는 것처럼 보인다
        data-active={progress.running !== null}
        data-testid="progress-bar"
      >
        {/* 진행 중인 단계까지 — 옅게 */}
        <span
          className={cn(
            styles.progressBuffer,
            progress.running === null && styles.progressBufferIdle,
          )}
          style={{ width: `${progress.buffer * 100}%` }}
          data-testid="progress-buffer"
        />
        {/* 성공이 확정된 단계만 — 진하게 */}
        <span
          className={styles.progressValue}
          style={{ width: `${progress.value * 100}%` }}
          data-testid="progress-value"
        />
      </div>

      <div className={styles.progressSummary} data-testid="progress-caption">
        <span className={styles.progressPosition}>
          {progress.current}/{progress.total}
        </span>
        <span className={styles.progressCurrent}>
          {currentTask ? taskKindLabel(currentTask.kind) : '단계 준비 중'}
        </span>
        <span className={styles.progressPercent}>{percentage}% 완료</span>
      </div>

      {collapseStages ? (
        <details className={styles.stageDetails}>
          <summary>전체 {progress.total}단계 보기</summary>
          <StageList rows={rows} />
        </details>
      ) : (
        <StageList rows={rows} />
      )}
    </div>
  )
}

/** 인라인과 접힌 상세가 같은 단계 행 구조를 공유하도록 하는 목록 렌더러. */
function StageList({ rows }: { rows: StageRow[] }) {
  return (
    <ol className={styles.stages} data-testid="stage-list">
      {rows.map((row) => (
        <li
          key={row.key}
          className={styles.stage}
          data-status={row.status}
          data-testid="stage-item"
        >
          <span className={styles.stageMark} aria-hidden="true">
            {stageMark(row.status)}
          </span>
          <span className={styles.stageName}>{taskKindLabel(row.kind)}</span>
          <span className={styles.stageNote} data-testid="stage-note">
            {rowNote(row)}
          </span>
        </li>
      ))}
    </ol>
  )
}

/**
 * 묶인 생성 칸은 `7/9` 진척을, 개별 단계는 기존 한 줄을 보여준다.
 *
 * 묶인 칸에 지연이나 시도 횟수를 쓰지 않는 이유는 그것이 공정마다 다르기 때문이다 —
 * 하나로 뭉치면 어느 파츠 이야기인지 알 수 없다.
 */
function rowNote(row: StageRow): string {
  if (row.task !== null) {
    return stageNote(row.task)
  }

  const failed = row.group?.filter((t) => t.status === 'failed').length ?? 0

  // 실패 수를 진척 옆에 붙인다 — `7/9` 만으로는 나머지 둘이 아직 도는지 실패했는지 모른다
  return failed > 0 ? `${row.note} · 실패 ${failed}` : (row.note ?? '')
}

function stageMark(status: JobTask['status']): string {
  if (status === 'succeeded') return '✓'
  if (status === 'running') return '▶'
  if (status === 'failed') return '✕'
  if (status === 'canceled') return '—'
  return '·'
}

/**
 * 단계 오른쪽에 붙는 한 줄.
 *
 * **재시도 횟수를 보여주는 것이 중요하다.** 계약 위반은 조용히 다시 물어보는데,
 * 그 사실이 안 보이면 사용자는 같은 단계에서 오래 머무는 것을 멈춘 것으로 읽는다.
 */
function stageNote(task: JobTask): string {
  const seconds = taskDurationSeconds(task)

  if (task.status === 'succeeded') {
    return seconds !== null ? `${seconds}초` : '완료'
  }

  if (task.status === 'running') {
    // 1회차는 굳이 말하지 않는다 — 재시도 중일 때만 알린다
    return task.attemptCount > 1 ? `진행 중 · ${task.attemptCount}번째 시도` : '진행 중…'
  }

  if (task.status === 'failed') {
    const attempts = task.attemptCount > 1 ? ` (${task.attemptCount}회 시도)` : ''
    return `${formatFailureReason(task.failureReason, '실패')}${attempts}`
  }

  if (task.status === 'canceled') return '취소됨'

  return '대기'
}

export function TaskAttemptDetails({ tasks }: StageProgressProps) {
  const retried = groupGenerationStages(tasks)
    .map((row) => ({
      ...row,
      attempts:
        row.task?.attemptCount ?? Math.max(...(row.group ?? []).map((task) => task.attemptCount)),
    }))
    .filter((row) => row.attempts > 1)
  if (retried.length === 0) return null

  return (
    <ul
      aria-label="공정 시도 정보"
      className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground"
    >
      {retried.map((row) => (
        <li key={row.key}>
          {taskKindLabel(row.kind)} · {row.attempts}회 시도
        </li>
      ))}
    </ul>
  )
}
