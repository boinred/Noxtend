/**
 * Design Ref: §5.4 · §4.2 #23·#25 — 실행 비교와 판정.
 *
 * **이 화면이 "잘 했는지" 를 사람이 판단하는 자리다** (Plan D-3).
 *
 * 두 실행을 나란히 놓는다. **같은 버전끼리도 고를 수 있다** (FR-13) — LLM 출력은
 * 비결정적이라 같은 프롬프트도 결과가 다르고, 그 흔들림의 폭을 눈으로 익혀야
 * "이건 프롬프트 효과인가 우연인가" 를 구분할 수 있다 (R-2). 그 구분을 자동화할
 * 방법이 없어서 화면이 대신 한다.
 */
import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { PageContainer } from '@/features/screens/PageContainer'
import { Button } from '@/components/ui/button'
import { AdminTabs } from './AdminTabs'
import { useGoldenRuns, useVerdictMutation } from '@/app/queries/useTuning'
import { useJob } from '@/app/queries/useJob'
import { PartsOverlay } from '@/features/screens/background/PartsOverlay'
import { generatedImageUrl } from '@/app/queries/media'
import { describeRun } from '@/domain/tuning/types'
import { adminStyles as styles } from './adminStyles'
import type { GoldenRun } from '@/domain/tuning/types'

export function GoldenRunsScreen() {
  const { sampleId } = useParams<{ sampleId: string }>()
  const { runs, isLoading } = useGoldenRuns(sampleId ?? null)

  const [left, setLeft] = useState<string | null>(null)
  const [right, setRight] = useState<string | null>(null)

  return (
    <PageContainer
      width="max"
      title="실행 비교"
      subtitle="같은 이미지의 두 실행을 나란히 봅니다. 같은 버전끼리 비교하면 결과가 얼마나 흔들리는지 알 수 있습니다."
      testId="golden-runs-screen"
    >
      <AdminTabs />

      {isLoading ? null : runs.length === 0 ? (
        <p className={styles.empty} data-testid="runs-empty">
          이 이미지로 돌린 실행이 없습니다. 스튜디오에서 한 번 돌리면 여기에 쌓입니다.
        </p>
      ) : (
        <>
          <div className={styles.panel}>
            <table className={styles.table} data-testid="runs-table">
              <thead>
                <tr>
                  <th>실행</th>
                  <th>버전</th>
                  <th>모델</th>
                  <th>파츠</th>
                  <th>판정</th>
                  <th aria-label="선택" />
                </tr>
              </thead>
              <tbody>
                {runs.map((run) => (
                  <tr key={run.jobId} data-testid="run-row">
                    <td>{new Date(run.createdAt).toLocaleString('ko-KR')}</td>
                    <td>
                      <code className={styles.variables}>{describeRun(run)}</code>
                    </td>
                    <td>{run.model ?? '—'}</td>
                    <td>{run.partCount}</td>
                    <td>{renderVerdict(run)}</td>
                    <td>
                      <div className={styles.actions}>
                        <div className={styles.actionRow}>
                          <Button
                            variant="secondary"
                            onClick={() => setLeft(run.jobId)}
                            disabled={left === run.jobId}
                            data-testid="run-pick-left"
                          >
                            왼쪽
                          </Button>
                          <Button
                            variant="secondary"
                            onClick={() => setRight(run.jobId)}
                            disabled={right === run.jobId}
                            data-testid="run-pick-right"
                          >
                            오른쪽
                          </Button>
                        </div>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className={styles.compare} data-testid="run-compare">
            <RunPane jobId={left} side="왼쪽" sampleId={sampleId ?? null} />
            <RunPane jobId={right} side="오른쪽" sampleId={sampleId ?? null} />
          </div>
        </>
      )}
    </PageContainer>
  )
}

function renderVerdict(run: GoldenRun) {
  if (!run.verdict) {
    return <span className={styles.missing}>미판정</span>
  }

  return (
    <span className={styles.verdict} data-pass={run.verdict.isPass} data-testid="run-verdict">
      {run.verdict.isPass ? '쓸만함' : '아님'}
      {run.verdict.memo ? ` · ${run.verdict.memo}` : ''}
    </span>
  )
}

/**
 * 한쪽 실행.
 *
 * 오버레이를 스튜디오 결과 화면과 **같은 컴포넌트**로 그린다. 비교용으로 따로 만들면
 * 두 화면이 어긋나고, 그러면 "여기선 맞았는데 저기선 다르다" 가 생긴다.
 */
function RunPane({
  jobId,
  side,
  sampleId,
}: {
  jobId: string | null
  side: string
  sampleId: string | null
}) {
  const { job } = useJob(jobId ?? undefined)
  const verdict = useVerdictMutation(sampleId)

  const [memo, setMemo] = useState('')

  if (jobId === null) {
    return (
      <div className={styles.pane} data-testid="run-pane-empty">
        <p className={styles.empty}>{side} 실행을 고르세요.</p>
      </div>
    )
  }

  if (!job) {
    return <div className={styles.pane} data-testid="run-pane" />
  }

  return (
    <div className={styles.pane} data-testid="run-pane">
      <h3 className={styles.paneTitle}>{side}</h3>

      <PartsOverlay
        sourceImageId={job.sourceImageId}
        parts={job.parts.map((part) => ({
          ...part,
          label: `${part.depthOrder}. ${part.name}`,
        }))}
        scene={job.scene}
      />

      {/*
        파츠 이미지를 나란히 놓는다 (사이클 #7 FR-17 · Plan D-4).

        **이것이 두 공급자를 고르는 수단이다.** 화풍·재질이 파츠끼리 일치하는지는
        아직 사람 눈밖에 판정 기준이 없고 (Plan R-1), 그 눈이 볼 것이 여기 있다.

        **파츠 이름 순으로 정렬한다** — 두 실행이 같은 파츠를 마주 보게 해야 비교가
        성립한다. 깊이 순으로 두면 실행마다 순서가 달라 엉뚱한 짝이 마주 본다.
      */}
      <ul className={styles.paneParts} data-testid="pane-part-images">
        {[...job.parts]
          .sort((a, b) => a.name.localeCompare(b.name, 'ko-KR'))
          .map((part) => (
            <li key={part.id} data-testid="pane-part">
              {part.generatedImageId !== null ? (
                <img
                  className={styles.paneThumb}
                  src={generatedImageUrl(part.generatedImageId)}
                  alt={`${part.name} 생성 이미지`}
                  data-testid="pane-part-image"
                />
              ) : (
                <span className={styles.missing} data-testid="pane-part-missing">
                  이미지 없음
                </span>
              )}
              <span>{part.name}</span>
              {part.category ? <span className={styles.partCategory}>{part.category}</span> : null}
            </li>
          ))}
      </ul>

      <div className={styles.verdictForm}>
        <input
          className={styles.input}
          value={memo}
          onChange={(event) => setMemo(event.target.value)}
          placeholder="판정 이유"
          data-testid="verdict-memo"
        />
        <div className={styles.actionRow}>
          <Button
            variant="secondary"
            onClick={() => verdict.mutate({ jobId, isPass: true, memo })}
            data-testid="verdict-pass"
          >
            쓸만함
          </Button>
          <Button
            variant="destructive"
            onClick={() => verdict.mutate({ jobId, isPass: false, memo })}
            data-testid="verdict-fail"
          >
            아님
          </Button>
        </div>
      </div>
    </div>
  )
}
