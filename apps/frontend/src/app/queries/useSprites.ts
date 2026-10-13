import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '@/infra/api/client'
import {
  startSpriteJob,
  updateSpritePlan,
  approveSpritePlan,
  approveSpriteBases,
  regenerateSpriteFrame,
  approveSpriteAsset,
  exportSprites,
  createSpriteDraft,
  enhanceSpriteSource,
  saveSpriteSourcePrompt,
  generateSpriteSourceCandidate,
  selectSpriteSource,
} from '@/infra/api/spriteApi'
import type {
  SpriteAccepted,
  SpriteMutationContext,
  StartSpriteJobInput,
  CreateSpriteDraftInput,
} from '@/domain/sprites/types'
import { queryKeys } from './keys'

// 사용자 동작의 requestId·revision은 variables에 보존, 재전송은 같은 variables 사용
function useSpriteMutation<T extends SpriteMutationContext>(
  mutationFn: (input: T) => Promise<SpriteAccepted>,
) {
  const queryClient = useQueryClient()
  const invalidate = (jobId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.job(jobId) })
    void queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
  }
  return useMutation({
    mutationFn: (input: T) => mutationFn(input),
    retry: false,
    onSuccess: (_receipt, input) => invalidate(input.jobId),
    onError: (error, input) => {
      if (error instanceof ApiError && error.status === 409) invalidate(input.jobId)
    },
  })
}
// 접수 응답의 id 가 새 작업이므로 상세와 목록을 함께 무효화
function useSpriteStart<T>(mutationFn: (input: T) => Promise<SpriteAccepted>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: T) => mutationFn(input),
    retry: false,
    onSuccess: (receipt) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.job(receipt.id) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
    },
    onError: (error) => {
      if (error instanceof ApiError && error.status === 409) {
        void queryClient.invalidateQueries({ queryKey: queryKeys.jobLists() })
      }
    },
  })
}
export function useStartSpriteJob() {
  return useSpriteStart((input: StartSpriteJobInput) => startSpriteJob(input))
}
export function useCreateSpriteDraft() {
  return useSpriteStart((input: CreateSpriteDraftInput) => createSpriteDraft(input))
}
export function useUpdateSpritePlan() {
  return useSpriteMutation(updateSpritePlan)
}
export function useApproveSpritePlan() {
  return useSpriteMutation(approveSpritePlan)
}
export function useApproveSpriteBases() {
  return useSpriteMutation(approveSpriteBases)
}
export function useRegenerateSpriteFrame() {
  return useSpriteMutation(regenerateSpriteFrame)
}
export function useApproveSpriteAsset() {
  return useSpriteMutation(approveSpriteAsset)
}
export function useExportSprites() {
  return useSpriteMutation(exportSprites)
}
export function useEnhanceSpriteSource() {
  return useSpriteMutation(enhanceSpriteSource)
}
export function useSaveSpriteSourcePrompt() {
  return useSpriteMutation(saveSpriteSourcePrompt)
}
export function useGenerateSpriteSourceCandidate() {
  return useSpriteMutation(generateSpriteSourceCandidate)
}
export function useSelectSpriteSource() {
  return useSpriteMutation(selectSpriteSource)
}
