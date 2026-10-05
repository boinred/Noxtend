/**
 * 공정별 사용량·비용.
 *
 * Design Ref: §5.2 · Plan D-14 · FR-18·FR-19
 *
 * **스튜디오에서는 접혀 있다.** 일반 사용자에게 공정마다 달러 금액을 상시 노출하는 것은
 * 파이프라인 내부 노출이다 (`PRODUCT.md` 설계 원칙 1·2). 접어 두면 궁금한 사람만 편다.
 * 관리자 화면은 `defaultOpen` 으로 전부 편다.
 *
 * **갱신에 별도 장치가 없다** — `useJob` 이 이미 폴링하므로 공정이 끝나면 다음 폴에
 * 따라온다.
 */
import { callKindLabel } from '@/domain/tuning/types'
import { costLabel, formatUsageTotal, summarizeJobUsage } from '@/domain/tuning/usage'
import { backgroundStyles as styles } from './backgroundStyles'
import type { MeshUsage } from '@/domain/tuning/usage'
import type { LlmCall, ModelPrice } from '@/domain/tuning/types'

export interface UsagePanelProps {
  calls: LlmCall[]
  meshes?: MeshUsage[]
  prices?: ModelPrice[]
  /** 파츠 이름을 붙이기 위한 공정 id → 파츠 이름. 없으면 대시로 둔다 */
  partNameByTaskId?: Record<string, string>
  defaultOpen?: boolean
}

export function UsagePanel({
  calls,
  meshes = [],
  prices = [],
  partNameByTaskId = {},
  defaultOpen = false,
}: UsagePanelProps) {
  if (calls.length === 0 && meshes.length === 0) {
    return null
  }

  const summary = summarizeJobUsage(calls, meshes, prices)

  return (
    <details className={styles.usage} open={defaultOpen} data-testid="usage-panel">
      {/* 라벨이 "토큰" 이 아니라 "사용량" 이다 — 이미지 공정은 토큰이 빈다 (C-6) */}
      <summary data-testid="usage-summary">사용량 · {formatUsageTotal(summary)}</summary>

      <table className={styles.usageTable} data-testid="usage-table">
        <thead>
          <tr>
            <th className={styles.usageHeadCell}>단계</th>
            <th className={styles.usageHeadCell}>파츠</th>
            <th className={styles.usageHeadCell}>사용량</th>
            <th className={styles.usageHeadCell}>지연</th>
            <th className={styles.usageHeadCell}>비용</th>
          </tr>
        </thead>
        <tbody>
          {summary.rows.map((row) => (
            <tr key={row.id} data-testid="usage-row">
              <td className={styles.usageCell}>{callKindLabel(row.kind)}</td>
              <td className={styles.usageCell}>
                {row.partName ?? partNameByTaskId[row.taskId] ?? '—'}
              </td>
              <td className={styles.usageCell}>{row.amount}</td>
              <td className={styles.usageNumeric}>
                {row.latencyMs === null ? '—' : `${(row.latencyMs / 1000).toFixed(1)}s`}
              </td>
              {/*
                값이 없는 두 경우를 다른 낱말로 낸다 (C-7 · FR-20). 실패 자체는
                `StageProgress` 가 사유까지 보여주므로 여기서 겹쳐 적지 않는다
              */}
              <td
                className={row.costUsd === null ? styles.usageUnpriced : styles.usageNumeric}
                data-testid="usage-cost"
              >
                {costLabel(row)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </details>
  )
}
