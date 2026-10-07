import { describe, expect, it } from 'vitest'
import { sceneAnalysisSummary } from './ScenePanel'
import { formatScaleReference } from '@/domain/job/sceneScale'
import type { SceneSpec } from '@/domain/job/types'

const scene: SceneSpec = {
  palette: [
    { name: '청록색 심연의 물', hex: '#1BB5C4' },
    { name: '이끼 낀 황록색 식생', hex: null },
  ],
  timeOfDay: '맑은 낮',
  mood: '고요한 탐험 공간',
  renderingStyle: '스타일라이즈드 3D',
  materialFeel: '손으로 칠한 듯한 질감',
  camera: { type: 'isometric', eyeLevel: '지면에서 약 12m', horizonY: 0.375 },
  light: { direction: '좌측 전방 35° 방위', temperature: '따뜻한 주광', shadowHardness: '중간' },
  scale: { object: '청색 발광 단말기', realWorldSize: '높이 1.8m', heightMeters: 1.8 },
}

describe('sceneAnalysisSummary', () => {
  it('keeps the current scene wording and test ids', () => {
    const props = sceneAnalysisSummary(scene)
    expect(props.label).toBe('장면')
    expect(props.summary).toBe('맑은 낮 · 고요한 탐험 공간')
    expect(props.palette).toEqual(scene.palette)
    expect(props.fields.map((f) => [f.label, f.testId])).toEqual([
      ['시점', 'scene-camera'],
      ['광원', 'scene-light'],
      ['스케일 기준', 'scene-scale'],
      ['렌더링', 'scene-style'],
    ])
    expect(props.fields[0]!.value).toBe('isometric · 지면에서 약 12m · 수평선 0.38')
    expect(props.fields[1]!.value).toBe('좌측 전방 35° 방위 · 따뜻한 주광 · 중간')
    expect(props.fields[2]!.value).toBe(formatScaleReference(scene.scale))
    expect(props.fields[3]!.value).toBe('스타일라이즈드 3D · 손으로 칠한 듯한 질감')
    expect(props.testIds).toEqual({
      root: 'scene-panel',
      summary: 'scene-summary',
      palette: 'scene-palette',
      swatch: 'scene-swatch',
      swatchChip: 'scene-swatch-chip',
    })
  })

  it('passes an empty palette through so the shared card can omit the row', () => {
    expect(sceneAnalysisSummary({ ...scene, palette: [] }).palette).toEqual([])
  })
})
