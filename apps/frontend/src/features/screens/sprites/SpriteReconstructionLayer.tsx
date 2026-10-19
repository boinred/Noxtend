import { spriteImageUrl } from '@/app/queries/media'
import { placementRects } from '@/domain/sprites/reconstruction'
import type { CellSize, TileFill } from '@/domain/sprites/reconstruction'
import { assetRole } from '@/domain/sprites/rules'
import type { SpriteAssetPlan, SpriteState } from '@/domain/sprites/types'

const OUTLINE = {
  fill: 'none',
  stroke: 'var(--primary)',
  strokeWidth: 2,
  strokeDasharray: '6 4',
  vectorEffect: 'non-scaling-stroke',
} as const

export function SpriteReconstructionLayer({
  jobId,
  sprite,
  plans,
  fill,
  cell,
  failedImageIds,
  onImageError,
}: {
  jobId: string
  sprite: SpriteState
  plans: SpriteAssetPlan[]
  fill: TileFill | null
  cell: CellSize | null
  failedImageIds: string[]
  onImageError: (imageId: string) => void
}) {
  const { width, height } = sprite.sourceCanvas
  const assets = [...plans]
    .sort((a, b) => a.order - b.order)
    .map((plan) => {
      const role = assetRole(sprite.settings, plan)
      const imageId =
        sprite.assets
          .find((asset) => asset.id === plan.id)
          ?.frames.find((frame) => frame.index === 0)?.currentImageId ?? null
      const failed = imageId !== null && failedImageIds.includes(imageId)
      return {
        id: plan.id,
        role,
        failed,
        // 표시 실패 이미지는 누락과 같이 윤곽으로
        imageId: failed ? null : imageId,
        // 원경 배경·레이어는 원본 전체, 그 밖은 자리
        rects:
          role === 'backdrop' || role === 'layer'
            ? [{ x: 0, y: 0, width, height }]
            : placementRects(plan, sprite.sourceCanvas),
      }
    })
  const imageIds = new Map(assets.map((asset) => [asset.id, asset.imageId]))
  const failedTiles = new Set(assets.filter((asset) => asset.failed).map((asset) => asset.id))
  const diamond = sprite.settings.view === 'isometric'
  const drawn = (roles: string[]) =>
    assets.flatMap((asset) =>
      asset.imageId && roles.includes(asset.role) ? [{ ...asset, imageId: asset.imageId }] : [],
    )
  return (
    <svg
      className="block h-full w-full"
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      role="img"
      aria-label="원본 재구성"
    >
      <g data-testid="reconstruction-images">
        {drawn(['backdrop', 'layer']).map((asset) => (
          <image
            key={asset.id}
            data-asset-id={asset.id}
            href={spriteImageUrl(jobId, asset.imageId)}
            preserveAspectRatio="none"
            onError={() => onImageError(asset.imageId)}
            {...asset.rects[0]}
          />
        ))}
        {cell
          ? fill?.cells.map((tile, index) => {
              const imageId = tile.tileId === null ? null : imageIds.get(tile.tileId)
              return imageId ? (
                <image
                  key={index}
                  data-tile-id={tile.tileId}
                  href={spriteImageUrl(jobId, imageId)}
                  preserveAspectRatio="none"
                  x={tile.x}
                  y={tile.y}
                  width={cell.width}
                  height={cell.height}
                  onError={() => onImageError(imageId)}
                />
              ) : null
            })
          : null}
        {/* 같은 오브젝트는 아래쪽 y 가 작은 자리부터 */}
        {drawn(['object']).flatMap((asset) =>
          [...asset.rects]
            .sort((a, b) => a.y + a.height - (b.y + b.height))
            .map((rect, index) => (
              <image
                key={`${asset.id}-${index}`}
                data-asset-id={asset.id}
                href={spriteImageUrl(jobId, asset.imageId)}
                preserveAspectRatio="none"
                onError={() => onImageError(asset.imageId)}
                {...rect}
              />
            )),
        )}
      </g>
      {/* 누락 대상 윤곽 */}
      {assets.flatMap((asset) =>
        asset.imageId
          ? []
          : asset.rects.map((rect, index) => (
              <rect key={`${asset.id}-${index}`} data-missing={asset.id} {...OUTLINE} {...rect} />
            )),
      )}
      {/* 표시 실패 타일 칸은 원본이 비쳐 보이지 않게 윤곽 */}
      {cell
        ? fill?.cells.map((tile, index) => {
            if (tile.tileId === null || !failedTiles.has(tile.tileId)) return null
            const { x, y } = tile
            const { width: w, height: h } = cell
            const points = diamond
              ? [x + w / 2, y, x + w, y + h / 2, x + w / 2, y + h, x, y + h / 2]
              : [x, y, x + w, y, x + w, y + h, x, y + h]
            return (
              <polygon
                key={index}
                data-failed-tile={tile.tileId}
                points={points.join(' ')}
                {...OUTLINE}
              />
            )
          })
        : null}
    </svg>
  )
}
