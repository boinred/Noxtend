import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { collectPriceUpdate, applyPriceUpdate } from '@/infra/api/priceUpdateApi'
import type { PriceApplyInput } from '@/domain/tuning/priceUpdate'
import { queryKeys } from './keys'
import { listProviders } from '@/infra/api/providerApi'

export function usePriceUpdates() {
  const client = useQueryClient()
  return {
    collect: useMutation({ mutationFn: collectPriceUpdate, retry: false }),
    apply: useMutation({
      mutationFn: ({ previewId, input }: { previewId: string; input: PriceApplyInput }) =>
        applyPriceUpdate(previewId, input),
      retry: false,
      onSuccess: () => {
        void client.invalidateQueries({ queryKey: queryKeys.prices() })
        void client.invalidateQueries({ queryKey: queryKeys.callStats() })
        void client.invalidateQueries({ queryKey: queryKeys.jobDetails() })
        void client.invalidateQueries({ queryKey: queryKeys.jobLists() })
        void client.invalidateQueries({ queryKey: queryKeys.similarity() })
      },
    }),
  }
}

export function usePriceUpdateConfigs() {
  const query = useQuery({
    queryKey: queryKeys.providers(),
    queryFn: ({ signal }) => listProviders(signal),
    retry: false,
  })
  return {
    providers: query.data ?? [],
    enabledProviders: (query.data ?? []).filter((config) => config.isEnabled),
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
