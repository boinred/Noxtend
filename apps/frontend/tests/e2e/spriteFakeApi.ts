import { assetOutputSize, cellWidthOf } from '../../src/domain/sprites/rules'
import type { Page, Route } from '@playwright/test'
import { crc32, deflateSync } from 'node:zlib'
import type { Job, JobTask } from '../../src/domain/job/types'
import type {
  SpriteState,
  SpriteSettings,
  SpriteAssetPlan,
  SpriteAsset,
  SpriteAccepted,
  SpriteSourceDraft,
  SpriteCanvas,
  SpriteGrid,
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
    | 'planReview'
    | 'baseReview'
    | 'failed'
    | 'canceled'
    | 'packFailed'
    | 'completed'
    | 'analyzing'
    | 'frameReview'
    | 'sourceDraft'
  // sourceDraft seed 초기값
  sourceCandidates?: number
  sourceDescription?: string
  sourcePrompt?: string | null
  // 실행 중 원본 공정은 작업 GET이 이 횟수에 닿으면 끝남, 테스트 중 변경 가능
  sourceTaskGets?: number
  // 순서대로 소비, 해당 종류의 다음 원본 공정 실패
  sourceFailures?: JobTask['kind'][]
  // 다른 탭이 먼저 생성한 상황
  sourceBusyOnce?: boolean
  textModelsEmpty?: boolean
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
  tileOnly?: boolean
  reorderedPlans?: boolean
  settings?: SpriteSettings
  // 재구성 부하 경계용 원본 크기·격자
  sourceCanvas?: SpriteCanvas
  grid?: SpriteGrid
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
  const { width, height } = asset.canvas
  const diamond = asset.plan.role === 'tile' && sprite.settings.view === 'isometric'
  const inside = diamond
    ? Math.abs((x + 0.5 - width / 2) / (width / 2)) +
        Math.abs((y + 0.5 - height / 2) / (height / 2)) <=
      1
    : !asset.plan.requiresTransparency ||
      (x > width / 5 && x < (width * 4) / 5 && y > height / 5 && y < (height * 4) / 5)
  return inside ? [70 + asset.plan.order * 35, 125, 100, 255] : [0, 0, 0, 0]
}
export function spritePng(sprite: SpriteState, asset: SpriteAsset) {
  return png(asset.canvas.width, asset.canvas.height, (x, y) => framePixel(sprite, asset, x, y))
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
    const basePath = `${{ layer: 'layers', tile: 'tiles', object: 'objects', backdrop: 'backdrop' }[a.plan.role]}/asset-${a.id}.png`
    const sheetPath = `sheets/asset-${a.id}-000.png`
    const { width, height } = a.canvas
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
    schemaVersion: 2,
    exportId,
    reviewRevision: sprite.reviewRevision,
    view: sprite.settings.view,
    outputKind: sprite.settings.outputKind,
    sourceCanvas: sprite.sourceCanvas,
    outputCanvas: sprite.outputCanvas,
    grid:
      sprite.settings.outputKind === 'tiles'
        ? {
            cellWidth: cellWidthOf(
              sprite.grid,
              sprite.assets.map((a) => a.plan),
              sprite.settings,
            )!,
          }
        : null,
    tileWidth: sprite.settings.tileWidth,
    assetDetails: assets.map((a) => ({
      id: a.id,
      role: a.plan.role,
      canvas: a.canvas,
      scale: a.scale,
      sourceBounds: a.plan.sourceBounds,
    })),
    assets: assets.map((a) => a.approval!.snapshot),
    excludedAssetIds: sprite.assets.filter((a) => !included.includes(a.id)).map((a) => a.id),
  }
  files.push({
    name: 'manifest.json',
    data: Buffer.from(
      JSON.stringify({
        schemaVersion: 2,
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
function fail(route: Route, status: number, message: string, code?: string) {
  return route.fulfill({
    status,
    contentType: 'application/json',
    json: {
      data: null,
      error: {
        code: code ?? (status === 409 ? 'SPRITE_REVISION_CONFLICT' : 'FAKE_ERROR'),
        message,
      },
    },
  })
}
const isSourceTask = (task: JobTask) =>
  task.kind === 'enhanceSpriteSource' || task.kind === 'generateSpriteSource'

export async function installSpriteFakeApi(page: Page, options: SpriteFakeOptions) {
  let sequence = 1
  const guid = (type: number) =>
    `${type}0000000-0000-4000-8000-${String(sequence++).padStart(12, '0')}`
  let job: Job | null = null
  const receipts = new Map<string, { fingerprint: string; receipt: SpriteAccepted }>()
  const archives = new Map<string, Buffer>()
  const exportInputs = new Map<string, string>()
  function exportFingerprint(sprite: SpriteState, ids: string[]) {
    return JSON.stringify({
      grid:
        sprite.settings.outputKind === 'tiles'
          ? cellWidthOf(
              sprite.grid,
              sprite.assets.map((a) => a.plan),
              sprite.settings,
            )
          : null,
      assets: sprite.assets
        .filter((a) => ids.includes(a.id))
        .map((a) => ({
          id: a.id,
          role: a.plan.role,
          canvas: a.canvas,
          scale: a.scale,
          sourceBounds: a.plan.sourceBounds,
          approval: a.approval,
        })),
      excludedAssetIds: sprite.assets.filter((a) => !ids.includes(a.id)).map((a) => a.id),
    })
  }
  const storedImages = new Set<string>()
  const sourceGets = new Map<string, number>()
  const sourceInputs = new Map<string, string>()
  let busyOnce = options.sourceBusyOnce ?? false
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
      grid: options.grid ?? null,
      sourceCanvas: options.sourceCanvas ?? { width: 320, height: 180 },
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
      source: null,
    }
    sprite.assets = (settings.outputKind === 'tiles' ? [0, 1, 2] : [0, 1]).map((order) => {
      const role =
        settings.outputKind === 'layers'
          ? 'layer'
          : options.tileOnly
            ? 'tile'
            : order === 0
              ? 'backdrop'
              : order === 1
                ? 'tile'
                : 'object'
      const plan: SpriteAssetPlan = {
        id: guid(6),
        name: order === 2 ? '오브젝트 바위' : order ? '전경 나무' : '후경 지면',
        order,
        sourceBounds: {
          x: 0,
          y: 0,
          w: role === 'object' ? 0.5 : 1,
          h: role === 'object' ? 0.5 : 1,
        },
        // 오브젝트는 ROI 와 다른 두 자리
        placements:
          role === 'object'
            ? [
                { x: 0.5, y: 0, w: 0.25, h: 0.5 },
                { x: 0.25, y: 0.5, w: 0.25, h: 0.5 },
              ]
            : null,
        requiresTransparency:
          role === 'object' ||
          (role === 'layer' && order !== 0) ||
          (role === 'tile' && settings.view === 'isometric'),
        loop: false,
        frameCount: 8,
        fps: 8,
        motionNotes: '',
        role,
      }
      const size = assetOutputSize(settings, sprite.sourceCanvas, 1, plan)
      if ('error' in size) throw new Error(size.error)
      const id = plan.id
      return {
        id,
        plan,
        ...size,
        planRevision: 1,
        anchor:
          role === 'layer' || role === 'backdrop'
            ? { x: 0, y: 0 }
            : { x: size.canvas.width / 2, y: size.canvas.height / 2 },
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
  function draft(settings: SpriteSettings, description: string): Job {
    const created = make(settings)
    const sprite = created.sprite!
    sprite.phase = 'sourceDraft'
    sprite.sourceCanvas = { width: 1024, height: 1024 }
    sprite.assets = []
    sprite.source = { description, prompt: null, candidates: [] }
    created.sourceImageId = null
    created.status = 'pendingReview'
    created.tasks = []
    return created
  }
  function startSource(kind: JobTask['kind'], input: string) {
    const started = task(kind, 'running')
    job!.tasks.push(started)
    sourceInputs.set(started.id, input)
    job!.status = 'running'
  }
  function candidate(source: SpriteSourceDraft, prompt: string) {
    source.candidates.push({ id: guid(9), prompt, width: 320, height: 180, createdAt: now })
  }
  // 작업 GET마다 실행 중 원본 공정 진행, 결과 반영은 revision 증가
  function settleSource() {
    const sprite = job?.sprite
    if (!sprite?.source || sprite.phase !== 'sourceDraft') return
    for (const t of job!.tasks.filter((t) => isSourceTask(t) && t.status === 'running')) {
      const gets = (sourceGets.get(t.id) ?? 0) + 1
      sourceGets.set(t.id, gets)
      if (gets < (options.sourceTaskGets ?? 1)) continue
      const failed = options.sourceFailures?.[0] === t.kind
      if (failed) options.sourceFailures!.shift()
      const input = sourceInputs.get(t.id)!
      t.completedAt = now
      t.status = failed ? 'failed' : 'succeeded'
      t.failureReason = failed
        ? `Fake ${t.kind === 'enhanceSpriteSource' ? '보완' : '원본 생성'} 실패`
        : null
      if (!failed && t.kind === 'enhanceSpriteSource')
        sprite.source.prompt = `${input} · 보완 프롬프트`
      if (!failed && t.kind === 'generateSpriteSource') candidate(sprite.source, input)
      job!.status = 'pendingReview'
      sprite.reviewRevision++
    }
  }
  function sourceCommand(path: string, body: Record<string, unknown>) {
    const sprite = job!.sprite!,
      source = sprite.source
    if (sprite.phase !== 'sourceDraft' || !source)
      return { code: 'SPRITE_NOT_READY', message: '원본 준비 단계가 아닙니다' }
    if (busyOnce) {
      busyOnce = false
      startSource('generateSpriteSource', source.prompt ?? '')
      sprite.reviewRevision++
    }
    if (
      job!.tasks.some((t) => isSourceTask(t) && (t.status === 'running' || t.status === 'pending'))
    )
      return { code: 'SPRITE_BUSY', message: '원본 준비 공정이 실행 중입니다' }
    if (path.endsWith('/source/enhance')) {
      source.description = String(body.description)
      startSource('enhanceSpriteSource', source.description)
    } else if (path.endsWith('/source/prompt')) source.prompt = String(body.prompt)
    else if (path.endsWith('/source/generate')) {
      if (source.candidates.length >= 8)
        return { code: 'SPRITE_SOURCE_LIMIT', message: '원본 후보는 최대 8장입니다' }
      source.prompt = String(body.prompt)
      startSource('generateSpriteSource', source.prompt)
    } else if (path.endsWith('/source/select')) {
      if (!source.candidates.some((c) => c.id === body.candidateId))
        return { code: 'SPRITE_NOT_READY', message: '후보가 없습니다' }
      const analyzed = make(sprite.settings)
      analyzed.sprite!.source = source
      analyzed.sprite!.reviewRevision = sprite.reviewRevision
      analyzed.sourceImageId = guid(3)
      storedImages.add(analyzed.sourceImageId)
      analyzed.tasks = [...job!.tasks, { ...analyzed.tasks[0]!, ordinal: job!.tasks.length }]
      job = analyzed
    }
    return null
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
          ...a.canvas,
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
        ...asset.canvas,
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
          sprite.settings.view === 'isometric' && a.plan.role === 'tile' ? 'diamond' : 'square',
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
    exportInputs.set(id, exportFingerprint(sprite, ids))
    const active = sprite.assets
      .flatMap((a) => a.frames)
      .filter((f) =>
        job!.tasks.some(
          (t) => t.id === f.currentTaskId && (t.status === 'running' || t.status === 'pending'),
        ),
      )
    if (active.length) {
      sprite.phase = active.some((f) => f.index === 0) ? 'baseGeneration' : 'frameGeneration'
      job!.status = 'running'
    } else {
      sprite.completedExportId = id
      sprite.phase = 'completed'
      job!.status = ids.length === sprite.assets.length ? 'succeeded' : 'partiallySucceeded'
    }
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
    if (options.seed === 'sourceDraft') {
      job = draft(job.sprite!.settings, options.sourceDescription ?? '항구 마을')
      const source = job.sprite!.source!
      job.tasks.push(task('enhanceSpriteSource'))
      source.prompt =
        options.sourcePrompt === undefined ? '항구 마을 · 보완 프롬프트' : options.sourcePrompt
      for (let i = 0; i < (options.sourceCandidates ?? 0); i++) {
        job.tasks.push(task('generateSpriteSource'))
        candidate(source, `${source.prompt ?? '직접 입력'} #${i + 1}`)
      }
    } else if (options.seed === 'analyzing') {
      job.sprite!.phase = 'analyzing'
      job.status = 'running'
      job.tasks[0]!.status = 'running'
    } else if (options.seed !== 'planReview') {
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
      return ok(
        route,
        options.textModelsEmpty ? [] : [{ id: 'analysis', displayName: '분석 Fake' }],
      )
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
    const uploadContent = /^\/api\/uploads\/([^/]+)\/content$/.exec(path)
    if (
      uploadContent &&
      (uploadContent[1] === SPRITE_IDS.upload || storedImages.has(uploadContent[1]!))
    )
      return route.fulfill({ status: 200, contentType: 'image/png', body: SPRITE_SOURCE_PNG })
    if (path === `/api/jobs/${SPRITE_IDS.job}`) {
      if (detailFailure) {
        detailFailure = false
        return fail(route, 503, '작업 연결 실패')
      }
      settleSource()
      return job
        ? ok(route, {
            ...job,
            sprite: {
              ...job.sprite!,
              assets: options.reorderedPlans
                ? [...job.sprite!.assets].reverse().map((asset) => ({
                    ...asset,
                    plan: Object.fromEntries(
                      Object.entries(asset.plan).reverse(),
                    ) as SpriteAssetPlan,
                  }))
                : job.sprite!.assets,
              completedExportId: job.sprite!.exports.some(
                (item) => item.id === job!.sprite!.completedExportId && item.isCurrent,
              )
                ? job.sprite!.completedExportId
                : null,
            },
          })
        : fail(route, 404, '작업을 찾을 수 없습니다')
    }
    const sourceImage = /^\/api\/jobs\/[^/]+\/sprites\/source\/candidates\/([^/]+)$/.exec(path)
    if (sourceImage)
      return job?.sprite?.source?.candidates.some((c) => c.id === sourceImage[1])
        ? route.fulfill({ status: 200, contentType: 'image/png', body: SPRITE_SOURCE_PNG })
        : fail(route, 404, '후보가 없습니다')
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
      if (failed && isSourceTask(failed)) {
        failed.status = 'running'
        failed.failureReason = null
        failed.completedAt = null
        failed.attemptCount++
        sourceGets.delete(failed.id)
        job.status = 'running'
      } else if (failed) {
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
      path === '/api/jobs/sprites/drafts' ||
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
      if (path === '/api/jobs/sprites' || path === '/api/jobs/sprites/drafts') {
        if (job) return fail(route, 409, '이미 접수된 작업')
        const settings = body.settings as SpriteSettings
        if (path === '/api/jobs/sprites') job = make(settings)
        else {
          job = draft(settings, String(body.description))
          startSource('enhanceSpriteSource', String(body.description))
        }
      } else {
        if (!job) return fail(route, 404, '작업이 없습니다')
        if (conflict) {
          conflict = false
          job.sprite!.reviewRevision++
          return fail(route, 409, '다른 검수자가 계획을 변경했습니다')
        }
        if (body.expectedRevision !== job.sprite!.reviewRevision)
          return fail(route, 409, '낡은 revision')
        if (path.includes('/sprites/source/')) {
          const rejected = sourceCommand(path, body)
          if (rejected) return fail(route, 409, rejected.message, rejected.code)
        }
        if (path.endsWith('/plan/approve') && approvalFailure) {
          approvalFailure = false
          return fail(route, 503, '승인 응답 유실')
        }
        const sprite = job.sprite!
        let placementsOnly = false
        if (path.endsWith('/plan') && method === 'PUT') {
          const plans = body.assets as SpriteAssetPlan[]
          const grid = (body.grid ?? null) as SpriteState['grid']
          if (
            grid !== null &&
            (sprite.settings.outputKind === 'layers' ||
              !Number.isFinite(grid.cellWidth) ||
              grid.cellWidth <= 0 ||
              grid.cellWidth > 1)
          )
            return fail(
              route,
              400,
              '격자는 타일 작업의 0 초과 1 이하 칸 너비여야 합니다',
              'SPRITE_PLAN_INVALID',
            )
          const backmost = Math.min(...plans.map((plan) => plan.order))
          if (
            plans.filter((plan) => plan.role === 'backdrop').length > 1 ||
            plans.some((plan) => plan.role === 'backdrop' && plan.order !== backmost)
          )
            return fail(
              route,
              400,
              '배경은 최대 하나이며 가장 뒤에 있어야 합니다',
              'SPRITE_PLAN_INVALID',
            )
          if (
            plans.some((plan) =>
              sprite.settings.outputKind === 'layers'
                ? plan.role !== 'layer'
                : !['tile', 'object', 'backdrop'].includes(plan.role),
            )
          )
            return fail(
              route,
              400,
              '결과 유형에 허용되지 않는 에셋 역할입니다',
              'SPRITE_PLAN_INVALID',
            )
          if (
            plans.some(
              (plan) =>
                ((plan.role === 'object' ||
                  (plan.role === 'tile' && sprite.settings.view === 'isometric') ||
                  (plan.role === 'layer' && plan.order !== backmost)) &&
                  !plan.requiresTransparency) ||
                (plan.role === 'backdrop' && plan.requiresTransparency),
            )
          )
            return fail(route, 400, '에셋 역할의 투명도가 유효하지 않습니다', 'SPRITE_PLAN_INVALID')
          const cell = cellWidthOf(grid, plans, sprite.settings)
          const sizes = plans.map((plan) =>
            assetOutputSize(sprite.settings, sprite.sourceCanvas, cell, plan),
          )
          const invalid = sizes.find((size) => 'error' in size)
          if (invalid && 'error' in invalid)
            return fail(route, 400, invalid.error, 'SPRITE_PLAN_INVALID')
          // 자리만 바뀐 저장은 승인·이미지·단계 유지, revision 만 증가
          const samePlan = (a: SpriteAssetPlan, b: SpriteAssetPlan) =>
            JSON.stringify({ ...a, placements: null }) ===
            JSON.stringify({ ...b, placements: null })
          placementsOnly =
            JSON.stringify(sprite.grid) === JSON.stringify(grid) &&
            sprite.assets.length === plans.length &&
            plans.every((plan) => sprite.assets.some((a) => samePlan(a.plan, plan))) &&
            plans.some(
              (plan) => !sprite.assets.some((a) => JSON.stringify(a.plan) === JSON.stringify(plan)),
            )
          sprite.grid = grid
          job.sprite!.assets = plans.map((plan, index) => {
            const size = sizes[index]!
            if ('error' in size) throw new Error(size.error)
            const anchor =
              plan.role === 'layer' || plan.role === 'backdrop'
                ? { x: 0, y: 0 }
                : { x: size.canvas.width / 2, y: size.canvas.height / 2 }
            const existing = job!.sprite!.assets.find((a) => a.id === plan.id)
            if (existing) {
              if (
                JSON.stringify(existing.plan) === JSON.stringify(plan) &&
                JSON.stringify(existing.canvas) === JSON.stringify(size.canvas)
              ) {
                existing.scale = size.scale
                return existing
              }
              if (placementsOnly) {
                existing.plan = plan
                return existing
              }
              const generationChanged =
                JSON.stringify(existing.canvas) !== JSON.stringify(size.canvas) ||
                (existing.plan.loop && existing.plan.frameCount !== plan.frameCount) ||
                ['role', 'sourceBounds', 'requiresTransparency', 'loop', 'motionNotes'].some(
                  (key) =>
                    JSON.stringify(existing.plan[key as keyof SpriteAssetPlan]) !==
                    JSON.stringify(plan[key as keyof SpriteAssetPlan]),
                )
              existing.canvas = size.canvas
              existing.scale = size.scale
              existing.anchor = anchor
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
              ...size,
              anchor,
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
        if (!path.endsWith('/exports') && !path.includes('/sprites/source/') && !placementsOnly) {
          sprite.exports.forEach((e) => {
            e.isCurrent =
              e.isCurrent &&
              exportInputs.get(e.id) === exportFingerprint(sprite, e.includedAssetIds)
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
