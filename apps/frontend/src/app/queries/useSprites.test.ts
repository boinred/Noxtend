import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { MutationOptions } from '@tanstack/react-query'
import { useJob } from './useJob'
import { useJobList } from './useJobList'
import { useProviders } from './useProviders'
import {
  useStartSpriteJob,
  useUpdateSpritePlan,
  useApproveSpritePlan,
  useApproveSpriteBases,
  useRegenerateSpriteFrame,
  useApproveSpriteAsset,
  useExportSprites,
} from './useSprites'
import { queryKeys } from './keys'
import { ApiError } from '@/infra/api/client'
import type { SpriteAccepted, SpriteMutationContext } from '@/domain/sprites/types'

const mocks = vi.hoisted(() => ({
  result: {
    data: undefined as unknown,
    error: null as Error | null,
    isError: false,
    isLoading: false,
    refetch: vi.fn(),
  },
  query: vi.fn<(options: unknown) => unknown>(),
  mutation: vi.fn<(options: unknown) => unknown>(),
  invalidate: vi.fn().mockResolvedValue(undefined),
}))
vi.mock('@tanstack/react-query', () => ({
  useQuery: (options: unknown) => {
    mocks.query(options)
    return mocks.result
  },
  useMutation: (options: unknown) => {
    mocks.mutation(options)
    return options
  },
  useQueryClient: () => ({ invalidateQueries: mocks.invalidate }),
}))

const context = { jobId: 'job', requestId: 'saved-request', expectedRevision: 4 }
const receipt: SpriteAccepted = { id: 'job', status: 'running', revision: 5, taskIds: [] }
interface QueryOptions {
  queryKey: readonly unknown[]
  queryFn: (context: { signal: AbortSignal }) => Promise<unknown>
  refetchInterval: (query: { state: { data?: unknown; dataUpdateCount: number } }) => number | false
}
function queryOptions() {
  return mocks.query.mock.calls.at(-1)![0] as QueryOptions
}
function mutationOptions() {
  return mocks.mutation.mock.calls.at(-1)![0] as MutationOptions<
    SpriteAccepted,
    Error,
    SpriteMutationContext
  >
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.unstubAllGlobals()
  mocks.result.data = undefined
  mocks.result.error = null
  mocks.result.isError = false
})

describe('shared query boundaries', () => {
  it.each([new ApiError('NETWORK_UNREACHABLE', 'offline', 0), new Error('contract')])(
    'keeps network and contract errors distinct: %s',
    (error) => {
      mocks.result.error = error
      mocks.result.isError = true
      expect(useJob('job')).toMatchObject({
        isNotFound: false,
        isError: true,
        error,
        refetch: mocks.result.refetch,
      })
    },
  )
  it('reports only actual HTTP 404 as not found', () => {
    mocks.result.error = new ApiError('JOB_NOT_FOUND', 'missing', 404)
    mocks.result.isError = true
    expect(useJob('job').isNotFound).toBe(true)
  })

  it('polls pendingReview and unfinished tasks even with a terminal job status', () => {
    useJob('job')
    const poll = queryOptions().refetchInterval
    expect(
      poll({ state: { data: { status: 'pendingReview', tasks: [] }, dataUpdateCount: 1 } }),
    ).toBe(2000)
    expect(
      poll({
        state: {
          data: { status: 'partiallySucceeded', tasks: [{ status: 'running' }] },
          dataUpdateCount: 3,
        },
      }),
    ).toBe(15000)
    expect(
      poll({
        state: {
          data: { status: 'succeeded', tasks: [{ status: 'succeeded' }] },
          dataUpdateCount: 3,
        },
      }),
    ).toBe(false)
    expect(poll({ state: { dataUpdateCount: 0 } })).toBe(false)
  })

  it('uses mode and limit in the list key and propagates errors and AbortSignal', async () => {
    const error = new ApiError('NETWORK_UNREACHABLE', 'offline', 0)
    mocks.result.error = error
    mocks.result.isError = true
    expect(useJobList('active', 20, 'twoD')).toMatchObject({
      jobs: [],
      isError: true,
      error,
      refetch: mocks.result.refetch,
    })
    const options = queryOptions()
    expect(options.queryKey).toEqual(queryKeys.jobList('active', 20, 'twoD'))
    const signal = new AbortController().signal
    const fetch = vi.fn().mockRejectedValue(new Error('offline'))
    vi.stubGlobal('fetch', fetch)
    await expect(options.queryFn({ signal })).rejects.toMatchObject({ status: 0 })
    expect(fetch.mock.calls[0]![1].signal).toBe(signal)
    const abort = new DOMException('canceled', 'AbortError')
    fetch.mockRejectedValue(abort)
    await expect(options.queryFn({ signal })).rejects.toBe(abort)
  })

  it('exposes provider lookup failure instead of empty success', async () => {
    const error = new ApiError('NETWORK_UNREACHABLE', 'offline', 0)
    mocks.result.error = error
    mocks.result.isError = true
    expect(useProviders()).toMatchObject({
      providers: [],
      isError: true,
      error,
      refetch: mocks.result.refetch,
    })
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('offline')))
    await expect(
      queryOptions().queryFn({ signal: new AbortController().signal }),
    ).rejects.toMatchObject({ status: 0 })
  })
})

describe('sprite mutation cache and retry', () => {
  it('all mutations turn automatic retry off', () => {
    for (const hook of [
      useStartSpriteJob,
      useUpdateSpritePlan,
      useApproveSpritePlan,
      useApproveSpriteBases,
      useRegenerateSpriteFrame,
      useApproveSpriteAsset,
      useExportSprites,
    ]) {
      hook()
      expect(mutationOptions().retry).toBe(false)
    }
  })

  it('invalidates shared detail and list on success or 409, preserving the visible error', async () => {
    useApproveSpritePlan()
    const options = mutationOptions()
    await options.onSuccess!(receipt, context, undefined, {} as never)
    expect(mocks.invalidate.mock.calls).toEqual([
      [{ queryKey: queryKeys.job('job') }],
      [{ queryKey: queryKeys.jobLists() }],
    ])
    mocks.invalidate.mockClear()
    await options.onError!(
      new ApiError('SPRITE_REVISION_CONFLICT', 'conflict', 409),
      context,
      undefined,
      {} as never,
    )
    expect(mocks.invalidate).toHaveBeenCalledTimes(2)
    mocks.invalidate.mockClear()
    await options.onError!(
      new ApiError('NETWORK_UNREACHABLE', 'offline', 0),
      context,
      undefined,
      {} as never,
    )
    expect(mocks.invalidate).not.toHaveBeenCalled()
  })

  it('keeps the supplied request ID and revision on user resend after response loss', async () => {
    useApproveSpritePlan()
    const options = mutationOptions()
    const fetch = vi
      .fn()
      .mockRejectedValueOnce(new Error('lost response'))
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ data: receipt, error: null }), { status: 202 }),
      )
    vi.stubGlobal('fetch', fetch)
    await expect(options.mutationFn!(context, {} as never)).rejects.toMatchObject({ status: 0 })
    expect(await options.mutationFn!(context, {} as never)).toEqual(receipt)
    expect(fetch.mock.calls[0]![1].signal).toBeUndefined()
    expect(fetch.mock.calls[0]![1].body).toEqual(fetch.mock.calls[1]![1].body)
    expect(JSON.parse(fetch.mock.calls[1]![1].body)).toEqual({
      requestId: 'saved-request',
      expectedRevision: 4,
    })
  })
})
