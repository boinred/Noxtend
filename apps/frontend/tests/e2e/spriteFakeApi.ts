import type { Page, Route } from '@playwright/test'
import { crc32, deflateSync } from 'node:zlib'
import type { Job, JobTask } from '../../src/domain/job/types'
import type {
  SpriteState,
  SpriteSettings,
  SpriteAssetPlan,
  SpriteAsset,
  SpriteAccepted,
} from '../../src/domain/sprites/types'

export const SPRITE_IDS = {
  job: '10000000-0000-4000-8000-000000000001',
  provider: '20000000-0000-4000-8000-000000000001',
  upload: '30000000-0000-4000-8000-000000000001',
  sourceJob: '10000000-0000-4000-8000-000000000099',
  sourceImage: '40000000-0000-4000-8000-000000000099',
}
const now = '2026-10-06T00:00:00Z'
export interface SpriteFakeOptions {
  records?: { path: string; body: Record<string, unknown> }[]
  seed?:
    'baseReview' | 'failed' | 'canceled' | 'packFailed' | 'completed' | 'analyzing' | 'frameReview'
  providerErrorOnce?: boolean
  providersEmpty?: boolean
  detailErrorOnce?: boolean
  imageModelsError?: boolean
  unsupportedModel?: boolean
  priceError?: boolean
  conflictOnce?: boolean
  lostStartOnce?: boolean
  lostAssetApprovalOnce?: boolean
  approveErrorOnce?: boolean
  frameStates?: boolean
  runningBase?: boolean
  partialBases?: boolean
  settings?: SpriteSettings
}

// Node stdlib CRC·zlib, 실제 RGBA PNG fixture
function png(width: number, height: number, pixel: (x: number, y: number) => number[]) {
  const raw = Buffer.alloc((width * 4 + 1) * height)
  for (let y = 0; y < height; y++)
    for (let x = 0; x < width; x++) raw.set(pixel(x, y), y * (width * 4 + 1) + 1 + x * 4)
  function chunk(type: string, data: Buffer) {
    const kind = Buffer.from(type)
    const size = Buffer.alloc(4)
    size.writeUInt32BE(data.length)
    const check = Buffer.alloc(4)
    check.writeUInt32BE(crc32(Buffer.concat([kind, data])))
    return Buffer.concat([size, kind, data, check])
  }
  const header = Buffer.alloc(13)
  header.writeUInt32BE(width)
  header.writeUInt32BE(height, 4)
  header[8] = 8
  header[9] = 6
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ])
}
export const SPRITE_SOURCE_PNG = png(320, 180, (x, y) => [
  75 + Math.floor(x / 4),
  110 + Math.floor(y / 4),
  95,
  255,
])
function framePixel(sprite: SpriteState, asset: SpriteAsset, x: number, y: number) {
  const { width, height } = sprite.outputCanvas
  const diamond = sprite.settings.outputKind === 'tiles' && sprite.settings.view === 'isometric'
  const inside = diamond
    ? Math.abs((x + 0.5 - width / 2) / (width / 2)) +
        Math.abs((y + 0.5 - height / 2) / (height / 2)) <=
      1
    : !asset.plan.requiresTransparency ||
      (x > width / 5 && x < (width * 4) / 5 && y > height / 5 && y < (height * 4) / 5)
  return inside ? [70 + asset.plan.order * 35, 125, 100, 255] : [0, 0, 0, 0]
}
export function spritePng(sprite: SpriteState, asset: SpriteAsset) {
  return png(sprite.outputCanvas.width, sprite.outputCanvas.height, (x, y) =>
    framePixel(sprite, asset, x, y),
  )
}

