/**
 * 분석 결과 요약 카드 — 3D 장면·2D 분석 공용 표시
 *
 * 작업 종류 판단 없음 — 표시 입력은 작업별 연결부 담당
 */
import type { ReactElement } from 'react'

export type AnalysisPaletteEntry = { name: string; hex: string | null }
export type AnalysisField = { id: string; label: string; value: string; testId?: string }
export type AnalysisSummaryTestIds = {
  root?: string
  summary?: string
  palette?: string
  swatch?: string
  swatchChip?: string
}
export type AnalysisSummaryPanelProps = {
  label: string
  summary: string
  palette?: readonly AnalysisPaletteEntry[]
  fields: readonly AnalysisField[]
  testIds?: AnalysisSummaryTestIds
}

const styles = {
  panel: 'rounded-xl border border-border bg-card px-[18px] py-4',
  header: 'mb-3 flex items-baseline gap-2.5',
  label: 'text-[0.8125rem] font-semibold text-muted-foreground',
  summary: 'text-[0.9375rem] font-semibold text-foreground',
  palette: 'mb-3.5 flex flex-wrap gap-x-3 gap-y-1.5',
  swatch: 'inline-flex items-center gap-1.5 text-xs text-muted-foreground',
  // 밝은 색도 경계가 보이도록 border 유지
  swatchChip: 'size-3.5 rounded border border-border',
  // 색상 미확정 — 투명 칩은 빈 칩으로 읽힘
  swatchChipUnresolved: 'size-3.5 rounded border border-dashed border-border bg-[var(--sunken-bg)]',
  grid: 'grid grid-cols-[repeat(auto-fit,minmax(260px,1fr))] gap-x-5 gap-y-2.5',
  field: 'min-w-0',
  fieldLabel: 'mb-0.5 text-xs text-muted-foreground',
  fieldValue: 'text-[0.8125rem] leading-normal text-foreground',
}

export function AnalysisSummaryPanel({
  label,
  summary,
  palette = [],
  fields,
  testIds = {},
}: AnalysisSummaryPanelProps): ReactElement {
  return (
    <section className={styles.panel} data-testid={testIds.root}>
      <div className={styles.header}>
        <span className={styles.label}>{label}</span>
        <span className={styles.summary} data-testid={testIds.summary}>
          {summary}
        </span>
      </div>

      {palette.length > 0 ? (
        <div className={styles.palette} data-testid={testIds.palette}>
          {palette.map((entry, index) => (
            <span
              key={`${entry.name}:${entry.hex ?? 'unresolved'}:${index}`}
              className={styles.swatch}
              title={entry.hex ? `${entry.name} ${entry.hex}` : `${entry.name} · 색상 미확정`}
              data-testid={testIds.swatch}
            >
              {/* 이름이 옆에 있으므로 칩은 보조 장식 */}
              <span
                className={entry.hex ? styles.swatchChip : styles.swatchChipUnresolved}
                style={entry.hex ? { backgroundColor: entry.hex } : undefined}
                data-resolved={entry.hex !== null}
                data-testid={testIds.swatchChip}
                aria-hidden="true"
              />
              {entry.name}
            </span>
          ))}
        </div>
      ) : null}

      <dl className={styles.grid}>
        {fields.map((field) => (
          <div key={field.id} className={styles.field}>
            <dt className={styles.fieldLabel}>{field.label}</dt>
            <dd className={styles.fieldValue} data-testid={field.testId}>
              {field.value}
            </dd>
          </div>
        ))}
      </dl>
    </section>
  )
}
