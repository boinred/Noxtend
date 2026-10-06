import { apiRequest, apiUrl } from './client'
import { readSpriteAccepted } from '@/domain/sprites/types'
import type {
  SpriteAccepted,
  SpriteMutationContext,
  StartSpriteJobInput,
  UpdateSpritePlanInput,
  SpriteAssetsInput,
  SpriteAssetInput,
  RegenerateSpriteFrameInput,
} from '@/domain/sprites/types'

export async function startSpriteJob(
  input: StartSpriteJobInput,
  signal?: AbortSignal,
): Promise<SpriteAccepted> {
  const { requestId, providerConfigId, model, imageProviderConfigId, imageModel, settings } = input
  const { view, outputKind, tileWidth, repeat } = settings
  const source =
    input.uploadId !== undefined
      ? { uploadId: input.uploadId }
      : { sourceJobId: input.sourceJobId, sourceGeneratedImageId: input.sourceGeneratedImageId }
  return readSpriteAccepted(
    await apiRequest<unknown>('/api/jobs/sprites', {
      method: 'POST',
      signal,
      body: {
        requestId,
        ...source,
        providerConfigId,
        model,
        imageProviderConfigId,
        imageModel,
        settings: { view, outputKind, tileWidth, repeat },
      },
    }),
  )
}
function mutationBody(input: SpriteMutationContext) {
  return { requestId: input.requestId, expectedRevision: input.expectedRevision }
}
async function mutate(
  input: SpriteMutationContext,
  path: string,
  body: unknown,
  method = 'POST',
  signal?: AbortSignal,
): Promise<SpriteAccepted> {
  return readSpriteAccepted(
    await apiRequest<unknown>(`/api/jobs/${input.jobId}/sprites/${path}`, { method, body, signal }),
  )
}
export function updateSpritePlan(
  input: UpdateSpritePlanInput,
  signal?: AbortSignal,
): Promise<SpriteAccepted> {
  const assets = input.assets.map(
    ({
      id,
      name,
      order,
      sourceBounds,
      requiresTransparency,
      loop,
      frameCount,
      fps,
      motionNotes,
    }) => {
      const { x, y, w, h } = sourceBounds
      return {
        id,
        name,
        order,
        sourceBounds: { x, y, w, h },
        requiresTransparency,
        loop,
        frameCount,
        fps,
        motionNotes,
      }
    },
  )
  return mutate(input, 'plan', { ...mutationBody(input), assets }, 'PUT', signal)
}
export function approveSpritePlan(
  input: SpriteMutationContext,
  signal?: AbortSignal,
): Promise<SpriteAccepted> {
  return mutate(input, 'plan/approve', mutationBody(input), 'POST', signal)
}
export function approveSpriteBases(
  input: SpriteAssetsInput,
  signal?: AbortSignal,
): Promise<SpriteAccepted> {
  return mutate(
    input,
    'base/approve',
    { ...mutationBody(input), assetIds: input.assetIds },
    'POST',
    signal,
  )
}
export function regenerateSpriteFrame(
  input: RegenerateSpriteFrameInput,
  signal?: AbortSignal,
): Promise<SpriteAccepted> {
  return mutate(
    input,
    `assets/${input.assetId}/frames/${input.index}/regenerate`,
    mutationBody(input),
    'POST',
    signal,
  )
}
export function approveSpriteAsset(
  input: SpriteAssetInput,
  signal?: AbortSignal,
): Promise<SpriteAccepted> {
  return mutate(input, `assets/${input.assetId}/approve`, mutationBody(input), 'POST', signal)
}
export function exportSprites(
  input: SpriteAssetsInput,
  signal?: AbortSignal,
): Promise<SpriteAccepted> {
  return mutate(
    input,
    'exports',
    { ...mutationBody(input), assetIds: input.assetIds },
    'POST',
    signal,
  )
}
export function spriteImageUrl(jobId: string, imageId: string): string {
  return apiUrl(`/api/jobs/${jobId}/sprites/images/${imageId}`)
}
export function spriteExportUrl(jobId: string, exportId: string): string {
  return apiUrl(`/api/jobs/${jobId}/sprites/exports/${exportId}`)
}