// ponytail: 소규모 uncompressed ZIP, ZIP64 fixture는 표준 압축 도구로 교체
function zip(files: { name: string; data: Buffer }[]) {
  const local: Buffer[] = [],
    central: Buffer[] = []
  let offset = 0
  for (const file of files) {
    const name = Buffer.from(file.name),
      checksum = crc32(file.data)
    const header = Buffer.alloc(30)
    header.writeUInt32LE(0x04034b50)
    header.writeUInt16LE(20, 4)
    header.writeUInt32LE(checksum, 14)
    header.writeUInt32LE(file.data.length, 18)
    header.writeUInt32LE(file.data.length, 22)
    header.writeUInt16LE(name.length, 26)
    local.push(header, name, file.data)
    const directory = Buffer.alloc(46)
    directory.writeUInt32LE(0x02014b50)
    directory.writeUInt16LE(20, 4)
    directory.writeUInt16LE(20, 6)
    directory.writeUInt32LE(checksum, 16)
    directory.writeUInt32LE(file.data.length, 20)
    directory.writeUInt32LE(file.data.length, 24)
    directory.writeUInt16LE(name.length, 28)
    directory.writeUInt32LE(offset, 42)
    central.push(directory, name)
    offset += header.length + name.length + file.data.length
  }
  const entries = Buffer.concat(central),
    end = Buffer.alloc(22)
  end.writeUInt32LE(0x06054b50)
  end.writeUInt16LE(files.length, 8)
  end.writeUInt16LE(files.length, 10)
  end.writeUInt32LE(entries.length, 12)
  end.writeUInt32LE(offset, 16)
  return Buffer.concat([...local, entries, end])
}
export function spriteZip(job: Job, included: string[], exportId: string) {
  const sprite = job.sprite!
  const files: { name: string; data: Buffer }[] = []
  const assets = sprite.assets.filter((a) => included.includes(a.id))
  const manifestAssets = assets.map((a) => {
    const basePath = `${sprite.settings.outputKind}/asset-${a.id}.png`
    const sheetPath = `sheets/asset-${a.id}-000.png`
    const { width, height } = sprite.outputCanvas
    const cellWidth = width + 4,
      cellHeight = height + 4
    files.push({ name: basePath, data: spritePng(sprite, a) })
    const frames = a.frames.map((frame) => {
      const path = `frames/asset-${a.id}/frame-${String(frame.index).padStart(3, '0')}.png`
      files.push({ name: path, data: spritePng(sprite, a) })
      return {
        imageId: frame.currentImageId!,
        index: frame.index,
        path,
        sheetPath,
        page: 0,
        rect: { x: frame.index * cellWidth + 2, y: 2, width, height },
      }
    })
    files.push({
      name: sheetPath,
      data: png(cellWidth * frames.length, cellHeight, (x, y) =>
        framePixel(
          sprite,
          a,
          Math.max(0, Math.min(width - 1, (x % cellWidth) - 2)),
          Math.max(0, Math.min(height - 1, y - 2)),
        ),
      ),
    })
    return { asset: a.approval!.snapshot, basePath, frames }
  })
  const input = {
    schemaVersion: 1,
    exportId,
    reviewRevision: sprite.reviewRevision,
    view: sprite.settings.view,
    outputKind: sprite.settings.outputKind,
    sourceCanvas: sprite.sourceCanvas,
    outputCanvas: sprite.outputCanvas,
    assets: assets.map((a) => a.approval!.snapshot),
    excludedAssetIds: sprite.assets.filter((a) => !included.includes(a.id)).map((a) => a.id),
  }
  files.push({
    name: 'manifest.json',
    data: Buffer.from(
      JSON.stringify({
        schemaVersion: 1,
        jobId: job.id,
        productionMode: 'twoD',
        input,
        coordinateOrigin: 'topLeft',
        coordinateUnits: 'pixels',
        assets: manifestAssets,
      }),
    ),
  })
  return zip(files)
}

function ok(route: Route, data: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', json: { data, error: null } })
}
function fail(route: Route, status: number, message: string) {
  return route.fulfill({
    status,
    contentType: 'application/json',
    json: {
      data: null,
      error: { code: status === 409 ? 'SPRITE_REVISION_CONFLICT' : 'FAKE_ERROR', message },
    },
  })
}

