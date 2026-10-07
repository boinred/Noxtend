/**
 * Design Ref: §5.3 · Plan D-13 — 장면 명세.
 *
 * 팔레트는 서버가 이름과 색을 나눠 준다 (사이클 #9). 전에는 한 문자열에서 HEX 를 긁어
 * 칠했는데, 모델이 자연어만 주면 칩이 투명해져 이름만 남았다. 화면은 이제 색을 복구하지
 * 않는다 — 계약 위반은 상류에서 막는다.
 *
 * **`camera` · `light` · `scale` 을 눈에 띄게 둔다.** 이 셋이 어긋나면 나머지가
 * 아무리 좋아도 파츠를 따로 그려 합칠 수 없다. 사이클 #4 의 자유 문장에는 이 셋이
 * 아예 없었고, 그것이 이 사이클에서 장면 단계를 분리한 이유다.
 */
import { AnalysisSummaryPanel, type AnalysisSummaryPanelProps } from '../AnalysisSummaryPanel'
import { formatScaleReference } from '@/domain/job/sceneScale'
import type { SceneSpec } from '@/domain/job/types'

export interface ScenePanelProps {
  scene: SceneSpec
}

export function sceneAnalysisSummary(scene: SceneSpec): AnalysisSummaryPanelProps {
  const { camera, light } = scene
  return {
    label: '장면',
    summary: `${scene.timeOfDay} · ${scene.mood}`,
    palette: scene.palette,
    fields: [
      {
        id: 'camera',
        label: '시점',
        value: `${camera.type} · ${camera.eyeLevel} · 수평선 ${camera.horizonY.toFixed(2)}`,
        testId: 'scene-camera',
      },
      {
        id: 'light',
        label: '광원',
        value: `${light.direction} · ${light.temperature} · ${light.shadowHardness}`,
        testId: 'scene-light',
      },
      {
        id: 'scale',
        label: '스케일 기준',
        value: formatScaleReference(scene.scale),
        testId: 'scene-scale',
      },
      {
        id: 'style',
        label: '렌더링',
        value: `${scene.renderingStyle} · ${scene.materialFeel}`,
        testId: 'scene-style',
      },
    ],
    testIds: {
      root: 'scene-panel',
      summary: 'scene-summary',
      palette: 'scene-palette',
      swatch: 'scene-swatch',
      swatchChip: 'scene-swatch-chip',
    },
  }
}

export function ScenePanel({ scene }: ScenePanelProps) {
  return <AnalysisSummaryPanel {...sceneAnalysisSummary(scene)} />
}
