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
import { backgroundStyles as styles } from './backgroundStyles'
import { formatScaleReference } from '@/domain/job/sceneScale'
import type { SceneSpec } from '@/domain/job/types'

export interface ScenePanelProps {
  scene: SceneSpec
}

export function ScenePanel({ scene }: ScenePanelProps) {
  return (
    <section className={styles.scene} data-testid="scene-panel">
      <div className={styles.sceneHeader}>
        <span className={styles.sceneLabel}>장면</span>
        <span className={styles.sceneSummary} data-testid="scene-summary">
          {scene.timeOfDay} · {scene.mood}
        </span>
      </div>

      {/* 팔레트는 값이 아니라 색으로 보여야 판단이 된다 */}
      <div className={styles.palette} data-testid="scene-palette">
        {scene.palette.map((entry, index) => (
          <span
            key={`${entry.name}:${entry.hex ?? 'unresolved'}:${index}`}
            className={styles.swatch}
            title={entry.hex ? `${entry.name} ${entry.hex}` : `${entry.name} · 색상 미확정`}
            data-testid="scene-swatch"
          >
            {/* 이름이 바로 옆에 있으므로 칩은 보조 장식이다 */}
            <span
              className={entry.hex ? styles.swatchChip : styles.swatchChipUnresolved}
              style={entry.hex ? { backgroundColor: entry.hex } : undefined}
              data-resolved={entry.hex !== null}
              data-testid="scene-swatch-chip"
              aria-hidden="true"
            />
            {entry.name}
          </span>
        ))}
      </div>

      <dl className={styles.sceneGrid}>
        <SceneField label="시점" testId="scene-camera">
          {scene.camera.type} · {scene.camera.eyeLevel} · 수평선 {scene.camera.horizonY.toFixed(2)}
        </SceneField>
        <SceneField label="광원" testId="scene-light">
          {scene.light.direction} · {scene.light.temperature} · {scene.light.shadowHardness}
        </SceneField>
        <SceneField label="스케일 기준" testId="scene-scale">
          {formatScaleReference(scene.scale)}
        </SceneField>
        <SceneField label="렌더링" testId="scene-style">
          {scene.renderingStyle} · {scene.materialFeel}
        </SceneField>
      </dl>
    </section>
  )
}

function SceneField({
  label,
  testId,
  children,
}: {
  label: string
  testId: string
  children: React.ReactNode
}) {
  return (
    <div className={styles.sceneField}>
      <dt className={styles.sceneFieldLabel}>{label}</dt>
      <dd className={styles.sceneFieldValue} data-testid={testId}>
        {children}
      </dd>
    </div>
  )
}
