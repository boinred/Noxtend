/**
 * Design Ref: §5.1 진행 단계.
 *
 * 사이클 #4 에서는 "퍼센트 대신 무엇을 하는 중인지" 였다 — 공정이 하나뿐이라 진척률을
 * 만들 근거가 없었고, 가짜 퍼센트는 30% 에서 5분간 멈춰 있는 순간 신뢰를 잃기 때문이다.
 *
 * **공정이 셋이 되면서 근거가 생겼다.** 이제 "몇 단계가 끝났나" 는 추측이 아니라
 * 사실이다. 다만 단계 **안**의 진행은 여전히 알 수 없으므로 (LLM 호출은 중간 신호를
 * 주지 않는다) 그것까지 지어내지는 않는다 — buffer 가 그 경계를 표시한다.
 */
import { StageProgress } from './StageProgress'
import { sourceImageUrl } from '@/app/queries/media'
import { backgroundStyles as styles } from './backgroundStyles'
import type { JobTask } from '@/domain/job/types'

export interface RunProgressProps {
  sourceImageId: string
  tasks: JobTask[]
}

export function RunProgress({ sourceImageId, tasks }: RunProgressProps) {
  return (
    <div className={styles.progress} data-testid="run-progress">
      <img
        className={styles.progressThumb}
        src={sourceImageUrl(sourceImageId)}
        alt=""
        data-testid="run-progress-thumb"
      />

      <div className={styles.progressBody}>
        <p className={styles.progressTitle}>이미지를 분석하는 중…</p>
        <p className={styles.progressHint}>보통 30~90초, 길면 더 걸릴 수 있습니다</p>

        <StageProgress tasks={tasks} />
      </div>
    </div>
  )
}
