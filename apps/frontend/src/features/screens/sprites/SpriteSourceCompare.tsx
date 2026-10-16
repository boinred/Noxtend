import { useState } from 'react'
import { sourceImageUrl, spriteImageUrl } from '@/app/queries/media'
import { assetSourceRect, cellWidthOf } from '@/domain/sprites/rules'
import type { SpriteState } from '@/domain/sprites/types'
import { spriteStyles as styles } from './spriteStyles'

export function SpriteSourceCompare({
  jobId,
  sourceImageId,
  sprite,
}: {
  jobId: string
  sourceImageId: string
  sprite: SpriteState
}) {
  const [opacity, setOpacity] = useState(50)
  const { width, height } = sprite.sourceCanvas
  const cellWidth = cellWidthOf(
    sprite.grid,
    sprite.assets.map((a) => a.plan),
    sprite.settings,
  )
  const placed = [...sprite.assets]
    .sort((a, b) => a.plan.order - b.plan.order)
    .map((asset) => ({
      id: asset.id,
      imageId: asset.frames.find((f) => f.index === 0)?.currentImageId ?? null,
      rect: assetSourceRect(sprite.settings, sprite.sourceCanvas, cellWidth, asset.plan),
    }))
  return (
    <section className={`${styles.panel} flex flex-col gap-3`} aria-label="원본 비교">
      <h2 className="text-base font-semibold text-foreground">원본 비교</h2>
      <p className={styles.hint}>
        현재 기준 이미지를 원본 영역에 다시 배치합니다. 기준 이미지가 없는 대상은 점선으로
        표시합니다.
      </p>
      <label className={styles.field}>
        재배치 투명도 {opacity}%
        <input
          className="w-full accent-primary focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-2"
          type="range"
          aria-label="재배치 투명도"
          aria-valuetext={`${opacity}%`}
          min="0"
          max="100"
          step="1"
          value={opacity}
          onChange={(e) => setOpacity(Number(e.target.value))}
        />
      </label>
      <svg
        className={styles.preview}
        style={{ maxHeight: 420 }}
        viewBox={`0 0 ${width} ${height}`}
        role="img"
        aria-label="원본 위 재배치"
      >
        <image href={sourceImageUrl(sourceImageId)} width={width} height={height} />
        <g opacity={opacity / 100}>
          {placed.map((p) =>
            p.imageId ? (
              <image
                key={p.id}
                data-asset-id={p.id}
                href={spriteImageUrl(jobId, p.imageId)}
                preserveAspectRatio="none"
                {...p.rect}
              />
            ) : null,
          )}
        </g>
        {/* 투명도와 무관하게 누락 대상 표시 */}
        {placed.map((p) =>
          p.imageId ? null : (
            <rect
              key={p.id}
              data-asset-id={p.id}
              fill="none"
              stroke="var(--primary)"
              strokeWidth={2}
              strokeDasharray="6 4"
              vectorEffect="non-scaling-stroke"
              {...p.rect}
            />
          ),
        )}
      </svg>
    </section>
  )
}
