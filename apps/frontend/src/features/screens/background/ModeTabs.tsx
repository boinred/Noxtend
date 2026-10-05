/**
 * Design Ref: §5.1 · §5.4 — 이미지 모드(기본) / 프롬프트 모드.
 */
import { backgroundStyles as styles } from './backgroundStyles'

export type StudioMode = 'image' | 'prompt'

export interface ModeTabsProps {
  mode: StudioMode
  onChange: (mode: StudioMode) => void
}

const TABS: ReadonlyArray<{ value: StudioMode; label: string }> = [
  { value: 'image', label: '이미지 모드' },
  { value: 'prompt', label: '프롬프트 모드' },
]

export function ModeTabs({ mode, onChange }: ModeTabsProps) {
  return (
    <div className={styles.tabs} role="tablist" aria-label="입력 방식" data-testid="mode-tabs">
      {TABS.map((tab) => (
        <button
          key={tab.value}
          type="button"
          role="tab"
          aria-selected={mode === tab.value}
          className={styles.tab}
          onClick={() => onChange(tab.value)}
          data-testid={`mode-tab-${tab.value}`}
        >
          {tab.label}
        </button>
      ))}
    </div>
  )
}
