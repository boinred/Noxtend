import type { Bounds, Job, JobStatus, JobSummary } from '../job/types'

export type ProductionMode = 'threeD' | 'twoD'
export type SpriteView = 'sideView' | 'topDown' | 'isometric'
export type SpriteOutputKind = 'layers' | 'tiles'
export type SpriteRepeat = 'x' | 'y' | 'both'
export type SpriteTileLayout = 'square' | 'diamond'
export type SpritePhase =
  | 'analyzing'
  | 'planReview'
  | 'baseGeneration'
  | 'baseReview'
  | 'frameGeneration'
  | 'frameReview'
  | 'exportReady'
  | 'packaging'
  | 'completed'

export interface SpriteCanvas {
  width: number
  height: number
}
export interface SpriteAnchor {
  x: number
  y: number
}
export interface SpriteTransform {
  scale: number
  offsetX: number
  offsetY: number
}
export interface SpriteSettings {
  view: SpriteView
  outputKind: SpriteOutputKind
  tileWidth: 64 | 128 | 256
  repeat: SpriteRepeat
}
export interface SpriteAssetPlan {
  id: string
  name: string
  order: number
  sourceBounds: Bounds
  requiresTransparency: boolean
  loop: boolean
  frameCount: number
  fps: number
  motionNotes: string
}
export interface SpriteFrame {
  index: number
  currentTaskId: string | null
  currentImageId: string | null
}
export interface SpriteApprovedAsset {
  id: string
  name: string
  order: number
  fps: number
  loop: boolean
  anchor: SpriteAnchor
  repeat: SpriteRepeat
  layout: SpriteTileLayout
  baseImageId: string
  imageIds: string[]
}
export interface SpriteAssetApproval {
  planRevision: number
  snapshot: SpriteApprovedAsset
}
export interface SpriteAsset {
  id: string
  plan: SpriteAssetPlan
  planRevision: number
  anchor: SpriteAnchor
  approvedBaseImageId: string | null
  approval: SpriteAssetApproval | null
  frames: SpriteFrame[]
}
export interface SpriteImage {
  id: string
  taskId: string
  assetId: string
  frameIndex: number
  planRevision: number
  baseImageId: string | null
  width: number
  height: number
  contentType: 'image/png'
  createdAt: string
}
export interface SpriteExport {
  id: string
  taskId: string
  reviewRevision: number
  isCurrent: boolean
  createdAt: string
  includedAssetIds: string[]
  excludedAssetIds: string[]
}
export interface SpriteState {
  settings: SpriteSettings
  sourceCanvas: SpriteCanvas
  generationCanvas: SpriteCanvas
  outputCanvas: SpriteCanvas
  transform: SpriteTransform
  phase: SpritePhase
  reviewRevision: number
  completedExportId: string | null
  assets: SpriteAsset[]
  images: SpriteImage[]
  exports: SpriteExport[]
}
export interface SpriteSummary {
  phase: SpritePhase
  assetCount: number
  approvedAssetCount: number
  imageCount: number
  exportCount: number
}
export interface SpriteAccepted {
  id: string
  status: JobStatus
  revision: number
  taskIds: string[]
}
export interface SpriteMutationContext {
  jobId: string
  requestId: string
  expectedRevision: number
}
export type UpdateSpritePlanInput = SpriteMutationContext & { assets: SpriteAssetPlan[] }
export type SpriteAssetsInput = SpriteMutationContext & { assetIds: string[] }
export type SpriteAssetInput = SpriteMutationContext & { assetId: string }
export type RegenerateSpriteFrameInput = SpriteAssetInput & { index: number }

interface SpriteStartSettings {
  view: SpriteView
  outputKind: SpriteOutputKind
  tileWidth?: 64 | 128 | 256
  repeat?: SpriteRepeat
}
interface SpriteStartCommon {
  requestId: string
  providerConfigId: string
  model: string
  imageProviderConfigId: string
  imageModel: string
  settings: SpriteStartSettings
}
export type StartSpriteJobInput = SpriteStartCommon &
  (
    | { uploadId: string; sourceJobId?: never; sourceGeneratedImageId?: never }
    | { uploadId?: never; sourceJobId: string; sourceGeneratedImageId: string }
  )

const PHASES: readonly SpritePhase[] = [
  'analyzing',
  'planReview',
  'baseGeneration',
  'baseReview',
  'frameGeneration',
  'frameReview',
  'exportReady',
  'packaging',
  'completed',
]
const STATUSES: readonly JobStatus[] = [
  'pending',
  'running',
  'pendingReview',
  'succeeded',
  'partiallySucceeded',
  'failed',
  'canceled',
]

