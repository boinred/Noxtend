import { useEffect, useState } from 'react'
import { fetchImageBlob, sourceImageUrl, spriteImageUrl } from '@/app/queries/media'
import { tileFillKey } from '@/domain/sprites/reconstruction'
import type { CellSize, Pixels, TileFill } from '@/domain/sprites/reconstruction'
import { assetRole } from '@/domain/sprites/rules'
import type { SpriteAssetPlan, SpriteState } from '@/domain/sprites/types'

// 초기 보호 기준, 실측 보장 아님
const TIMEOUT_MS = 5000
const EMPTY: TileFill = { offset: { x: 0, y: 0 }, cells: [] }
const START_FAILED = '재구성 계산을 시작하지 못했습니다'

type WorkerReply = { fill: TileFill } | { error: { name: string; message: string } }

async function pixelsOf(url: string, width: number, height: number): Promise<Pixels> {
  const image = await createImageBitmap(await fetchImageBlob(url))
  const context = new OffscreenCanvas(width, height).getContext('2d', { willReadFrequently: true })
  if (!context) throw new Error('canvas 2d unavailable')
  context.drawImage(image, 0, 0, width, height)
  image.close()
  return { width, height, data: context.getImageData(0, 0, width, height).data }
}

export function useTileFill({
  jobId,
  sourceImageId,
  sprite,
  plans,
  cell,
  enabled,
}: {
  jobId: string
  sourceImageId: string
  sprite: SpriteState
  plans: SpriteAssetPlan[]
  cell: CellSize | null
  enabled: boolean
}): {
  status: 'idle' | 'loading' | 'ready' | 'error'
  fill: TileFill | null
  error: string | null
  // 칸 상한·시간 초과는 입력 조정 안내라 제외
  retryable: boolean
  retry: () => void
} {
  const [attempt, setAttempt] = useState(0)
  const key = `${attempt}:${tileFillKey(plans, sprite, cell)}`
  const imageOf = (id: string) =>
    sprite.assets.find((asset) => asset.id === id)?.frames.find((frame) => frame.index === 0)
      ?.currentImageId ?? null
  const tiles = plans.flatMap((plan) => {
    const imageId = assetRole(sprite.settings, plan) === 'tile' ? imageOf(plan.id) : null
    return imageId ? [{ id: plan.id, imageId }] : []
  })
  const backdropPlan = plans.find((plan) => assetRole(sprite.settings, plan) === 'backdrop')
  const backdrop = backdropPlan ? imageOf(backdropPlan.id) : null
  const [result, setResult] = useState<{
    key: string
    fill: TileFill | null
    error: string | null
    retryable: boolean
  } | null>(null)

  useEffect(() => {
    if (!enabled || tiles.length === 0 || cell === null) return
    const { width, height } = sprite.sourceCanvas
    let worker: Worker | null = null
    let timer: number | undefined
    let done = false
    const stop = () => {
      done = true
      worker?.terminate()
      window.clearTimeout(timer)
    }
    // 종료·입력 변경 뒤 늦은 결과 폐기
    const finish = (fill: TileFill | null, error: string | null, retryable = true) => {
      if (done) return
      stop()
      setResult({ key, fill, error, retryable })
    }
    Promise.all([
      pixelsOf(sourceImageUrl(sourceImageId), width, height),
      backdrop ? pixelsOf(spriteImageUrl(jobId, backdrop), width, height) : null,
      Promise.all(
        tiles.map((tile) => pixelsOf(spriteImageUrl(jobId, tile.imageId), cell.width, cell.height)),
      ),
    ])
      .then(
        ([source, backdropPixels, tilePixels]) => {
          if (done) return
          worker = new Worker(new URL('./tileFill.worker.ts', import.meta.url), { type: 'module' })
          worker.onmessage = ({ data }: MessageEvent<WorkerReply>) => {
            if ('fill' in data) finish(data.fill, null)
            else if (data.error.name === 'RangeError')
              finish(null, '재구성 칸이 너무 많습니다. 격자 칸 너비를 늘려 주세요', false)
            else finish(null, data.error.message)
          }
          // 브라우저 원문 오류 대신 고정 문구
          worker.onerror = () => finish(null, START_FAILED)
          timer = window.setTimeout(
            () =>
              finish(
                null,
                '재구성 계산 시간이 초과되었습니다. 격자 칸 너비를 조정해 주세요',
                false,
              ),
            TIMEOUT_MS,
          )
          const pixels = [source, ...(backdropPixels ? [backdropPixels] : []), ...tilePixels]
          worker.postMessage(
            {
              source,
              backdrop: backdropPixels,
              tiles: tiles.map((tile, index) => ({ id: tile.id, pixels: tilePixels[index]! })),
              cell,
              diamond: sprite.settings.view === 'isometric',
            },
            pixels.map((item) => item.data.buffer),
          )
        },
        () => finish(null, '재구성에 필요한 이미지를 불러오지 못했습니다'),
      )
      // Worker 생성·전송 실패도 계산 중으로 남기지 않음
      .catch(() => finish(null, START_FAILED))
    return stop
    // 타일 계산 입력은 key 가 대표, 원본 크기·시점은 작업마다 고정
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled, key, jobId, sourceImageId])

  const retry = () => setAttempt((current) => current + 1)
  const quiet = { error: null, retryable: false, retry }
  if (!enabled) return { status: 'idle', fill: null, ...quiet }
  if (tiles.length === 0) return { status: 'ready', fill: EMPTY, ...quiet }
  if (cell === null) return { status: 'idle', fill: null, ...quiet }
  if (result?.key !== key) return { status: 'loading', fill: null, ...quiet }
  return result.error === null
    ? { status: 'ready', fill: result.fill, ...quiet }
    : { status: 'error', fill: null, error: result.error, retryable: result.retryable, retry }
}
