/**
 * 작업별 호출 내역 — 무엇을 보내 무엇을 받았나.
 *
 * Design Ref: §5.4 관리자 · 내역 · Plan FR-18·FR-19·FR-20
 *
 * **`useJobCalls` 가 정의만 되고 소비자가 없던 자리를 채운다** (사이클 #7). 서버는
 * 사이클 #5 부터 공정별 사용량·지연·환산 비용을 내려주고 있었는데 아무 화면도 읽지
 * 않았다.
 *
 * **스튜디오와 달리 전부 편다** (D-14 · FR-19). 여기는 정보 밀도를 높여도 되는 구획이다.
 * 다만 그 분리는 **권한이 아니라 밀도** 분리다 — 인증이 붙으면 `/admin/*` 전체가 가드
 * 뒤로 가지만(D-14a · NFR-11), 그 전까지는 누구나 들어온다. 그래서 여기 두는 것은
 * 노출돼도 무해한 것뿐이어야 한다. 키·비밀값은 구조적으로 여기 오지 않는다 (§7 S-1).
 */
import { Button } from '@/components/ui/button'
import { apiErrorMessage } from '@/app/queries/errors'
import { jobCategoryLabel, jobSummaryCount } from '../categoryLabels'
import { useState } from 'react'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { PageContainer } from '@/features/screens/PageContainer'
import { AdminTabs } from './AdminTabs'
import { useJobCalls } from '@/app/queries/useTuning'
import { useJobList } from '@/app/queries/useJobList'
import { callKindLabel } from '@/domain/tuning/types'
import { costLabel, formatUsageTotal, summarizeUsage } from '@/domain/tuning/usage'
import { adminStyles as styles } from './adminStyles'
import type { LlmCall } from '@/domain/tuning/types'

export function CallsScreen() {
  // 최근 끝난 작업들 — 내역은 끝난 뒤에 보는 것이다
  const { jobs, isLoading: jobsLoading, error: jobsError, refetch } = useJobList('terminal')
  const [selected, setSelected] = useState<string | null>(null)

  const jobId = selected ?? jobs[0]?.id ?? null
  const { calls, isLoading } = useJobCalls(jobId)

  return (
    <PageContainer
      width="max"
      title="호출 내역"
      subtitle="공정마다 무엇을 보내 무엇을 받았는지, 얼마나 걸렸고 얼마가 들었는지 봅니다."
      testId="calls-screen"
    >
      <AdminTabs />

      {jobsError ? (
        <div role="alert">
          <p>{apiErrorMessage(jobsError, '작업 목록을 불러올 수 없습니다')}</p>
          <Button variant="outline" onClick={() => void refetch()}>
            목록 다시 조회
          </Button>
        </div>
      ) : null}
      {jobsLoading ? <p role="status">작업 목록을 불러오는 중…</p> : null}
      {jobs.length === 0 ? (
        !jobsLoading && !jobsError ? (
          <p className={styles.empty} data-testid="calls-empty">
            끝난 작업이 없습니다.
          </p>
        ) : null
      ) : (
        <>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="calls-job">
              작업
            </label>
            <Select value={jobId ?? undefined} onValueChange={setSelected}>
              <SelectTrigger id="calls-job" data-testid="calls-job-select">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {jobs.map((job) => (
                  <SelectItem key={job.id} value={job.id}>
                    {job.id.slice(0, 8)} · {jobCategoryLabel(job)} · {jobSummaryCount(job)} ·{' '}
                    {job.status}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {isLoading ? null : <CallsTable calls={calls} />}
        </>
      )}
    </PageContainer>
  )
}

function CallsTable({ calls }: { calls: LlmCall[] }) {
  const summary = summarizeUsage(calls)

  if (calls.length === 0) {
    return (
      <p className={styles.empty} data-testid="calls-none">
        이 작업에는 기록된 호출이 없습니다.
      </p>
    )
  }

  return (
    <>
      {/* 미등록 건수를 합계와 함께 낸다 — 없으면 사용자가 낮은 합계를 실제 지출로 믿는다 */}
      <p className={styles.note} data-testid="calls-total">
        합계 {formatUsageTotal(summary)}
      </p>

      <div className={styles.panel}>
        <table className={styles.table} data-testid="calls-table">
          <thead>
            <tr>
              <th>단계</th>
              <th>모델</th>
              <th>프롬프트</th>
              <th>사용량</th>
              <th>지연</th>
              <th>비용</th>
              <th>성패</th>
            </tr>
          </thead>
          <tbody>
            {summary.rows.map((row) => (
              <tr key={row.call.id} data-testid="call-row">
                <td>{callKindLabel(row.call.kind)}</td>
                <td className={styles.note}>{row.call.model}</td>
                <td className={styles.variables}>{row.call.promptVersionId.slice(0, 8)}</td>
                {/* 이미지 행은 토큰 칸이 비고 장 수가 들어간다 (C-6) */}
                <td>{row.amount}</td>
                <td>{(row.latencyMs / 1000).toFixed(1)}s</td>
                <td data-testid="call-cost">{costLabel(row)}</td>
                <td>
                  {row.call.succeeded ? (
                    '성공'
                  ) : (
                    <span className={styles.missing} data-testid="call-failure">
                      {row.call.failureReason ?? '실패'}
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <CallPayloads calls={calls} />
    </>
  )
}

/**
 * 요청·응답 전문.
 *
 * **요약하지 않는다** — 이 화면의 목적이 "무엇을 보내 무엇을 받았나" 이므로 줄이면
 * 쓸모가 없다. 다만 길어서 기본은 접어 둔다.
 *
 * 이미지 호출의 응답 자리에는 바이트가 아니라 형식과 크기만 온다 (NFR-10).
 */
function CallPayloads({ calls }: { calls: LlmCall[] }) {
  return (
    <section className={styles.historySection}>
      <h2 className={styles.historyTitle}>요청 · 응답 전문</h2>
      <ul className={styles.history} data-testid="call-payloads">
        {calls.map((call) => (
          <li key={call.id} className={styles.historyItem}>
            <details>
              <summary className={styles.historyHead}>
                {callKindLabel(call.kind)} · {call.model}
              </summary>
              <pre className={styles.variables} data-testid="call-request">
                {call.requestPayload}
              </pre>
              <pre className={styles.variables} data-testid="call-response">
                {call.responsePayload ?? '(응답 없음)'}
              </pre>
            </details>
          </li>
        ))}
      </ul>
    </section>
  )
}