export async function installSpriteFakeApi(page: Page, options: SpriteFakeOptions) {
  let sequence = 1
  const guid = (type: number) =>
    `${type}0000000-0000-4000-8000-${String(sequence++).padStart(12, '0')}`
  let job: Job | null = null
  const receipts = new Map<string, { fingerprint: string; receipt: SpriteAccepted }>()
  const archives = new Map<string, Buffer>()
  let providerFailure = options.providerErrorOnce ?? false,
    detailFailure = options.detailErrorOnce ?? false,
    conflict = options.conflictOnce ?? false,
    lost = options.lostStartOnce ?? false,
    lostAssetApproval = options.lostAssetApprovalOnce ?? false,
    approvalFailure = options.approveErrorOnce ?? false
  function task(kind: JobTask['kind'], status: JobTask['status'] = 'succeeded'): JobTask {
    return {
      id: guid(5),
      kind,
      ordinal: job?.tasks.length ?? 0,
      status,
      failureReason: status === 'failed' ? 'Fake 이미지 생성 실패' : null,
      attemptCount: 1,
      startedAt: now,
      completedAt: status === 'running' ? null : now,
      partId: null,
      viewDirection: null,
      progress: null,
    }
  }
  function make(settings: SpriteSettings): Job {
    const outputCanvas =
      settings.outputKind === 'layers'
        ? { width: 320, height: 180 }
        : {
            width: settings.tileWidth,
            height: settings.view === 'isometric' ? settings.tileWidth / 2 : settings.tileWidth,
          }
    const scale = Math.min(outputCanvas.width / 1024, outputCanvas.height / 1024)
    const sprite: SpriteState = {
      settings,
      sourceCanvas: { width: 320, height: 180 },
      generationCanvas: { width: 1024, height: 1024 },
      outputCanvas,
      transform: {
        scale,
        offsetX: (outputCanvas.width - 1024 * scale) / 2,
        offsetY: (outputCanvas.height - 1024 * scale) / 2,
      },
      phase: 'planReview',
      reviewRevision: 1,
      completedExportId: null,
      assets: [],
      images: [],
      exports: [],
    }
    sprite.assets = [0, 1].map((order) => {
      const id = guid(6)
      return {
        id,
        plan: {
          id,
          name: order ? '전경 나무' : '후경 지면',
          order,
          sourceBounds: { x: 0, y: 0, w: 1, h: 1 },
          requiresTransparency:
            settings.outputKind === 'layers' ? order !== 0 : settings.view === 'isometric',
          loop: false,
          frameCount: 8,
          fps: 8,
          motionNotes: '',
        },
        planRevision: 1,
        anchor:
          settings.outputKind === 'layers'
            ? { x: 0, y: 0 }
            : { x: outputCanvas.width / 2, y: outputCanvas.height / 2 },
        approvedBaseImageId: null,
        approval: null,
        frames: [{ index: 0, currentTaskId: null, currentImageId: null }],
      }
    })
    return {
      id: SPRITE_IDS.job,
      productionMode: 'twoD',
      sprite,
      category: 'background',
      sourceImageId: SPRITE_IDS.upload,
      status: 'pendingReview',
      scene: null,
      models: {
        text: { providerConfigId: SPRITE_IDS.provider, model: 'analysis' },
        image: { providerConfigId: SPRITE_IDS.provider, model: 'sprite-image' },
        mesh: null,
      },
      tasks: [task('analyzeSprites')],
      parts: [],
      failureReason: null,
      createdAt: now,
      completedAt: null,
      gender: null,
      partHints: [],
      requiresReview: true,
    }
  }
  function bases() {
    const sprite = job!.sprite!
    sprite.phase = 'baseReview'
    job!.status = 'pendingReview'
    sprite.assets.forEach((a, index) => {
      if (
        a.frames[0]?.currentImageId ||
        job!.tasks.some(
          (t) =>
            t.id === a.frames[0]?.currentTaskId &&
            (t.status === 'running' || t.status === 'pending'),
        )
      )
        return
      const failed = options.partialBases && index === 1
      const running = options.runningBase && index === 1
      const generated = task(
        'generateSprite',
        running ? 'running' : failed ? 'failed' : 'succeeded',
      )
      if (running) {
        sprite.phase = 'baseGeneration'
        job!.status = 'running'
      }
      job!.tasks.push(generated)
      const id = failed || running ? null : guid(4)
      a.frames = Array.from({ length: a.plan.loop ? a.plan.frameCount : 1 }, (_, index) => ({
        index,
        currentTaskId: index === 0 ? generated.id : null,
        currentImageId: index === 0 ? id : null,
      }))
      if (id)
        sprite.images.push({
          id,
          taskId: generated.id,
          assetId: a.id,
          frameIndex: 0,
          planRevision: a.planRevision,
          baseImageId: null,
          ...sprite.outputCanvas,
          contentType: 'image/png',
          createdAt: now,
        })
    })
  }
  function generate(asset: SpriteAsset, index: number, status: JobTask['status'] = 'succeeded') {
    const sprite = job!.sprite!
    const generated = task('generateSprite', status)
    job!.tasks.push(generated)
    const id = status === 'succeeded' ? guid(4) : null
    asset.frames[index] = { index, currentTaskId: generated.id, currentImageId: id }
    if (id)
      sprite.images.push({
        id,
        taskId: generated.id,
        assetId: asset.id,
        frameIndex: index,
        planRevision: asset.planRevision,
        baseImageId: index === 0 ? null : asset.approvedBaseImageId,
        ...sprite.outputCanvas,
        contentType: 'image/png',
        createdAt: now,
      })
  }
  function approveAsset(a: SpriteAsset) {
    const sprite = job!.sprite!
    a.approval = {
      planRevision: a.planRevision,
      snapshot: {
        id: a.id,
        name: a.plan.name,
        order: a.plan.order,
        fps: a.plan.fps,
        loop: a.plan.loop,
        anchor: a.anchor,
        repeat: sprite.settings.repeat,
        layout:
          sprite.settings.view === 'isometric' && sprite.settings.outputKind === 'tiles'
            ? 'diamond'
            : 'square',
        baseImageId: a.approvedBaseImageId!,
        imageIds: a.frames.map((f) => f.currentImageId!),
      },
    }
    if (sprite.assets.every((asset) => asset.approval)) sprite.phase = 'exportReady'
  }
  function approve(ids: string[]) {
    const sprite = job!.sprite!
    for (const a of sprite.assets.filter((a) => ids.includes(a.id))) {
      if (a.approvedBaseImageId === a.frames[0]!.currentImageId) continue
      a.approvedBaseImageId = a.frames[0]!.currentImageId!
      if (!a.plan.loop) approveAsset(a)
      else {
        a.frames.slice(1).forEach((frame) => generate(a, frame.index))
        sprite.phase = 'frameReview'
      }
    }
    if (sprite.assets.every((a) => a.approval)) sprite.phase = 'exportReady'
  }
  function pack(ids: string[]) {
    const sprite = job!.sprite!,
      id = guid(7),
      packed = task('packSprites')
    job!.tasks.push(packed)
    const item = {
      id,
      taskId: packed.id,
      reviewRevision: sprite.reviewRevision,
      isCurrent: true,
      createdAt: now,
      includedAssetIds: ids,
      excludedAssetIds: sprite.assets.filter((a) => !ids.includes(a.id)).map((a) => a.id),
    }
    sprite.exports.forEach((e) => (e.isCurrent = false))
    sprite.exports.push(item)
    archives.set(id, spriteZip(job!, ids, id))
    sprite.completedExportId = id
    sprite.phase = 'completed'
    job!.status = ids.length === sprite.assets.length ? 'succeeded' : 'partiallySucceeded'
  }
  if (options.seed) {
    job = make(
      options.settings ?? {
        view: 'sideView',
        outputKind: 'layers',
        tileWidth: 128,
        repeat: 'both',
      },
    )
    if (options.seed === 'analyzing') {
      job.sprite!.phase = 'analyzing'
      job.status = 'running'
      job.tasks[0]!.status = 'running'
    } else {
      if (options.seed === 'frameReview')
        job.sprite!.assets.forEach((a, index) => {
          a.plan.loop = true
          a.plan.frameCount = 8
          a.plan.fps = index === 0 ? 4 : 8
        })
      bases()
      if (options.seed === 'frameReview') {
        approve(job.sprite!.assets.map((a) => a.id))
        if (options.frameStates) {
          const a = job.sprite!.assets[0]!
          generate(a, 1, 'failed')
          generate(a, 2, 'running')
          a.frames[3] = { index: 3, currentTaskId: null, currentImageId: null }
          job.sprite!.phase = 'frameGeneration'
          job.status = 'running'
        }
      }
      if (options.seed === 'failed') {
        job.status = 'failed'
        job.sprite!.images = []
        job.sprite!.assets.forEach((a) => {
          a.frames[0]!.currentImageId = null
        })
        job.tasks.slice(1).forEach((t) => (t.status = 'failed'))
      }
      if (options.seed === 'canceled') job.status = 'canceled'
      if (options.seed === 'packFailed' || options.seed === 'completed') {
        approve(
          job.sprite!.assets.filter((a) => a.frames[0]!.currentImageId !== null).map((a) => a.id),
        )
        if (options.seed === 'completed') pack(job.sprite!.assets.map((a) => a.id))
        else {
          job.sprite!.phase = 'packaging'
          job.status = options.runningBase ? 'running' : 'partiallySucceeded'
          job.sprite!.completedExportId = guid(7)
          const failed = task('packSprites', 'failed')
          failed.failureReason = 'ZIP 크기 상한 초과'
          job.tasks.push(failed)
        }
      }
    }
  }
  await page.route('**/api/**', async (route) => {
    const request = route.request(),
      url = new URL(request.url()),
      path = url.pathname,
      method = request.method()
    if (path === '/api/providers') {
      if (providerFailure) {
        providerFailure = false
        return fail(route, 503, '공급자 연결 실패')
      }
      return ok(
        route,
        options.providersEmpty
          ? []
          : [
              {
                id: SPRITE_IDS.provider,
                displayName: 'Sprite Fake',
                kind: 'openai',
                capabilities: ['textAnalysis', 'imageGeneration'],
                apiKeyMasked: '••••fixture',
                isEnabled: true,
              },
            ],
      )
    }
    if (path === `/api/providers/${SPRITE_IDS.provider}/models`)
      return ok(route, [{ id: 'analysis', displayName: '분석 Fake' }])
    if (path === `/api/providers/${SPRITE_IDS.provider}/image-models`) {
      if (options.imageModelsError) return fail(route, 503, '이미지 모델 조회 실패')
      return ok(route, [
        {
          id: 'sprite-image',
          displayName: 'Sprite Image Fake',
          sprite: {
            supportsTransparency: !options.unsupportedModel,
            sizes: [{ width: 1024, height: 1024 }],
          },
        },
      ])
    }
    if (path === '/api/prices')
      return options.priceError ? fail(route, 503, '단가 조회 실패') : ok(route, [])
    if (path === '/api/uploads' && method === 'POST')
      return ok(
        route,
        {
          id: SPRITE_IDS.upload,
          originalName: 'source.png',
          contentType: 'image/png',
          sizeBytes: SPRITE_SOURCE_PNG.length,
        },
        201,
      )
    if (path === `/api/uploads/${SPRITE_IDS.upload}/content`)
      return route.fulfill({ status: 200, contentType: 'image/png', body: SPRITE_SOURCE_PNG })
    if (path === `/api/jobs/${SPRITE_IDS.job}`) {
      if (detailFailure) {
        detailFailure = false
        return fail(route, 503, '작업 연결 실패')
      }
      return job ? ok(route, job) : fail(route, 404, '작업을 찾을 수 없습니다')
    }
    const image = /^\/api\/jobs\/[^/]+\/sprites\/images\/([^/]+)$/.exec(path)
    if (image) {
      const sprite = job?.sprite,
        found = sprite?.images.find((i) => i.id === image[1]),
        asset = sprite?.assets.find((a) => a.id === found?.assetId)
      return sprite && asset
        ? route.fulfill({
            status: 200,
            contentType: 'image/png',
            headers: { 'Content-Disposition': `attachment; filename="sprite-${image[1]}.png"` },
            body: spritePng(sprite, asset),
          })
        : fail(route, 404, '이미지가 없습니다')
    }
    const archive = /^\/api\/jobs\/[^/]+\/sprites\/exports\/([^/]+)$/.exec(path)
    if (archive && method === 'GET') {
      const bytes = archives.get(archive[1]!)
      return bytes
        ? route.fulfill({
            status: 200,
            contentType: 'application/zip',
            headers: { 'Content-Disposition': `attachment; filename="sprites-${archive[1]}.zip"` },
            body: bytes,
          })
        : fail(route, 404, 'ZIP이 없습니다')
    }
    if (path === `/api/jobs/${SPRITE_IDS.job}/cancel` && job) {
      job.status = 'canceled'
      return ok(route, { id: job.id, status: 'canceled' }, 202)
    }
    if (path.includes(`/api/jobs/${SPRITE_IDS.job}/tasks/`) && job) {
      const failed = job.tasks.find((t) => path.includes(t.id))
      if (failed) {
        failed.status = 'succeeded'
        failed.failureReason = null
        failed.attemptCount++
        if (failed.kind === 'packSprites')
          pack(job.sprite!.assets.filter((a) => a.approval).map((a) => a.id))
        else if (job.sprite!.images.length === 0) bases()
        else {
          const a = job.sprite!.assets.find((a) =>
            a.frames.some((f) => f.currentTaskId === failed.id),
          )
          const frame = a?.frames.find((f) => f.currentTaskId === failed.id)
          if (a && frame) generate(a, frame.index)
        }
      }
      return ok(route, { id: job.id, status: job.status }, 202)
    }
    if (
      path === '/api/jobs/sprites' ||
      (path.startsWith(`/api/jobs/${SPRITE_IDS.job}/sprites/`) && method !== 'GET')
    ) {
      const body = request.postDataJSON() as Record<string, unknown>
      options.records?.push({ path, body })
      const requestId = String(body.requestId),
        fingerprint = JSON.stringify({ path, body }),
        existing = receipts.get(requestId)
      if (existing)
        return existing.fingerprint === fingerprint
          ? ok(route, existing.receipt, 202)
          : fail(route, 409, '요청 본문 충돌')
      if (path === '/api/jobs/sprites') {
        if (job) return fail(route, 409, '이미 접수된 작업')
        const settings = body.settings as SpriteSettings
        job = make(settings)
      } else {
        if (!job) return fail(route, 404, '작업이 없습니다')
        if (conflict) {
          conflict = false
          job.sprite!.reviewRevision++
          return fail(route, 409, '다른 검수자가 계획을 변경했습니다')
        }
        if (body.expectedRevision !== job.sprite!.reviewRevision)
          return fail(route, 409, '낡은 revision')
        if (path.endsWith('/plan/approve') && approvalFailure) {
          approvalFailure = false
          return fail(route, 503, '승인 응답 유실')
        }
        const sprite = job.sprite!
        let affectedIds: string[] | null = []
        if (path.endsWith('/plan')) {
          const plans = body.assets as SpriteAssetPlan[]
          affectedIds =
            plans.length !== sprite.assets.length ||
            sprite.assets.some((a) => !plans.some((p) => p.id === a.id))
              ? null
              : plans
                  .filter(
                    (p) => !sprite.assets.some((a) => JSON.stringify(a.plan) === JSON.stringify(p)),
                  )
                  .map((p) => p.id)
        } else if (path.endsWith('/plan/approve')) {
          affectedIds = sprite.assets.filter((a) => !a.frames[0]?.currentImageId).map((a) => a.id)
        } else if (path.endsWith('/base/approve')) {
          affectedIds = sprite.assets
            .filter(
              (a) =>
                (body.assetIds as string[]).includes(a.id) &&
                a.approvedBaseImageId !== a.frames[0]?.currentImageId,
            )
            .map((a) => a.id)
        } else if (path.includes('/assets/'))
          affectedIds = sprite.assets.filter((a) => path.includes(a.id)).map((a) => a.id)
        if (path.endsWith('/plan') && method === 'PUT') {
          const plans = body.assets as SpriteAssetPlan[]
          job.sprite!.assets = plans.map((plan) => {
            const existing = job!.sprite!.assets.find((a) => a.id === plan.id)
            if (existing) {
              if (JSON.stringify(existing.plan) === JSON.stringify(plan)) return existing
              const generationChanged =
                (existing.plan.loop && existing.plan.frameCount !== plan.frameCount) ||
                ['sourceBounds', 'requiresTransparency', 'loop', 'motionNotes'].some(
                  (key) =>
                    JSON.stringify(existing.plan[key as keyof SpriteAssetPlan]) !==
                    JSON.stringify(plan[key as keyof SpriteAssetPlan]),
                )
              existing.plan = plan
              existing.approval = null
              if (generationChanged) {
                existing.planRevision++
                existing.approvedBaseImageId = null
                existing.frames = Array.from(
                  { length: plan.loop ? plan.frameCount : 1 },
                  (_, index) => ({ index, currentTaskId: null, currentImageId: null }),
                )
                job!.sprite!.phase = 'planReview'
              } else if (job!.sprite!.phase !== 'planReview') job!.sprite!.phase = 'frameReview'
              return existing
            }
            return {
              id: plan.id,
              plan,
              planRevision: 1,
              anchor:
                job!.sprite!.settings.outputKind === 'layers'
                  ? { x: 0, y: 0 }
                  : {
                      x: job!.sprite!.outputCanvas.width / 2,
                      y: job!.sprite!.outputCanvas.height / 2,
                    },
              approvedBaseImageId: null,
              approval: null,
              frames: [{ index: 0, currentTaskId: null, currentImageId: null }],
            }
          })
        } else if (path.endsWith('/plan/approve')) bases()
        else if (path.endsWith('/base/approve')) approve(body.assetIds as string[])
        else if (path.endsWith('/exports')) pack(body.assetIds as string[])
        else if (path.endsWith('/approve') && path.includes('/assets/')) {
          approveAsset(job.sprite!.assets.find((a) => path.includes(a.id))!)
        } else if (path.endsWith('/regenerate')) {
          const asset = job.sprite!.assets.find((a) => path.includes(a.id))!
          const index = Number(/frames\/(\d+)\/regenerate$/.exec(path)![1])
          asset.approval = null
          if (index === 0) {
            asset.approvedBaseImageId = null
            asset.frames.forEach((f) => {
              f.currentTaskId = null
              f.currentImageId = null
            })
          }
          generate(asset, index)
          job.sprite!.phase = index === 0 ? 'baseReview' : 'frameReview'
          job.status = 'pendingReview'
        }
        if (!path.endsWith('/exports')) {
          sprite.exports.forEach((e) => {
            if (affectedIds === null || e.includedAssetIds.some((id) => affectedIds?.includes(id)))
              e.isCurrent = false
          })
          const activeFrames = sprite.assets
            .flatMap((a) => a.frames)
            .filter((f) =>
              job!.tasks.some(
                (t) =>
                  t.id === f.currentTaskId && (t.status === 'running' || t.status === 'pending'),
              ),
            )
          job.status = activeFrames.length ? 'running' : 'pendingReview'
          if (sprite.phase !== 'planReview')
            sprite.phase = activeFrames.some((f) => f.index === 0)
              ? 'baseGeneration'
              : activeFrames.length
                ? 'frameGeneration'
                : sprite.assets.every((a) => a.approval)
                  ? 'exportReady'
                  : sprite.assets.some((a) => !a.approvedBaseImageId)
                    ? 'baseReview'
                    : 'frameReview'
        }
        job.sprite!.reviewRevision++
      }
      const receipt: SpriteAccepted = {
        id: job!.id,
        status: job!.status,
        revision: job!.sprite!.reviewRevision,
        taskIds: job!.tasks.map((t) => t.id),
      }
      receipts.set(requestId, { fingerprint, receipt })
      if (path.includes('/assets/') && path.endsWith('/approve') && lostAssetApproval) {
        lostAssetApproval = false
        return route.abort('failed')
      }
      if (path === '/api/jobs/sprites' && lost) {
        lost = false
        return route.abort('failed')
      }
      return ok(route, receipt, 202)
    }
    return route.fallback()
  })
}
