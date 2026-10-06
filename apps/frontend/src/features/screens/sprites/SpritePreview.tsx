import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { spriteImageUrl } from '@/app/queries/media'
import { tileOffsets } from '@/domain/sprites/rules'
import type { SpriteState } from '@/domain/sprites/types'
import { checkerboard, spriteStyles as styles } from './spriteStyles'

export function SpritePreview({
  sprite,
  timeMs,
  playing,
}: {
  sprite: SpriteState
  timeMs: number
  playing: boolean
}) {
  const { jobId } = useParams()
  const [hidden, setHidden] = useState<string[]>([])
  const [tileId, setTileId] = useState<string | null>(null)
  const assets = [...sprite.assets].sort((a, b) => a.plan.order - b.plan.order)
  const width = sprite.outputCanvas.width
  const height = sprite.outputCanvas.height
  const tiles = sprite.settings.outputKind === 'tiles'
  const tile = assets.find((a) => a.id === tileId) ?? assets[0]
  // 정적 기준 프레임, Task 13의 재생 props 경계
  void timeMs
  void playing
  return (
    <section className={styles.panel} aria-label="배경 미리보기">
      <h2 className={styles.title}>{tiles ? '타일 반복 미리보기' : '레이어 합성 미리보기'}</h2>
      <p className={styles.hint}>
        고정 캔버스 {width}×{height}px ·{' '}
        {tiles ? '셀 중심 앵커 · 3×3 격자' : '왼쪽 위 앵커 (0, 0) · 뒤에서 앞으로 합성'}
      </p>
      {tiles ? (
        <div className={styles.field}>
          <label htmlFor="sprite-preview-tile">미리볼 타일</label>
          <Select value={tile?.id} onValueChange={setTileId}>
            <SelectTrigger id="sprite-preview-tile">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {assets.map((a) => (
                <SelectItem key={a.id} value={a.id}>
                  {a.plan.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      ) : (
        <div className={styles.row}>
          {assets.map((a) => (
            <label key={a.id} className={styles.row}>
              <input
                type="checkbox"
                checked={!hidden.includes(a.id)}
                onChange={(e) =>
                  setHidden(
                    e.target.checked ? hidden.filter((id) => id !== a.id) : [...hidden, a.id],
                  )
                }
              />
              {a.plan.name} 표시
            </label>
          ))}
        </div>
      )}
      <svg
        className={styles.preview}
        style={{ ...checkerboard, maxHeight: 420 }}
        viewBox={
          tiles
            ? `${-width * 1.5} ${-height * 1.5} ${width * 3} ${height * 3}`
            : `0 0 ${width} ${height}`
        }
        role="img"
        aria-label={tiles ? '반복 경계 검수' : '레이어 합성 검수'}
      >
        {(tiles ? (tile ? [tile] : []) : assets.filter((a) => !hidden.includes(a.id))).map(
          (asset) => {
            const id = asset.frames[0]?.currentImageId
            if (!id || !jobId) return null
            const offsets = tiles
              ? tileOffsets(
                  width,
                  height,
                  sprite.settings.repeat,
                  sprite.settings.view === 'isometric' ? 'diamond' : 'square',
                )
              : [{ x: 0, y: 0 }]
            return offsets.map((offset, index) => (
              <image
                key={`${asset.id}-${index}`}
                href={spriteImageUrl(jobId, id)}
                x={offset.x - (tiles ? asset.anchor.x : 0)}
                y={offset.y - (tiles ? asset.anchor.y : 0)}
                width={width}
                height={height}
              />
            ))
          },
        )}
      </svg>
      {sprite.images.length === 0 ? (
        <p className={styles.hint}>기준 이미지가 도착하면 같은 캔버스에서 검수합니다.</p>
      ) : null}
    </section>
  )
}
