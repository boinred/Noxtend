/**
 * "분석 / 3D 배경" 탭. Design Ref: scene-assembly §5.1 · Plan D-03
 *
 * **3D 청크는 선택 전에 로드하지 않는다** — 탭 자체는 정적 경로에 있어도 가볍다.
 * 상태는 URL 쿼리 `?view=scene` 에 산다 — 새로고침·공유에 살아남는다 (§7.3-7 확정).
 */
import { backgroundStyles as styles } from './backgroundStyles'

export type StudioView = 'analysis' | 'scene'

export interface StudioViewTabsProps {
  view: StudioView
  onSelect: (view: StudioView) => void
  /** 완성 GLB 가 없으면 3D 탭은 비활성이다 (FR-04) — 빈 캔버스는 실패로 읽힌다 */
  sceneEnabled: boolean
}

export function StudioViewTabs({ view, onSelect, sceneEnabled }: StudioViewTabsProps) {
  return (
    <div className={styles.viewTabs} role="tablist" data-testid="studio-view-tabs">
      <button
        type="button"
        role="tab"
        className={styles.viewTab}
        aria-selected={view === 'analysis'}
        data-testid="tab-analysis"
        onClick={() => onSelect('analysis')}
      >
        분석
      </button>
      <button
        type="button"
        role="tab"
        className={styles.viewTab}
        aria-selected={view === 'scene'}
        data-testid="tab-scene"
        disabled={!sceneEnabled}
        title={sceneEnabled ? undefined : '완성된 3D 가 아직 없습니다'}
        onClick={() => onSelect('scene')}
      >
        3D 배경
      </button>
    </div>
  )
}