function record(value: unknown): Record<string, unknown> {
  requireShape(typeof value === 'object' && value !== null && !Array.isArray(value))
  return value as Record<string, unknown>
}
function requireShape(valid: boolean): asserts valid {
  if (!valid) throw new Error('2D 작업 응답 계약이 유효하지 않습니다')
}
function text(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0
}
function nullableId(value: unknown): boolean {
  return value === null || text(value)
}
function finite(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}
function integer(value: unknown, min = 0): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= min
}
function strings(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(text)
}
function member(value: unknown, values: readonly string[]): boolean {
  return typeof value === 'string' && values.includes(value)
}
function items(value: unknown, read: (item: unknown) => void): void {
  requireShape(Array.isArray(value))
  value.forEach(read)
}
function readCanvas(value: unknown): void {
  const canvas = record(value)
  requireShape(integer(canvas.width, 1) && integer(canvas.height, 1))
}
function readAnchor(value: unknown): void {
  const anchor = record(value)
  requireShape(finite(anchor.x) && finite(anchor.y))
}
function readPlan(value: unknown): void {
  const plan = record(value)
  requireShape(
    text(plan.id) &&
      text(plan.name) &&
      integer(plan.order, -2147483648) &&
      typeof plan.requiresTransparency === 'boolean' &&
      typeof plan.loop === 'boolean' &&
      integer(plan.frameCount, -2147483648) &&
      integer(plan.fps, 1) &&
      plan.fps <= 30 &&
      typeof plan.motionNotes === 'string',
  )
  const bounds = record(plan.sourceBounds)
  requireShape(finite(bounds.x) && finite(bounds.y) && finite(bounds.w) && finite(bounds.h))
}
function readApprovedAsset(value: unknown): void {
  const asset = record(value)
  requireShape(
    text(asset.id) &&
      text(asset.name) &&
      integer(asset.order, -2147483648) &&
      integer(asset.fps, 1) &&
      typeof asset.loop === 'boolean' &&
      member(asset.repeat, ['x', 'y', 'both']) &&
      member(asset.layout, ['square', 'diamond']) &&
      text(asset.baseImageId) &&
      strings(asset.imageIds),
  )
  readAnchor(asset.anchor)
}
function readAsset(value: unknown): void {
  const asset = record(value)
  requireShape(
    text(asset.id) && integer(asset.planRevision) && nullableId(asset.approvedBaseImageId),
  )
  readPlan(asset.plan)
  readAnchor(asset.anchor)
  if (asset.approval !== null) {
    const approval = record(asset.approval)
    requireShape(integer(approval.planRevision))
    readApprovedAsset(approval.snapshot)
  }
  items(asset.frames, (value) => {
    const frame = record(value)
    requireShape(
      integer(frame.index) && nullableId(frame.currentTaskId) && nullableId(frame.currentImageId),
    )
  })
}
function readImage(value: unknown): void {
  const image = record(value)
  requireShape(
    text(image.id) &&
      text(image.taskId) &&
      text(image.assetId) &&
      integer(image.frameIndex) &&
      integer(image.planRevision) &&
      nullableId(image.baseImageId) &&
      image.contentType === 'image/png' &&
      text(image.createdAt),
  )
  readCanvas(image)
}
function readExport(value: unknown): void {
  const exported = record(value)
  requireShape(
    text(exported.id) &&
      text(exported.taskId) &&
      integer(exported.reviewRevision) &&
      typeof exported.isCurrent === 'boolean' &&
      text(exported.createdAt) &&
      strings(exported.includedAssetIds) &&
      strings(exported.excludedAssetIds),
  )
}
function readState(value: unknown): void {
  const state = record(value)
  const settings = record(state.settings)
  requireShape(
    member(settings.view, ['sideView', 'topDown', 'isometric']) &&
      member(settings.outputKind, ['layers', 'tiles']) &&
      member(settings.repeat, ['x', 'y', 'both']) &&
      [64, 128, 256].includes(settings.tileWidth as number),
  )
  readCanvas(state.sourceCanvas)
  readCanvas(state.generationCanvas)
  readCanvas(state.outputCanvas)
  const transform = record(state.transform)
  requireShape(
    finite(transform.scale) &&
      transform.scale > 0 &&
      finite(transform.offsetX) &&
      finite(transform.offsetY) &&
      member(state.phase, PHASES) &&
      integer(state.reviewRevision) &&
      nullableId(state.completedExportId),
  )
  items(state.assets, readAsset)
  items(state.images, readImage)
  items(state.exports, readExport)
}
function readModeAndSprite(raw: Record<string, unknown>): {
  productionMode: ProductionMode
  sprite: unknown
} {
  const productionMode = raw.productionMode === undefined ? 'threeD' : raw.productionMode
  requireShape(member(productionMode, ['threeD', 'twoD']))
  // 구형 응답의 신규 필드 누락만 호환
  const sprite = raw.productionMode === undefined && raw.sprite === undefined ? null : raw.sprite
  requireShape(
    productionMode === 'threeD' ? sprite === null : sprite !== null && sprite !== undefined,
  )
  return { productionMode: productionMode as ProductionMode, sprite }
}
export function readSpriteJob(raw: unknown): Job {
  const job = record(raw)
  const fields = readModeAndSprite(job)
  if (fields.productionMode === 'twoD') readState(fields.sprite)
  return { ...job, ...fields } as unknown as Job
}
export function readSpriteSummary(raw: unknown): JobSummary {
  const job = record(raw)
  const fields = readModeAndSprite(job)
  if (fields.productionMode === 'twoD') {
    const summary = record(fields.sprite)
    requireShape(
      member(summary.phase, PHASES) &&
        integer(summary.assetCount) &&
        integer(summary.approvedAssetCount) &&
        integer(summary.imageCount) &&
        integer(summary.exportCount),
    )
  }
  return { ...job, ...fields } as unknown as JobSummary
}
export function readSpriteAccepted(raw: unknown): SpriteAccepted {
  const receipt = record(raw)
  requireShape(
    text(receipt.id) &&
      member(receipt.status, STATUSES) &&
      integer(receipt.revision) &&
      strings(receipt.taskIds),
  )
  return receipt as unknown as SpriteAccepted
}
