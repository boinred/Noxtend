/**
 * 조립 명세 조회 (scene-assembly §5.3).
 *
 * **완성 GLB 수가 조회 키에 들어간다.** 서버의 낡음 판정(§3.4)과 같은 신호다 —
 * 3D 가 하나 도착하면 키가 바뀌고, 새 조회가 서버의 재유도를 끌어낸다.
 */
import { useQuery } from '@tanstack/react-query'
import { getSceneLayout } from '@/infra/api/jobApi'
import { queryKeys } from './keys'
import type { Job, SceneLayoutData } from '@/domain/job/types'

export interface UseSceneLayoutResult {
  layout: SceneLayoutData | null
  isLoading: boolean
  isError: boolean
}

export function useSceneLayout(
  job: Job | null | undefined,
  enabled: boolean,
): UseSceneLayoutResult {
  // 작업이 아직 로딩 중이면 조회하지 않는다 — enabled 만 믿으면 키 계산에서 터진다
  const meshCount = job?.parts.filter((part) => part.generatedMesh !== null).length ?? 0

  const query = useQuery({
    queryKey: queryKeys.sceneLayout(job?.id ?? '', meshCount),
    queryFn: ({ signal }) => getSceneLayout(job!.id, signal),
    enabled: enabled && job != null,
    // 명세는 결정적이다 — 같은 키면 다시 받을 이유가 없다
    staleTime: Infinity,
    retry: 1,
  })

  return { layout: query.data ?? null, isLoading: query.isLoading, isError: query.isError }
}
