/**
 * Design Ref: §5.1 · §12.1 · §12.2 — 프롬프트 관리 (단계 × 카테고리 격자).
 *
 * **이 화면이 사이클 #5 의 핵심 가치를 나른다.** 프롬프트를 배포 없이 고치고 되돌릴 수
 * 있어야 반복 주기가 분 단위가 된다 (FR-08·FR-09).
 *
 * 평면 목록을 `Map(kind → prompt)` 로 접으면 같은 kind 의 카테고리 행이 서로 덮어써
 * 하나가 조용히 버려진다. 대신 서버가 계산한 `(단계 × 카테고리)` 유효 활성 격자를 받아
 * 표로 펼치기만 한다 — 폴백(전용 없으면 기본)은 서버가 이미 반영했다 (§12.1).
 *
 * 빈 카테고리 칸도 링크로 열려 "그 카테고리의 첫 프롬프트를 만든다" 에 도달한다 (§12.2).
 */
import { Link } from 'react-router-dom'
import { PageContainer } from '@/features/screens/PageContainer'
import { AdminTabs } from './AdminTabs'
import { usePromptGrid } from '@/app/queries/useTuning'
import { ASSET_CATEGORIES, assetCategoryLabel } from '@/domain/job/types'
import { promptKindCategories, promptKindLabel } from '@/domain/tuning/types'
import { adminPromptEditPath } from '@/routes/paths'
import { adminStyles as styles } from './adminStyles'
import type { AssetCategory } from '@/domain/job/types'
import type { PromptKind } from '@/domain/tuning/types'
import type { PromptGridCell } from '@/domain/tuning/types'

/** 실행 순서대로. 재구성(3D)은 텍스트 프롬프트가 없어 격자에서 뺀다 */
// 프롬프트 슬롯 전부 — 유사도 평가는 Background 전용이지만 행으로는 선다 (§15.1)
const STAGES: PromptKind[] = [
  'analyze',
  'extract',
  'decompose',
  'rewriteDescriptions',
  'generate',
  'similarityEvaluate',
  'analyzeSprites',
  'generateSprite',
  'generateSpriteSource',
]

/** 열: 기본(null) → 캐릭터 → 소품 → 배경 */
const COLUMNS: (AssetCategory | null)[] = [null, ...ASSET_CATEGORIES]

function columnLabel(category: AssetCategory | null): string {
  return category ? assetCategoryLabel(category) : '기본'
}

export function PromptsScreen() {
  const { rows, isLoading } = usePromptGrid()

  const byKind = new Map(rows.map((row) => [row.kind, row]))

  return (
    <PageContainer
      width="max"
      title="프롬프트"
      subtitle="단계마다 카테고리별로 활성 버전이 하나입니다. 전용이 없으면 기본으로 폴백합니다."
      testId="prompts-screen"
    >
      <AdminTabs />

      {isLoading ? null : (
        <div className={styles.panel}>
          <table className={styles.table} data-testid="prompt-grid">
            <thead>
              <tr>
                <th>단계</th>
                {COLUMNS.map((category) => (
                  <th key={category ?? 'default'}>{columnLabel(category)}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {STAGES.map((kind) => {
                const cellsByCategory = new Map(
                  (byKind.get(kind)?.cells ?? []).map((cell) => [cell.category, cell]),
                )

                return (
                  <tr key={kind} data-testid="prompt-row" data-kind={kind}>
                    <td>{promptKindLabel(kind)}</td>
                    {COLUMNS.map((category) => (
                      <td key={category ?? 'default'}>
                        <GridCell
                          kind={kind}
                          category={category}
                          cell={cellsByCategory.get(category)}
                        />
                      </td>
                    ))}
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </PageContainer>
  )
}

/**
 * 한 칸. 상태에 따라 전용 버전 · 기본으로 폴백 · 실행 불가를 보여준다.
 *
 * 어느 상태든 편집 화면(그 카테고리)으로 링크한다 — 실행 불가 칸을 눌러 첫 프롬프트를
 * 만드는 것이 §12.2 의 진입점이다.
 */
function GridCell({
  kind,
  category,
  cell,
}: {
  kind: PromptKind
  category: AssetCategory | null
  cell: PromptGridCell | undefined
}) {
  const status = cell?.status ?? 'unavailable'

  // 이 슬롯에 존재할 수 없는 칸 — 링크를 주면 만들 수 없는 프롬프트로 안내하게 된다
  if (!promptKindCategories(kind).includes(category)) {
    return (
      <span className="text-[0.8125rem] text-muted-foreground/60" data-testid="prompt-cell-na">
        해당 없음
      </span>
    )
  }

  return (
    <Link
      to={adminPromptEditPath(kind, category)}
      data-testid="prompt-cell"
      data-status={status}
      data-category={category ?? 'default'}
      className="inline-flex flex-col gap-0.5"
    >
      {status === 'dedicated' ? (
        <span className={styles.version} data-testid="prompt-cell-dedicated">
          v{cell!.version}
        </span>
      ) : status === 'fallback' ? (
        // 전용이 없어 기본으로 돈다 — 어느 버전이 실제로 쓰이는지 밝힌다
        <span className="text-[0.8125rem] text-muted-foreground" data-testid="prompt-cell-fallback">
          기본으로 폴백 (v{cell!.version})
        </span>
      ) : (
        <span className={styles.missing} data-testid="prompt-cell-unavailable">
          없음 — 실행 불가
        </span>
      )}
    </Link>
  )
}
