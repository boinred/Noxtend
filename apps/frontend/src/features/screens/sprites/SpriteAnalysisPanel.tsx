import { AnalysisSummaryPanel, type AnalysisSummaryPanelProps } from '../AnalysisSummaryPanel'
import { spriteOutputLabel, spriteViewLabel } from '@/domain/sprites/labels'
import type { SpriteCanvas, SpriteState } from '@/domain/sprites/types'

const size = (canvas: SpriteCanvas) => `${canvas.width}×${canvas.height}`

// 서버 저장 계획 기준 — 미저장 편집은 검수 패널에만 반영
export function spriteAnalysisSummary(sprite: SpriteState): AnalysisSummaryPanelProps {
  const { settings } = sprite
  const plans = sprite.assets.map((asset) => asset.plan)
  const frames = plans.reduce((sum, plan) => sum + (plan.loop ? plan.frameCount : 1), 0)
  const view = spriteViewLabel(settings.view)
  const unit = settings.outputKind === 'layers' ? '레이어' : '타일'
  return {
    label: '분석',
    summary: `${view} · ${unit} ${plans.length}개`,
    fields: [
      { id: 'view', label: '시점', value: view, testId: 'sprite-analysis-view' },
      {
        id: 'output',
        label: '출력',
        value: spriteOutputLabel(settings),
        testId: 'sprite-analysis-output',
      },
      {
        id: 'canvas',
        label: '캔버스',
        value: `원본 ${size(sprite.sourceCanvas)} → 요청 ${size(sprite.generationCanvas)} → 최종 ${size(sprite.outputCanvas)}`,
        testId: 'sprite-analysis-canvas',
      },
      {
        id: 'assets',
        label: '대상',
        value: `${plans.length}개 · 총 ${frames}프레임`,
        testId: 'sprite-analysis-assets',
      },
    ],
    testIds: { root: 'sprite-analysis-panel', summary: 'sprite-analysis-summary' },
  }
}

export function SpriteAnalysisPanel({ sprite }: { sprite: SpriteState }) {
  return <AnalysisSummaryPanel {...spriteAnalysisSummary(sprite)} />
}
